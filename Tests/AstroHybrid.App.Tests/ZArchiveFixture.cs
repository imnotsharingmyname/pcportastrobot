using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZstdSharp;

namespace AstroHybrid.App.Tests
{
    // Independent writer for the public upstream ZArchive format. No production reader code is reused.
    internal static class ZArchiveFixture
    {
        internal const int BlockSize = 64 * 1024;

        internal sealed class Node
        {
            public string Name = "";
            public byte[]? Data;
            public readonly List<Node> Children = new List<Node>();
            public uint NodeStart;
            public uint NameOffset;
            public ulong FileOffset;
            public bool IsFile => Data != null;
        }

        public static string Create(string directory, IDictionary<string, byte[]> files, bool compressed = true)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "PPSA21567-test.zar");
            Write(path, files, compressed);
            return path;
        }

        public static void Write(string path, IDictionary<string, byte[]> files, bool compressed)
        {
            var root = new Node();
            ulong inputOffset = 0;
            foreach (var pair in files)
            {
                var parts = pair.Key.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) throw new ArgumentException("Empty archive path");
                var parent = root;
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    var next = parent.Children.SingleOrDefault(n => n.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                    if (next == null)
                    {
                        next = new Node { Name = parts[i] };
                        parent.Children.Add(next);
                    }
                    if (next.IsFile) throw new ArgumentException("A file is used as a directory");
                    parent = next;
                }
                var name = parts[parts.Length - 1];
                var data = pair.Value ?? throw new ArgumentNullException(nameof(files));
                parent.Children.Add(new Node { Name = name, Data = data, FileOffset = inputOffset });
                inputOffset = CheckedAdd(inputOffset, (ulong)data.LongLength);
            }

            var blockCount = Math.Max(1UL, (inputOffset + BlockSize - 1) / BlockSize);
            var input = new byte[checked((int)(blockCount * BlockSize))];
            CopyFileData(root, input);
            var output = new MemoryStream();
            var recordSizes = new List<ushort>();
            var recordOffsets = new List<ulong>();
            using (var compressor = new Compressor(6))
            {
                for (ulong block = 0; block < blockCount; block++)
                {
                    if (block % 16 == 0) recordOffsets.Add((ulong)output.Length);
                    var chunk = new byte[BlockSize];
                    Buffer.BlockCopy(input, checked((int)(block * BlockSize)), chunk, 0, BlockSize);
                    byte[] stored = chunk;
                    if (compressed)
                    {
                        var candidate = compressor.Wrap(chunk).ToArray();
                        if (candidate.Length < BlockSize) stored = candidate;
                    }
                    output.Write(stored, 0, stored.Length);
                    recordSizes.Add(checked((ushort)(stored.Length - 1)));
                }
            }
            var compressedDataSize = (ulong)output.Length;
            WritePadding(output, 8);

            var offsetRecordsOffset = (ulong)output.Length;
            for (var group = 0; group < (recordSizes.Count + 15) / 16; group++)
            {
                WriteBe64(output, recordOffsets[group]);
                for (var sub = 0; sub < 16; sub++)
                {
                    var index = group * 16 + sub;
                    WriteBe16(output, index < recordSizes.Count ? recordSizes[index] : (ushort)0);
                }
            }
            var offsetRecordsSize = (ulong)output.Length - offsetRecordsOffset;

            var namesOffset = (ulong)output.Length;
            AssignNames(root, output, (long)namesOffset);
            var namesSize = (ulong)output.Length - namesOffset;

            var treeOffset = (ulong)output.Length;
            var queue = new Queue<Node>();
            var all = new List<Node>();
            queue.Enqueue(root);
            uint nextIndex = 1;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                node.NodeStart = nextIndex;
                nextIndex = checked(nextIndex + (uint)node.Children.Count);
                foreach (var child in node.Children) queue.Enqueue(child);
            }
            queue.Enqueue(root);
            all.Add(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                WriteBe32(output, (node.IsFile ? 0x80000000U : 0U) | (ReferenceEquals(node, root) ? 0x7FFFFFFFU : node.NameOffset));
                if (node.IsFile)
                {
                    WriteBe32(output, (uint)node.FileOffset);
                    WriteBe32(output, (uint)node.Data!.LongLength);
                    WriteBe32(output, (uint)((node.FileOffset >> 32) & 0xFFFF) | (uint)(((ulong)node.Data.LongLength >> 16) & 0xFFFF0000));
                }
                else
                {
                    WriteBe32(output, node.NodeStart);
                    WriteBe32(output, (uint)node.Children.Count);
                    WriteBe32(output, 0);
                }
                foreach (var child in node.Children)
                {
                    all.Add(child);
                    queue.Enqueue(child);
                }
            }
            var treeSize = (ulong)output.Length - treeOffset;
            var metadataOffset = (ulong)output.Length;
            var totalSize = (ulong)output.Length + 144;

            WriteBe64(output, 0);
            WriteBe64(output, compressedDataSize);
            WriteBe64(output, offsetRecordsOffset);
            WriteBe64(output, offsetRecordsSize);
            WriteBe64(output, namesOffset);
            WriteBe64(output, namesSize);
            WriteBe64(output, treeOffset);
            WriteBe64(output, treeSize);
            WriteBe64(output, metadataOffset);
            WriteBe64(output, 0);
            WriteBe64(output, metadataOffset);
            WriteBe64(output, 0);
            output.Write(new byte[32], 0, 32);
            WriteBe64(output, totalSize);
            WriteBe32(output, 0x61bf3a01);
            WriteBe32(output, 0x169f52d6);
            File.WriteAllBytes(path, output.ToArray());
        }

        public static void CorruptCompressedBlock(string path, int blockIndex)
        {
            var bytes = File.ReadAllBytes(path);
            var records = checked((int)ReadBe64(bytes, bytes.Length - 144 + 16));
            var record = records + (blockIndex / 16) * 40;
            var offset = ReadBe64(bytes, record);
            for (var sub = 0; sub < blockIndex % 16; sub++)
                offset += (ulong)(((bytes[record + 8 + sub * 2] << 8) | bytes[record + 9 + sub * 2]) + 1);
            bytes[checked((int)offset + 2)] ^= 0xFF;
            File.WriteAllBytes(path, bytes);
        }

        private static ulong ReadBe64(byte[] bytes, int offset)
        {
            ulong value = 0;
            for (var i = 0; i < 8; i++) value = (value << 8) | bytes[offset + i];
            return value;
        }

        public static byte[] StandardEboot(int size = 4) => Enumerable.Range(0, size).Select(i => (byte)(i * 31 + 7)).ToArray();

        public static Dictionary<string, byte[]> ValidFiles(int ebootSize = 4)
        {
            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["eboot.bin"] = StandardEboot(ebootSize),
                ["sce_module/libc.prx"] = new byte[] { 1, 3, 3, 7 },
                ["sce_sys/param.json"] = Encoding.UTF8.GetBytes("{\"titleId\":\"PPSA21567\",\"contentVersion\":\"01.018.000\",\"conceptId\":\"10002684\"}"),
                ["data/prein/payload.bin"] = new byte[] { 9, 8, 7, 6 }
            };
        }

        private static void CopyFileData(Node node, byte[] destination)
        {
            foreach (var child in node.Children)
            {
                if (child.IsFile) Buffer.BlockCopy(child.Data!, 0, destination, checked((int)child.FileOffset), child.Data!.Length);
                else CopyFileData(child, destination);
            }
        }

        private static void AssignNames(Node node, Stream output, long namesStart)
        {
            foreach (var child in node.Children)
            {
                child.NameOffset = checked((uint)(output.Position - namesStart));
                var bytes = Encoding.UTF8.GetBytes(child.Name);
                if (bytes.Length >= 0x80) throw new NotSupportedException("Long fixture names are not needed");
                output.WriteByte((byte)bytes.Length);
                output.Write(bytes, 0, bytes.Length);
                AssignNames(child, output, namesStart);
            }
        }

        private static ulong CheckedAdd(ulong a, ulong b) => a > ulong.MaxValue - b ? throw new OverflowException() : a + b;

        private static void WritePadding(Stream stream, int alignment)
        {
            var padding = (alignment - (int)(stream.Position % alignment)) % alignment;
            stream.Write(new byte[padding], 0, padding);
        }

        private static void WriteBe16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteBe32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteBe64(Stream stream, ulong value)
        {
            WriteBe32(stream, (uint)(value >> 32));
            WriteBe32(stream, (uint)value);
        }
    }
}
