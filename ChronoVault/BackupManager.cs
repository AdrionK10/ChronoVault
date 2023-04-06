using System.Globalization;

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
        private bool allowSubfolders;
        public bool BackupModifiedOnly { get; private set; }
        public DateTime NextBackupTime { get; private set; }

        public DateTime LastBackupTime { get; private set; }

        public bool IsPaused
        {
            get { return isPaused; }
        }

        private AutoResetEvent pauseHandle = new(false);

        public BackupManager(string source, string backupRoot, bool allowSubfolders, string[] fileTypes, int maxBackups,
            int interval,
            bool backupModifiedOnly, bool copyAllOnStartup)
        {
            this.source = source;
            this.backupRoot = backupRoot;
            this.fileTypes = fileTypes;
            this.maxBackups = maxBackups;
            this.interval = interval;
            this.allowSubfolders = allowSubfolders;

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
                else
                {
                    var now = DateTime.Now;
                    NextBackupTime = now.AddSeconds(interval);

                    var currentBackup = FindFolderWithPrefix(backupRoot, $"{currentBackupNum}_");
                    if (currentBackup != null)
                    {
                        Directory.Delete(currentBackup, true); // Delete the old backup folder
                    }

                    currentBackup =
                        CreateBackupFolder(backupRoot,
                            currentBackupNum); // Create a new backup folder with an updated timestamp

                    var copiedFilesCount = CopyFiles(source, currentBackup, fileTypes);
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

        private int CopyFiles(string source, string destination, string[] fileTypes, bool restoring = false)
        {
            return allowSubfolders ? 
                CopyFilesRecursively(source, destination, fileTypes, restoring) : 
                CopyFilesFromDirectory(source, destination, fileTypes, restoring);
        }

        private int CopyFilesRecursively(string source, string destination, string[] fileTypes, bool restoring)
        {
            var copiedFilesCount = 0;

            // Copy files from the current directory
            copiedFilesCount += CopyFilesFromDirectory(source, destination, fileTypes, restoring);

            // Get all subdirectories
            var subDirectories = Directory.GetDirectories(source);

            // Recursively copy files from each subdirectory
            foreach (var subDirectory in subDirectories)
            {
                var destSubDirectory = Path.Combine(destination, new DirectoryInfo(subDirectory).Name);
                copiedFilesCount += CopyFilesRecursively(subDirectory, destSubDirectory, fileTypes, restoring);
            }

            return copiedFilesCount;
        }

        private int CopyFilesFromDirectory(string source, string destination, string[] fileTypeArray, bool restoring)
        {
            var copiedFilesCount = 0;
            var filesToCopy = new List<string>();

            foreach (var fileType in fileTypeArray)
            {
                foreach (var file in Directory.GetFiles(source, fileType))
                {
                    var fileInfo = new FileInfo(file);

                    if (BackupModifiedOnly && !restoring)
                    {
                        if (fileInfo.LastWriteTime <= LastBackupTime)
                        {
                            // Skip the file if it hasn't been modified since the last backup
                            continue;
                        }
                    }

                    filesToCopy.Add(file);
                }
            }

            if (filesToCopy.Count > 0)
            {
                if (!Directory.Exists(destination))
                {
                    Directory.CreateDirectory(destination);
                }

                foreach (var file in filesToCopy)
                {
                    var fileInfo = new FileInfo(file);
                    var destinationFile = Path.Combine(destination, fileInfo.Name);
                    File.Copy(file, destinationFile, true);
                    copiedFilesCount++;
                }
            }

            return copiedFilesCount;
        }

        public void RestoreBackup(string backupFolder)
        {
            if (Directory.Exists(backupFolder))
            {
                CopyFiles(backupFolder, source, fileTypes, restoring: true);
                Console.WriteLine($"Backup restored from {backupFolder}");
            }
            else
            {
                Console.WriteLine($"Backup folder {backupFolder} not found");
            }
        }
    }
}