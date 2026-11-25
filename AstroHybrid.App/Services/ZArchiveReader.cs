using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ZstdSharp;

namespace AstroHybrid.App.Services
{
    public sealed class ArchiveEntry
    {
        public ArchiveEntry(string path, bool isDirectory, ulong size)
        {
            Path = path;
            IsDirectory = isDirectory;
            Size = size;
        }

        public string Path { get; }
        public bool IsDirectory { get; }
        public ulong Size { get; }
        internal ulong FileOffset { get; set; }
    }

    public sealed class ArchiveProgress
    {
        public ArchiveProgress(int percent, string message, long processedBytes, long totalBytes)
        {
            Percent = percent;
            Message = message;
            ProcessedBytes = processedBytes;
            TotalBytes = totalBytes;
        }

        public int Percent { get; }
        public string Message { get; }
        public long ProcessedBytes { get; }
        public long TotalBytes { get; }
    }

    // Read-only ZArchive v1 access for launcher validation and small setup extracts.
    // The archive itself is never opened for writing and optional data is never read implicitly.
    public sealed class ZArchiveReader : IDisposable
    {
        private const int FooterSize = 144;
        private const int BlockSize = 64 * 1024;
        private const int OffsetRecordSize = 40;
        private const int NodeSize = 16;
        private const int MaxNameTableBytes = 128 * 1024 * 1024;
        private const int MaxOffsetTableBytes = 128 * 1024 * 1024;
        private const int MaxReadEntryBytes = 64 * 1024 * 1024;
        private const int MaxNodeCount = 2_000_000;
        private const uint InvalidNameOffset = 0x7FFFFFFF;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private sealed class Node
        {
            public uint NameAndType;
            public uint First;
            public uint Second;
            public uint Third;
            public bool IsFile => (NameAndType & 0x80000000) != 0;
            public uint NameOffset => NameAndType & 0x7FFFFFFF;
            public ulong FileOffset => First | ((ulong)(Third & 0xFFFF) << 32);
            public ulong FileSize => Second | ((ulong)(Third & 0xFFFF0000) << 16);
        }

        private sealed class OffsetRecord
        {
            public ulong BaseOffset;
            public ushort[] Sizes = new ushort[16];
        }

        private readonly FileStream _stream;
        private readonly byte[] _names;
        private readonly List<Node> _nodes;
        private readonly List<OffsetRecord> _records;
        private readonly ulong _compressedOffset;
        private readonly ulong _compressedSize;
        private readonly object _sync = new object();
        private bool _disposed;

        public IReadOnlyDictionary<string, ArchiveEntry> Entries { get; }

        private ZArchiveReader(FileStream stream, byte[] names, List<Node> nodes, List<OffsetRecord> records,
            ulong compressedOffset, ulong compressedSize, IReadOnlyDictionary<string, ArchiveEntry> entries)
        {
            _stream = stream;
            _names = names;
            _nodes = nodes;
            _records = records;
            _compressedOffset = compressedOffset;
            _compressedSize = compressedSize;
            Entries = entries;
        }

