using System.Globalization;
using Octodiff.Core;
using Octodiff.Diagnostics;

namespace ChronoVault
{
    public class BackupManager
    {
        private string source;
        private string backupRoot;
        private string[] fileTypes;
        private int maxBackups;
        private int interval;
        private bool isPaused;
        public bool BackupModifiedOnly { get; private set; }
        public DateTime NextBackupTime { get; private set; }

        public DateTime LastBackupTime { get; private set; }
        public bool IsPaused
        {
            get { return isPaused; }
        }
        
        private AutoResetEvent pauseHandle = new AutoResetEvent(false);

        public BackupManager(string source, string backupRoot, string[] fileTypes, int maxBackups, int interval,
            bool backupModifiedOnly, bool copyAllOnStartup)
        {
            this.source = source;
            this.backupRoot = backupRoot;
            this.fileTypes = fileTypes;
            this.maxBackups = maxBackups;
            this.interval = interval;
            isPaused = false;
            BackupModifiedOnly = backupModifiedOnly;
            Directory.CreateDirectory(backupRoot);
            if (!copyAllOnStartup)
                LastBackupTime = DateTime.Now.AddSeconds(-interval);
        }

        public void StartBackupLoop()
        {
            var currentBackupNum = GetMostRecentIndex(backupRoot);
            
            if (currentBackupNum > maxBackups)
                currentBackupNum = maxBackups;
            else
                currentBackupNum++;
            
            while (true)
            {
                if (isPaused)
                {
                    pauseHandle.WaitOne();
                }

                var now = DateTime.Now;
                NextBackupTime = now.AddSeconds(interval);

                var currentBackup = FindFolderWithPrefix(backupRoot, $"{currentBackupNum}_");
                if (currentBackup != null)
                {
                    Directory.Delete(currentBackup, true); // Delete the old backup folder
                }

                currentBackup = CreateBackupFolder(backupRoot, currentBackupNum); // Create a new backup folder with an updated timestamp

                
                var copiedFilesCount = CopyFiles(source, currentBackup, fileTypes, false);
                
                LastBackupTime = DateTime.Now;

                if (copiedFilesCount > 0)
                {
                    // Progress the index only if files were copied
                    currentBackupNum = currentBackupNum % maxBackups + 1;
                }
                else
                {
                    // Delete the empty backup folder
                    Directory.Delete(currentBackup, true);
                }

                var timeToSleep = NextBackupTime - DateTime.Now;
                if (timeToSleep.TotalMilliseconds > 0)
                {
                    Thread.Sleep(timeToSleep);
                }
            }
        }
        
        private int GetMostRecentIndex(string root)
        {
            int maxIndex = 0;

            foreach (var folder in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(folder);
                if (int.TryParse(folderName.Split('_')[0], out var currentIndex))
                {
                    if (currentIndex > maxIndex)
                        maxIndex = currentIndex;
                }
            }

            if (maxIndex >= maxBackups)
            {
                maxIndex = maxBackups;
            }

            Console.WriteLine(maxIndex == 0 ? "No previous backups found" : $"Found backups up to index {maxIndex}");

            Console.WriteLine($"Resuming at index {maxIndex % maxBackups + 1}");
            return maxIndex;
        }
        
        public void PauseBackup()
        {
            isPaused = true;
        }

        public void ResumeBackup()
        {
            isPaused = false;
            pauseHandle.Set();
        }

        private string? FindFolderWithPrefix(string root, string prefix)
        {
            return Directory.GetDirectories(root).FirstOrDefault(folder => Path.GetFileName(folder).StartsWith(prefix));
        }

