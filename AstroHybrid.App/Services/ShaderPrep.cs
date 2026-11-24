using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace AstroHybrid.App.Services
{
    // Shader preparation before the first Play: the runtime ships a pipeline list
    // (Build\RunHybrid\PipelineLists\PPSA21567.kyplst, offsets and hashes only, no game code); Kyty builds every
    // shader and pipeline of it (KYTY_PIPELINE_LIST_PREPARE=1), saves its caches and exits. Without it a new
    // player meets hundreds of skipped draws and stutters while the game builds them on the fly.
    // Runs again when kyty_gpu.dll, the list or the graphics driver changes.
    public static class ShaderPrep
    {
        public enum Result { Done, NoList, Failed, Cancelled }

        private const string MarkerFile = "astrohybrid-prepared.txt";

        public static string ListPath(string runHybrid) => Path.Combine(runHybrid, "PipelineLists", "PPSA21567.kyplst");

        private static string MarkerPath(string runHybrid) => Path.Combine(runHybrid, "_PipelineCache", MarkerFile);

        public static bool Needed(string runHybrid)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("ASTROHYBRID_SHADER_PREP") == "0" ||
                    Environment.GetEnvironmentVariable("ASTROHYBRID_SHADER_LIST") == "0")
                    return false;
                if (!File.Exists(ListPath(runHybrid))) return false;
                var marker = MarkerPath(runHybrid);
                return !File.Exists(marker) || File.ReadAllText(marker).Trim() != Key(runHybrid);
            }
            catch
            {
                return false;
            }
        }

        public static void MarkDone(string runHybrid)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(runHybrid, "_PipelineCache"));
                File.WriteAllText(MarkerPath(runHybrid), Key(runHybrid));
            }
            catch { }
        }

        private static string Key(string runHybrid)
        {
            var dll = new FileInfo(Path.Combine(runHybrid, "kyty_gpu.dll"));
            var list = new FileInfo(ListPath(runHybrid));
            var preset = new FileInfo(Path.Combine(runHybrid, "u59-preset.json"));
            string Stamp(FileInfo f) => f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}" : "none";
            return $"renderer {Stamp(dll)} | list {Stamp(list)} | preset {Stamp(preset)} | drivers {Drivers()}";
        }

        private static string Drivers()
        {
            try
            {
                var found = new List<string>();
                using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                foreach (ManagementObject card in searcher.Get())
                {
                    var pnp = card["PNPDeviceID"]?.ToString() ?? "";
                    if (!pnp.StartsWith("PCI", StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add($"{card["Name"]} {card["DriverVersion"]}");
                }
                found.Sort(StringComparer.Ordinal);
                return found.Count == 0 ? "unknown" : string.Join(", ", found);
            }
            catch
            {
                return "unknown";
            }
        }

        public static string? Step(string line)
        {
            if (line.StartsWith("Pipeline list:", StringComparison.Ordinal) && line.Contains("shaders match"))
                return "Loading shader list...";
            if (line.StartsWith("Prepare shaders: building", StringComparison.Ordinal))
                return "Compiling shaders and pipelines...";
            if (line.StartsWith("Pipeline journal: replay done", StringComparison.Ordinal))
                return "Saving shader cache...";
            return null;
        }

        public static bool IsDone(string line) => line.StartsWith("Prepare shaders: done", StringComparison.Ordinal);

        public static bool IsNoList(string line) => line.StartsWith("Prepare shaders: no usable pipeline list", StringComparison.Ordinal);
    }
}
