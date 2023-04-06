using System.Globalization;

namespace ChronoVault
{
    class Program
    {
        private static void Main()
        {
            var config = ReadConfig("config.ini");

            var source = config["source"];
            var backupRoot = config["backupRoot"];
            var fileTypes = config["fileTypes"].Split(',');
            var maxBackups = Convert.ToInt32(config["maxBackups"]);
            var seconds = Convert.ToInt32(config["secondsBetweenBackups"]);
            var modifiedOnly = Convert.ToBoolean(config["backupModifiedOnly"]);
            var copyAllOnStartup = Convert.ToBoolean(config["copyAllOnStartup"]);

            var backupManager = new BackupManager(source, backupRoot, fileTypes, maxBackups, seconds, modifiedOnly,
                copyAllOnStartup);

            // Start the backup loop in a separate thread
            var backupThread = new Thread(backupManager.StartBackupLoop);
            var timeLoopSpan = TimeSpan.FromSeconds(seconds * maxBackups);
            backupThread.Start();

            Console.WriteLine("Configuration loaded:");
            Console.WriteLine();
            Console.WriteLine("Source: " + source
                              + Environment.NewLine + "Backup root: " + backupRoot
                              + Environment.NewLine + "File types: " + config["fileTypes"]
                              + Environment.NewLine + "Max backups: " + maxBackups
                              + Environment.NewLine + "Seconds between backups: " + seconds
                              + Environment.NewLine + "Backup modified only: " + modifiedOnly
                              + Environment.NewLine + "Copy all on startup: " + copyAllOnStartup);
            Console.WriteLine();
            Console.WriteLine("Backup Span: ("
                              + timeLoopSpan.Days + ") Days ("
                              + timeLoopSpan.Hours + ") Hours ("
                              + timeLoopSpan.Minutes + ") Minutes ("
                              + timeLoopSpan.Seconds + ") Seconds ");
            
            Console.WriteLine();
            Console.WriteLine("ChronoVault is running in the background");
            Console.WriteLine();
            Console.WriteLine("Press 'P' to pause or resume the backup.");
            Console.WriteLine("Press 'R' to restore a backup.");
            Console.WriteLine("Press 'Q' to quit.");
            Console.WriteLine("");

            while (true)
            {
                var keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.P)
                {
                    if (backupManager.IsPaused)
                    {
                        Console.WriteLine("Resumed backup >>");
                        backupManager.ResumeBackup();
                    }
                    else
                    {
                        Console.WriteLine("Paused Backup.");
                        backupManager.PauseBackup();
                    }
                }
                else if (keyInfo.Key == ConsoleKey.R)
                {
                    Console.WriteLine("\nAvailable backups:");
                    var backupFolders = Directory.GetDirectories(backupRoot);
                    var index = 1;

                    foreach (var folder in backupFolders)
                    {
                        var folderName = Path.GetFileName(folder);

                        var splitFolderName = folderName.Split('_');

// Combine the time and date parts from the folder name
                        var timestamp = $"{splitFolderName[1]}_{splitFolderName[2]}";

// Parse the timestamp using the correct format
                        var parsedTime = DateTime.ParseExact(timestamp, "HH-mm_dd-MM-yyyy", CultureInfo.InvariantCulture);

// Format the parsed time as desired
                        var outputTime = parsedTime.ToString("hh:mm tt dd/MM/yyyy", CultureInfo.InvariantCulture);
                        
                        Console.WriteLine($"{index}: {outputTime}");
                        
                        index++;
                    }

                    Console.WriteLine("Enter the number of the backup you want to restore, or type 'C' to cancel:");
                    var input = Console.ReadLine();
                    if (string.IsNullOrEmpty(input)) continue;
                    if (int.TryParse(input, out var selectedIndex) && selectedIndex > 0 &&
                        selectedIndex <= backupFolders.Length)
                    {
                        var selectedBackup = backupFolders[selectedIndex - 1];
                        Console.WriteLine($"Restoring backup from {Path.GetFileName(selectedBackup)}...");
                        backupManager.RestoreBackup(selectedBackup);
                    }
                    else if (input.Equals("C", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Restore operation canceled.");
                    }
                    else
                    {
                        Console.WriteLine("Invalid selection. Please try again.");
                    }

                    Console.WriteLine("\nPress 'P' to pause or resume the backup.");
                    Console.WriteLine("Press 'R' to restore a backup.");
                    Console.WriteLine("Press 'Q' to quit.");
                }
                else if (keyInfo.Key == ConsoleKey.Q)
                {
                    break;
                }
            }
        }

        private static Dictionary<string, string> ReadConfig(string fileName)
        {
            var config = new Dictionary<string, string>();

            using var sr = new StreamReader(fileName);

            while (sr.ReadLine() is { } line)
            {
                var keyValue = line.Split('=');

                if (keyValue.Length == 2)
                    config[keyValue[0].Trim()] = keyValue[1].Trim();
            }

            return config;
        }
    }
}