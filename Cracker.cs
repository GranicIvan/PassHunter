using Aspose.Zip;

namespace PassHunter
{
    class Cracker
    {

        // --- in-memory probe state ---
        private byte[]? _archiveBytes;              // entire archive kept in RAM
        private string? _probeEntryName;            // name of smallest file entry we probe
        private readonly byte[] _probeBuffer = new byte[256]; // single reusable read buffer

        public static bool Extraction(string zipFilePath, string outputDirectory, string password, out string foundPassword)
        {
            foundPassword = null;

            //Console.WriteLine("Trying password: " + password);

            // Set up the decryption options with the provided password
            var options = new ArchiveLoadOptions
            {
                DecryptionPassword = password
            };

            // Open the ZIP archive with the specified options
            try
            {
                using (var archive = new Archive(zipFilePath, options))
                {
                    // Ensure the output directory exists
                    Directory.CreateDirectory(outputDirectory);

                    // Extract all contents to the output directory
                    archive.ExtractToDirectory(outputDirectory);

                    foundPassword = password;
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static bool TryPasswordsOfLength(int currentLength, Options options, out string finalPassword)
        {

            finalPassword = null;

            //PasswordGenerator passwordGen = new PasswordGenerator(currentLength, options);
            PasswordGeneratorOD passwordGen = new PasswordGeneratorOD(currentLength, options);

            long totalCombinations = (long)Math.Pow(passwordGen.possibleCharacters.Count, currentLength);

            for (long i = 0; i < totalCombinations; i++)
            {

                string password = passwordGen.ToString();

                if (Cracker.Extraction(options.zipFilePath, options.outputDirectory, password, out finalPassword))
                {
                    return true;
                }

                passwordGen.nextPassword();
            }

            return false;
        }

        public static bool TryPasswordsOfLengthWEstimate(int currentLength, Options options, out string finalPassword)
        {

            finalPassword = null;

            //PasswordGenerator passwordGen = new PasswordGenerator(currentLength, options);
            PasswordGeneratorOD passwordGen = new PasswordGeneratorOD(currentLength, options);

            long totalCombinations = (long)Math.Pow(passwordGen.possibleCharacters.Count, currentLength);

            for (long i = 0; i < totalCombinations; i++)
            {

                string password = passwordGen.ToString();

                if (Cracker.Extraction(options.zipFilePath, options.outputDirectory, password, out finalPassword))
                {
                    return true;
                }

                passwordGen.nextPassword();

                if (i % 1000 == 0 && i > 0)
                {
                    double elapsedSeconds = options.watch.Elapsed.TotalSeconds;
                    double percent = (double)i / totalCombinations * 100;
                    double avgTimePerTry = elapsedSeconds / i;
                    double etaSeconds = (totalCombinations - i) * avgTimePerTry;

                    TimeSpan eta = TimeSpan.FromSeconds(etaSeconds);
                    TimeSpan elapsed = options.watch.Elapsed;

                    ConsolePrinter.LiveInfo(
                        $"Progress: {i:N0} / {totalCombinations:N0} ({percent:F2}%) | Elapsed: {elapsed:hh\\:mm\\:ss} | ETA: {eta:hh\\:mm\\:ss}"
                    );
                }
            }

            return false;
        }

    }
}
