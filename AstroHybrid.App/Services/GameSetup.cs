using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AstroHybrid.App.Services
{
    // What a game build supports, written by GameSetup next to the game (Build\RunHybrid\astrohybrid-game.json)
    // and read by the launchers. A missing file means the original 01.019.000 install, where everything works.
    public class GameFeatures
    {
        public string TitleId { get; set; } = "";
        public string Version { get; set; } = "";
        // Faster lighting: AstroBot-NonRT.exe exists (both patches found exactly once).
        public bool FasterLighting { get; set; }
        // Real-time game speed writes one global at a fixed address, known for 01.019.000 only.
        public bool RealTimeSpeed { get; set; }
        public DateTime SetupAt { get; set; }

        public const string FileName = "astrohybrid-game.json";
        public const string TestedVersion = "01.019.000";

        public static GameFeatures? Load(string runHybrid)
        {
            try
            {
                var path = Path.Combine(runHybrid, FileName);
                return File.Exists(path) ? JsonSerializer.Deserialize<GameFeatures>(File.ReadAllText(path)) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    // Builds Build\RunHybrid's game files from the user's own decrypted dump with the shipped runtime:
    //  1. app0: a folder of junctions to the dump's folders plus copies of its few top-level files, and a
    //     real sce_module folder (copies of the original modules), so nothing is ever written into the dump;
    //  2. eboot.bin and sce_module\*.prx unwrapped from their SELF containers (SelfUnwrapper, no keys);
    //  3. AnyPS5's relinker (tools\relinker.exe) turns the game code into AstroBot.exe (it also writes the
    //     *.guest.prx modules into app0\sce_module);
    //  4. the faster-lighting and soft-assert byte patches, found by their exact bytes (each must occur
    //     exactly once, else the patch is skipped), make AstroBot-NonRT.exe and AstroBot-NonRT-SoftAssert.exe.
    // On 01.019.000 this reproduces the development build byte for byte (OVERNIGHT.md 2026-11-13).
    public class GameSetup
    {
        private readonly string _root;
        private readonly string _runHybrid;
        private readonly string _relinker;

        public GameSetup(string rootDirectory)
        {
            _root = rootDirectory;
            _runHybrid = Path.Combine(rootDirectory, "Build", "RunHybrid");
            _relinker = Path.Combine(rootDirectory, "tools", "relinker.exe");
        }

        public bool IsReleaseLayout => File.Exists(_relinker) && File.Exists(Path.Combine(_runHybrid, "kyty_gpu.dll"));

        private static readonly (string Name, string Original, string Patched)[] FasterLightingPatches =
        {
            ("non-tiled lighting", "4584F60F84F10800004531F64C8D3D3EBBA90141B430", "4584F6E9F2080000904531F64C8D3D3EBBA90141B430"),
            ("lighting shaders off", "0FB682A00700008887E4050000", "B80000000090908887E4050000"),
        };

        private static readonly (string Name, string Original, string Patched)[] SoftAssertPatches =
        {
            ("soft command-buffer assert",
             "554889e5488d3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbffcd4131c05dc3",
             "554889e5eb2a3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbff909031c05dc3"),
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

        public async Task<GameFeatures> RunAsync(string dump, IProgress<(int Percent, string Message)>? progress = null)
        {
            return await Task.Run(() => Run(dump, progress));
        }

        private GameFeatures Run(string dump, IProgress<(int Percent, string Message)>? progress)
        {
            void Report(int percent, string message) => progress?.Report((percent, message));
            if (!IsReleaseLayout)
                throw new InvalidOperationException("The runtime files are missing (tools\\relinker.exe or Build\\RunHybrid\\kyty_gpu.dll). Extract the whole download again.");

            var (titleId, version) = ReadParam(dump);
            var features = new GameFeatures { TitleId = titleId, Version = version, SetupAt = DateTime.Now };
            var logPath = Path.Combine(_root, "Logs", "setup.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            void Log(string line) => File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\r\n");
            Log($"setup from {dump}: {titleId} {version}");

            Report(5, "Linking the game folder (nothing is copied into or changed in your dump)...");
            BuildApp0(dump, Path.Combine(_runHybrid, "app0"), Log);

            var work = Path.Combine(_root, "Build", "work");
            DeleteTree(work);
            Directory.CreateDirectory(Path.Combine(work, "sce_module"));
            try
            {
                Report(15, "Unwrapping the game code (eboot.bin)...");
                SelfUnwrapper.Unwrap(Path.Combine(dump, "eboot.bin"), Path.Combine(work, "eboot.elf"));
                foreach (var module in OriginalModules(dump))
                {
                    Report(25, $"Unwrapping {Path.GetFileName(module)}...");
                    SelfUnwrapper.Unwrap(module, Path.Combine(work, "sce_module", Path.GetFileName(module)));
                }

                Report(35, "Converting the game code to a Windows program (about a minute)...");
                var exe = Path.Combine(_runHybrid, "AstroBot.exe");
                foreach (var old in Directory.GetFiles(_runHybrid, "AstroBot*.exe").Concat(Directory.GetFiles(_runHybrid, "AstroBot*.json")))
                    File.Delete(old);
                var (code, output) = RunProcess(_relinker, $"--windows --registry \"{Path.Combine(work, "eboot.elf")}\" \"{exe}\"", _runHybrid);
                File.WriteAllText(Path.Combine(_root, "Logs", "setup-relinker.log"), output);
                if (code != 0 || !File.Exists(exe))
                    throw new InvalidOperationException($"The relinker failed (exit {code}); see Logs\\setup-relinker.log.");
                Log($"relinker: exit {code}, {new FileInfo(exe).Length} bytes");
            }
            finally
            {
                DeleteTree(work);
            }

            Report(80, "Applying the faster-lighting patch...");
            var baseBytes = File.ReadAllBytes(Path.Combine(_runHybrid, "AstroBot.exe"));
            var nonRt = (byte[])baseBytes.Clone();
            features.FasterLighting = ApplyAll(nonRt, FasterLightingPatches, Log);
            if (features.FasterLighting)
            {
                WriteVariant(nonRt, "AstroBot-NonRT");
                var soft = (byte[])nonRt.Clone();
                if (ApplyAll(soft, SoftAssertPatches, Log))
                    WriteVariant(soft, "AstroBot-NonRT-SoftAssert");
            }
            features.RealTimeSpeed = version == GameFeatures.TestedVersion;
            Log($"faster lighting: {features.FasterLighting}, real-time speed: {features.RealTimeSpeed}");

            File.WriteAllText(Path.Combine(_runHybrid, GameFeatures.FileName),
                JsonSerializer.Serialize(features, new JsonSerializerOptions { WriteIndented = true }));
            Report(100, "Done.");
            return features;
        }

        public static (string TitleId, string Version) ReadParam(string dump)
        {
            var path = Path.Combine(dump, "sce_sys", "param.json");
            if (!File.Exists(path)) return ("", "");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            string Get(string name) => document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            return (Get("titleId"), Get("contentVersion"));
        }

        private static IEnumerable<string> OriginalModules(string dump)
        {
            var folder = Path.Combine(dump, "sce_module");
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.prx").Where(f => !f.EndsWith(".guest.prx", StringComparison.OrdinalIgnoreCase))
                : Enumerable.Empty<string>();
        }

        private void BuildApp0(string dump, string app0, Action<string> log)
        {
            DeleteTree(app0);
            Directory.CreateDirectory(app0);
            foreach (var dir in Directory.GetDirectories(dump))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("sce_module", StringComparison.OrdinalIgnoreCase)) continue;
                var (code, output) = RunProcess("cmd.exe", $"/c mklink /J \"{Path.Combine(app0, name)}\" \"{dir}\"", app0);
                if (code != 0) throw new InvalidOperationException($"Could not link {name}: {output.Trim()}");
            }
            foreach (var file in Directory.GetFiles(dump))
                LinkOrCopy(file, Path.Combine(app0, Path.GetFileName(file)));
            var modules = Path.Combine(app0, "sce_module");
            Directory.CreateDirectory(modules);
            foreach (var module in OriginalModules(dump))
                File.Copy(module, Path.Combine(modules, Path.GetFileName(module)), true);
            log($"app0: {Directory.GetDirectories(app0).Length} folders, {Directory.GetFiles(app0).Length} files");
        }

        private static void LinkOrCopy(string source, string target)
        {
            if (!CreateHardLink(target, source, IntPtr.Zero))
                File.Copy(source, target, true);
        }

        // Deletes a folder this setup made. Junctions are removed as links, never followed into the dump.
        public static void DeleteTree(string path)
        {
            if (!Directory.Exists(path)) return;
            var info = new DirectoryInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(path, false);
                return;
            }
            foreach (var dir in info.GetDirectories()) DeleteTree(dir.FullName);
            foreach (var file in info.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
                file.Delete();
            }
            Directory.Delete(path, false);
        }

        private static int FindUnique(byte[] data, byte[] pattern, out int count)
        {
            count = 0;
            int found = -1;
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                if (data[i] != pattern[0]) continue;
                int j = 1;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length)
                {
                    count++;
                    found = i;
                    if (count > 1) return -1;
                }
            }
            return count == 1 ? found : -1;
        }

        private static byte[] Hex(string hex) => Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

        private static bool ApplyAll(byte[] data, (string Name, string Original, string Patched)[] patches, Action<string> log)
        {
            var offsets = new List<int>();
            foreach (var (name, original, _) in patches)
            {
                int at = FindUnique(data, Hex(original), out int count);
                if (at < 0)
                {
                    log($"patch '{name}': found {count} times, skipped (this game version differs)");
                    return false;
                }
                offsets.Add(at);
            }
            for (int i = 0; i < patches.Length; i++)
            {
                var bytes = Hex(patches[i].Patched);
                Array.Copy(bytes, 0, data, offsets[i], bytes.Length);
                log($"patch '{patches[i].Name}' at 0x{offsets[i]:x}");
            }
            return true;
        }

        private void WriteVariant(byte[] data, string name)
        {
            File.WriteAllBytes(Path.Combine(_runHybrid, name + ".exe"), data);
            foreach (var json in Directory.GetFiles(_runHybrid, "AstroBot.*json"))
            {
                var suffix = Path.GetFileName(json).Substring("AstroBot".Length);
                File.Copy(json, Path.Combine(_runHybrid, name + suffix), true);
            }
        }

        private static (int Code, string Output) RunProcess(string file, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo(file, arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi)!;
            var output = new StringBuilder();
            process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return (process.ExitCode, output.ToString());
        }
    }
}
