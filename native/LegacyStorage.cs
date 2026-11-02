using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GgdHelp
{
    // A read-only reader for the Chromium LevelDB formats used by Electron.
    // It follows CURRENT/MANIFEST, verifies masked CRC32C checksums, decodes
    // fragmented WAL records and Snappy table blocks, and respects tombstones.
    // Format references: google/leveldb doc/log_format.md, doc/table_format.md,
    // db/version_edit.cc, and google/snappy format_description.txt.
    // Unsupported or damaged data fails closed; the original database is never
    // opened through LevelDB or modified by this application.
    public static class LegacyStorage
    {
        private const int MaxFile = 64 * 1024 * 1024, MaxBlock = 32 * 1024 * 1024;
        private const string StorageKey = "goose-duck-overlay:v2";
        public static string LastFailureDetail { get; private set; }
        private static readonly uint[] CrcTable = BuildCrcTable();
        private sealed class Manifest
        {
            public ulong log, previousLog;
            public bool hasLog;
            public Dictionary<ulong, ulong> tables = new Dictionary<ulong, ulong>();
        }
        private sealed class Candidate
        {
            public bool found, deleted;
            public ulong sequence;
            public byte[] value;
        }
        private sealed class Entry { public byte[] key, value; }
        private sealed class Cursor
        {
            public byte[] bytes;
            public int at, end;
            public Cursor(byte[] value) { bytes = value; end = value.Length; }
            public Cursor(byte[] value, int start, int stop) { bytes = value; at = start; end = stop; }
            public byte Byte() { if (at >= end) throw Bad("truncated byte"); return bytes[at++]; }
            public ulong Varint()
            {
                ulong value = 0;
                for (int shift = 0; shift < 64; shift += 7) {
                    byte part = Byte();
                    if (shift == 63 && part > 1) throw Bad("varint overflow");
                    value |= (ulong)(part & 127) << shift;
                    if ((part & 128) == 0) return value;
                }
                throw Bad("invalid varint");
            }
            public byte[] Slice()
            {
                ulong length = Varint();
                if (length > MaxBlock || length > (ulong)(end - at)) throw Bad("invalid slice");
                byte[] value = Copy(bytes, at, (int)length); at += (int)length; return value;
            }
        }
        private static InvalidDataException Bad(string reason) { return new InvalidDataException("Unsupported or damaged LevelDB: " + reason); }
        public static bool TryRead(string profileDirectory, out string json, out string message)
        {
            json = null; message = ""; LastFailureDetail = "";
            string directory = Path.Combine(profileDirectory, "Local Storage", "leveldb");
            if (!Directory.Exists(directory)) return false;
            try {
                string lockPath = Path.Combine(directory, "LOCK");
                // Electron keeps this file open. Sharing none prevents reading
                // a changing database and prevents a second process opening it
                // during this short snapshot; no lock bytes are written.
                using (FileStream guard = File.Exists(lockPath) ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None) : null) {
                    byte[] currentBytes = ReadFile(Path.Combine(directory, "CURRENT"));
                    string current = Encoding.ASCII.GetString(currentBytes).Trim();
                    if (!Regex.IsMatch(current, "^MANIFEST-[0-9]+$")) throw Bad("CURRENT filename");
                    byte[] manifestBytes = ReadFile(Path.Combine(directory, current));
                    Manifest manifest = new Manifest();
                    ReadLog(manifestBytes, delegate(byte[] record) { ApplyManifest(record, manifest); });
                    if (!manifest.hasLog) throw Bad("missing log number");
                    // A missing referenced journal must not make an older SST
                    // value look current. Zero is the valid new-DB sentinel.
                    if (manifest.log != 0 && !File.Exists(Path.Combine(directory, manifest.log.ToString("D6", CultureInfo.InvariantCulture) + ".log")))
                        throw Bad("missing referenced WAL");
                    Candidate latest = new Candidate();
                    foreach (KeyValuePair<ulong, ulong> table in manifest.tables) {
                        string name = table.Key.ToString("D6", CultureInfo.InvariantCulture);
                        string file = Path.Combine(directory, name + ".ldb");
                        if (!File.Exists(file)) file = Path.Combine(directory, name + ".sst");
                        byte[] bytes = ReadFile(file);
                        if ((ulong)bytes.Length != table.Value) throw Bad("table size mismatch");
                        ReadTable(bytes, latest);
                    }
                    string[] logs = Directory.GetFiles(directory, "*.log");
                    Array.Sort(logs, StringComparer.Ordinal);
                    foreach (string file in logs) {
                        ulong number;
                        if (!UInt64.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out number)) continue;
                        if (number >= manifest.log || number == manifest.previousLog)
                            ReadLog(ReadFile(file), delegate(byte[] record) { ReadBatch(record, latest); });
                    }
                    if (!Equal(currentBytes, ReadFile(Path.Combine(directory, "CURRENT"))) ||
                        !Equal(manifestBytes, ReadFile(Path.Combine(directory, current)))) throw Bad("database changed while reading");
                    if (!latest.found || latest.deleted) return false;
                    json = DecodeString(latest.value);
                    return true;
                }
            } catch (Exception error) {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is OverflowException)) throw;
                LastFailureDetail = error.Message;
                message = "未能安全读取旧版记录，请退出旧版后重试，或在设置中导入 JSON。旧版文件未改动。";
                return false;
            }
        }
        private static byte[] ReadFile(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (stream.Length > MaxFile) throw Bad("file size limit");
                byte[] bytes = new byte[(int)stream.Length]; int offset = 0;
                while (offset < bytes.Length) { int read = stream.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw Bad("short file read"); offset += read; }
                return bytes;
            }
        }
        private static byte[] Copy(byte[] input, int at, int count)
        { if (at < 0 || count < 0 || at > input.Length - count) throw Bad("slice bounds"); byte[] value = new byte[count]; Buffer.BlockCopy(input, at, value, 0, count); return value; }
        private static bool Equal(byte[] first, byte[] second)
        { if (first.Length != second.Length) return false; for (int i = 0; i < first.Length; i++) if (first[i] != second[i]) return false; return true; }
        private static uint Fixed32(byte[] value, int at)
        { if (at < 0 || at > value.Length - 4) throw Bad("uint32 bounds"); return (uint)value[at] | (uint)value[at+1] << 8 | (uint)value[at+2] << 16 | (uint)value[at+3] << 24; }
        private static ulong Fixed64(byte[] value, int at) { return Fixed32(value, at) | (ulong)Fixed32(value, at + 4) << 32; }
        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++) { uint value = i; for (int j = 0; j < 8; j++) value = (value >> 1) ^ ((value & 1) != 0 ? 0x82f63b78U : 0); table[i] = value; }
            return table;
        }
        private static uint Crc(byte[] bytes, int at, int count, byte tag, bool tagFirst)
        {
            uint value = 0xffffffffU;
            if (tagFirst) value = CrcTable[(value ^ tag) & 255] ^ (value >> 8);
            for (int i = at; i < at + count; i++) value = CrcTable[(value ^ bytes[i]) & 255] ^ (value >> 8);
            if (!tagFirst) value = CrcTable[(value ^ tag) & 255] ^ (value >> 8);
            value ^= 0xffffffffU;
            return unchecked(((value >> 15) | (value << 17)) + 0xa282ead8U);
        }
        private static void ReadLog(byte[] bytes, Action<byte[]> consume)
        {
            int at = 0; MemoryStream fragments = null;
            try {
                while (at < bytes.Length) {
                    int blockEnd = Math.Min(bytes.Length, (at / 32768 + 1) * 32768);
                    if (blockEnd - at < 7) { at = blockEnd; continue; }
                    uint checksum = Fixed32(bytes, at);
                    int length = bytes[at+4] | bytes[at+5] << 8; byte type = bytes[at+6]; at += 7;
                    if (type == 0 && length == 0 && checksum == 0) { at = blockEnd; continue; }
                    if (length > blockEnd - at) {
                        // A crash may leave an incomplete final physical record;
                        // LevelDB only replays complete checksummed batches.
                        if (blockEnd == bytes.Length) break;
                        throw Bad("WAL record crosses block");
                    }
                    if (Crc(bytes, at, length, type, true) != checksum) throw Bad("WAL checksum");
                    if (type == 1) {
                        if (fragments != null) throw Bad("unfinished WAL fragments");
                        consume(Copy(bytes, at, length));
                    } else if (type == 2) {
                        if (fragments != null) throw Bad("duplicate WAL first fragment");
                        fragments = new MemoryStream(); fragments.Write(bytes, at, length);
                    } else if (type == 3 || type == 4) {
                        if (fragments == null) throw Bad("missing WAL first fragment");
                        if (fragments.Length + length > MaxBlock) throw Bad("WAL batch size limit");
                        fragments.Write(bytes, at, length);
                        if (type == 4) { consume(fragments.ToArray()); fragments.Dispose(); fragments = null; }
                    } else throw Bad("unknown WAL record type");
                    at += length;
                }
            } finally { if (fragments != null) fragments.Dispose(); }
        }
        private static void ApplyManifest(byte[] bytes, Manifest state)
        {
            Cursor reader = new Cursor(bytes);
            while (reader.at < reader.end) {
                ulong tag = reader.Varint();
                switch (tag) {
                    case 1:
                        if (Encoding.ASCII.GetString(reader.Slice()) != "leveldb.BytewiseComparator") throw Bad("unknown key comparator");
                        break;
                    case 2: state.log = reader.Varint(); state.hasLog = true; break;
                    case 3: case 4: reader.Varint(); break;
                    case 5: reader.Varint(); reader.Slice(); break;
                    case 6: reader.Varint(); state.tables.Remove(reader.Varint()); break;
                    case 7:
                        reader.Varint(); ulong number = reader.Varint(), size = reader.Varint(); reader.Slice(); reader.Slice();
                        if (size > MaxFile || state.tables.Count > 10000) throw Bad("table limit");
                        state.tables[number] = size; break;
                    case 9: state.previousLog = reader.Varint(); break;
                    default: throw Bad("unknown MANIFEST tag " + tag);
                }
            }
        }
        private static void ReadBatch(byte[] bytes, Candidate latest)
        {
            if (bytes.Length < 12) throw Bad("short write batch");
            ulong sequence = Fixed64(bytes, 0); uint count = Fixed32(bytes, 8);
            if (count > 1000000 || sequence > 0x00ffffffffffffffUL - count) throw Bad("write batch limit");
            Cursor reader = new Cursor(bytes, 12, bytes.Length);
            for (uint i = 0; i < count; i++) {
                byte type = reader.Byte(); if (type != 0 && type != 1) throw Bad("write batch type");
                byte[] key = reader.Slice(), value = type == 1 ? reader.Slice() : null;
                Consider(key, value, sequence + i, type == 0, latest);
            }
            if (reader.at != reader.end) throw Bad("write batch count");
        }
        private static bool IsTarget(byte[] key)
        {
            byte[] origin = Encoding.ASCII.GetBytes("_file://\0");
            if (key.Length <= origin.Length) return false;
            for (int i = 0; i < origin.Length; i++) if (key[i] != origin[i]) return false;
            return DecodeString(Copy(key, origin.Length, key.Length - origin.Length)) == StorageKey;
        }
        private static void Consider(byte[] key, byte[] value, ulong sequence, bool deleted, Candidate latest)
        {
            if (!IsTarget(key) || (latest.found && sequence < latest.sequence)) return;
            if (latest.found && sequence == latest.sequence && (deleted != latest.deleted || (!deleted && !Equal(value, latest.value))))
                throw Bad("conflicting values at one sequence");
            latest.found = true; latest.sequence = sequence; latest.deleted = deleted; latest.value = value;
        }
        private static string DecodeString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw Bad("empty DOM string");
            if (bytes[0] == 1) return Encoding.GetEncoding(28591).GetString(bytes, 1, bytes.Length - 1);
            if (bytes[0] == 0 && (bytes.Length - 1) % 2 == 0) return new UnicodeEncoding(false, false, true).GetString(bytes, 1, bytes.Length - 1);
            throw Bad("unknown DOM string encoding");
        }
        private static void ReadTable(byte[] bytes, Candidate latest)
        {
            if (bytes.Length < 48 || Fixed64(bytes, bytes.Length - 8) != 0xdb4775248b80fb57UL) throw Bad("table footer");
            Cursor footer = new Cursor(bytes, bytes.Length - 48, bytes.Length - 8);
            footer.Varint(); footer.Varint(); // metaindex is unnecessary for reading data entries.
            ulong offset = footer.Varint(), length = footer.Varint();
            foreach (Entry index in Entries(ReadBlock(bytes, offset, length))) {
                Cursor handle = new Cursor(index.value); ulong blockOffset = handle.Varint(), blockSize = handle.Varint();
                if (handle.at != handle.end) throw Bad("index block handle");
                foreach (Entry entry in Entries(ReadBlock(bytes, blockOffset, blockSize))) {
                    if (entry.key.Length < 8) throw Bad("short internal key");
                    ulong trailer = Fixed64(entry.key, entry.key.Length - 8);
                    byte type = (byte)(trailer & 255); if (type != 0 && type != 1) throw Bad("internal key type");
                    Consider(Copy(entry.key, 0, entry.key.Length - 8), type == 0 ? null : entry.value, trailer >> 8, type == 0, latest);
                }
            }
        }
        private static byte[] ReadBlock(byte[] table, ulong offset, ulong length)
        {
            if (offset > (ulong)table.Length || length > MaxBlock || offset + length + 5 > (ulong)table.Length - 48) throw Bad("table block bounds");
            int at = (int)offset, count = (int)length; byte type = table[at + count];
            if (Crc(table, at, count, type, false) != Fixed32(table, at + count + 1)) throw Bad("table block checksum");
            byte[] block = Copy(table, at, count);
            if (type == 0) return block;
            if (type == 1) return DecodeSnappy(block);
            throw Bad("unsupported table compression");
        }
        private static IEnumerable<Entry> Entries(byte[] bytes)
        {
            if (bytes.Length < 4) throw Bad("short table block");
            uint restartCount = Fixed32(bytes, bytes.Length - 4);
            if (restartCount > (bytes.Length - 4) / 4) throw Bad("block restart count");
            int end = bytes.Length - 4 - (int)restartCount * 4;
            uint previousRestart = 0;
            for (int i = 0; i < restartCount; i++) {
                uint restart = Fixed32(bytes, end + i * 4);
                if (restart > end || (i > 0 && restart < previousRestart)) throw Bad("restart offset");
                previousRestart = restart;
            }
            Cursor reader = new Cursor(bytes, 0, end); byte[] previous = new byte[0];
            while (reader.at < reader.end) {
                ulong shared = reader.Varint(), unshared = reader.Varint(), valueLength = reader.Varint();
                if (shared > (ulong)previous.Length || unshared > MaxBlock || valueLength > MaxBlock || shared + unshared > MaxBlock || unshared + valueLength > (ulong)(reader.end - reader.at)) throw Bad("prefix-compressed entry");
                byte[] key = new byte[(int)(shared + unshared)];
                Buffer.BlockCopy(previous, 0, key, 0, (int)shared); Buffer.BlockCopy(bytes, reader.at, key, (int)shared, (int)unshared); reader.at += (int)unshared;
                byte[] value = Copy(bytes, reader.at, (int)valueLength); reader.at += (int)valueLength; previous = key;
                yield return new Entry { key = key, value = value };
            }
        }
        internal static byte[] DecodeSnappy(byte[] bytes)
        {
            Cursor reader = new Cursor(bytes); ulong size = reader.Varint();
            if (size > MaxBlock) throw Bad("Snappy output size");
            byte[] output = new byte[(int)size]; int at = 0;
            while (reader.at < reader.end) {
                byte tag = reader.Byte(); int type = tag & 3, length;
                if (type == 0) {
                    int code = tag >> 2;
                    if (code < 60) length = code + 1;
                    else {
                        ulong longLength = 0; int count = code - 59;
                        for (int i = 0; i < count; i++) longLength |= (ulong)reader.Byte() << (i * 8);
                        if (longLength >= MaxBlock) throw Bad("Snappy literal length"); length = (int)longLength + 1;
                    }
                    if (length > output.Length - at || length > reader.end - reader.at) throw Bad("Snappy literal bounds");
                    Buffer.BlockCopy(bytes, reader.at, output, at, length); reader.at += length; at += length;
                } else {
                    ulong offset;
                    if (type == 1) { length = 4 + ((tag >> 2) & 7); offset = (ulong)((uint)(tag & 224) << 3) | (ulong)reader.Byte(); }
                    else {
                        length = 1 + (tag >> 2); int count = type == 2 ? 2 : 4; offset = 0;
                        for (int i = 0; i < count; i++) offset |= (ulong)reader.Byte() << (i * 8);
                    }
                    if (offset == 0 || offset > (ulong)at || length > output.Length - at) throw Bad("Snappy backreference bounds");
                    for (int i = 0; i < length; i++) { output[at] = output[at - (int)offset]; at++; }
                }
            }
            if (at != output.Length) throw Bad("Snappy output length");
            return output;
        }
    }
}
