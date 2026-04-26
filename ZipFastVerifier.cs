using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace PassHunter
{
    // Which encryption scheme the probed entry uses.
    internal enum ZipEncryptionKind { Unknown, ZipCrypto, WinZipAes }

    // All data extracted from the ZIP binary that is needed for the fast header check.
    // Populated once by ZipFastVerifier.ParseProbeEntry(); shared read-only across threads.
    internal sealed class ZipProbeData
    {
        public ZipEncryptionKind Kind { get; init; }

        // --- ZipCrypto fields ---
        // The 12-byte traditional encryption header stored at the start of the entry's data.
        public byte[]? EncryptionHeader12 { get; init; }
        // The check byte expected at position [11] after decrypting the header with the password.
        // For entries with a data descriptor (GP bit 3 set) this is MSB of last-mod-time;
        // otherwise it is the high byte of the CRC-32 stored in the local file header.
        public byte CheckByte { get; init; }

        // --- WinZip AES fields ---
        // PBKDF2 salt (8, 12, or 16 bytes depending on AES key strength).
        public byte[]? AesSalt { get; init; }
        // The 2-byte password-verification value stored immediately after the salt.
        public byte[]? AesPwdVerifier2 { get; init; }
        // AES key length in bits: 128, 192, or 256.
        public int AesKeyBits { get; init; }
    }

    /// <summary>
    /// Parses the raw ZIP binary once to extract crypto metadata from the probe entry,
    /// and provides fast per-password header-only checks without touching Aspose.Zip.
    ///
    /// ZipCrypto  : ~0.1 us per attempt; rejects ~255/256 wrong passwords instantly.
    /// WinZip AES : PBKDF2 + 2-byte verifier; rejects ~65535/65536 wrong passwords.
    /// </summary>
    internal static class ZipFastVerifier
    {
        private const uint LocalFileSig = 0x04034B50;

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Scans <paramref name="zipBytes"/> and returns crypto metadata for the
        /// entry at <paramref name="entryIndex"/> (0-based order of local file headers).
        /// Returns null if the entry is unencrypted or uses an unsupported scheme.
        /// </summary>
        public static ZipProbeData? ParseProbeEntry(byte[] zipBytes, int entryIndex)
        {
            int pos   = 0;
            int found = 0;

            while (pos + 30 <= zipBytes.Length)
            {
                uint sig = LE32(zipBytes, pos);
                if (sig != LocalFileSig)
                    break;

                ushort gpFlag      = LE16(zipBytes, pos + 6);
                ushort compMethod  = LE16(zipBytes, pos + 8);
                ushort lastModTime = LE16(zipBytes, pos + 12);
                uint   crc32       = LE32(zipBytes, pos + 14);
                uint   compSize    = LE32(zipBytes, pos + 18);
                ushort fnLen       = LE16(zipBytes, pos + 26);
                ushort extraLen    = LE16(zipBytes, pos + 28);

                bool isEncrypted = (gpFlag & 0x0001) != 0;
                bool hasDataDesc = (gpFlag & 0x0008) != 0;

                int headerEnd = pos + 30 + fnLen + extraLen;

                if (found == entryIndex)
                {
                    if (!isEncrypted)
                        return null;

                    if (compMethod == 99)
                        return ParseWinZipAes(zipBytes, pos, headerEnd, extraLen, fnLen);
                    else
                        return ParseZipCrypto(zipBytes, headerEnd, crc32, lastModTime, hasDataDesc);
                }

                found++;
                pos = headerEnd + (int)compSize;
            }

            return null;
        }

        // ----------------------------------------------------------------
        // Fast per-password checks  (called millions of times - must be lean)
        // ----------------------------------------------------------------

        /// <summary>
        /// ZipCrypto header-only check (PKWARE AppNote section 6.1.6).
        /// Initializes the 3-key schedule with the password, decrypts the 12-byte
        /// encryption header, and verifies byte [11] against the stored check byte.
        /// Returns true when the password is plausible (~1/256 false-positive rate).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckZipCrypto(ZipProbeData probe, ReadOnlySpan<char> password)
        {
            uint key0 = 305419896u;
            uint key1 = 591751049u;
            uint key2 = 878082192u;

            foreach (char c in password)
                UpdateKeys(ref key0, ref key1, ref key2, (byte)c);

            ReadOnlySpan<byte> hdr = probe.EncryptionHeader12!;
            byte decrypted = 0;
            for (int i = 0; i < 12; i++)
                decrypted = DecryptByte(ref key0, ref key1, ref key2, hdr[i]);

            return decrypted == probe.CheckByte;
        }

        /// <summary>
        /// WinZip AES password-verification check (WinZip AE-1/AE-2 specification).
        /// Derives key material with PBKDF2-HMAC-SHA1 / 1000 iterations; the last
        /// 2 bytes are compared to the stored verifier.
        /// Returns true when the password is plausible (~1/65536 false-positive rate).
        /// </summary>
        public static bool CheckWinZipAes(ZipProbeData probe, ReadOnlySpan<char> password)
        {
            int keyBytes   = probe.AesKeyBits / 8;
            int totalBytes = 2 * keyBytes + 2;

            byte[] pwdBytes = Encoding.UTF8.GetBytes(password.ToString());
            byte[] derived  = Rfc2898DeriveBytes.Pbkdf2(
                pwdBytes,
                probe.AesSalt!,
                1000,
                HashAlgorithmName.SHA1,
                totalBytes);

            return derived[totalBytes - 2] == probe.AesPwdVerifier2![0]
                && derived[totalBytes - 1] == probe.AesPwdVerifier2![1];
        }

        // ----------------------------------------------------------------
        // Private parse helpers
        // ----------------------------------------------------------------

        private static ZipProbeData ParseZipCrypto(
            byte[] bytes, int dataOffset, uint crc32, ushort lastModTime, bool hasDataDesc)
        {
            if (dataOffset + 12 > bytes.Length)
                return new ZipProbeData { Kind = ZipEncryptionKind.Unknown };

            byte[] hdr = new byte[12];
            Buffer.BlockCopy(bytes, dataOffset, hdr, 0, 12);

            // When the data descriptor flag is set the CRC in the local header is zero,
            // so PKWARE says to use the MSB of last-mod-time as the check byte instead.
            byte checkByte = hasDataDesc
                ? (byte)(lastModTime >> 8)
                : (byte)(crc32 >> 24);

            return new ZipProbeData
            {
                Kind               = ZipEncryptionKind.ZipCrypto,
                EncryptionHeader12 = hdr,
                CheckByte          = checkByte
            };
        }

        private static ZipProbeData ParseWinZipAes(
            byte[] bytes, int lhOffset, int headerEnd, ushort extraLen, ushort fnLen)
        {
            // Find tag 0x9901 inside the extra-field block to read the AES strength byte.
            int extraStart = lhOffset + 30 + fnLen;
            int extraStop  = extraStart + extraLen;
            int aesStrength = 3; // default to 256-bit if tag is missing

            int pos = extraStart;
            while (pos + 4 <= extraStop)
            {
                ushort tag  = LE16(bytes, pos);
                ushort size = LE16(bytes, pos + 2);
                if (tag == 0x9901 && size >= 7 && pos + 4 + size <= extraStop)
                {
                    // Layout: vendor version (2) + vendor ID "AE" (2) + strength (1) + method (2)
                    aesStrength = bytes[pos + 8];
                    break;
                }
                pos += 4 + size;
            }

            int keyBits = aesStrength == 1 ? 128 : aesStrength == 2 ? 192 : 256;
            int saltLen = keyBits / 16; // 8 / 12 / 16

            if (headerEnd + saltLen + 2 > bytes.Length)
                return new ZipProbeData { Kind = ZipEncryptionKind.Unknown };

            byte[] salt     = new byte[saltLen];
            byte[] verifier = new byte[2];
            Buffer.BlockCopy(bytes, headerEnd,           salt,     0, saltLen);
            Buffer.BlockCopy(bytes, headerEnd + saltLen, verifier, 0, 2);

            return new ZipProbeData
            {
                Kind            = ZipEncryptionKind.WinZipAes,
                AesSalt         = salt,
                AesPwdVerifier2 = verifier,
                AesKeyBits      = keyBits
            };
        }

        // ----------------------------------------------------------------
        // ZipCrypto key-schedule primitives (PKWARE AppNote section 6.1.6)
        // ----------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateKeys(ref uint k0, ref uint k1, ref uint k2, byte b)
        {
            k0 = Crc32(k0, b);
            k1 = (k1 + (k0 & 0xFF)) * 134775813u + 1u;
            k2 = Crc32(k2, (byte)(k1 >> 24));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte DecryptByte(ref uint k0, ref uint k1, ref uint k2, byte cipherByte)
        {
            ushort temp  = (ushort)(k2 | 2u);
            byte   plain = (byte)(cipherByte ^ (byte)((temp * (temp ^ 1u)) >> 8));
            UpdateKeys(ref k0, ref k1, ref k2, plain);
            return plain;
        }

        // Table-free CRC-32 (polynomial 0xEDB88320).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Crc32(uint crc, byte b)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            return crc;
        }

        // ----------------------------------------------------------------
        // Little-endian read helpers
        // ----------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint LE32(byte[] b, int i)
            => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort LE16(byte[] b, int i)
            => (ushort)(b[i] | b[i + 1] << 8);
    }
}
