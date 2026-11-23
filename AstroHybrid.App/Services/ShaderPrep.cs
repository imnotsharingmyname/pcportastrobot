using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;

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
            string Stamp(FileInfo f) => f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}" : "none";
            return $"renderer {Stamp(dll)} | list {Stamp(list)} | drivers {Drivers()}";
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

        public readonly struct ProgressUpdate
        {
            public double? Percent { get; }
            public string Status { get; }

            public ProgressUpdate(double? percent, string status)
            {
                Percent = percent;
                Status = status;
            }
        }

        private static readonly Regex ShaderPrecompileRegex =
            new Regex(@"^Shader precompile:\s+(\d+)/(\d+)\s+\((\d+)%\)", RegexOptions.Compiled);

        private static readonly Regex PipelineReplayRegex =
            new Regex(@"^Pipeline replay:\s+(\d+)/(\d+)\s+\((\d+)%\)", RegexOptions.Compiled);

        private static readonly Regex PresetLineRegex =
            new Regex(@"^\s*if not defined\s+([A-Za-z0-9_]+)\s+set\s+""([A-Za-z0-9_]+)=([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static Dictionary<string, string> LoadPresetEnv(string runHybrid)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var presetPath = Path.Combine(runHybrid, "u59-preset.cmd");
            if (!File.Exists(presetPath)) return result;
            try
            {
                foreach (var line in File.ReadAllLines(presetPath))
                {
                    var m = PresetLineRegex.Match(line);
                    if (m.Success)
                        result[m.Groups[2].Value] = m.Groups[3].Value;
                }
            }
            catch { }
            return result;
        }

        public static ProgressUpdate? ParseProgress(string line)
        {
            if (line.StartsWith("Pipeline list:", StringComparison.Ordinal) && line.Contains("shaders match"))
                return new ProgressUpdate(2.0, "Loaded shader list; preparing precompilation...");
            if (line.StartsWith("Prepare shaders: building", StringComparison.Ordinal))
                return new ProgressUpdate(3.0, "Compiling shaders and pipelines...");

            var sm = ShaderPrecompileRegex.Match(line);
            if (sm.Success && int.TryParse(sm.Groups[1].Value, out int sc) && int.TryParse(sm.Groups[2].Value, out int st) && st > 0)
            {
                double pct = 3.0 + (12.0 * sc / st);
                return new ProgressUpdate(pct, $"Compiling shaders: {sc} / {st} ({sm.Groups[3].Value}%)...");
            }

            if (line.StartsWith("Shader precompile: done", StringComparison.Ordinal))
                return new ProgressUpdate(15.0, "Shaders compiled. Building Vulkan pipelines...");

            if (line.StartsWith("Pipeline list:", StringComparison.Ordinal) && line.Contains("pipelines queued"))
                return new ProgressUpdate(16.0, "Building Vulkan pipelines (first-time cache)...");

            var pm = PipelineReplayRegex.Match(line);
            if (pm.Success && int.TryParse(pm.Groups[1].Value, out int pc) && int.TryParse(pm.Groups[2].Value, out int pt) && pt > 0)
            {
                double pct = 16.0 + (81.0 * pc / pt);
                return new ProgressUpdate(pct, $"Building Vulkan pipelines: {pc} / {pt} ({pm.Groups[3].Value}%)...");
            }

            if (line.StartsWith("Pipeline journal: replay done", StringComparison.Ordinal))
                return new ProgressUpdate(98.0, "Saving Vulkan shader cache...");

            if (line.StartsWith("Vulkan pipeline cache: saved", StringComparison.Ordinal) &&
                !line.Contains("(periodic"))
                return new ProgressUpdate(99.0, "Shader cache saved.");

            if (IsDone(line))
                return new ProgressUpdate(100.0, "Shader cache ready!");

            return null;
        }

        public static string? Step(string line) => ParseProgress(line)?.Status;

        public static bool IsDone(string line) => line.StartsWith("Prepare shaders: done", StringComparison.Ordinal);

        public static bool IsReplayDone(string line) => line.StartsWith("Pipeline journal: replay done", StringComparison.Ordinal);

        public static bool IsCacheSaved(string line) => line.StartsWith("Vulkan pipeline cache: saved", StringComparison.Ordinal);

        public static bool IsGameStarting(string line) => line.StartsWith("Transferring control to ELF entry point", StringComparison.Ordinal);

        public static bool IsNoList(string line) => line.StartsWith("Prepare shaders: no usable pipeline list", StringComparison.Ordinal);
    }
}
