using System.Text.Json;

namespace PassHunter
{
    class SessionCheckpoint
    {
        public string ArchivePath { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
        public int MaxLength { get; set; }
        public bool Number { get; set; }
        public bool Lowercase { get; set; }
        public bool Uppercase { get; set; }
        public bool Special { get; set; }
        public int CurrentLength { get; set; }
        public long LastTriedLinearIndex { get; set; }
        public double TotalElapsedSeconds { get; set; }
        public DateTime SavedAt { get; set; }

        private static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        public void Save(string filePath)
        {
            SavedAt = DateTime.Now;
            File.WriteAllText(filePath, JsonSerializer.Serialize(this, _jsonOpts));
        }

        public static SessionCheckpoint Load(string filePath)
        {
            return JsonSerializer.Deserialize<SessionCheckpoint>(File.ReadAllText(filePath))
                ?? throw new InvalidOperationException("Checkpoint file is empty or invalid.");
        }

        public static string GetDefaultPath(string archivePath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
            string name = Path.GetFileNameWithoutExtension(archivePath);
            return Path.Combine(dir, name + ".checkpoint.json");
        }
    }

    // Tracks the minimum linear index safely attempted across all parallel workers.
    // Each worker thread claims one slot (via ThreadLocal) and updates it periodically.
    // On Ctrl+C, GetSafeResumeIndex() returns the minimum value across all slots,
    // which is the earliest index guaranteed not to have been skipped.
    class CheckpointState
    {
        private long[] _slots = Array.Empty<long>();
        private ThreadLocal<int>? _threadSlot;
        private int _slotCounter = -1;

        public volatile int CurrentLength;

        public void Reset(int workerCount)
        {
            _slots = new long[Math.Max(workerCount, 1)];
            Array.Fill(_slots, long.MaxValue);
            _slotCounter = -1;

            _threadSlot?.Dispose();
            _threadSlot = new ThreadLocal<int>(() =>
                Interlocked.Increment(ref _slotCounter) % _slots.Length
            );
        }

        // Called by each worker periodically to record its current position.
        public void UpdateCurrentIndex(long index)
        {
            if (_threadSlot == null) return;
            Volatile.Write(ref _slots[_threadSlot.Value], index);
        }

        // Called by a worker when its chunk is fully done so it does not
        // hold back the safe resume index after completing its range.
        public void ReleaseSlot()
        {
            if (_threadSlot == null) return;
            Volatile.Write(ref _slots[_threadSlot.Value], long.MaxValue);
        }

        // Returns the lowest index any worker is currently processing.
        // Resuming from this index is safe: no password is ever skipped.
        public long GetSafeResumeIndex()
        {
            long min = long.MaxValue;
            for (int i = 0; i < _slots.Length; i++)
            {
                long v = Volatile.Read(ref _slots[i]);
                if (v < min) min = v;
            }
            return min == long.MaxValue ? 0 : min;
        }
    }
}
