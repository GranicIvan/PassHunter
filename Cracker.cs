using System.Collections.Concurrent;
using Aspose.Zip;
using System.Buffers;

namespace PassHunter
{
    class Cracker
    {

        // --- in-memory probe state ---
        private byte[]? _archiveBytes;              // entire archive kept in RAM
        //private string? _probeEntryName;            // name of smallest file entry we probe
        private int _probeIndex = -1;               // index of smallest file entry we probe

        [Obsolete] // Very slow, extracted to disk per guess.
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

            //long totalCombinations = (long)Math.Pow(passwordGen.possibleCharacters.Count, currentLength); // 5
            long totalCombinations = passwordGen.SpaceSize;


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

        public static bool TryPasswordsOfLengthParallelWEstimate(int currentLength, Options options, out string finalPassword)
        {
            finalPassword = null;

            // Prepare the fast in-memory probe once
            Cracker probe = new Cracker();
            probe.InitializeProbe(options.zipFilePath);

            // Compute keyspace exactly (no doubles)
            PasswordGeneratorOD passwordGenProbe = new PasswordGeneratorOD(currentLength, options);
            long total = passwordGenProbe.SpaceSize;

            // Chunk the range [0, total) into coarse blocks to reduce overhead
            const long targetChunk = 200_000; // tune 50k–500k
            long chunkSize = Math.Max(50_000, Math.Min(targetChunk, Math.Max(10_000, total / (Environment.ProcessorCount * 4))));
            OrderablePartitioner<Tuple<long, long>> ranges = Partitioner.Create(0L, total, chunkSize);

            using CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            // Accurate global progress (sum of all workers)
            long tried = 0;

            // Gate so only one thread prints per ~second
            System.Diagnostics.Stopwatch logSw = System.Diagnostics.Stopwatch.StartNew();
            int printGate = 0;

            ParallelOptions po = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            string? foundLocal = null;

            try
            {
                Parallel.ForEach(ranges, po, (Tuple<long, long> range, ParallelLoopState state) =>
                {
                    // Each worker has its own generator; jump to the start of its range
                    PasswordGeneratorOD gen = new PasswordGeneratorOD(currentLength, options);
                    gen.SetPositionFromLinearIndex(range.Item1);

                    int localTried = 0;

                    for (long i = range.Item1; i < range.Item2; i++)
                    {
                        if (token.IsCancellationRequested) break;

                        string pwd = gen.ToString();
                        if (probe.TryPasswordFast(pwd) && probe.TryPasswordFull(pwd))
                        {
                            // Capture once, then stop everyone
                            Interlocked.CompareExchange(ref foundLocal, pwd, null);
                            cts.Cancel();
                            state.Stop();
                            break;
                        }

                        gen.nextPassword();

                        // Count locally to reduce contention
                        localTried++;

                        // Flush to global every 4096 attempts
                        if ((localTried & 0xFFF) == 0) // 0xFFF == 4095
                        {
                            Interlocked.Add(ref tried, localTried);
                            localTried = 0;
                        }

                        // Time-gated progress print (~once per second total)
                        if (logSw.ElapsedMilliseconds >= 1000 &&
                            Interlocked.CompareExchange(ref printGate, 1, 0) == 0)
                        {
                            try
                            {
                                long doneNow = Volatile.Read(ref tried);
                                if (doneNow > total) doneNow = total;

                                double percent = (double)doneNow / total * 100.0;
                                double elapsedSec = options.watch.Elapsed.TotalSeconds;
                                double avg = doneNow > 0 ? elapsedSec / doneNow : 0.0;
                                double etaSec = (total - doneNow) * avg;

                                ConsolePrinter.LiveInfo(
                                    $"Progress: {doneNow:N0}/{total:N0} ({percent:F2}%) | Elapsed: {options.watch.Elapsed:hh\\:mm\\:ss} | ETA: {TimeSpan.FromSeconds(etaSec):hh\\:mm\\:ss}"
                                );

                                logSw.Restart();
                            }
                            finally
                            {
                                Volatile.Write(ref printGate, 0);
                            }
                        }
                    }

                    // Flush any remainder when the worker exits
                    if (localTried > 0)
                    {
                        Interlocked.Add(ref tried, localTried);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // normal on success cancel
            }

            if (foundLocal is null)
            {
                finalPassword = string.Empty;   // satisfy 'out' assignment
                return false;
            }

            finalPassword = foundLocal;
            return true;
        }



        private void InitializeProbe(string archivePath)
        {
            if (_archiveBytes != null) return; 

            // 1) Slurp archive into RAM
            using (FileStream fs = File.OpenRead(archivePath))
            {
                _archiveBytes = new byte[fs.Length];
                fs.ReadExactly(_archiveBytes);
            }


            /* OLD CODE
            // 2) Pick the smallest *file* entry (skip directories)
            using Archive preview = new Archive(new MemoryStream(_archiveBytes, writable: false));
            Aspose.Zip.ArchiveEntry? smallest = preview.Entries
                .Where(e => !e.IsDirectory)                 // avoid directories
                .OrderBy(e => e.CompressedSize)             // use compressed size
                .FirstOrDefault();

            if (smallest == null)
                throw new InvalidOperationException("Archive contains no file entries to probe.");

            _probeEntryName = smallest.Name;                // persist stable identifier
            
             */

            using Archive preview = new Archive(new MemoryStream(_archiveBytes, writable: false));
            var smallest = preview.Entries
                .Select((e, i) => new { e, i })
                .Where(x => !x.e.IsDirectory)
                .OrderBy(x => x.e.CompressedSize)
                .First(); // throws if archive has no files (good)

            _probeIndex = smallest.i;
        }


        private bool TryPasswordFast(string password)
        {
            // Precondition sanity
            if (_archiveBytes == null || _probeIndex < 0)
                throw new InvalidOperationException("Probe not initialized. Call InitializeProbe() first.");

            try
            {
                // Create a fresh stream over the same bytes (cheap)
                using MemoryStream ms = new MemoryStream(_archiveBytes, writable: false);

                // Open the archive with the candidate password
                using Archive a = new Archive(ms, new ArchiveLoadOptions { DecryptionPassword = password }); // :contentReference[oaicite:2]{index=2}

                // Locate the preselected smallest entry and open it *in memory*
                Aspose.Zip.ArchiveEntry entry = a.Entries[_probeIndex];



                using Stream s = entry.Open();
                Span<byte> buf = stackalloc byte[512];
                _ = s.Read(buf);

                // If we got here without an exception, password is correct
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch
            {
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


        private bool TryPasswordFull(string password)
        {
            if (_archiveBytes == null || _probeIndex == null)
                throw new InvalidOperationException("Probe not initialized.");

            try
            {
                using var ms = new MemoryStream(_archiveBytes, writable: false);
                using var a = new Aspose.Zip.Archive(ms, new Aspose.Zip.ArchiveLoadOptions { DecryptionPassword = password });

                var e = a.Entries[_probeIndex];

                using var s = e.Open();

                byte[] buf = System.Buffers.ArrayPool<byte>.Shared.Rent(4096); //Tweak buffer size as needed
                try
                {
                    while (s.Read(buf, 0, buf.Length) > 0) { /* drain */ }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buf);
                }
                return true;
            }
            catch (InvalidDataException) { return false; }
            catch { return false; }
        }

    }
}
