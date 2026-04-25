using System.Diagnostics;

namespace PassHunter
{
    static class FastTestRunner
    {
        // Parses one line from FastTestPassworsd.md
        // Format: filename, password, flags (space-separated), length
        // e.g.:  test1.zip, c3a, n l, 3
        private record TestCase(string FileName, string ExpectedPassword, string[] Flags, int MaxLength);

        public static void Run(string testDirectory)
        {
            string mdPath = Path.Combine(testDirectory, "FastTestPassworsd.md");
            if (!File.Exists(mdPath))
            {
                ConsolePrinter.Error($"Test definition file not found: {mdPath}");
                return;
            }

            var cases = ParseMd(mdPath);
            if (cases.Count == 0)
            {
                ConsolePrinter.Error("No test cases found in the file.");
                return;
            }

            ConsolePrinter.SetUpInfo($"Found {cases.Count} test case(s) in {mdPath}");
            Console.WriteLine();

            int passed = 0;
            int failed = 0;
            var totalSw = Stopwatch.StartNew();

            foreach (var tc in cases)
            {
                string archivePath = Path.Combine(testDirectory, tc.FileName);
                if (!File.Exists(archivePath))
                {
                    ConsolePrinter.Warning($"[{tc.FileName}] File not found, skipping.");
                    failed++;
                    continue;
                }

                // Build options exactly as the main program does
                Options options = new Options();
                options.zipFilePath = archivePath;
                options.outputDirectory = Path.Combine(testDirectory, "output", Path.GetFileNameWithoutExtension(tc.FileName));
                options.DetectArchiveType();

                // Apply only the flags specified in the test file
                options.number    = tc.Flags.Contains("n");
                options.lowercase = tc.Flags.Contains("l");
                options.uppercase = tc.Flags.Contains("u");
                options.special   = tc.Flags.Contains("s");
                options.BuildCharSet();

                ConsolePrinter.Info($"[{tc.FileName}] Starting | maxLen={tc.MaxLength} | flags=[{string.Join(" ", tc.Flags.Select(f => "-" + f))}] | expected=\"{tc.ExpectedPassword}\"");

                var caseSw = Stopwatch.StartNew();
                options.watch = new Stopwatch();
                options.startWatch();

                string foundPassword = string.Empty;
                bool found = false;

                try
                {
                    for (int len = 1; len <= tc.MaxLength; len++)
                    {
                        if (Cracker.TryPasswordsOfLengthParallelWEstimate(len, options, out foundPassword))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    caseSw.Stop();
                    options.watch.Stop();
                    ConsolePrinter.Error($"[{tc.FileName}] ERROR | {ex.GetType().Name}: {ex.Message}");
                    failed++;
                    Console.WriteLine();
                    continue;
                }

                caseSw.Stop();
                options.watch.Stop();

                string timeStr = FormatElapsed(caseSw.Elapsed);

                if (found && foundPassword == tc.ExpectedPassword)
                {
                    ConsolePrinter.Success($"[{tc.FileName}] PASS | found=\"{foundPassword}\" | time={timeStr}");
                    passed++;
                }
                else if (found && foundPassword != tc.ExpectedPassword)
                {
                    ConsolePrinter.Error($"[{tc.FileName}] WRONG PASSWORD | got=\"{foundPassword}\" expected=\"{tc.ExpectedPassword}\" | time={timeStr}");
                    failed++;
                }
                else
                {
                    ConsolePrinter.Error($"[{tc.FileName}] NOT FOUND | expected=\"{tc.ExpectedPassword}\" | time={timeStr}");
                    failed++;
                }

                Console.WriteLine();
            }

            totalSw.Stop();

            Console.WriteLine(new string('─', 60));
            ConsolePrinter.SetUpInfo($"Results: {passed} passed, {failed} failed out of {cases.Count} total");
            ConsolePrinter.SetUpInfo($"Total time: {FormatElapsed(totalSw.Elapsed)}");

            if (failed == 0)
                ConsolePrinter.Success("PASS");
            else if (passed == 0)
                ConsolePrinter.Error("FAILED");
            else
                ConsolePrinter.Warning("PARTIALLY FAILED");
        }

        private static List<TestCase> ParseMd(string path)
        {
            var result = new List<TestCase>();

            foreach (string rawLine in File.ReadAllLines(path))
            {
                // Skip empty lines and markdown headings/comments
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                // Split on comma — format: filename, password, flags, maxLength
                string[] parts = line.Split(',');
                if (parts.Length < 4)
                    continue;

                string fileName       = parts[0].Trim();
                string expectedPwd    = parts[1].Trim();
                string[] flags        = parts[2].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string rawLen         = parts[3].Trim();

                if (!int.TryParse(rawLen, out int maxLen) || maxLen < 1)
                    continue;

                result.Add(new TestCase(fileName, expectedPwd, flags, maxLen));
            }

            return result;
        }

        private static string FormatElapsed(TimeSpan t) =>
            $"{t.Hours:D2}h {t.Minutes:D2}m {t.Seconds:D2}s {t.Milliseconds:D3}ms";
    }
}
