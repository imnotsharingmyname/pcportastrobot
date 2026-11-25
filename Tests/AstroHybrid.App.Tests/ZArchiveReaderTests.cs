using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class ZArchiveReaderTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astrohybrid-zar-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void ReadsCompressedAndRawEntriesCaseInsensitively()
        {
            var random = Enumerable.Range(0, ZArchiveFixture.BlockSize).Select(i => (byte)((i * 73) ^ (i >> 3))).ToArray();
            var files = ZArchiveFixture.ValidFiles();
            files["data/random.bin"] = random;
            var path = ZArchiveFixture.Create(_root, files, compressed: true);

            using var reader = ZArchiveReader.Open(path);
            Assert.Equal(files["eboot.bin"], reader.ReadEntry(reader.Entries["EBOOT.BIN"], CancellationToken.None));
            Assert.Equal(random, reader.ReadEntry(reader.Entries["DATA/RANDOM.BIN"], CancellationToken.None));
            Assert.True(reader.Entries["data/"].IsDirectory);
        }

        [Fact]
        public void ValidationProgressIsMonotonicAndComplete()
        {
            var path = ZArchiveFixture.Create(_root, ZArchiveFixture.ValidFiles(8));
            using var reader = ZArchiveReader.Open(path);
            var progress = new List<ArchiveProgress>();
            var entries = new[] { reader.Entries["eboot.bin"], reader.Entries["sce_sys/param.json"] };
            var total = entries.Sum(e => (long)e.Size);

            reader.ValidateEntries(entries, new ActionProgress<ArchiveProgress>(progress.Add), CancellationToken.None);

            Assert.NotEmpty(progress);
            Assert.Equal(total, progress.Last().TotalBytes);
            Assert.Equal(total, progress.Last().ProcessedBytes);
            Assert.Equal(100, progress.Last().Percent);
            Assert.Equal(progress, progress.OrderBy(p => p.Percent).ToList());
        }

        [Fact]
        public void ExtractionStaysInsideOutputRoot()
        {
            var files = new Dictionary<string, byte[]> { ["../escape.bin"] = new byte[] { 42 } };
            var path = ZArchiveFixture.Create(_root, files, compressed: false);
            using var reader = ZArchiveReader.Open(path);
            var output = Path.Combine(_root, "output");

            Assert.Throws<InvalidDataException>(() => reader.ExtractEntry(reader.Entries["../escape.bin"], output, null, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(_root, "escape.bin")));
        }

        [Fact]
        public void MalformedTablesAreRejectedSafely()
        {
            var path = ZArchiveFixture.Create(_root, ZArchiveFixture.ValidFiles());
            var truncated = Path.Combine(_root, "truncated.zar");
            File.WriteAllBytes(truncated, File.ReadAllBytes(path).Take(100).ToArray());
            Assert.Throws<InvalidDataException>(() => ZArchiveReader.Open(truncated));

            var bytes = File.ReadAllBytes(path);
            // sectionNames.size in the 144-byte footer: compressed(16), records(16), names.offset(8).
            var position = bytes.Length - 144 + 40;
            for (var i = 0; i < 8; i++) bytes[position + i] = 0xFF;
            var huge = Path.Combine(_root, "huge.zar");
            File.WriteAllBytes(huge, bytes);
            Assert.Throws<InvalidDataException>(() => ZArchiveReader.Open(huge));

            var treeOffset = (int)ReadBe64(bytes, bytes.Length - 144 + 48);
            var invalidChildren = (byte[])File.ReadAllBytes(path).Clone();
            WriteBe32(invalidChildren, treeOffset + 4, uint.MaxValue);
            var childrenPath = Path.Combine(_root, "children.zar");
            File.WriteAllBytes(childrenPath, invalidChildren);
            Assert.Throws<InvalidDataException>(() => ZArchiveReader.Open(childrenPath));

            var invalidName = (byte[])File.ReadAllBytes(path).Clone();
            WriteBe32(invalidName, treeOffset + 16, 0x7FFFFFFE);
            var namePath = Path.Combine(_root, "name.zar");
            File.WriteAllBytes(namePath, invalidName);
            Assert.Throws<InvalidDataException>(() => ZArchiveReader.Open(namePath));
        }

        [Fact]
        public void DeclaredHugeEntryIsRejectedBeforeAllocatingItsSize()
        {
            var path = ZArchiveFixture.Create(_root, new Dictionary<string, byte[]>
            {
                ["sce_sys/param.json"] = new byte[] { 1, 2, 3, 4 }
            });
            var bytes = File.ReadAllBytes(path);
            var treeOffset = (int)ReadBe64(bytes, bytes.Length - 144 + 48);
            // Root + sce_sys + param.json; declare a 96 MiB metadata entry, below int.MaxValue.
            WriteBe32(bytes, treeOffset + 2 * 16 + 8, 96 * 1024 * 1024);
            WriteBe32(bytes, treeOffset + 2 * 16 + 12, 0);
            File.WriteAllBytes(path, bytes);

            using var reader = ZArchiveReader.Open(path);
            var error = Record.Exception(() => reader.ReadEntry(reader.Entries["sce_sys/param.json"], CancellationToken.None));
            var invalid = Assert.IsType<InvalidDataException>(error);
            Assert.Contains("too large", invalid.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static ulong ReadBe64(byte[] bytes, int offset)
        {
            ulong result = 0;
            for (var i = 0; i < 8; i++) result = (result << 8) | bytes[offset + i];
            return result;
        }

        private static void WriteBe32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        [Fact]
        public void DamagedRequiredCompressedDataThrows()
        {
            var files = ZArchiveFixture.ValidFiles(128);
            var path = ZArchiveFixture.Create(_root, files, compressed: true);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = 2;
                stream.WriteByte(0xEE);
            }
            using var reader = ZArchiveReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.ReadEntry(reader.Entries["eboot.bin"], CancellationToken.None));
        }

        [Fact]
        public void ValidationHonoursCancellation()
        {
            var path = ZArchiveFixture.Create(_root, ZArchiveFixture.ValidFiles());
            using var reader = ZArchiveReader.Open(path);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => reader.ValidateEntries(
                new[] { reader.Entries["eboot.bin"] }, null, cancel.Token));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    internal sealed class ActionProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public ActionProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}
