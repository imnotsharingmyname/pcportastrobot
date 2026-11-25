using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class GameValidator
    {
        private readonly long _minEbootSize;
        private const string ExpectedTitleId = "PPSA21567";
        private const string ExpectedVersion = "01.019.000";

        // Versions with a known failure, and what to tell the player. 01.007.000 (US PPSA21564 launch version)
        // stops with game asserts in the asset loader on New Game and galaxy travel ("Duplicated definition name
        // []", "Xml file \"(null)\" is broken", OdxAsyncLoader crashes: #54, #47, #80, #130); players who moved to
        // 01.019.000 report that it plays.
        public const string KnownBadVersion = "01.007.000";
        public const string KnownBadVersionWarning =
            "This dump is game version 01.007.000. With this version Astro Bot is known to crash when you start a New Game, " +
            "load the world map or travel to a galaxy (errors such as \"Duplicated definition name\" or \"Xml file is broken\").\n\n" +
            "Version 01.019.000 is the tested one and plays normally. You can still set this version up, but if it crashes, " +
            "please update your dump to 01.019.000.";

        public GameValidator(long minEbootSize = 100_000_000)
        {
            if (minEbootSize < 0) throw new ArgumentOutOfRangeException(nameof(minEbootSize));
            _minEbootSize = minEbootSize;
        }

        public async Task<GameInfo> ValidateGameDumpAsync(string gamePath, IProgress<ArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new ArchiveProgress(0, "Checking the selected game source...", 0, 0));
            if (GameSource.IsArchiveFile(gamePath))
                return await Task.Run(() => ValidateArchive(gamePath, progress, cancellationToken), cancellationToken);

            var gameInfo = new GameInfo
            {
                Path = gamePath,
                LastValidated = DateTime.Now
            };

            try
            {
                // Check if path exists
                if (!Directory.Exists(gamePath))
                {
                    gameInfo.IsValid = false;
                    gameInfo.ValidationError = "Selected folder does not exist.";
                    return gameInfo;
                }

                // Look for eboot.bin
                var ebootPath = Path.Combine(gamePath, "eboot.bin");
                if (!File.Exists(ebootPath))
                {
                    gameInfo.IsValid = false;
                    gameInfo.ValidationError = "eboot.bin not found in the selected folder. Please select the game dump root folder (containing eboot.bin).";
                    return gameInfo;
                }

                gameInfo.EbootPath = ebootPath;

                // Check eboot size
                var ebootInfo = new FileInfo(ebootPath);
                gameInfo.EbootSize = ebootInfo.Length;

                if (ebootInfo.Length < _minEbootSize)
                {
                    gameInfo.IsValid = false;
                    gameInfo.ValidationError = $"eboot.bin is too small ({FormatBytes(ebootInfo.Length)}). Expected at least {FormatBytes(_minEbootSize)}. This may be an incomplete dump.";
                    return gameInfo;
                }

                // Read title ID and version from param.sfx or other metadata files
                ReadGameMetadata(gamePath, gameInfo);

                // Validate title ID
                // Other regions (PPSA21564 and so on) are the same game under another title ID: they share the
                // concept ID, and the title name says ASTRO BOT.
                if (!string.IsNullOrEmpty(gameInfo.TitleId) && gameInfo.TitleId != ExpectedTitleId && !GameSetup.IsAstroBot(gamePath))
                {
                    gameInfo.IsValid = false;
                    gameInfo.ValidationError = $"This dump ({gameInfo.TitleId}) does not appear to be Astro Bot (PPSA21567 or another region of the same game).";
                    return gameInfo;
                }

                // Any version is accepted; ones other than the tested one get a note, not a refusal.
                bool testedVersion = string.IsNullOrEmpty(gameInfo.Version) || gameInfo.Version == ExpectedVersion;

                // A normal extracted dump has a data directory.  A streamed ZArchive dump has
                // app0.zar instead, so do not make users create a meaningless empty data folder
                // just to get through setup.
                var requiredDirs = new[] { "sce_module", "sce_sys" };
                foreach (var dir in requiredDirs)
                {
                    var dirPath = Path.Combine(gamePath, dir);
                    if (!Directory.Exists(dirPath))
                    {
                        gameInfo.IsValid = false;
                        gameInfo.ValidationError = $"Required directory '{dir}' not found in game dump.";
                        return gameInfo;
                    }
                }

                var dataPath = Path.Combine(gamePath, "data");
                var archivePath = Path.Combine(gamePath, "app0.zar");
                if (!Directory.Exists(dataPath) && !File.Exists(archivePath))
                {
                    gameInfo.IsValid = false;
                    gameInfo.ValidationError = "Neither a 'data' directory nor 'app0.zar' was found in the game dump.";
                    return gameInfo;
                }

                gameInfo.IsValid = true;
                gameInfo.ValidationError = null;
                if (gameInfo.Version == KnownBadVersion)
                {
                    gameInfo.Warning = KnownBadVersionWarning;
                    gameInfo.Version += " (known to crash: 01.019.000 recommended)";
                }
                else if (!testedVersion)
                {
                    gameInfo.Version += " (not tested; features this version does not support turn themselves off)";
                }
                progress?.Report(new ArchiveProgress(100, "Game dump validated successfully.", 0, 0));
                return gameInfo;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                gameInfo.IsValid = false;
                gameInfo.ValidationError = $"Validation error: {ex.Message}";
                return gameInfo;
            }
        }

        private GameInfo ValidateArchive(string archivePath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
        {
            var gameInfo = new GameInfo { Path = archivePath, LastValidated = DateTime.Now };
            try
            {
                using (var archive = ZArchiveReader.Open(archivePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (archive.Entries.ContainsKey("app0/") && !archive.Entries.ContainsKey("eboot.bin"))
                        return InvalidArchive(gameInfo, "The ZArchive has a wrapped app0/ root. Store app0's contents at the archive root, without an app0/ prefix.");
                    if (!archive.Entries.TryGetValue("eboot.bin", out var eboot) || eboot.IsDirectory)
                        return InvalidArchive(gameInfo, "eboot.bin not found in the selected ZArchive.");
                    if (eboot.Size > long.MaxValue) return InvalidArchive(gameInfo, "eboot.bin is too large for this launcher.");
                    gameInfo.EbootPath = archivePath + "!/eboot.bin";
                    gameInfo.EbootSize = (long)eboot.Size;
                    if (gameInfo.EbootSize < _minEbootSize)
                        return InvalidArchive(gameInfo, $"eboot.bin is too small ({FormatBytes(gameInfo.EbootSize)}). Expected at least {FormatBytes(_minEbootSize)}. This may be an incomplete dump.");
                    if (!archive.Entries.ContainsKey("sce_module/")) return InvalidArchive(gameInfo, "Required directory 'sce_module' not found in the ZArchive.");
                    if (!archive.Entries.ContainsKey("sce_sys/")) return InvalidArchive(gameInfo, "Required directory 'sce_sys' not found in the ZArchive.");
                    if (!archive.Entries.ContainsKey("data/")) return InvalidArchive(gameInfo, "Required directory 'data' not found in the ZArchive.");

                    var required = new List<ArchiveEntry> { eboot };
                    var moduleCount = 0;
                    foreach (var entry in archive.Entries.Values)
                    {
                        if (entry.IsDirectory) continue;
                        if (entry.Path.StartsWith("sce_module/", StringComparison.OrdinalIgnoreCase) &&
                            entry.Path.EndsWith(".prx", StringComparison.OrdinalIgnoreCase) &&
                            !entry.Path.EndsWith(".guest.prx", StringComparison.OrdinalIgnoreCase))
                        {
                            required.Add(entry);
                            moduleCount++;
                        }
                        if (entry.Path.StartsWith("sce_sys/", StringComparison.OrdinalIgnoreCase)) required.Add(entry);
                    }
                    if (moduleCount == 0) return InvalidArchive(gameInfo, "No original .prx modules were found in 'sce_module'.");

                    string paramJson = "";
                    const ulong maxTextEntrySize = 16 * 1024 * 1024;
                    if (archive.Entries.TryGetValue("sce_sys/param.json", out var paramEntry))
                    {
                        if (paramEntry.Size > maxTextEntrySize) return InvalidArchive(gameInfo, "sce_sys/param.json is unreasonably large.");
                        paramJson = Encoding.UTF8.GetString(archive.ReadEntry(paramEntry, cancellationToken));
                        try
                        {
                            var (titleId, version) = GameSetup.ReadParamText(paramJson);
                            gameInfo.TitleId = titleId;
                            gameInfo.Version = version;
                        }
                        catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is InvalidOperationException) { }
                    }
                    if (archive.Entries.TryGetValue("param.sfx", out var sfxEntry))
                    {
                        if (sfxEntry.Size > maxTextEntrySize) return InvalidArchive(gameInfo, "param.sfx is unreasonably large.");
                        ParseParamSfx(Encoding.UTF8.GetString(archive.ReadEntry(sfxEntry, cancellationToken)), gameInfo);
                    }
                    if (string.IsNullOrEmpty(gameInfo.TitleId))
                    {
                        var fileName = Path.GetFileName(archivePath);
                        if (fileName.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase) && fileName.Length >= 9)
                            gameInfo.TitleId = fileName.Substring(0, 9).ToUpperInvariant();
                    }
                    if (!string.IsNullOrEmpty(gameInfo.TitleId) && gameInfo.TitleId != ExpectedTitleId &&
                        (string.IsNullOrEmpty(paramJson) || !GameSetup.IsAstroBotMetadata(paramJson)))
                        return InvalidArchive(gameInfo, $"This dump ({gameInfo.TitleId}) does not appear to be Astro Bot (PPSA21567 or another region of the same game).");

                    var scaled = progress == null ? null : new InlineProgress<ArchiveProgress>(p =>
                        progress.Report(new ArchiveProgress(Math.Min(99, 5 + p.Percent * 94 / 100), p.Message, p.ProcessedBytes, p.TotalBytes)));
                    cancellationToken.ThrowIfCancellationRequested();
                    archive.ValidateEntries(required, scaled, cancellationToken);
                    ApplyVersionStatus(gameInfo);
                    gameInfo.IsValid = true;
                    gameInfo.ValidationError = null;
                    progress?.Report(new ArchiveProgress(100, "ZArchive validated successfully.", 0, 0));
                    return gameInfo;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                gameInfo.IsValid = false;
                gameInfo.ValidationError = $"ZArchive validation failed: {ex.Message}";
                return gameInfo;
            }
        }

        private static GameInfo InvalidArchive(GameInfo gameInfo, string error)
        {
            gameInfo.IsValid = false;
            gameInfo.ValidationError = error;
            return gameInfo;
        }

        private static void ApplyVersionStatus(GameInfo gameInfo)
        {
            if (gameInfo.Version == KnownBadVersion)
            {
                gameInfo.Warning = KnownBadVersionWarning;
                gameInfo.Version += " (known to crash: 01.019.000 recommended)";
            }
            else if (gameInfo.Version.Length > 0 && gameInfo.Version != ExpectedVersion)
            {
                gameInfo.Version += " (not tested; features this version does not support turn themselves off)";
            }
        }

        private static (string, string) SetupParam(string gamePath)
        {
            try { return GameSetup.ReadParam(gamePath); } catch { return ("", ""); }
        }

        private void ReadGameMetadata(string gamePath, GameInfo gameInfo)
        {
            try
            {
                var (titleId, version) = SetupParam(gamePath);
                if (!string.IsNullOrEmpty(titleId)) gameInfo.TitleId = titleId;
                if (!string.IsNullOrEmpty(version)) gameInfo.Version = version;

                // Try to read from param.sfx
                var paramPath = Path.Combine(gamePath, "param.sfx");
                if (File.Exists(paramPath))
                {
                    var content = File.ReadAllText(paramPath);
                    ParseParamSfx(content, gameInfo);
                }

                // Try to read from sce_sys/param.sfo (PS4/PS5 format)
                var sfoPath = Path.Combine(gamePath, "sce_sys", "param.sfo");
                if (File.Exists(sfoPath) && string.IsNullOrEmpty(gameInfo.TitleId))
                {
                    // Binary format, would need proper parsing
                    // For now, we'll use the folder name or param.sfx
                }

                // Fallback: try to extract from folder name
                if (string.IsNullOrEmpty(gameInfo.TitleId))
                {
                    var folderName = Path.GetFileName(gamePath);
                    if (folderName.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase))
                    {
                        gameInfo.TitleId = folderName.Substring(0, 9).ToUpperInvariant();
                    }
                }
            }
            catch
            {
                // Ignore metadata read errors
            }
        }

        private void ParseParamSfx(string content, GameInfo gameInfo)
        {
            // param.sfx is typically a simple key=value format
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key.Equals("TITLE_ID", StringComparison.OrdinalIgnoreCase))
                        gameInfo.TitleId = value;
                    else if (key.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
                        gameInfo.Version = value;
                }
            }
        }

        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:F1} {suffixes[i]}";
        }
    }
}
