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
        // 21:9 render-space patch (the original game constant is 16:9).
        public bool Ultrawide { get; set; }
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

        // Astro Bot's internal render aspect constant: 16:9 -> 2.3888889 (3440x1440 / 21:9-class displays).
        // The patch is kept as a separate executable variant so 16:9 users retain the original framing.
        private static readonly (string Name, string Original, string Patched)[] UltrawidePatches =
        {
            ("ultrawide render aspect", "398EE33F", "8EE31840"),
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
            bool clothPatched = ApplyClothPhysicsPatches(baseBytes, Log);
            bool cmdbufPatched = ApplyCommandBufferPatches(baseBytes, Log);
            if (clothPatched || cmdbufPatched)
                File.WriteAllBytes(Path.Combine(_runHybrid, "AstroBot.exe"), baseBytes);
            var ultrawide = (byte[])baseBytes.Clone();
            features.Ultrawide = ApplyAllOccurrences(ultrawide, UltrawidePatches, Log);
            if (features.Ultrawide)
                WriteVariant(ultrawide, "AstroBot-Ultrawide");
            var nonRt = (byte[])baseBytes.Clone();
            features.FasterLighting = ApplyAll(nonRt, FasterLightingPatches, Log);
            if (features.FasterLighting)
            {
                WriteVariant(nonRt, "AstroBot-NonRT");
                var nonRtUltrawide = (byte[])nonRt.Clone();
                if (features.Ultrawide && ApplyAllOccurrences(nonRtUltrawide, UltrawidePatches, Log))
                    WriteVariant(nonRtUltrawide, "AstroBot-NonRT-Ultrawide");
                var soft = (byte[])nonRt.Clone();
                if (ApplyAll(soft, SoftAssertPatches, Log))
                    WriteVariant(soft, "AstroBot-NonRT-SoftAssert");
                if (features.Ultrawide)
                {
                    var softUltrawide = (byte[])nonRtUltrawide.Clone();
                    if (ApplyAll(softUltrawide, SoftAssertPatches, Log))
                        WriteVariant(softUltrawide, "AstroBot-NonRT-Ultrawide-SoftAssert");
                }
            }
            // Real-time speed needs the frame-rate block; it is found by its initial values in any version (the
            // engine checks the block again before every use and turns the feature off if it is not one).
            features.GameStepAddress = FindGameStepBlock(baseBytes, Log);
            features.RealTimeSpeed = features.GameStepAddress != 0;
            Log($"faster lighting: {features.FasterLighting}, real-time speed: {features.RealTimeSpeed}");

            File.WriteAllText(Path.Combine(_runHybrid, GameFeatures.FileName),
                JsonSerializer.Serialize(features, new JsonSerializerOptions { WriteIndented = true }));
            InstallationManager.SaveManifest(_root,
                InstallationManager.CreateManifest(_root, dump, version, titleId, features.SetupAt));
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

        // Astro Bot in any region: the concept ID (one per game, shared by its regional releases; 10002684 for
        // PPSA21567) or the title name in param.json.
        public const string AstroBotConceptId = "10002684";

        public static bool IsAstroBot(string dump)
        {
            try
            {
                var path = Path.Combine(dump, "sce_sys", "param.json");
                if (!File.Exists(path)) return false;
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.TryGetProperty("conceptId", out var concept) &&
                    (concept.ValueKind == JsonValueKind.Number ? concept.GetRawText() : concept.ValueKind == JsonValueKind.String ? concept.GetString() : "") == AstroBotConceptId)
                    return true;
                if (root.TryGetProperty("localizedParameters", out var localized) && localized.ValueKind == JsonValueKind.Object)
                    foreach (var language in localized.EnumerateObject())
                        if (language.Value.ValueKind == JsonValueKind.Object && language.Value.TryGetProperty("titleName", out var name) &&
                            name.ValueKind == JsonValueKind.String && (name.GetString() ?? "").Replace(" ", "").ToUpperInvariant().Contains("ASTROBOT"))
                            return true;
            }
            catch { }
            return false;
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
            var archive = Path.Combine(_runHybrid, "app0.zar");
            if (File.Exists(archive))
                File.Delete(archive);
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
            {
                // app0.zar belongs beside the app0 directory so the runtime can mount it as the
                // streamed replacement for app0\data. It must not be copied inside app0 itself.
                if (Path.GetFileName(file).Equals("app0.zar", StringComparison.OrdinalIgnoreCase))
                {
                    LinkOrCopy(file, archive);
                    log("app0.zar: linked beside app0 for streamed data");
                    continue;
                }
                LinkOrCopy(file, Path.Combine(app0, Path.GetFileName(file)));
            }
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

        // Keep ClothSimulation and ApCloth stepped at <= 1/60s when real-time game speed runs below 60 FPS (#30, #35).
        // Leaves the main game loop untouched so gameplay stays 100% real-time while cloth/blankets stay stable.
        private static bool ApplyClothPhysicsPatches(byte[] data, Action<string> log)
        {
            int cave1Match = FindUnique(data, Hex("FE08C3" + new string('C', 26) + "488B05"), out int cave1Count);
            int cave2Match = FindUnique(data, Hex("FE08C3" + new string('C', 30) + "C5FA1005"), out int cave2Count);
            int apClothMatch = FindUnique(data, Hex("488B8380000000488B8B88000000C5F8298540F8FFFF4839C8742C"), out int apClothCount);
            if (cave1Match < 0 || cave2Match < 0 || apClothMatch < 0)
            {
                log($"cloth physics patch skipped (matches: cave1={cave1Count}, cave2={cave2Count}, apCloth={apClothCount})");
                return false;
            }
            int cave1Off = cave1Match + 3;
            int getDtUsOff = cave1Match + 16;
            int cave2Off = cave2Match + 3;
            int apClothStoreOff = apClothMatch + 14;
            int cloth1CallOff = FindUniqueCallTo(data, Hex("C578294580"), Hex("C4E1F96EC0"), getDtUsOff, 0, data.Length, out int cloth1Count);
            int cloth2Count = 0;
            int cloth2CallOff = cloth1CallOff < 0 ? -1 : FindUniqueCallTo(data, Hex("C5F82945C0"), Hex("C4E1F96EC0"), getDtUsOff, cloth1CallOff, Math.Min(data.Length, cloth1CallOff + 0x10000), out cloth2Count);
            if (cloth1CallOff < 0 || cloth2CallOff < 0 || !InSameSection(data, cave1Off, cave2Off, apClothStoreOff, cloth1CallOff, cloth2CallOff))
            {
                log($"cloth physics patch skipped (cloth1={cloth1Count}, cloth2={cloth2Count})");
                return false;
            }

            // Pin ClothSimulation::get_dt_us calls to 16666 us (1/60s).
            byte[] fixedUs = Hex("B81A410000");
            Array.Copy(fixedUs, 0, data, cloth1CallOff, fixedUs.Length);
            Array.Copy(fixedUs, 0, data, cloth2CallOff, fixedUs.Length);

            // Clamp ApCloth step dt in xmm0 to min(dt, 1.0f/60.0f) via two INT3 code caves.
            byte[] cave1Code = Hex("C5FA5DC1C5F8298540F8FFFFC3");
            Array.Copy(cave1Code, 0, data, cave1Off, cave1Code.Length);

            byte[] cave2Prefix = Hex("BA8988883CC4E1F96ECA");
            Array.Copy(cave2Prefix, 0, data, cave2Off, cave2Prefix.Length);
            data[cave2Off + 10] = 0xE9;
            Array.Copy(BitConverter.GetBytes(cave1Off - (cave2Off + 15)), 0, data, cave2Off + 11, 4);

            data[apClothStoreOff] = 0xE8;
            Array.Copy(BitConverter.GetBytes(cave2Off - (apClothStoreOff + 5)), 0, data, apClothStoreOff + 1, 4);
            data[apClothStoreOff + 5] = 0x0F;
            data[apClothStoreOff + 6] = 0x1F;
            data[apClothStoreOff + 7] = 0x00;

            log($"cloth physics 60Hz clamp applied (cloth1=0x{cloth1CallOff:x}, cloth2=0x{cloth2CallOff:x}, apCloth=0x{apClothStoreOff:x})");
            return true;
        }

        // Prevent worker command buffer exhaustion and NULL memcpy crashes in heavy scenes (#47, #50, #6).
        // Raises the 5-worker command buffer MiB table (7,7,4,4,6 -> 8,8,8,8,8) and skips memcpy when dst is NULL.
        // Note: Do NOT patch 0x471644 (guard0); its eb 37 jump is shared by non-null allocations and skipping it drops command buffer copies.
        private static bool ApplyCommandBufferPatches(byte[] data, Action<string> log)
        {
            return ApplyAll(data, new[]
            {
                ("cmdbuf_worker_mib_table",
                    "0700000007000000040000000400000006000000100020003000400008000000",
                    "0800000008000000080000000800000008000000100020003000400008000000"),
                ("cmdbuf_null_memcpy_guard1",
                    "4531FFEB0F49C1E4024D29E74983E7FC4D897E18488B7318",
                    "4531FFEB2649C1E4024D29E74983E7FC4D897E18488B7318"),
                ("cmdbuf_null_memcpy_guard2",
                    "4531FFEB0F49C1E4024D29E74983E7FC4D897E18488B7328",
                    "4531FFEB5849C1E4024D29E74983E7FC4D897E18488B7328"),
                ("cmdbuf_null_memcpy_guard3",
                    "4531FFEB134A8D04A5000000004929C74983E7FC4D897E18488B7320",
                    "4531FFEB634A8D04A5000000004929C74983E7FC4D897E18488B7320"),
            }, log);
        }

        private static int FindUniqueCallTo(byte[] data, byte[] prefix, byte[] suffix, int targetOffset, int startOff, int endOff, out int count)
        {
            count = 0;
            int found = -1;
            int total = prefix.Length + 5 + suffix.Length;
            int limit = Math.Min(endOff, data.Length) - total;
            for (int i = Math.Max(0, startOff); i <= limit; i++)
            {
                if (data[i] != prefix[0] || data[i + prefix.Length] != 0xE8) continue;
                bool ok = true;
                for (int j = 1; j < prefix.Length; j++)
                    if (data[i + j] != prefix[j]) { ok = false; break; }
                if (!ok) continue;
                int callOff = i + prefix.Length;
                if (callOff + 5 + BitConverter.ToInt32(data, callOff + 1) != targetOffset) continue;
                for (int j = 0; j < suffix.Length; j++)
                    if (data[callOff + 5 + j] != suffix[j]) { ok = false; break; }
                if (!ok) continue;
                count++;
                found = callOff;
                if (count > 1) return -1;
            }
            return count == 1 ? found : -1;
        }

        private static bool InSameSection(byte[] exe, params int[] offsets)
        {
            int pe = BitConverter.ToInt32(exe, 0x3C);
            int sections = BitConverter.ToUInt16(exe, pe + 6);
            int optional = BitConverter.ToUInt16(exe, pe + 20);
            for (int s = 0; s < sections; s++)
            {
                int o = pe + 24 + optional + s * 40;
                uint rawSize = BitConverter.ToUInt32(exe, o + 16), rawPtr = BitConverter.ToUInt32(exe, o + 20);
                if (offsets.All(off => off >= rawPtr && off < rawPtr + rawSize))
                    return true;
            }
            return false;
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

        // Some game constants are intentionally duplicated in the executable. The ultrawide aspect
        // constant is one of those: every copy must change or the game can still use 16:9 in one path.
        private static bool ApplyAllOccurrences(byte[] data, (string Name, string Original, string Patched)[] patches, Action<string> log)
        {
            foreach (var (name, original, patched) in patches)
            {
                var source = Hex(original);
                var replacement = Hex(patched);
                int count = 0;
                for (int i = 0; i <= data.Length - source.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < source.Length; j++)
                    {
                        if (data[i + j] != source[j]) { match = false; break; }
                    }
                    if (!match) continue;
                    Array.Copy(replacement, 0, data, i, replacement.Length);
                    count++;
                    log($"patch '{name}' at 0x{i:x}");
                    i += source.Length - 1;
                }
                if (count == 0)
                {
                    log($"patch '{name}': found 0 times, skipped (this game version differs)");
                    return false;
                }
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