        public static ZArchiveReader Open(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                if (stream.Length <= FooterSize) throw new InvalidDataException("This is not a readable ZArchive: the file is too short.");
                var footer = ReadExactly(stream, checked(stream.Length - FooterSize), FooterSize);
                var totalSize = Be64(footer, 128);
                var version = Be32(footer, 136);
                var magic = Be32(footer, 140);
                if (magic != 0x169f52d6 || version != 0x61bf3a01 || totalSize != (ulong)stream.Length)
                    throw new InvalidDataException("This is not a readable ZArchive: its footer does not match.");

                var compressedOffset = Be64(footer, 0);
                var compressedSize = Be64(footer, 8);
                var recordsOffset = Be64(footer, 16);
                var recordsSize = Be64(footer, 24);
                var namesOffset = Be64(footer, 32);
                var namesSize = Be64(footer, 40);
                var treeOffset = Be64(footer, 48);
                var treeSize = Be64(footer, 56);
                ValidateSection(compressedOffset, compressedSize, totalSize, "compressed data");
                ValidateSection(recordsOffset, recordsSize, totalSize, "offset table");
                ValidateSection(namesOffset, namesSize, totalSize, "name table");
                ValidateSection(treeOffset, treeSize, totalSize, "file table");
                ValidateSection(Be64(footer, 64), Be64(footer, 72), totalSize, "metadata directory");
                ValidateSection(Be64(footer, 80), Be64(footer, 88), totalSize, "metadata");
                if (recordsSize == 0 || recordsSize % OffsetRecordSize != 0 || recordsSize > MaxOffsetTableBytes)
                    throw new InvalidDataException("The ZArchive offset table is malformed or too large.");
                if (namesSize > MaxNameTableBytes) throw new InvalidDataException("The ZArchive name table is unreasonably large.");
                if (treeSize == 0 || treeSize % NodeSize != 0 || treeSize / NodeSize > MaxNodeCount)
                    throw new InvalidDataException("The ZArchive file table is malformed or too large.");
                var recordCount = recordsSize / OffsetRecordSize;
                if (recordCount > int.MaxValue) throw new InvalidDataException("The ZArchive offset table is too large.");

                var names = ReadExactly(stream, (long)namesOffset, (int)namesSize);
                var tree = ReadExactly(stream, (long)treeOffset, (int)treeSize);
                var nodes = new List<Node>((int)(treeSize / NodeSize));
                for (var offset = 0; offset < tree.Length; offset += NodeSize)
                {
                    nodes.Add(new Node
                    {
                        NameAndType = Be32(tree, offset),
                        First = Be32(tree, offset + 4),
                        Second = Be32(tree, offset + 8),
                        Third = Be32(tree, offset + 12)
                    });
                }
                if (nodes[0].IsFile || nodes[0].NameOffset != InvalidNameOffset)
                    throw new InvalidDataException("The ZArchive root directory is malformed.");
                var records = ReadOffsetRecords(stream, recordsOffset, (int)recordCount);
                var entries = BuildEntries(names, nodes);
                return new ZArchiveReader(stream, names, nodes, records, compressedOffset, compressedSize, entries);
            }
            catch (InvalidDataException)
            {
                stream.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is OverflowException || ex is ArgumentException || ex is DecoderFallbackException)
            {
                stream.Dispose();
                throw new InvalidDataException("The ZArchive index is damaged or unsupported.", ex);
            }
        }

        public byte[] ReadEntry(ArchiveEntry entry, CancellationToken cancellationToken)
        {
            var stored = ResolveFile(entry);
            if (stored.Size > MaxReadEntryBytes) throw new InvalidDataException("This archive entry is too large to read into memory.");
            var data = new byte[(int)stored.Size];
            using (var destination = new MemoryStream(data, true))
                ReadToStream(stored, destination, null, null, 0, 0, cancellationToken);
            return data;
        }

        public void ValidateEntries(IEnumerable<ArchiveEntry> entries, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var files = entries.Select(ResolveFile).ToList();
            long total = 0;
            foreach (var file in files) total = checked(total + ToLong(file.Size, "entry size"));
            long processed = 0;
            progress?.Report(new ArchiveProgress(total == 0 ? 100 : 0, "Preparing required archive files...", 0, total));
            foreach (var file in files)
            {
                var completedBefore = processed;
                ReadToStream(file, Stream.Null, progress, file.Path, processed, total, cancellationToken);
                processed = checked(processed + ToLong(file.Size, "entry size"));
                if (processed != completedBefore + ToLong(file.Size, "entry size")) throw new InvalidDataException("The archive entry length changed while reading.");
            }
            progress?.Report(new ArchiveProgress(100, "Required archive files have been checked.", total, total));
        }

