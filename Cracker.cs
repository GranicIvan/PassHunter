using Aspose.Zip;

namespace PassHunter
{
    class Cracker
    {

        // --- in-memory probe state ---
        private byte[]? _archiveBytes;              // entire archive kept in RAM
        private string? _probeEntryName;            // name of smallest file entry we probe
        private readonly byte[] _probeBuffer = new byte[256]; // single reusable read buffer

        // [Depricated] Very slow, extracted to disk per guess.
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

            var probe = new Cracker();
            probe.InitializeProbe(options.zipFilePath);

            var passwordGen = new PasswordGeneratorOD(currentLength, options);
            long totalCombinations = (long)Math.Pow(passwordGen.possibleCharacters.Count, currentLength);

            for (long i = 0; i < totalCombinations; i++)
            {
                string password = passwordGen.ToString();

                // FAST path: open smallest entry in memory and read a tiny buffer
                if (probe.TryPasswordFast(password))
                {
                    finalPassword = password;
                    return true;
                }

                passwordGen.nextPassword();
            }

            return false;
        }

        public static bool TryPasswordsOfLengthWEstimate(int currentLength, Options options, out string finalPassword)
        {

            finalPassword = null;


            var probe = new Cracker();
            probe.InitializeProbe(options.zipFilePath);



            //PasswordGenerator passwordGen = new PasswordGenerator(currentLength, options);
            PasswordGeneratorOD passwordGen = new PasswordGeneratorOD(currentLength, options);

            long totalCombinations = (long)Math.Pow(passwordGen.possibleCharacters.Count, currentLength);

            for (long i = 0; i < totalCombinations; i++)
            {

                string password = passwordGen.ToString();

                if (probe.TryPasswordFast(password))
                {
                    finalPassword = password;
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


        private void InitializeProbe(string archivePath)
        {
            if (_archiveBytes != null) return;

            // 1) Slurp archive into RAM
            using (var fs = File.OpenRead(archivePath))
            {
                _archiveBytes = new byte[fs.Length];
                fs.ReadExactly(_archiveBytes);
            }

            // 2) Pick the smallest *file* entry (skip directories)
            using var preview = new Archive(new MemoryStream(_archiveBytes, writable: false));
            var smallest = preview.Entries
                .Where(e => !e.IsDirectory)                 // avoid directories
                .OrderBy(e => e.CompressedSize)             // use compressed size
                .FirstOrDefault();

            if (smallest == null)
                throw new InvalidOperationException("Archive contains no file entries to probe.");

            _probeEntryName = smallest.Name;                // persist stable identifier
        }


        private bool TryPasswordFast(string password)
        {
            // Precondition sanity
            if (_archiveBytes == null || _probeEntryName == null)
                throw new InvalidOperationException("Probe not initialized. Call InitializeProbe() first.");

            try
            {
                // Create a fresh stream over the same bytes (cheap)
                using var ms = new MemoryStream(_archiveBytes, writable: false);

                // Open the archive with the candidate password
                using var a = new Archive(ms, new ArchiveLoadOptions { DecryptionPassword = password }); // :contentReference[oaicite:2]{index=2}

                // Locate the preselected smallest entry and open it *in memory*
                var entry = a.Entries.First(e => e.Name == _probeEntryName);

                // Read a tiny chunk to force decryption; wrong pwd => exception
                using var s = entry.Open();        // stream with decompressed contents, no disk I/O :contentReference[oaicite:3]{index=3}
                _ = s.Read(_probeBuffer, 0, _probeBuffer.Length);

                // If we got here without an exception, password is correct
                return true;
            }
            catch (InvalidDataException)
            {
                // Wrong password (or corrupted archive) => keep brute-forcing
                // Aspose docs state wrong passwords raise InvalidDataException on extraction/open. :contentReference[oaicite:4]{index=4}
                return false;
            }
        }

        public static void ExtractOnce(string zipFilePath, string outputDirectory, string password)
        {
            var load = new ArchiveLoadOptions { DecryptionPassword = password };
            using var archive = new Archive(zipFilePath, load);
            Directory.CreateDirectory(outputDirectory);
            archive.ExtractToDirectory(outputDirectory);
        }

    }
}