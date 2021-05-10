using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace XSOverlay_VRChat_Parser_Updater
{
    class Program
    {
        private const int RetryMax = 10;
        private static bool isElevated = false;

        static void Main(string[] args)
        {
            string sourceDir = args[0];
            string targetDir = args[1];
            int parserPid = int.Parse(args[2]);
            isElevated = bool.Parse(args[3]);

            Log($"Updater initialized with arguments: ");

            foreach (string arg in args)
                Log($"Argument: {arg}");

            Log($"Waiting for XSOverlay VRChat Parser to close...");

            while (true)
            {
                try
                {
                    Process p = Process.GetProcessById(parserPid);

                    if (p.HasExited)
                        break;

                    Log("XSOverlay VRChat Parser process is still running. Waiting for exit...");
                    p.WaitForExitAsync().GetAwaiter().GetResult();
                }
                catch (ArgumentException aex)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                    return;
                }
            }

            Log("Checking source directory exists...");
            if (!Directory.Exists(sourceDir))
            {
                Log("Source directory could not be found. Aborting.");
                return;
            }

            Log("Checking target directory exists...");
            if (!Directory.Exists(targetDir))
            {
                Log("Target directory could not be found. Aborting.");
                return;
            }

            Log("Validating that target directory is writable...");
            bool writeSuccess = false;
            for (int i = 0; i < RetryMax; i++)
            {
                try
                {
                    // I was going to use System.Security.AccessControl.DirectorySecurity here and check ACLs, but it's much faster and easier to just, well, try to write something

                    File.WriteAllBytes($@"{targetDir}\.writable", new byte[1] { 0x01 });
                    File.Delete($@"{targetDir}\.writable");

                    writeSuccess = true;

                    break;
                }
                catch (Exception ex)
                {
                    Task.Delay(100).GetAwaiter().GetResult();
                }
            }

            if (!writeSuccess)
            {
                Log($"Can't write to target directory. Attempting to relaunch as elevated user.");

                try
                {
                    string currentLocation = Assembly.GetExecutingAssembly().Location;
                    currentLocation = currentLocation[0..(currentLocation.LastIndexOf('\\'))];

                    ProcessStartInfo updaterInfo = new ProcessStartInfo()
                    {
                        FileName = currentLocation + "\\XSOverlay VRChat Parser Updater.exe",
                        UseShellExecute = true,
                        RedirectStandardOutput = false,
                        Arguments = $"\"{args[0]}\" \"{args[1]}\" {args[2]} true",
                        WorkingDirectory = currentLocation
                    };

                    updaterInfo.Verb = "runas";

                    Process p = Process.Start(updaterInfo);

                    p.WaitForExitAsync().GetAwaiter().GetResult();
                    Log($"Elevated process exited.");
                }
                catch (Exception ex)
                {
                    Log("Failed to restart process as elevated user.");
                    Log(ex.Message);
                    return;
                }
            }
            else
            {
                RunUpdate(sourceDir, targetDir);
            }

            if (!isElevated)
            {
                ProcessStartInfo parserInfo = new ProcessStartInfo()
                {
                    FileName = $@"{targetDir}\XSOverlay VRChat Parser.exe",
                    UseShellExecute = true,
                    RedirectStandardOutput = false,
                    WorkingDirectory = targetDir
                };

                Process.Start(parserInfo);
            }
        }

        static void RunUpdate(string sourceDir, string targetDir)
        {
            // The Resources directory is a special case. We will ever only overwrite this directory, not delete it.
            Log("Cleaning up target directory...");

            try
            {
                CopyDirectoryAdditive(sourceDir + "\\Resources", targetDir + "\\Resources");
            }
            catch (Exception ex)
            {
                Log("Failed to copy resources directory from source directory to target directory.");
                Log(ex.Message);
                return;
            }

            bool moveSuccess = false;

            try
            {
                string[] sourceDirectories = Directory.GetDirectories(sourceDir).Where(x => x[(x.LastIndexOf('\\') + 1)..].ToLower() != "resources").ToArray();
                string[] sourceFiles = Directory.GetFiles(sourceDir);
                string[] targetDirectories = Directory.GetDirectories(targetDir).Where(x => x[(x.LastIndexOf('\\') + 1)..].ToLower() != "resources").ToArray();
                string[] targetFiles = Directory.GetFiles(targetDir);

                foreach (string dir in targetDirectories)
                {
                    Log($"Attempting to remove target directory: {dir}");
                    for (int i = 0; i < RetryMax; i++)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Task.Delay(100).GetAwaiter().GetResult();
                        }
                    }
                }
                foreach (string fn in targetFiles)
                {
                    Log($"Attempting to remove target file: {fn}");
                    for (int i = 0; i < RetryMax; i++)
                    {
                        try
                        {
                            File.Delete(fn);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Task.Delay(100).GetAwaiter().GetResult();
                        }
                    }
                }

                foreach (string fn in sourceFiles)
                {
                    Log($"Attempting to move file to target directory: {fn}");
                    for (int i = 0; i < RetryMax; i++)
                    {
                        try
                        {
                            File.Move(fn, $@"{targetDir}\{fn[(fn.LastIndexOf('\\') + 1)..]}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            Task.Delay(100).GetAwaiter().GetResult();
                        }
                    }
                }
                foreach (string dir in sourceDirectories)
                {
                    Log($"Attempting to move directory to target directory: {dir}");
                    for (int i = 0; i < RetryMax; i++)
                    {
                        try
                        {
                            Directory.Move(dir, $@"{targetDir}\{dir[(dir.LastIndexOf('\\') + 1)..]}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            Task.Delay(100).GetAwaiter().GetResult();
                        }
                    }
                }

                moveSuccess = true;
            }
            catch (Exception ex)
            {
                string currentAssemblyLocation = Assembly.GetExecutingAssembly().Location;
                currentAssemblyLocation = currentAssemblyLocation.Substring(0, currentAssemblyLocation.LastIndexOf('\\'));

                Log(ex.Message);
                Log($"Failed to clean and copy source and target directories. Please send the log file generated at {currentAssemblyLocation}\\update.log to the developer!");
                Log("You may need to redownload the parser from GitHub. This is a critical failure.");
                Console.ReadLine();
            }

            if (moveSuccess)
                Log($"Successfully updated binaries and resources in target directory. Starting...");

            Log("Exiting.");
        }

        static void CopyDirectoryAdditive(string sourceDir, string targetDir)
        {
            Log($"CopyDirectoryAdditive called for source directory {sourceDir} and target directory {targetDir}");

            if (!Directory.Exists(targetDir))
            {
                Log($"Directory didn't exist in target: {targetDir}");
                Directory.CreateDirectory(targetDir);
            }

            string[] sourceFiles = Directory.GetFiles(sourceDir);
            string[] sourceDirectories = Directory.GetDirectories(sourceDir);

            foreach (string sourceFile in sourceFiles)
            {
                string fn = sourceFile[(sourceFile.LastIndexOf('\\') + 1)..];

                if (!File.Exists(targetDir + "\\" + fn))
                {
                    Log($"{fn} exists in source directory but not in target directory. Copying...");
                    File.Move(sourceFile, $@"{targetDir}\{fn}");
                }
            }

            foreach (string sourceDirectory in sourceDirectories)
            {
                string dirName = sourceDirectory[(sourceDirectory.LastIndexOf('\\') + 1)..];
                CopyDirectoryAdditive(sourceDirectory, targetDir + '\\' + dirName);
            }
        }

        static void Log(string message)
        {
            DateTime now = DateTime.Now;
            string msg = $"[{now.Year:0000}/{now.Month:00}/{now.Day:00} {now.Hour:00}:{now.Minute:00}:{now.Second:00}]{(isElevated ? " (Elevated)": "")} {message}\r\n";

            Console.Write(msg);
            File.AppendAllText($@"update.log", msg);
        }
    }
}
