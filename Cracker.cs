using System.Collections.Concurrent;
using Aspose.Zip;
using System.Buffers;
using SharpCompress.Common;
using SharpCompress.Readers;
using ScRar = SharpCompress.Archives.Rar.RarArchive;

namespace PassHunter
{
    class Cracker
    {

        // --- in-memory probe state (ZIP) ---
        private byte[]? _archiveBytes;              // entire archive kept in RAM
        private int _probeIndex = -1;               // index of smallest file entry we probe

        // --- in-memory probe state (RAR) ---
        private byte[]? _rarBytes;
        private int _rarProbeIndex = -1;
        private bool _rarHeadersEncrypted = false;



        public static bool TryPasswordsOfLengthParallelWEstimate(int currentLength, Options options, out string finalPassword, long startLinearIndex = 0, CheckpointState? checkpointState = null)
        {
            finalPassword = null;

            // Load archive bytes + pick probe index ONCE on the main thread.
            // The raw bytes are then shared read-only; each worker gets its OWN Cracker
            // so RarArchive/Archive objects are never shared - eliminates internal locking.
            Cracker master = new Cracker();
            if (options.archiveType == ArchiveType.Rar)
                master.InitializeRarProbe(options.zipFilePath);
            else
                master.InitializeProbe(options.zipFilePath);

            // Compute keyspace exactly (no doubles)
            PasswordGenerator passwordGenProbe = new PasswordGenerator(currentLength, options);
            long total = passwordGenProbe.SpaceSize;

            // Chunk the range [0, total) into coarse blocks to reduce overhead.
            // RAR attempts are slow (~ms each due to crypto), so tiny chunks are fine and
            // necessary to create enough work items to keep all cores busy.
            // ZIP attempts are fast (~µs), so a larger minimum avoids partitioner overhead.
            long chunkSize;
            if (options.archiveType == ArchiveType.Rar)
                chunkSize = Math.Max(1, total / (Environment.ProcessorCount * 8));
            else
                chunkSize = Math.Max(50_000, Math.Min(200_000, Math.Max(10_000, total / (Environment.ProcessorCount * 4))));
            OrderablePartitioner<Tuple<long, long>> ranges = Partitioner.Create(startLinearIndex, total, chunkSize);

            if (checkpointState != null)
            {
                checkpointState.CurrentLength = currentLength;
                checkpointState.Reset(Environment.ProcessorCount);
            }

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
                    // Each worker gets its OWN Cracker - critical for RAR: RarArchive has
                    // internal locking so sharing one instance across threads serialises all
                    // workers down to ~2. BorrowXxxProbe shares the bytes reference (no copy).
                    Cracker probe = new Cracker();
                    if (options.archiveType == ArchiveType.Rar)
                        probe.BorrowRarProbe(master._rarBytes!, master._rarProbeIndex, master._rarHeadersEncrypted);
                    else
                        probe.BorrowZipProbe(master._archiveBytes!, master._probeIndex);

                    // Each worker has its own generator; jump to the start of its range
                    PasswordGenerator gen = new PasswordGenerator(currentLength, options);
                    gen.SetPositionFromLinearIndex(range.Item1);

                    int localTried = 0;

                    for (long i = range.Item1; i < range.Item2; i++)
                    {
                        if (token.IsCancellationRequested) break;

                        // Report position to checkpoint tracker every 4096 attempts
                        if ((i & 0xFFF) == 0)
                            checkpointState?.UpdateCurrentIndex(i);

                        string pwd = gen.ToString();
                        bool matched = options.archiveType == ArchiveType.Rar
                            ? probe.TryRarPassword(pwd)
                            : (probe.TryPasswordFast(pwd) && probe.TryPasswordFull(pwd));
                        if (matched)
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
                                TimeSpan elapsed = options.PreviousElapsed + options.watch.Elapsed;
                                double elapsedSec = elapsed.TotalSeconds;
                                double avg = doneNow > 0 ? elapsedSec / doneNow : 0.0;
                                double etaSec = (total - doneNow) * avg;

                                ConsolePrinter.LiveInfo(
                                    $"Progress: {doneNow:N0}/{total:N0} ({percent:F2}%) | Elapsed: {elapsed:hh\\:mm\\:ss} | ETA: {TimeSpan.FromSeconds(etaSec):hh\\:mm\\:ss}"
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

                    checkpointState?.ReleaseSlot();
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

        /// <summary>
        /// Lets a worker thread reuse the ZIP bytes already loaded by the master Cracker.
        /// No file I/O, no memory copy - just a reference share (bytes are read-only).
        /// </summary>
        internal void BorrowZipProbe(byte[] bytes, int probeIndex)
        {
            _archiveBytes = bytes;
            _probeIndex   = probeIndex;
        }

        /// <summary>
        /// Lets a worker thread reuse the RAR bytes already loaded by the master Cracker.
        /// No file I/O, no memory copy - just a reference share (bytes are read-only).
        /// </summary>
        internal void BorrowRarProbe(byte[] bytes, int probeIndex, bool headersEncrypted)
        {
            _rarBytes            = bytes;
            _rarProbeIndex       = probeIndex;
            _rarHeadersEncrypted = headersEncrypted;
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

        public static void ExtractOnce(string zipFilePath, string outputDirectory, string password, ArchiveType archiveType = ArchiveType.Zip)
        {
            Directory.CreateDirectory(outputDirectory);
            if (archiveType == ArchiveType.Rar)
            {
                var load = new Aspose.Zip.Rar.RarArchiveLoadOptions { DecryptionPassword = password };
                using var archive = new Aspose.Zip.Rar.RarArchive(zipFilePath, load);
                archive.ExtractToDirectory(outputDirectory);
            }
            else
            {
                var load = new ArchiveLoadOptions { DecryptionPassword = password };
                using var archive = new Archive(zipFilePath, load);
                archive.ExtractToDirectory(outputDirectory);
            }
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

        // ----------------------------------------------------------------
        // RAR support
        // ----------------------------------------------------------------

        private void InitializeRarProbe(string archivePath)
        {
            if (_rarBytes != null) return;

            using (FileStream fs = File.OpenRead(archivePath))
            {
                _rarBytes = new byte[fs.Length];
                fs.ReadExactly(_rarBytes);
            }

            // Try to enumerate entries without a password.
            // If the archive uses header encryption (filenames are encrypted too),
            // SharpCompress throws CryptographicException before we can read any entry.
            // In that case we set a flag and defer probing until we have a password candidate.
            try
            {
                using var ms = new MemoryStream(_rarBytes, writable: false);
                using var preview = ScRar.OpenArchive(ms);
                var smallest = preview.Entries
                    .Select((e, i) => new { e, i })
                    .Where(x => !x.e.IsDirectory)
                    .OrderBy(x => x.e.CompressedSize)
                    .First();

                _rarProbeIndex = smallest.i;
            }
            catch (SharpCompress.Common.CryptographicException)
            {
                _rarHeadersEncrypted = true;
                ConsolePrinter.Warning("RAR archive has encrypted headers (filenames hidden). Will probe using password to decrypt headers.");
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                _rarHeadersEncrypted = true;
                ConsolePrinter.Warning("RAR archive has encrypted headers (filenames hidden). Will probe using password to decrypt headers.");
            }
        }

        private bool TryRarPassword(string password)
        {
            if (_rarBytes == null)
                throw new InvalidOperationException("RAR probe not initialized. Call InitializeRarProbe() first.");
            if (!_rarHeadersEncrypted && _rarProbeIndex < 0)
                throw new InvalidOperationException("RAR probe not initialized. Call InitializeRarProbe() first.");

            try
            {
                using var ms = new MemoryStream(_rarBytes, writable: false);
                var opts = new ReaderOptions { Password = password, LeaveStreamOpen = false };
                using var archive = ScRar.OpenArchive(ms, opts);

                // For header-encrypted archives the password is needed just to read entry list.
                // We take the first available file entry and drain it to confirm the password.
                var entry = _rarHeadersEncrypted
                    ? archive.Entries.Where(e => !e.IsDirectory).First()
                    : archive.Entries.Where(e => !e.IsDirectory).ElementAt(_rarProbeIndex);

                using var entryStream = entry.OpenEntryStream();
                byte[] buf = ArrayPool<byte>.Shared.Rent(4096);
                try
                {
                    while (entryStream.Read(buf, 0, buf.Length) > 0) { /* drain */ }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
                return true;
            }
            catch (System.Security.Cryptography.CryptographicException) { return false; }
            catch (InvalidFormatException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch { return false; }
        }

    }
}
