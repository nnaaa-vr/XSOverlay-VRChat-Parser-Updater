using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace XSOverlay_VRChat_Parser_Updater
{
    class Program
    {
        private static bool hasApplicationMutex = false;
        private static Mutex applicationMutex;

        static void Main(string[] args)
        {
            // Args: (sourceDir) (targetDir)

            Log($"Updater initialized with {args.Length} arguments.");

            foreach (string arg in args)
                Log($"Argument: {arg}");

            if (args.Length != 2)
            {
                Log("Unexpected number of arguments. Aborting.");
                return;
            }

            Log($"Waiting for XSOverlay VRChat Parser to close...");

            try
            {
                applicationMutex = new Mutex(true, "XSOVRCParser", out hasApplicationMutex);
                applicationMutex.WaitOne();
            }
            catch (Exception ex)
            {
                Log("Failed to obtain exclusivity. Is the parser still running?");
                Log(ex.Message);
                Console.ReadLine();
                return;
            }

            string sourceDir = args[0];
            string targetDir = args[1];

            Log("Checking source directory exists...");
            if (!Directory.Exists(sourceDir))
            {
                Log("Source directory could not be found. Aborting.");
                ReleaseMutex();
                return;
            }

            Log("Checking target directory exists...");
            if (!Directory.Exists(targetDir))
            {
                Log("Target directory could not be found. Aborting.");
                ReleaseMutex();
                return;
            }

            Log("Validating that target directory is writable...");
            try
            {
                // I was going to use System.Security.AccessControl.DirectorySecurity here and check ACLs, but it's much faster and easier to just, well, try to write something

                File.WriteAllBytes($@"{targetDir}\.writable", new byte[1] { 0x01 });
                File.Delete($@"{targetDir}\.writable");
            }
            catch (Exception ex)
            {
                Log("Failed to write to target directory. Aborting.");
                Log(ex.Message);
                ReleaseMutex();
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
                ReleaseMutex();
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
                    Log($"Removing target directory directory: {dir}");
                    Directory.Delete(dir, true);
                }
                foreach (string fn in targetFiles)
                {
                    Log($"Removing target directory file: {fn}");
                    File.Delete(fn);
                }

                foreach (string fn in sourceFiles)
                {
                    Log($"Moving source directory file to target directory: {fn}");
                    File.Move(fn, $@"{targetDir}\{fn[(fn.LastIndexOf('\\') + 1)..]}");
                }
                foreach (string dir in sourceDirectories)
                {
                    Log($"Moving source directory to target directory: {dir}");
                    Directory.Move(dir, $@"{targetDir}\{dir[(dir.LastIndexOf('\\') + 1)..]}");
                }

                moveSuccess = true;
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                Log($"Failed to clean and copy source and target directories. Please send the log file generated at {Assembly.GetExecutingAssembly().Location}\\update.log to the developer!");
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
            ReleaseMutex();
        }

        static void ReleaseMutex()
        {
            if (hasApplicationMutex)
                applicationMutex.ReleaseMutex();
        }

        static void CopyDirectoryAdditive(string sourceDir, string targetDir)
        {
            Log($"CopyDirectoryAdditive called for source directory {sourceDir} and target directory {targetDir}");

            if(!Directory.Exists(targetDir))
            {
                Log($"Directory didn't exist in target: {targetDir}");
                Directory.CreateDirectory(targetDir);
            }

            string[] sourceFiles = Directory.GetFiles(sourceDir);
            string[] sourceDirectories = Directory.GetDirectories(sourceDir);

            foreach (string sourceFile in sourceFiles)
            {
                string fn = sourceFile[(sourceFile.LastIndexOf('\\') + 1)..];

                if(!File.Exists(targetDir + "\\" + fn))
                {
                    Log($"{fn} exists in source directory but not in target directory. Copying...");
                    File.Move(sourceFile, $@"{targetDir}\{fn}");
                }
            }

            foreach(string sourceDirectory in sourceDirectories)
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
