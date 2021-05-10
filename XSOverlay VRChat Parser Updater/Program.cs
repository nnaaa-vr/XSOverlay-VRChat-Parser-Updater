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
        static void Main(string[] args)
        {
            // Args: (sourceDir) (targetDir)

            Log($"Updater initialized with {args.Length} arguments.");

            foreach (string arg in args)
                Log($"Argument: {arg}");

            if (args.Length != 3)
            {
                Log("Unexpected number of arguments. Aborting.");
                return;
            }

            Log($"Waiting for XSOverlay VRChat Parser to close...");

            while (true)
            {
                try
                {
                    Task.Delay(100).GetAwaiter().GetResult();
                    Process p = Process.GetProcessById(int.Parse(args[2]));
                    Log("XSOverlay VRChat Parser process is still running. Waiting for exit...");
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

            string sourceDir = args[0];
            string targetDir = args[1];

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

            int retryMax = 10;

            Log("Validating that target directory is writable...");
            bool writeSuccess = false;
            for (int i = 0; i < retryMax; i++)
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
                    Log($"Failed to write to target directory: attempt ({i+1}) of ({retryMax}).");
                } 
            }

            if (!writeSuccess)
            {
                Log("Aborting.");
                return;
            }

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
                    for (int i = 0; i < retryMax; i++)
                    {
                        try
                        {

                            Log($"Attempt ({i+1}) of ({retryMax}) to remove target directory: {dir}");
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
                    for (int i = 0; i < retryMax; i++)
                    {
                        try
                        {
                            Log($"Attempt ({i+1}) of ({retryMax}) to remove target file: {fn}");
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
                    for (int i = 0; i < retryMax; i++)
                    {
                        try
                        {
                            Log($"Attempt ({i+1}) of ({retryMax}) to move file to target directory: {fn}");
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
                    for (int i = 0; i < retryMax; i++)
                    {
                        try
                        {
                            Log($"Attempt ({i+1}) of ({retryMax}) to move directory to target directory: {dir}");
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
            {
                Log($"Successfully updated binaries and resources in target directory. Starting...");

                ProcessStartInfo parserInfo = new ProcessStartInfo()
                {
                    FileName = $@"{targetDir}\XSOverlay VRChat Parser.exe",
                    UseShellExecute = true,
                    RedirectStandardOutput = false,
                    WorkingDirectory = targetDir
                };

                Process.Start(parserInfo);
            }

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
            string msg = $"[{now.Year:0000}/{now.Month:00}/{now.Day:00} {now.Hour:00}:{now.Minute:00}:{now.Second:00}] {message}\r\n";

            Console.Write(msg);
            File.AppendAllText($@"update.log", msg);
        }
    }
}