        private string CreateBackupFolder(string root, int index)
        {
            var folderName = $"{index}_{DateTime.Now:HH-mm_dd-MM-yyyy}";
            var folderPath = Path.Combine(root, folderName);
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        private int CopyFiles(string source, string destination, string[] fileTypes, bool restoring)
        {
            var copiedFilesCount = 0;

            foreach (var fileType in fileTypes)
            {
                foreach (var file in Directory.GetFiles(source, restoring? "" : fileType))
                {
                    var fileInfo = new FileInfo(file);
                    var destinationFile = restoring
                        ? Path.Combine(source, fileInfo.Name): Path.Combine(destination, fileInfo.Name);

                    if (BackupModifiedOnly && !restoring)
                    {
                        if (fileInfo.LastWriteTime <= LastBackupTime)
                        {
                            // Skip the file if it hasn't been modified since the last backup
                            continue;
                        }

                        // Copy the file to memory
                        MemoryStream fileContent;
                        using (var fileStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            fileContent = new MemoryStream();
                            fileStream.CopyTo(fileContent);
                            fileContent.Position = 0; // Reset the position to the beginning of the stream
                        }

                        // Create signature
                        string signatureFile = Path.Combine(destination, fileInfo.Name + ".sig");
                        using (var signatureStream = File.Create(signatureFile))
                        {
                            var signatureBuilder = new SignatureBuilder();
                            signatureBuilder.Build(fileContent, new SignatureWriter(signatureStream));
                        }

                        // Reset the position of the MemoryStream to the beginning
                        fileContent.Position = 0;

                        // Create delta
                        string deltaFile = Path.Combine(destination, fileInfo.Name + ".delta");
                        using (var signatureStream = File.OpenRead(signatureFile))
                        using (var deltaStream = File.Create(deltaFile))
                        {
                            var deltaBuilder = new DeltaBuilder();
                            deltaBuilder.BuildDelta(fileContent,
                                new SignatureReader(signatureStream, new NullProgressReporter()),
                                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
                        }

                        // Delete the original file from the destination
                        if (File.Exists(destinationFile))
                        {
                            File.Delete(destinationFile);
                        }

                        copiedFilesCount++;
                    }
                    else if (restoring)
                    {
                        
                        //set filename to fileinfo.name but remove everything after the last dot and the dot included
                        string filename = fileInfo.Name;
                        filename = filename.Substring(0, filename.LastIndexOf('.'));
                        string originalFile = Path.Combine(source, filename);
                        
                        
                        string deltaFile = Path.Combine(destination, filename + ".delta");
                        if (File.Exists(deltaFile))
                        {
                            using (var basisStream = File.Open(originalFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var deltaStream = File.OpenRead(deltaFile))
                            using (var destinationStream = new MemoryStream())
                            {
                                var deltaApplier = new DeltaApplier { SkipHashCheck = true };
                                deltaApplier.Apply(basisStream, new BinaryDeltaReader(deltaStream, new NullProgressReporter()), destinationStream);

                                destinationStream.Position = 0;
                                using (var fileStream = File.Open(originalFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                                {
                                    destinationStream.CopyTo(fileStream);
                                }
                            }
                        }
                        else
                        {
                            // If there's no delta, copy the entire file
                            using (var fileStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var destinationStream = File.Open(destinationFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                            {
                                fileStream.CopyTo(destinationStream);
                            }
                        }
                        copiedFilesCount++;
                    }
                }
            }

            if (!restoring)
            {
                var changedString = BackupModifiedOnly ? "modified" : "";

                var folderName = Path.GetFileName(destination);

                var splitFolderName = folderName.Split('_');

                var index = splitFolderName[0].Replace("_", "");

// Combine the time and date parts from the folder name
                var timestamp = $"{splitFolderName[1]}_{splitFolderName[2]}";

// Parse the timestamp using the correct format
                var parsedTime = DateTime.ParseExact(timestamp, "HH-mm_dd-MM-yyyy", CultureInfo.InvariantCulture);

// Format the parsed time as desired
                var outputTime = parsedTime.ToString("hh:mm tt dd/MM/yyyy", CultureInfo.InvariantCulture);

                if (copiedFilesCount > 0)
                    Console.WriteLine(
                        $"{copiedFilesCount} {changedString} files backed up to index {index} at {outputTime}");
            }

            return copiedFilesCount;
        }

        public void RestoreBackup(string backupFolder)
        {
            if (Directory.Exists(backupFolder))
            {
                CopyFiles(source, backupFolder, fileTypes, true);
                Console.WriteLine($"Backup restored from {backupFolder}");
            }
            else
            {
                Console.WriteLine($"Backup folder {backupFolder} not found");
            }
        }
    }
}