using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AstroHybrid.App.Services
{
    public static class GameSource
    {
        public static bool IsArchive(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return string.Equals(Path.GetExtension(Normalize(path!)), ".zar", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsArchiveFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var source = Normalize(path!);
            return IsArchive(source) && File.Exists(source);
        }

        public static bool Exists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var source = Normalize(path!);
            if (Directory.Exists(source)) return File.Exists(Path.Combine(source, "eboot.bin"));
            return IsArchiveFile(source);
        }

        public static bool SamePath(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(Path.GetFullPath(Normalize(left!)), Path.GetFullPath(Normalize(right!)), StringComparison.OrdinalIgnoreCase);
        }

        // Materializes only what the relinker/startup requires. Optional data remains in the original archive.
        public static string MaterializeForSetup(string archivePath, string outputDirectory,
            IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken = default)
        {
            var output = Path.GetFullPath(outputDirectory);
            var source = Path.GetFullPath(archivePath);
            var outputPrefix = output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (source.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The source archive cannot be inside the generated setup directory.");

            using (var archive = ZArchiveReader.Open(source))
            {
                if (archive.Entries.ContainsKey("app0/") && !archive.Entries.ContainsKey("eboot.bin"))
                    throw new InvalidDataException("The ZArchive has a wrapped app0/ root.");
                if (!archive.Entries.TryGetValue("eboot.bin", out var eboot) || eboot.IsDirectory)
                    throw new InvalidDataException("eboot.bin not found in the selected ZArchive.");
                if (!archive.Entries.ContainsKey("sce_module/") || !archive.Entries.ContainsKey("sce_sys/") || !archive.Entries.ContainsKey("data/"))
                    throw new InvalidDataException("The ZArchive is missing sce_module, sce_sys, or data.");

                var required = new List<ArchiveEntry> { eboot };
                var moduleCount = 0;
                foreach (var entry in archive.Entries.Values)
                {
                    if (entry.IsDirectory) continue;
                    if (entry.Path.StartsWith("sce_module/", StringComparison.OrdinalIgnoreCase) &&
                        entry.Path.EndsWith(".prx", StringComparison.OrdinalIgnoreCase))
                    {
                        if (entry.Path.EndsWith(".guest.prx", StringComparison.OrdinalIgnoreCase)) continue;
                        required.Add(entry);
                        moduleCount++;
                    }
                    else if (entry.Path.StartsWith("sce_sys/", StringComparison.OrdinalIgnoreCase) ||
                             entry.Path.Equals("args.txt", StringComparison.OrdinalIgnoreCase) ||
                             entry.Path.Equals("param.sfx", StringComparison.OrdinalIgnoreCase))
                    {
                        required.Add(entry);
                    }
                }
                if (moduleCount == 0) throw new InvalidDataException("No original .prx modules were found in sce_module.");

                long total = 0;
                foreach (var entry in required) total = checked(total + ToLong(entry.Size));
                long completed = 0;
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(output)) GameSetup.DeleteTree(output);
                Directory.CreateDirectory(output);
                progress?.Report(new ArchiveProgress(total == 0 ? 100 : 0, "Preparing archive setup files...", 0, total));
                foreach (var entry in required)
                {
                    var before = completed;
                    var entryProgress = progress == null ? null : new InlineProgress<ArchiveProgress>(p =>
                    {
                        var done = checked(before + p.ProcessedBytes);
                        var percent = total == 0 ? 100 : (int)Math.Min(99, done * 100 / total);
                        progress.Report(new ArchiveProgress(percent, "Extracting setup files from ZArchive", done, total));
                    });
                    archive.ExtractEntry(entry, output, entryProgress, cancellationToken);
                    completed = checked(completed + ToLong(entry.Size));
                }
                progress?.Report(new ArchiveProgress(100, "Archive setup files are ready.", total, total));
            }
            return output;
        }

        private static long ToLong(ulong size)
        {
            if (size > long.MaxValue) throw new InvalidDataException("An archive setup entry is too large.");
            return (long)size;
        }

        private static string Normalize(string path) => path.Trim().Trim('"').Trim();
    }
}