        public void ExtractEntry(ArchiveEntry entry, string outputRoot, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
        {
            var stored = ResolveFile(entry);
            var root = Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(root);
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var relative = stored.Path.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(p => p == "." || p == ".." || p.Length == 0))
                throw new InvalidDataException("The archive contains an unsafe extraction path: " + stored.Path);
            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || destination.Length <= rootPrefix.Length)
                throw new InvalidDataException("The archive extraction path leaves its output directory.");
            var parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent)) throw new InvalidDataException("The archive extraction path is invalid.");
            Directory.CreateDirectory(parent);
            var total = ToLong(stored.Size, "entry size");
            try
            {
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    ReadToStream(stored, output, progress, stored.Path, 0, total, cancellationToken);
                progress?.Report(new ArchiveProgress(100, "Extracted " + stored.Path, total, total));
            }
            catch
            {
                try { File.Delete(destination); } catch { }
                throw;
            }
        }

        private void ReadToStream(ArchiveEntry entry, Stream destination, IProgress<ArchiveProgress>? progress,
            string? label, long progressBase, long progressTotal, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var remaining = entry.Size;
                var filePosition = entry.FileOffset;
                var buffer = new byte[BlockSize];
                long processedInFile = 0;
                using (var decompressor = new Decompressor())
                {
                    while (remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var blockIndex = filePosition / BlockSize;
                        var blockOffset = (int)(filePosition % BlockSize);
                        var block = ReadBlock(blockIndex, buffer, decompressor);
                        var count = (int)Math.Min(remaining, (ulong)(BlockSize - blockOffset));
                        destination.Write(block, blockOffset, count);
                        filePosition = CheckedAdd(filePosition, (ulong)count, "file offset");
                        remaining -= (ulong)count;
                        processedInFile = checked(processedInFile + count);
                        if (progress != null && progressTotal > 0)
                        {
                            var done = checked(progressBase + processedInFile);
                            var percent = (int)Math.Min(99, done * 100 / progressTotal);
                            progress.Report(new ArchiveProgress(percent, "Checking " + label, done, progressTotal));
                        }
                    }
                }
            }
        }

        private byte[] ReadBlock(ulong blockIndex, byte[] buffer, Decompressor decompressor)
        {
            var recordIndex = blockIndex / 16;
            var subIndex = (int)(blockIndex % 16);
            if (recordIndex >= (ulong)_records.Count) throw new InvalidDataException("An archive entry points beyond the offset table.");
            var record = _records[(int)recordIndex];
            ulong relativeOffset = record.BaseOffset;
            for (var i = 0; i < subIndex; i++) relativeOffset = CheckedAdd(relativeOffset, (ulong)record.Sizes[i] + 1, "compressed offset");
            var storedSize = (ulong)record.Sizes[subIndex] + 1;
            if (relativeOffset > _compressedSize || storedSize > _compressedSize - relativeOffset)
                throw new InvalidDataException("An archive block points beyond the compressed data section.");
            var stored = ReadExactly(_stream, ToLong(_compressedOffset + relativeOffset, "compressed offset"), (int)storedSize);
            if (storedSize == BlockSize) return stored;
            try
            {
                var written = decompressor.Unwrap(stored, buffer);
                if (written != BlockSize) throw new InvalidDataException("A ZArchive block has an invalid decompressed size.");
                return buffer;
            }
            catch (Exception ex) when (ex is ZstdException || ex is ArgumentException || ex is OverflowException)
            {
                throw new InvalidDataException("A required ZArchive data block is damaged.", ex);
            }
        }

        private ArchiveEntry ResolveFile(ArchiveEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var key = entry.Path.Replace('\\', '/');
            if (!Entries.TryGetValue(key, out var stored) || stored.IsDirectory)
                throw new InvalidDataException("The archive entry does not exist or is not a file: " + entry.Path);
            return stored;
        }

        private static IReadOnlyDictionary<string, ArchiveEntry> BuildEntries(byte[] names, List<Node> nodes)
        {
            var entries = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<KeyValuePair<int, string>>();
            var visited = new bool[nodes.Count];
            visited[0] = true;
            queue.Enqueue(new KeyValuePair<int, string>(0, ""));
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var node = nodes[current.Key];
                if (node.IsFile) throw new InvalidDataException("A file node was used as a directory.");
                var start = (ulong)node.First;
                var count = (ulong)node.Second;
                if (start < 1 || start > (ulong)nodes.Count || count > (ulong)nodes.Count - start)
                    throw new InvalidDataException("A ZArchive directory points outside the file table.");
                for (uint index = (uint)start; index < start + count; index++)
                {
                    var child = nodes[(int)index];
                    var name = ReadName(names, child.NameOffset);
                    var path = current.Value + name;
                    ulong size = 0;
                    if (child.IsFile)
                    {
                        size = child.FileSize;
                        if (child.FileOffset > ulong.MaxValue - size) throw new InvalidDataException("A ZArchive file range overflows.");
                    }
                    else
                    {
                        path += "/";
                        if (visited[index]) throw new InvalidDataException("The ZArchive directory graph contains a cycle or duplicate node.");
                        visited[index] = true;
                        queue.Enqueue(new KeyValuePair<int, string>((int)index, path));
                    }
                    if (entries.ContainsKey(path)) throw new InvalidDataException("The ZArchive contains a duplicate path: " + path);
                    entries.Add(path, new ArchiveEntry(path, !child.IsFile, size) { FileOffset = child.FileOffset });
                }
            }
            return entries;
        }

        private static string ReadName(byte[] table, uint offset)
        {
            if (offset == InvalidNameOffset || offset >= table.Length) throw new InvalidDataException("A ZArchive node has an invalid name offset.");
            var position = (int)offset;
            int length = table[position++] & 0x7F;
            if ((table[position - 1] & 0x80) != 0)
            {
                if (position >= table.Length) throw new InvalidDataException("A ZArchive name header is truncated.");
                length |= table[position++] << 7;
            }
            if (length == 0 || position > table.Length - length) throw new InvalidDataException("A ZArchive name is empty or truncated.");
            var name = StrictUtf8.GetString(table, position, length);
            if (name.IndexOf('\0') >= 0 || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
                throw new InvalidDataException("A ZArchive node name contains an invalid path character.");
            return name;
        }

        private static List<OffsetRecord> ReadOffsetRecords(FileStream stream, ulong offset, int count)
        {
            var records = new List<OffsetRecord>(Math.Min(count, 4096));
            var bytes = ReadExactly(stream, ToLong(offset, "offset table"), checked(count * OffsetRecordSize));
            for (var index = 0; index < count; index++)
            {
                var position = index * OffsetRecordSize;
                var record = new OffsetRecord { BaseOffset = Be64(bytes, position) };
                for (var sub = 0; sub < 16; sub++) record.Sizes[sub] = Be16(bytes, position + 8 + sub * 2);
                records.Add(record);
            }
            return records;
        }

        private static byte[] ReadExactly(FileStream stream, long offset, int count)
        {
            var bytes = new byte[count];
            stream.Position = offset;
            var read = 0;
            while (read < count)
            {
                var current = stream.Read(bytes, read, count - read);
                if (current == 0) throw new InvalidDataException("The ZArchive ended unexpectedly.");
                read += current;
            }
            return bytes;
        }

        private static void ValidateSection(ulong offset, ulong size, ulong fileSize, string name)
        {
            if (offset > fileSize || size > fileSize - offset)
                throw new InvalidDataException("The ZArchive " + name + " section lies outside the file.");
        }

        private static long ToLong(ulong value, string description)
        {
            if (value > long.MaxValue) throw new InvalidDataException("The ZArchive " + description + " is too large.");
            return (long)value;
        }

        private static ulong CheckedAdd(ulong left, ulong right, string description)
        {
            if (left > ulong.MaxValue - right) throw new InvalidDataException("The ZArchive " + description + " overflows.");
            return left + right;
        }

        private static ushort Be16(byte[] bytes, int offset) => (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        private static uint Be32(byte[] bytes, int offset) => ((uint)Be16(bytes, offset) << 16) | Be16(bytes, offset + 2);
        private static ulong Be64(byte[] bytes, int offset) => ((ulong)Be32(bytes, offset) << 32) | Be32(bytes, offset + 4);

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ZArchiveReader));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
        }
    }
}
