using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AstroHybrid.App.Services
{
    // C# port of AnyPS5's tools/unwrap_self.py (same rules, same output bytes): turns a decrypted SELF
    // container (eboot.bin, sce_module/*.prx) into the plain ELF the relinker reads. Only decrypted
    // containers are handled; no keys are involved. Verified byte for byte against the Python tool.
    public static class SelfUnwrapper
    {
        private const uint SelfMagic = 0x1D3D154F;
        private const uint SelfMagicKernel = 0xEEF51454;
        private const int RecordOffset = 0x40;
        private const int RecordSize = 0x20;
        private const long RecordHasSegment = 0x800;
        private const int RecordSegmentShift = 20;
        private const long FirstRegion = 0x400;
        private const long MaxAlignmentPadding = 64L * 1024 * 1024;
        private const long MaxOutputFactor = 4;
        private const long MinOutputLimit = 256L * 1024 * 1024;
        private const uint PtLoad = 1;
        private const uint PtNote = 4;
        private const uint PtTls = 7;
        private const double EntropyLimit = 7.5;
        private const int EntropySample = 0x10000;

        public class RefusedException : Exception
        {
            public RefusedException(string message) : base(message) { }
        }

        private sealed class Header
        {
            public int Index;
            public uint Type;
            public uint Flags;
            public long Offset;
            public long Vaddr;
            public long Size;
            public long Memory;
            public long Align;
            public long Source;
            public Header? Host;
            public long New;
        }

        private sealed class Region
        {
            public long Old;
            public long Size;
            public long Vaddr;
            public long Align;
            public long New;
        }

        private static long PyMod(long a, long m) => ((a % m) + m) % m;

        private static double Entropy(byte[] data, long start, long length)
        {
            if (length <= 0) return 0.0;
            var counts = new long[256];
            for (long i = start; i < start + length; i++) counts[data[i]]++;
            double result = 0.0;
            foreach (var count in counts)
            {
                if (count == 0) continue;
                double p = (double)count / length;
                result -= p * Math.Log(p, 2);
            }
            return result;
        }

        private static List<(long Flags, long Segment, long Size)> ReadRecords(byte[] data)
        {
            var records = new List<(long, long, long)>();
            int offset = RecordOffset;
            while (offset + RecordSize <= FirstRegion)
            {
                long flags = BitConverter.ToInt64(data, offset);
                long segment = BitConverter.ToInt64(data, offset + 8);
                long size = BitConverter.ToInt64(data, offset + 16);
                long sizeAgain = BitConverter.ToInt64(data, offset + 24);
                if (size == 0 || size != sizeAgain) break;
                if (segment >= data.LongLength || segment < 0x350 || segment % 0x10 != 0) break;
                if (size < 0 || size > data.LongLength) break;
                records.Add((flags, segment, size));
                offset += RecordSize;
            }
            return records;
        }

        private static int FindElf(byte[] data)
        {
            for (int i = 0; i + 4 <= data.Length; i++)
            {
                if (data[i] == 0x7F && data[i + 1] == (byte)'E' && data[i + 2] == (byte)'L' && data[i + 3] == (byte)'F') return i;
            }
            return -1;
        }

        private static (int Inner, byte[] ElfHeader, List<Header> Headers) ParseElf(byte[] data)
        {
            int inner = FindElf(data);
            if (inner < 0) throw new RefusedException("no inner ELF header");
            if (inner + 0x40 > data.Length) throw new RefusedException("inner ELF header is truncated");
            var header = new byte[0x40];
            Array.Copy(data, inner, header, 0, 0x40);
            if (header[4] != 2 || header[5] != 1) throw new RefusedException("inner ELF is not 64-bit little-endian");
            uint version = BitConverter.ToUInt32(header, 0x14);
            if (header[6] != 1 || version != 1) throw new RefusedException($"unsupported inner ELF version (identification {header[6]}, object {version})");
            long programOffset = BitConverter.ToInt64(header, 0x20);
            ushort machine = BitConverter.ToUInt16(header, 0x12);
            if (machine != 62) throw new RefusedException($"unsupported inner ELF machine {machine} (expected 62)");
            ushort headerSize = BitConverter.ToUInt16(header, 0x34);
            if (headerSize != 64) throw new RefusedException($"invalid inner ELF header size {headerSize} (expected 64)");
            ushort entrySize = BitConverter.ToUInt16(header, 0x36);
            ushort count = BitConverter.ToUInt16(header, 0x38);
            if (entrySize != 56 || count == 0) throw new RefusedException($"program header table is {count} entries of {entrySize} bytes");
            if (programOffset < header.Length) throw new RefusedException("ELF program header table overlaps the ELF header");
            if (inner + programOffset + count * entrySize > data.LongLength) throw new RefusedException("ELF program header table does not fit the file");

            var headers = new List<Header>();
            for (int index = 0; index < count; index++)
            {
                int o = checked((int)(inner + programOffset + index * 56));
                uint kind = BitConverter.ToUInt32(data, o);
                uint flags = BitConverter.ToUInt32(data, o + 4);
                long fileOffset = BitConverter.ToInt64(data, o + 8);
                long vaddr = BitConverter.ToInt64(data, o + 16);
                long fileSize = BitConverter.ToInt64(data, o + 32);
                long memorySize = BitConverter.ToInt64(data, o + 40);
                long alignment = BitConverter.ToInt64(data, o + 48);
                if (kind == PtLoad || kind == PtTls)
                {
                    if ((ulong)fileSize > (ulong)memorySize) throw new RefusedException($"segment {index} file size exceeds memory size");
                    if (alignment != 0 && (alignment & (alignment - 1)) != 0) throw new RefusedException($"segment {index} alignment is not a power of two");
                }
                if (kind == PtTls)
                {
                    if ((ulong)alignment > 8192) throw new RefusedException($"segment {index} TLS alignment exceeds 8192");
                    if ((ulong)memorySize > 0x7FFF0000) throw new RefusedException($"segment {index} TLS memory size exceeds 0x7fff0000");
                }
                if (kind == PtLoad && (ulong)memorySize > ulong.MaxValue - (ulong)vaddr + 1UL && vaddr != 0)
                    throw new RefusedException($"segment {index} memory range exceeds ELF64");
                if (fileOffset < 0 || vaddr < 0 || fileSize < 0 || memorySize < 0 || alignment < 0)
                    throw new RefusedException($"segment {index} has values beyond what this tool handles");
                headers.Add(new Header
                {
                    Index = index, Type = kind, Flags = flags, Offset = fileOffset, Vaddr = vaddr,
                    Size = fileSize, Memory = memorySize, Align = alignment, Source = fileOffset, Host = null,
                });
            }
            return (inner, header, headers);
        }

        private static void PlaceSegments(byte[] data, List<Header> headers, List<(long Flags, long Segment, long Size)> records)
        {
            var loads = headers.Where(h => h.Type == PtLoad).ToList();
            var used = new HashSet<int>();
            var placed = new HashSet<int>();
            for (int index = 0; index < records.Count; index++)
            {
                var (flags, segment, size) = records[index];
                long target = (long)((ulong)flags >> RecordSegmentShift);
                if ((flags & RecordHasSegment) != 0 && target < headers.Count && !placed.Contains((int)target) && headers[(int)target].Size == size)
                {
                    headers[(int)target].Source = segment;
                    placed.Add((int)target);
                    used.Add(index);
                }
            }
            var recorded = headers.Where(h => placed.Contains(h.Index) && h.Vaddr == 0 && h.Type != PtLoad).ToList();
            if (recorded.Count > 0)
            {
                // Python max() keeps the first of equal keys.
                Header last = recorded[0];
                foreach (var h in recorded) if (h.Offset > last.Offset) last = h;
                long cursor = last.Source + last.Size;
                var trailing = headers.Where(h => !placed.Contains(h.Index) && h.Vaddr == 0 && h.Type != PtLoad && h.Size != 0 && h.Offset > last.Offset)
                                      .OrderBy(h => h.Offset).ToList();
                foreach (var h in trailing)
                {
                    placed.Add(h.Index);
                    if (h.Type == PtNote)
                    {
                        h.Source = 0;
                        h.Size = 0;
                        continue;
                    }
                    h.Source = cursor;
                    cursor += h.Size;
                    if (cursor > data.LongLength)
                    {
                        h.Source = 0;
                        h.Size = 0;
                    }
                }
            }
            foreach (var h in loads)
            {
                if (h.Size == 0 || placed.Contains(h.Index)) continue;
                for (int index = 0; index < records.Count; index++)
                {
                    if (used.Contains(index)) continue;
                    if (records[index].Size == h.Size)
                    {
                        used.Add(index);
                        h.Source = records[index].Segment;
                        break;
                    }
                }
            }

            Header? LoadFor(long vaddr, long size)
            {
                Header? found = null;
                foreach (var h in loads)
                {
                    long end = h.Vaddr + h.Memory;
                    if (h.Memory != 0 && h.Vaddr <= vaddr && (vaddr < end || (size == 0 && vaddr == end))) found = h;
                }
                return found;
            }

            (long Offset, long Shift)? tail = null;
            foreach (var h in headers)
            {
                if (h.Type == PtLoad) continue;
                if (placed.Contains(h.Index))
                {
                    var host = LoadFor(h.Vaddr, h.Size);
                    if (host != null)
                    {
                        long offset = h.Vaddr - host.Vaddr;
                        if (host.Size != 0 && offset <= host.Size && h.Size <= host.Size - offset && h.Source == host.Source + offset)
                            h.Host = host;
                    }
                    if (h.Size != 0 && (tail == null || h.Offset > tail.Value.Offset))
                        tail = (h.Offset, h.Source - h.Offset);
                    continue;
                }
                if (h.Vaddr != 0)
                {
                    var host = LoadFor(h.Vaddr, h.Size);
                    if (host == null)
                    {
                        if (h.Size == 0)
                        {
                            h.Source = 0;
                            continue;
                        }
                        throw new RefusedException($"segment {h.Index} vaddr {h.Vaddr:x} is in no PT_LOAD");
                    }
                    long offset = h.Vaddr - host.Vaddr;
                    if (h.Size != 0 && (offset > host.Size || h.Size > host.Size - offset))
                        throw new RefusedException($"segment {h.Index} does not fit the file-backed range of PT_LOAD {host.Index}");
                    if (host.Size == 0 || offset > host.Size)
                    {
                        h.Source = 0;
                        continue;
                    }
                    h.Host = host;
                    h.Source = host.Source + offset;
                    if (tail == null || h.Offset > tail.Value.Offset)
                        tail = (h.Offset, h.Source - h.Offset);
                }
                else if (h.Size != 0)
                {
                    h.Source = h.Offset + (tail?.Shift ?? 0);
                    if (h.Source > data.LongLength) h.Source = data.LongLength;
                    long keep = Math.Max(0, data.LongLength - h.Source);
                    h.Size = Math.Min(h.Size, keep);
                    h.Memory = Math.Min(h.Memory, keep);
                }
            }
            foreach (var h in headers)
            {
                if (h.Type == PtLoad && h.Size == 0) continue;
                if (h.Source < 0 || h.Source > data.LongLength || h.Size > data.LongLength - h.Source)
                    throw new RefusedException($"segment {h.Index} does not fit the file");
            }
        }

        private static void CheckDecrypted(byte[] data, List<Header> headers)
        {
            var encrypted = new List<string>();
            foreach (var h in headers)
            {
                if (h.Type != PtLoad || h.Size == 0) continue;
                double value = Entropy(data, h.Source, Math.Min(h.Size, EntropySample));
                if (value >= EntropyLimit) encrypted.Add($"segment {h.Index} at {h.Source:x} ({value:F2} bits per byte)");
            }
            int loads = headers.Count(h => h.Type == PtLoad && h.Size != 0);
            if (encrypted.Count > 0 && encrypted.Count == loads)
                throw new RefusedException("every load segment looks encrypted (this dump is not decrypted): " + string.Join(", ", encrypted));
        }

        public static long Unwrap(string path, string output)
        {
            var source = Path.GetFullPath(path);
            var destination = Path.GetFullPath(output);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                throw new RefusedException("output aliases the input container");
            byte[] data = File.ReadAllBytes(source);
            if (data.LongLength < FirstRegion) throw new RefusedException("file is smaller than the SELF header area");
            uint magic = BitConverter.ToUInt32(data, 0);
            if (magic != SelfMagic && magic != SelfMagicKernel) throw new RefusedException($"not a SELF container (magic {magic:x})");

            var records = ReadRecords(data);
            var (_, elfHeader, headers) = ParseElf(data);
            PlaceSegments(data, headers, records);
            CheckDecrypted(data, headers);

            var regions = new List<Region>();
            var loadRegions = new Dictionary<int, Region>();
            foreach (var h in headers)
            {
                if (h.Type == PtLoad && h.Size != 0)
                {
                    var region = new Region { Old = h.Source, Size = h.Size, Vaddr = h.Vaddr, Align = h.Align };
                    regions.Add(region);
                    loadRegions[h.Index] = region;
                }
            }

            Region? RegionFor(long offset, long size)
            {
                foreach (var region in regions)
                {
                    if (region.Old <= offset && offset + size <= region.Old + region.Size) return region;
                }
                return null;
            }

            foreach (var h in headers)
            {
                if (h.Type == PtLoad || h.Size == 0) continue;
                if (RegionFor(h.Source, h.Size) == null)
                    regions.Add(new Region { Old = h.Source, Size = h.Size, Vaddr = h.Vaddr, Align = h.Align });
            }
            // Python's sort is stable; so is OrderBy.
            regions = regions.OrderBy(r => r.Old).ToList();

            Region? OutputRegion(Header h)
            {
                var host = h.Type == PtLoad ? h : h.Host;
                Region? region = null;
                if (host != null) loadRegions.TryGetValue(host.Index, out region);
                return region ?? RegionFor(h.Source, h.Size);
            }

            foreach (var h in headers)
            {
                if (h.Size == 0 && (h.Type == PtLoad || h.Source == 0)) continue;
                var region = OutputRegion(h);
                long alignment = h.Align;
                if (region == null || alignment <= 1 || (alignment & (alignment - 1)) != 0) continue;
                long current = region.Align;
                current = current != 0 && (current & (current - 1)) == 0 ? current : 1;
                long address = h.Vaddr - (h.Source - region.Old);
                if (PyMod(address - region.Vaddr, Math.Min(current, alignment)) != 0)
                    throw new RefusedException($"segment {h.Index} has incompatible alignment within its output region");
                if (alignment > current)
                {
                    region.Align = alignment;
                    region.Vaddr = address;
                }
            }

            long programOffset = BitConverter.ToInt64(elfHeader, 0x20);
            long cursor = Math.Max(FirstRegion, programOffset + headers.Count * 56);
            long padding = 0;
            foreach (var region in regions)
            {
                long alignment = region.Align != 0 && (region.Align & (region.Align - 1)) == 0 ? region.Align : 1;
                long placedAt = cursor + PyMod(region.Vaddr - cursor, alignment);
                padding += placedAt - cursor;
                region.New = placedAt;
                cursor = placedAt + region.Size;
            }
            long emptyEnd = cursor;
            foreach (var h in headers)
            {
                if (h.Type == PtLoad && h.Size == 0)
                    emptyEnd = Math.Max(emptyEnd, PyMod(h.Vaddr, Math.Max(h.Align, 1)));
            }
            padding += emptyEnd - cursor;
            if (padding > MaxAlignmentPadding)
                throw new RefusedException($"segment alignment requires more than {MaxAlignmentPadding} bytes of output padding");
            cursor = emptyEnd;
            long outputLimit = Math.Max(MaxOutputFactor * data.LongLength, MinOutputLimit);
            if (cursor > outputLimit)
                throw new RefusedException($"the load segments need {cursor} bytes of output, more than the {outputLimit} allowed for a {data.LongLength} byte container");

            var result = new byte[cursor];
            foreach (var region in regions)
                Array.Copy(data, region.Old, result, region.New, region.Size);
            foreach (var h in headers)
            {
                if (h.Size == 0 && (h.Type == PtLoad || h.Source == 0))
                {
                    h.New = h.Type == PtLoad ? PyMod(h.Vaddr, Math.Max(h.Align, 1)) : 0;
                    continue;
                }
                var region = OutputRegion(h);
                h.New = region == null ? h.Source : region.New + (h.Source - region.Old);
                if (h.New + h.Size > result.LongLength)
                    throw new RefusedException($"segment {h.Index} escaped the output");
            }

            Array.Copy(elfHeader, 0, result, 0, 0x40);
            Array.Copy(BitConverter.GetBytes(0L), 0, result, 0x28, 8);
            Array.Copy(BitConverter.GetBytes((ushort)0), 0, result, 0x3C, 2);
            Array.Copy(BitConverter.GetBytes((ushort)0), 0, result, 0x3E, 2);
            foreach (var h in headers)
            {
                long o = programOffset + h.Index * 56;
                void Put(long at, byte[] bytes) => Array.Copy(bytes, 0, result, at, bytes.Length);
                Put(o, BitConverter.GetBytes(h.Type));
                Put(o + 4, BitConverter.GetBytes(h.Flags));
                Put(o + 8, BitConverter.GetBytes(h.New));
                Put(o + 16, BitConverter.GetBytes(h.Vaddr));
                Put(o + 24, BitConverter.GetBytes(h.Vaddr));
                Put(o + 32, BitConverter.GetBytes(h.Size));
                Put(o + 40, BitConverter.GetBytes(h.Memory));
                Put(o + 48, BitConverter.GetBytes(h.Align));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".unwrap-self-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(temporary, result);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return result.LongLength;
        }
    }
}
