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
        // The game code was converted for CPUs without AMD-only instructions (relinker --to-intel).
        public bool IntelConversion { get; set; }
        // Guest address of the game's frame-rate block (f64 60.0, f32 1/60, u64 16666 microseconds), found by
        // those values in AstroBot.exe; 0 = not found. Passed to the engine as ASTROHYBRID_GAME_STEP_ADDR.
        public ulong GameStepAddress { get; set; }
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
                // The game uses AMD-only instructions (SSE4a EXTRQ, and VRSQRTPS / VRCPPS whose results differ on
                // other CPUs: the game hangs after the title screen there). --to-intel replaces them with
                // equivalent code; an AMD CPU keeps the original code.
                features.IntelConversion = NeedsIntelConversion();
                Log($"CPU: {CpuVendor()}, Intel conversion: {features.IntelConversion}");
                var options = features.IntelConversion ? "--windows --registry --to-intel" : "--windows --registry";
                var (code, output) = RunProcess(_relinker, $"{options} \"{Path.Combine(work, "eboot.elf")}\" \"{exe}\"", _runHybrid);
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
            // Real-time speed needs the frame-rate block; it is found by its initial values in any version (the
            // engine checks the block again before every use and turns the feature off if it is not one).
            features.GameStepAddress = FindGameStepBlock(baseBytes, Log);
            features.RealTimeSpeed = features.GameStepAddress != 0;
            Log($"faster lighting: {features.FasterLighting}, real-time speed: {features.RealTimeSpeed}");

            File.WriteAllText(Path.Combine(_runHybrid, GameFeatures.FileName),
                JsonSerializer.Serialize(features, new JsonSerializerOptions { WriteIndented = true }));
            Report(100, "Done.");
            return features;
        }

        private static ulong FindGameStepBlock(byte[] exe, Action<string> log)
        {
            var prefix = new byte[12];
            Array.Copy(BitConverter.GetBytes(60.0), 0, prefix, 0, 8);
            Array.Copy(BitConverter.GetBytes(1.0f / 60.0f), 0, prefix, 8, 4);
            int found = -1, count = 0;
            for (int i = 0; i + 24 <= exe.Length; i++)
            {
                if (exe[i] != prefix[0] || exe[i + 6] != prefix[6] || exe[i + 7] != prefix[7]) continue;
                int j = 1;
                while (j < 12 && exe[i + j] == prefix[j]) j++;
                if (j < 12) continue;
                ulong us = BitConverter.ToUInt64(exe, i + 16);
                if (us != 16666 && us != 16667) continue;
                count++;
                found = i;
            }
            if (count != 1)
            {
                log($"frame-rate block: found {count} times, real-time speed off");
                return 0;
            }
            // File offset -> guest address through the PE section table (image base + rva).
            int pe = BitConverter.ToInt32(exe, 0x3C);
            int sections = BitConverter.ToUInt16(exe, pe + 6);
            int optional = BitConverter.ToUInt16(exe, pe + 20);
            ulong imageBase = BitConverter.ToUInt64(exe, pe + 24 + 24);
            for (int s = 0; s < sections; s++)
            {
                int o = pe + 24 + optional + s * 40;
                uint va = BitConverter.ToUInt32(exe, o + 12), rawSize = BitConverter.ToUInt32(exe, o + 16), rawPtr = BitConverter.ToUInt32(exe, o + 20);
                if (found >= rawPtr && found < rawPtr + rawSize)
                {
                    ulong address = imageBase + va + (ulong)(found - rawPtr);
                    log($"frame-rate block at 0x{address:x}");
                    return address;
                }
            }
            log("frame-rate block: outside every section, real-time speed off");
            return 0;
        }

        public static string CpuVendor()
        {
            try
            {
                return Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier", null) as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        // ASTROHYBRID_SETUP_TO_INTEL=1 / 0 forces the choice; otherwise every CPU that is not an AMD one.
        public static bool NeedsIntelConversion()
        {
            var forced = Environment.GetEnvironmentVariable("ASTROHYBRID_SETUP_TO_INTEL");
            if (forced == "1") return true;
            if (forced == "0") return false;
            return CpuVendor().IndexOf("AMD", StringComparison.OrdinalIgnoreCase) < 0;
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
                if (code != 0)
                {
                    // Folder junctions cannot point to a network drive; a directory symbolic link can, when Windows
                    // allows this user to make one (Developer Mode, or AstroHybrid run as administrator).
                    var (linkCode, linkOutput) = RunProcess("cmd.exe", $"/c mklink /D \"{Path.Combine(app0, name)}\" \"{dir}\"", app0);
                    if (linkCode != 0)
                    {
                        throw new InvalidOperationException(
                            $"Could not link the dump's '{name}' folder ({output.Trim()}). If the dump is on a network drive, turn on Developer Mode " +
                            "(Windows Settings > System > For developers) or run AstroHybrid once as administrator, then press Install again; " +
                            "or copy the dump to a local drive.");
                    }
                    log($"app0\\{name}: symbolic link (junction refused: {output.Trim()})");
                }
            }
            foreach (var file in Directory.GetFiles(dump))
                LinkOrCopy(file, Path.Combine(app0, Path.GetFileName(file)));
            // args.txt carries the game's start arguments; some dumps leave it out, and without "-mode product" the
            // game starts as a development build that reads its data from the developer's host and stops at its
            // error screen. The standard arguments go into app0 (never into the dump).
            var args = Path.Combine(app0, "args.txt");
            if (!File.Exists(args))
            {
                File.WriteAllText(args, "-package -sequence product -mode product -odxBinary -perfDisplay false -debugDisplay false -debugInput false\r\n");
                log("app0\\args.txt: missing in the dump, standard product arguments written");
            }
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
