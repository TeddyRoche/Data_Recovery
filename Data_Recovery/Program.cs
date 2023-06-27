using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

class Program
{
    static void Main()
    {
        // To run this program:
        // 1. Open the folder where the file is located
        // 2. Make sure that the drive path letter matches the drive you are trying to scan
        // 3. Once in the project folder, execute the program
        // 4. To stop early, press Ctrl+C

        // Define the drive path to scan
        string drivePath = @"\\.\D:"; // Open drive as raw bytes (G = windows "|")
        
        //change the letter to the type you are scanning
        DriveInfo dDrive = new DriveInfo("D");
        long sizeOfDrive = 0;

        // When the drive is accessible..
        if (dDrive.IsReady)
        {
            // Calculate the percentage free space
            double freeSpacePerc = (dDrive.AvailableFreeSpace / (float)dDrive.TotalSize) * 100;
            // Ouput drive information
            Console.WriteLine("Drive: {0} ({1}, {2})", dDrive.Name, dDrive.DriveFormat, dDrive.DriveType);

            Console.WriteLine("\tFree space:\t{0:F3}", (double)dDrive.AvailableFreeSpace / (1024 * 1024 * 1024));
            Console.WriteLine("\tTotal space:\t{0:F3}", (double)dDrive.TotalSize / (1024 * 1024 * 1024));

            Console.WriteLine("\n\tPercentage free space: {0:0.00}%.", freeSpacePerc);
            sizeOfDrive = dDrive.TotalSize;
        }

        // Open the drive in binary mode
        using (FileStream fileHandle = new FileStream(drivePath, FileMode.Open, FileAccess.Read))
        {
            // Define the chunk sizes for reading the file
            int[] chunkSizes = { 4096, 8192, 65536, 1048576 };

            // Prompt the user to choose a chunk size
            Console.WriteLine("\nChoose the chunk size:\n1. 4096 bytes\n2. 8192 bytes\n3. 65536 bytes\n4. 1048576 bytes");
            int chunkSizeChoice = int.Parse(Console.ReadLine());
            int chunkSize = chunkSizes[chunkSizeChoice - 1];

            // If an invalid choice is made, use the default chunk size of 4096 bytes
            if (chunkSize == 0)
            {
                Console.WriteLine("Invalid choice. Using default chunk size of 4096 bytes.");
                chunkSize = 4096;
            }

            // Read the first chunk of data from the file
            byte[] dataChunk = new byte[chunkSize];
            int bytesRead = fileHandle.Read(dataChunk, 0, chunkSize);

            // Initialize variables for recovery process
            int offset = 0; // Offset location
            bool recoveryMode = false; // Recovery mode flag
            int recoveredFileId = 0; // Recovered file ID
            long totalBytesScanned = 0; // Total bytes scanned

            // Create a directory to store the recovered files
            string recoveryDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Recovered_Files");
            Directory.CreateDirectory(recoveryDirectory);

            // Supported file types and their signatures
            var fileTypes = new Dictionary<string, Tuple<byte[], byte[]>>()
            {
                { "jpg", Tuple.Create(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }, new byte[] { 0xFF, 0xD9 }) },
                { "jpeg", Tuple.Create(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }, new byte[] { 0xFF, 0xD9 }) },
                { "png", Tuple.Create(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, new byte[] { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }) },
                { "pdf", Tuple.Create(new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-' }, new byte[] { (byte)'%', (byte)'%', (byte)'E', (byte)'O', (byte)'F' }) },
                { "docx", Tuple.Create(new byte[] { (byte)'P', (byte)'K', 0x03, 0x04 }, new byte[] { (byte)'P', (byte)'K', 0x05, 0x06 }) }
            };

            // Get the file type from user input
            Console.WriteLine("Enter the file type you want to search for (jpg, png, pdf, docx): ");
            string fileType = Console.ReadLine().ToLower();

            // Check if the file type is supported
            if (!fileTypes.ContainsKey(fileType))
            {
                Console.WriteLine("Invalid file type. Supported file types: jpg, jpeg, png, pdf, docx");
                return;
            }

            // Retrieve the file signatures and extension based on the chosen file type
            Tuple<byte[], byte[]> fileSignature = fileTypes[fileType];
            byte[] signatureStart = fileSignature.Item1;
            byte[] signatureEnd = fileSignature.Item2;
            string fileExtension = "." + fileType;

            // Start scanning the file in chunks
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (totalBytesScanned < sizeOfDrive)
            {
                totalBytesScanned += bytesRead;

                // Display message every 150 megabytes
                if (totalBytesScanned % (250 * 1024 * 1024) == 0)
                {
                    TimeSpan elapsedTime = stopwatch.Elapsed;
                    Console.WriteLine("Elapsed Time: {0:hh\\:mm\\:ss}", elapsedTime);
                    Console.WriteLine("Scanned: {0:F3} GB", (double)totalBytesScanned / (1024 * 1024 * 1024));
                }

                // Search for file signatures based on file types
                if (FindSignatureIndex(dataChunk, signatureStart) >= 0)
                {
                    int signatureIndex = FindSignatureIndex(dataChunk, signatureStart);
                    recoveryMode = true;
                    Console.WriteLine("==== Found " + fileType.ToUpper() + " at location: " + (signatureIndex + (chunkSize * offset)).ToString("X") + " ====");

                    // Create a recovered file and search for the end marker
                    string recoveredFilePath = Path.Combine(recoveryDirectory, $"{recoveredFileId}{fileExtension}");
                    using (FileStream recoveredFileHandle = new FileStream(recoveredFilePath, FileMode.Create, FileAccess.Write))
                    {
                        recoveredFileHandle.Write(dataChunk, signatureIndex, bytesRead - signatureIndex);

                        while (recoveryMode)
                        {
                            bytesRead = fileHandle.Read(dataChunk, 0, chunkSize);

                            int signatureEndIndex = FindSignatureIndex(dataChunk, signatureEnd);
                            if (signatureEndIndex >= 0)
                            {
                                recoveredFileHandle.Write(dataChunk, 0, signatureEndIndex + signatureEnd.Length);
                                fileHandle.Seek((offset + 1) * chunkSize, SeekOrigin.Begin);
                                Console.WriteLine("==== Wrote " + fileType.ToUpper() + " to location: " + recoveredFilePath + " ====\n");
                                recoveryMode = false;
                                recoveredFileId++;
                                break;
                            }
                            else
                            {
                                recoveredFileHandle.Write(dataChunk, 0, bytesRead);
                            }
                        }
                    }
                }

                bytesRead = fileHandle.Read(dataChunk, 0, chunkSize);
                offset++;
            }

            stopwatch.Stop();
            TimeSpan totalElapsedTime = stopwatch.Elapsed;
            Console.WriteLine("Total Elapsed Time: {0:hh\\:mm\\:ss}", totalElapsedTime);
            Console.WriteLine("Scanned: {0:F3} MB", (double)totalBytesScanned / (1024 * 1024 * 1024));
        }
        Console.WriteLine("Press any key to end the program...");
        Console.ReadKey();
    }

    static int FindSignatureIndex(byte[] data, byte[] signature)
    {
        for (int i = 0; i <= data.Length - signature.Length; i++)
        {
            if (data[i] == signature[0])
            {
                bool match = true;
                for (int j = 1; j < signature.Length; j++)
                {
                    if (data[i + j] != signature[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }
            }
        }
        return -1;
    }
}
