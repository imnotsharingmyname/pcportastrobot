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
        // 32:9 render-space patch, used by the 5120x1440 preset.
        public bool SuperUltrawide { get; set; }
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
    //  4. the faster-lighting and soft-assert byte patches, found by their bytes with RIP-relative displacements
    //     left open (each must occur exactly once, else the patch is skipped), make AstroBot-NonRT.exe and
    //     AstroBot-NonRT-SoftAssert.exe.
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

        // "??" marks a RIP-relative displacement: it differs between game versions, matches any byte and is kept
        // as the game has it. 01.018.000 has the same code as 01.019.000 with other displacements (non-tiled
        // lighting at va 0x73b3bcf, the offset of the community 01.018.000 mod in non_tiled_lighting.toml).
        private static readonly (string Name, string Original, string Patched)[] FasterLightingPatches =
        {
            ("non-tiled lighting", "4584F60F84F10800004531F64C8D3D????????41B430", "4584F6E9F2080000904531F64C8D3D????????41B430"),
            ("lighting shaders off", "0FB682A00700008887E4050000", "B80000000090908887E4050000"),
        };

        private static readonly (string Name, string Original, string Patched)[] SoftAssertPatches =
        {
            ("soft command-buffer assert",
             "554889e5488d3d????????488d0d????????4c8d05????????be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d????????31c0e88235fbffcd4131c05dc3",
             "554889e5eb2a3d????????488d0d????????4c8d05????????be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d????????31c0e88235fbff909031c05dc3"),
        };

        // Astro Bot's internal render aspect constant: 16:9 -> 2.3888889 (21:9).
        // Separate executable variants keep 16:9 and 32:9 users on their correct aspect ratio.
        private static readonly (string Name, string Original, string Patched)[] UltrawidePatches =
        {
            ("ultrawide render aspect", "398EE33F", "8EE31840"),
        };

        // 16:9 (1.7777778) -> 32:9 (3.5555556), IEEE-754 little-endian bytes.
        private static readonly (string Name, string Original, string Patched)[] SuperUltrawidePatches =
        {
            ("super-ultrawide render aspect", "398EE33F", "398E6340"),
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
            var basePath = Path.Combine(_runHybrid, "AstroBot.exe");
            var baseBytes = File.ReadAllBytes(basePath);
            bool clothPatched = ApplyClothPhysicsPatches(baseBytes, Log);
            bool cmdbufPatched = ApplyCommandBufferPatches(baseBytes, Log);
            bool garlicPatched = ApplyGarlicPoolPatch(baseBytes, Log);
            if (clothPatched || cmdbufPatched || garlicPatched)
                File.WriteAllBytes(basePath, baseBytes);
            ApplyVariantPatches(baseBytes, features, overwriteExisting: true, Log);
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

        // Creates any missing executable variants (21:9, 32:9, NonRT, SoftAssert) directly from the existing
        // AstroBot.exe so users who updated from an older launcher get ultrawide EXEs without re-dumping.
        public void EnsureVariantExecutables(Action<string>? log = null)
        {
            var basePath = Path.Combine(_runHybrid, "AstroBot.exe");
            if (!File.Exists(basePath))
                return;

            var logFn = log ?? (_ => { });
            var baseBytes = File.ReadAllBytes(basePath);
            var features = GameFeatures.Load(_runHybrid) ?? new GameFeatures { SetupAt = DateTime.UtcNow };
            ApplyVariantPatches(baseBytes, features, overwriteExisting: false, logFn);
            if (features.GameStepAddress == 0)
            {
                features.GameStepAddress = FindGameStepBlock(baseBytes, logFn);
                features.RealTimeSpeed = features.GameStepAddress != 0;
            }
            try
            {
                File.WriteAllText(Path.Combine(_runHybrid, GameFeatures.FileName),
                    JsonSerializer.Serialize(features, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Non-fatal if metadata write fails right before launch.
            }
        }

        private void ApplyVariantPatches(byte[] baseBytes, GameFeatures features, bool overwriteExisting, Action<string> log)
        {
            bool ShouldWrite(string name) => overwriteExisting || !File.Exists(Path.Combine(_runHybrid, name + ".exe"));

            var ultrawide = (byte[])baseBytes.Clone();
            features.Ultrawide = ApplyAllOccurrences(ultrawide, UltrawidePatches, log);
            if (features.Ultrawide && ShouldWrite("AstroBot-Ultrawide"))
                WriteVariant(ultrawide, "AstroBot-Ultrawide");

            var superUltrawide = (byte[])baseBytes.Clone();
            features.SuperUltrawide = ApplyAllOccurrences(superUltrawide, SuperUltrawidePatches, log);
            if (features.SuperUltrawide && ShouldWrite("AstroBot-SuperUltrawide"))
                WriteVariant(superUltrawide, "AstroBot-SuperUltrawide");

            var nonRt = (byte[])baseBytes.Clone();
            features.FasterLighting = ApplyAll(nonRt, FasterLightingPatches, log);
            if (!features.FasterLighting)
            {
                var patched0107 = Patch0107.Apply(baseBytes, log);
                if (patched0107 != null)
                {
                    nonRt = patched0107;
                    features.FasterLighting = true;
                }
            }
            if (features.FasterLighting)
            {
                if (ShouldWrite("AstroBot-NonRT"))
                    WriteVariant(nonRt, "AstroBot-NonRT");

                var nonRtUltrawide = (byte[])nonRt.Clone();
                bool hasNonRtUltrawide = features.Ultrawide && ApplyAllOccurrences(nonRtUltrawide, UltrawidePatches, log);
                if (hasNonRtUltrawide && ShouldWrite("AstroBot-NonRT-Ultrawide"))
                    WriteVariant(nonRtUltrawide, "AstroBot-NonRT-Ultrawide");

                var nonRtSuperUltrawide = (byte[])nonRt.Clone();
                bool hasNonRtSuperUltrawide = features.SuperUltrawide && ApplyAllOccurrences(nonRtSuperUltrawide, SuperUltrawidePatches, log);
                if (hasNonRtSuperUltrawide && ShouldWrite("AstroBot-NonRT-SuperUltrawide"))
                    WriteVariant(nonRtSuperUltrawide, "AstroBot-NonRT-SuperUltrawide");

                var soft = (byte[])nonRt.Clone();
                if (ApplyAll(soft, SoftAssertPatches, log) && ShouldWrite("AstroBot-NonRT-SoftAssert"))
                    WriteVariant(soft, "AstroBot-NonRT-SoftAssert");

                if (hasNonRtUltrawide)
                {
                    var softUltrawide = (byte[])nonRtUltrawide.Clone();
                    if (ApplyAll(softUltrawide, SoftAssertPatches, log) && ShouldWrite("AstroBot-NonRT-Ultrawide-SoftAssert"))
                        WriteVariant(softUltrawide, "AstroBot-NonRT-Ultrawide-SoftAssert");
                }

                if (hasNonRtSuperUltrawide)
                {
                    var softSuperUltrawide = (byte[])nonRtSuperUltrawide.Clone();
                    if (ApplyAll(softSuperUltrawide, SoftAssertPatches, log) && ShouldWrite("AstroBot-NonRT-SuperUltrawide-SoftAssert"))
                        WriteVariant(softSuperUltrawide, "AstroBot-NonRT-SuperUltrawide-SoftAssert");
                }
            }
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

        // Folders of the dump the game reads through APS5_PATH_ALIASES instead of a link in app0 (#135), one
        // "/app0/<name>=<folder>" per line; Run-AstroHybrid.bat passes them to the game.
        public const string App0AliasesFile = "app0-aliases.txt";

        public static Dictionary<string, string> ReadApp0Aliases(string runHybrid)
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(runHybrid, App0AliasesFile);
            if (!File.Exists(path)) return aliases;
            foreach (var item in File.ReadAllText(path).Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = item.IndexOf('=');
                if (separator <= 0) continue;
                var name = item.Substring(0, separator).Trim().Trim('/', '\\');
                if (name.StartsWith("app0/", StringComparison.OrdinalIgnoreCase)) name = name.Substring(5);
                aliases[name] = item.Substring(separator + 1).Trim();
            }
            return aliases;
        }

        private void BuildApp0(string dump, string app0, Action<string> log)
        {
            DeleteTree(app0);
            Directory.CreateDirectory(app0);
            var archive = Path.Combine(_runHybrid, "app0.zar");
            if (File.Exists(archive))
                File.Delete(archive);
            var aliasesFile = Path.Combine(_runHybrid, App0AliasesFile);
            if (File.Exists(aliasesFile))
                File.Delete(aliasesFile);
            var aliases = new List<string>();
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
                        // Neither link is possible: junctions need a local NTFS drive, and a symbolic link needs
                        // Developer Mode or administrator rights. An exFAT or FAT32 external drive (#135) or a network
                        // drive gets here. The game then reads the folder from the dump through AnyPS5's path aliases.
                        aliases.Add($"/app0/{name}={dir}");
                        log($"app0\\{name}: read from the dump through APS5_PATH_ALIASES (junction refused: {output.Trim()}; " +
                            $"symbolic link refused: {linkOutput.Trim()})");
                        continue;
                    }
                    log($"app0\\{name}: symbolic link (junction refused: {output.Trim()})");
                }
            }
            if (aliases.Count > 0)
                File.WriteAllText(aliasesFile, string.Join(";", aliases));
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

        // Build\RunHybrid\app0 holds junctions into the player's dump. When the dump is moved, renamed or on a
        // drive that is not connected, those links dangle and the game stops at start with
        // "param.json or param.sfo not found" (#44). Returns a description of the first broken link, or null
        // when the game folder is usable.
        public static string? DescribeBrokenGameLink(string runHybrid)
        {
            var app0 = Path.Combine(runHybrid, "app0");
            if (!Directory.Exists(app0))
                return "the game folder (Build\\RunHybrid\\app0) is missing";
            foreach (var dir in Directory.GetDirectories(app0))
            {
                if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) == 0)
                    continue;
                try
                {
                    // Listing a junction whose target is gone throws; Directory.Exists does not notice it.
                    using (var entries = Directory.EnumerateFileSystemEntries(dir).GetEnumerator())
                        entries.MoveNext();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return $"the link app0\\{Path.GetFileName(dir)} points to a folder that can't be opened ({ex.Message.Trim()})";
                }
            }
            // Folders read through APS5_PATH_ALIASES (#135) must still be where setup found them.
            var aliases = ReadApp0Aliases(runHybrid);
            foreach (var alias in aliases)
            {
                if (!Directory.Exists(alias.Value))
                    return $"the dump folder {alias.Value} (app0\\{alias.Key}) can't be found; is its drive connected?";
            }
            var sceSys = aliases.TryGetValue("sce_sys", out var aliasedSceSys) ? aliasedSceSys : Path.Combine(app0, "sce_sys");
            if (!File.Exists(Path.Combine(sceSys, "param.json")) && !File.Exists(Path.Combine(sceSys, "param.sfo")))
                return "the game's sce_sys\\param.json can't be found through app0";
            return null;
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

        private const string CommandBufferTable8 = "0800000008000000080000000800000008000000100020003000400008000000";
        private const string CommandBufferTable16 = "1000000010000000100000001000000010000000100020003000400008000000";
        // The game's fixed graphics (Garlic) memory pool: mov eax, 0x87C00000 (2172 MiB). The 16 MiB command
        // buffers (#159) come out of it, and Trunk of Funk then missed it by 256 KB ("Out of graphics memory
        // [Garlic]", a freeze at the end of the loading animation: #166, #173, #178, #179). Raised by 256 MiB to
        // 0x97C00000 (2428 MiB), below the 2564 MiB the same routine picks on its other path.
        private const string GarlicPool = "B80000C08748C785";
        private const string GarlicPoolRaised = "B80000C09748C785";
        // Upgrades of installs made by older versions, one marker file each: an executable listed there was
        // already checked for that upgrade.
        private const string UpgradeMarker = "astrohybrid-upgrades-1.2.7.txt";

        // Installs set up by 1.1.0 - 1.2.6 lack these patches in their AstroBot*.exe files: the 16 MiB
        // command buffers (#159; 1.1.0 - 1.2.5 have 8 MiB) and the larger Garlic pool. Applies whichever is
        // missing in the executable about to start, once: the names already checked are kept in a marker
        // file next to them, so the large file is read only the first time. Patterns that are not there (a
        // new setup, another game version) leave the file as it is. Returns true when it changed the file.
        public bool UpgradeCommandBufferTable(string exeName, Action<string>? log = null)
        {
            log ??= _ => { };
            var marker = Path.Combine(_runHybrid, UpgradeMarker);
            var done = File.Exists(marker)
                ? new HashSet<string>(File.ReadAllLines(marker), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (done.Contains(exeName)) return false;
            var path = Path.Combine(_runHybrid, exeName);
            if (!File.Exists(path)) return false;
            var bytes = File.ReadAllBytes(path);
            bool table = ApplyAll(bytes, new[] { ("cmdbuf_worker_mib_table_16", CommandBufferTable8, CommandBufferTable16) }, log);
            bool garlic = ApplyAll(bytes, new[] { ("garlic_pool_plus_256mib", GarlicPool, GarlicPoolRaised) }, log);
            if (table || garlic) File.WriteAllBytes(path, bytes);
            File.AppendAllLines(marker, new[] { exeName });
            return table || garlic;
        }

        // New setups: the larger Garlic pool, with the 16 MiB command buffers (see GarlicPool).
        private static bool ApplyGarlicPoolPatch(byte[] data, Action<string> log) =>
            ApplyAll(data, new[] { ("garlic_pool_plus_256mib", GarlicPool, GarlicPoolRaised) }, log);

        // Prevent worker command buffer exhaustion and NULL memcpy crashes in heavy scenes (#47, #50, #6, #159).
        // Raises the 5-worker command buffer MiB table (7,7,4,4,6 -> 16 each) and skips memcpy when dst is NULL.
        // 8 MiB each (1.1.0 - 1.2.5) still ran out in booster_construction (#159); with 1 MiB each the
        // crash reproduces in Sky Garden, with 16 MiB it does not happen there or in booster_construction.
        // Note: Do NOT patch 0x471644 (guard0); its eb 37 jump is shared by non-null allocations and skipping it drops command buffer copies.
        private static bool ApplyCommandBufferPatches(byte[] data, Action<string> log)
        {
            return ApplyAll(data, new[]
            {
                ("cmdbuf_worker_mib_table",
                    "0700000007000000040000000400000006000000100020003000400008000000",
                    CommandBufferTable16),
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

        // anyByte[j] = true lets position j match any byte (pattern[0] must be a real byte).
        private static int FindUnique(byte[] data, byte[] pattern, out int count, bool[]? anyByte = null)
        {
            count = 0;
            int found = -1;
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                if (data[i] != pattern[0]) continue;
                int j = 1;
                while (j < pattern.Length && ((anyByte != null && anyByte[j]) || data[i + j] == pattern[j])) j++;
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

        // Hex with "??" for any byte: returns the bytes (0 where "??") and which positions were "??".
        private static (byte[] Bytes, bool[] AnyByte) HexPattern(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            var anyByte = new bool[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                var pair = hex.Substring(i * 2, 2);
                if (pair == "??") anyByte[i] = true;
                else bytes[i] = Convert.ToByte(pair, 16);
            }
            return (bytes, anyByte);
        }

        private static bool ApplyAll(byte[] data, (string Name, string Original, string Patched)[] patches, Action<string> log)
        {
            var offsets = new List<int>();
            foreach (var (name, original, _) in patches)
            {
                var (pattern, anyByte) = HexPattern(original);
                int at = FindUnique(data, pattern, out int count, anyByte);
                if (at < 0)
                {
                    log($"patch '{name}': found {count} times, skipped (this game version differs)");
                    return false;
                }
                offsets.Add(at);
            }
            for (int i = 0; i < patches.Length; i++)
            {
                var (bytes, keep) = HexPattern(patches[i].Patched);
                for (int j = 0; j < bytes.Length; j++)
                    if (!keep[j]) data[offsets[i] + j] = bytes[j];
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
                // "??" works as in ApplyAll: matches any byte and keeps the game's byte when writing.
                var (source, anyByte) = HexPattern(original);
                var (replacement, keep) = HexPattern(patched);
                int count = 0;
                for (int i = 0; i <= data.Length - source.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < source.Length; j++)
                    {
                        if (!anyByte[j] && data[i + j] != source[j]) { match = false; break; }
                    }
                    if (!match) continue;
                    for (int j = 0; j < replacement.Length; j++)
                        if (!keep[j]) data[i + j] = replacement[j];
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

        private static readonly string Tier0ResolutionRetPattern =
            "C707????????C706????????C3C707000D0000C70650070000C3C707000F0000C70670080000";

        private static readonly string Tier0ResolutionJmpPattern =
            "C707????????C706????????EB1AC707000D0000C70650070000EB0CC707000F0000C70670080000";

        // Clang-compiled .rodata lookup tables for dynamic-resolution tiers 0..4:
        //   widths[5]  = { tier0_w, 2432, 2432, 3328, 3840 }  (tier 0 sits at anchor - 4)
        //   heights[5] = { tier0_h, 1368, 1368, 1872, 2160 }  (tier 0 sits at anchor - 4)
        private static readonly string Tier0WidthTableAnchor =
            "8009000080090000000D0000000F0000";

        private static readonly string Tier0HeightTableAnchor =
            "58050000580500005007000070080000";

        // Upscale / post-process output target sites (default 2432x1368 when tier < 2).
        // Site 1: width at +1, height at +38; Site 2: width at +6, height at +16.
        private static readonly string UpscaleOutputSite1Pattern =
            "BB????????B970080000488D35????????488DBDC0FEFFFFC5F857C0664489A0B216050041BC????????80B850010000";

        private static readonly string UpscaleOutputSite2Pattern =
            "B9000F0000BA????????BE70080000BF????????4C8DB3301600000F44D10F44FE";

        // Patches Astro Bot's dynamic-resolution tier-0 width/height across both the 3 switch functions
        // and the 3 .rodata lookup table pairs anchored on tiers 1..4, plus the 2 upscale output target
        // sites (default 2432x1368). Idempotent and reversible back to 1920x1080 -> 2432x1368.
        public bool ApplyRenderResolutionTier0(string exeName, int width, int height, int outWidth = 2432, int outHeight = 1368, Action<string>? log = null)
        {
            var logFn = log ?? (_ => { });
            if (width < 640 || width > 2432 || height < 360 || height > 1368 || (width % 64) != 0 || (height % 8) != 0 ||
                outWidth < 1280 || outWidth > 2432 || outHeight < 720 || outHeight > 1368 || (outWidth % 64) != 0 || (outHeight % 8) != 0)
            {
                logFn($"FSR resolution tier-0: rejected invalid size {width}x{height} -> {outWidth}x{outHeight}");
                return false;
            }

            var path = Path.Combine(_runHybrid, exeName);
            if (!File.Exists(path))
            {
                logFn($"FSR resolution tier-0: {exeName} not found");
                return false;
            }

            var data = File.ReadAllBytes(path);
            var retOffsets = FindAllMatches(data, Tier0ResolutionRetPattern);
            var jmpOffsets = FindAllMatches(data, Tier0ResolutionJmpPattern);
            var widthTableAnchors = FindAllMatches(data, Tier0WidthTableAnchor);
            var heightTableAnchors = FindAllMatches(data, Tier0HeightTableAnchor);
            var outSite1 = FindAllMatches(data, UpscaleOutputSite1Pattern);
            var outSite2 = FindAllMatches(data, UpscaleOutputSite2Pattern);

            if (retOffsets.Count != 2 || jmpOffsets.Count != 1 ||
                widthTableAnchors.Count != 3 || heightTableAnchors.Count != 3 ||
                outSite1.Count != 1 || outSite2.Count != 1)
            {
                logFn($"FSR resolution tier-0: expected 2+1 switch, 3+3 table & 1+1 output sites in {exeName}, " +
                      $"found {retOffsets.Count}+{jmpOffsets.Count}, {widthTableAnchors.Count}+{heightTableAnchors.Count}, {outSite1.Count}+{outSite2.Count}, skipped");
                return false;
            }

            var widthSlots = new List<int>(6);
            var heightSlots = new List<int>(6);
            foreach (int off in retOffsets)
            {
                widthSlots.Add(off + 2);
                heightSlots.Add(off + 8);
            }
            foreach (int off in jmpOffsets)
            {
                widthSlots.Add(off + 2);
                heightSlots.Add(off + 8);
            }
            foreach (int anchor in widthTableAnchors)
            {
                if (anchor < 4) return false;
                widthSlots.Add(anchor - 4);
            }
            foreach (int anchor in heightTableAnchors)
            {
                if (anchor < 4) return false;
                heightSlots.Add(anchor - 4);
            }

            var outWidthSlots = new[] { outSite1[0] + 1, outSite2[0] + 6 };
            var outHeightSlots = new[] { outSite1[0] + 38, outSite2[0] + 16 };

            bool changed = false;
            for (int i = 0; i < widthSlots.Count; i++)
            {
                if (BitConverter.ToInt32(data, widthSlots[i]) != width ||
                    BitConverter.ToInt32(data, heightSlots[i]) != height)
                {
                    changed = true;
                    break;
                }
            }
            if (!changed)
            {
                for (int i = 0; i < outWidthSlots.Length; i++)
                {
                    if (BitConverter.ToInt32(data, outWidthSlots[i]) != outWidth ||
                        BitConverter.ToInt32(data, outHeightSlots[i]) != outHeight)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
            {
                logFn($"FSR resolution tier-0: {exeName} already set to {width}x{height} -> {outWidth}x{outHeight}");
                return true;
            }

            var wBytes = BitConverter.GetBytes(width);
            var hBytes = BitConverter.GetBytes(height);
            var owBytes = BitConverter.GetBytes(outWidth);
            var ohBytes = BitConverter.GetBytes(outHeight);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                foreach (int slot in widthSlots)
                {
                    fs.Seek(slot, SeekOrigin.Begin);
                    fs.Write(wBytes, 0, 4);
                }
                foreach (int slot in heightSlots)
                {
                    fs.Seek(slot, SeekOrigin.Begin);
                    fs.Write(hBytes, 0, 4);
                }
                foreach (int slot in outWidthSlots)
                {
                    fs.Seek(slot, SeekOrigin.Begin);
                    fs.Write(owBytes, 0, 4);
                }
                foreach (int slot in outHeightSlots)
                {
                    fs.Seek(slot, SeekOrigin.Begin);
                    fs.Write(ohBytes, 0, 4);
                }
                fs.Flush(true);
            }

            logFn($"FSR resolution tier-0: patched {exeName} to {width}x{height} -> {outWidth}x{outHeight} (3 switch + 3 table + 2 output sites)");
            return true;
        }

        private static List<int> FindAllMatches(byte[] data, string hexPattern)
        {
            var (pattern, anyByte) = HexPattern(hexPattern);
            var results = new List<int>();
            int limit = data.Length - pattern.Length;
            for (int i = 0; i <= limit; i++)
            {
                if (!anyByte[0] && data[i] != pattern[0]) continue;
                bool match = true;
                for (int j = 1; j < pattern.Length; j++)
                {
                    if (!anyByte[j] && data[i + j] != pattern[j]) { match = false; break; }
                }
                if (match)
                {
                    results.Add(i);
                    i += pattern.Length - 1;
                }
            }
            return results;
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
