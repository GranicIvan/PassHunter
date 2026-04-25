using Aspose.Zip;
using PassHunter;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;


class Program
{

    static bool found = false;
    static int maxLength = -1;
    public static string foundPassword;

    static void Main(string[] args)
    {
        Options options = new Options();
        int startLength = 1;
        long startLinearIndex = 0;
        string? checkpointPath = null;

        // --resume <checkpoint.json> [newArchivePath]
        if (args.Length >= 2 && args[0].Equals("--resume", StringComparison.OrdinalIgnoreCase))
        {
            string cpFile = Path.GetFullPath(args[1]);
            if (!File.Exists(cpFile))
            {
                ConsolePrinter.Error("Checkpoint file not found: " + cpFile);
                return;
            }

            SessionCheckpoint cp;
            try
            {
                cp = SessionCheckpoint.Load(cpFile);
            }
            catch (Exception ex)
            {
                ConsolePrinter.Error("Failed to read checkpoint: " + ex.Message);
                return;
            }

            // Optional third argument overrides the archive path (in case it moved)
            string archivePath = args.Length >= 3
                ? Path.GetFullPath(args[2])
                : cp.ArchivePath;

            if (!File.Exists(archivePath))
            {
                ConsolePrinter.Error("Archive file not found: " + archivePath + ". Provide the new path as a third argument.");
                return;
            }

            options.zipFilePath     = archivePath;
            options.outputDirectory = cp.OutputDirectory;
            options.number          = cp.Number;
            options.lowercase       = cp.Lowercase;
            options.uppercase       = cp.Uppercase;
            options.special         = cp.Special;
            options.PreviousElapsed = TimeSpan.FromSeconds(cp.TotalElapsedSeconds);
            maxLength               = cp.MaxLength;
            startLength             = cp.CurrentLength;
            startLinearIndex        = cp.LastTriedLinearIndex;
            checkpointPath          = cpFile;

            options.DetectArchiveType();
            options.BuildCharSet();

            ConsolePrinter.SetUpInfo("Resuming from checkpoint: " + cpFile);
            ConsolePrinter.SetUpInfo("Archive: " + archivePath);
            ConsolePrinter.SetUpInfo($"Resuming at length {startLength}, index {startLinearIndex:N0}");
            ConsolePrinter.SetUpInfo($"Previous elapsed time: {options.PreviousElapsed:hh\\:mm\\:ss}");
        }
        else if (args.Length < 3)
        {
            // Special case: --fasttest|-ft [directory]
            if (args.Length >= 1 && (args[0].Equals("--fasttest", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-ft", StringComparison.OrdinalIgnoreCase)))
            {
                string testDir = args.Length == 2
                    ? Path.GetFullPath(args[1])
                    : Path.Combine(AppContext.BaseDirectory, "testFiles", "FastTest");
                FastTestRunner.Run(testDir);
                return;
            }

            ConsolePrinter.printHelp();
            return;
        }
        else
        {
            try
            {
                maxLength = Convert.ToInt32(args[0]);
                if (maxLength < 1)
                {
                    ConsolePrinter.Error("Max length of password must be greater than 0");
                    return;
                }
                ConsolePrinter.SetUpInfo("Max length of password is: " + maxLength);
            }
            catch (Exception ex)
            {
                ConsolePrinter.Error("First argument must be number, length of password \nError: " + ex.Message);
                return;
            }

            options.zipFilePath     = Path.GetFullPath(args[1]);
            options.outputDirectory = Path.GetFullPath(args[2]);

            if (!File.Exists(options.zipFilePath))
            {
                ConsolePrinter.Warning("Archive file not found. Use / or \\\\ or \"\\\"");
                return;
            }

            options.DetectArchiveType();
            ConsolePrinter.SetUpInfo("Detected archive type: " + options.archiveType);

            options.setOptions(args);

            checkpointPath = SessionCheckpoint.GetDefaultPath(options.zipFilePath);
        }

        ConsolePrinter.SetUpInfo("Options: numbers:" + options.number + ",  a-z:" + options.lowercase + ",  A-Z:" + options.uppercase + ",  special:" + options.special);
        ConsolePrinter.SetUpInfo("Checkpoint will be saved to: " + checkpointPath);

        // Shared checkpoint state - tracks per-worker positions for safe resume index
        CheckpointState cpState = new CheckpointState();

        // Ctrl+C handler: save checkpoint then exit
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent immediate kill; let us save first

            options.watch.Stop();
            TimeSpan totalElapsed = options.PreviousElapsed + options.watch.Elapsed;
            long safeIndex = cpState.GetSafeResumeIndex();

            var checkpoint = new SessionCheckpoint
            {
                ArchivePath          = options.zipFilePath,
                OutputDirectory      = options.outputDirectory,
                MaxLength            = maxLength,
                Number               = options.number,
                Lowercase            = options.lowercase,
                Uppercase            = options.uppercase,
                Special              = options.special,
                CurrentLength        = cpState.CurrentLength,
                LastTriedLinearIndex = safeIndex,
                TotalElapsedSeconds  = totalElapsed.TotalSeconds,
            };

            try
            {
                checkpoint.Save(checkpointPath!);
                ConsolePrinter.SetUpInfo($"Checkpoint saved to: {checkpointPath}");
                ConsolePrinter.SetUpInfo($"Stopped at length {checkpoint.CurrentLength}, index {safeIndex:N0}. Total elapsed: {totalElapsed:hh\\:mm\\:ss}");
            }
            catch (Exception ex)
            {
                ConsolePrinter.Error("Failed to save checkpoint: " + ex.Message);
            }

            Environment.Exit(0);
        };

        options.startWatch();

        for (int i = startLength; i <= maxLength; i++)
        {
            cpState.CurrentLength = i;
            ConsolePrinter.Info("Trying length: " + i);

            long indexForThisLength = (i == startLength) ? startLinearIndex : 0;

            if (Cracker.TryPasswordsOfLengthParallelWEstimate(i, options, out foundPassword, indexForThisLength, cpState))
            {
                found = true;
                break;
            }
        }

        options.watch.Stop();
        TimeSpan elapsed = options.PreviousElapsed + options.watch.Elapsed;

        ConsolePrinter.SetUpInfo($"Program is DONE, and took: {elapsed.Days}d {elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s {elapsed.Milliseconds}ms");
        if (found)
        {
            ConsolePrinter.Success($"Password found: \"{foundPassword}\"");

            // Delete checkpoint since it is no longer needed
            if (checkpointPath != null && File.Exists(checkpointPath))
            {
                try { File.Delete(checkpointPath); } catch { /* best effort */ }
            }

            ConsolePrinter.SetUpInfo($"Extracting files...");
            Cracker.ExtractOnce(options.zipFilePath, options.outputDirectory, foundPassword, options.archiveType);
        }
        else
        {
            ConsolePrinter.Error("Failed to find password, file is still LOCKED.");
        }
    }
}