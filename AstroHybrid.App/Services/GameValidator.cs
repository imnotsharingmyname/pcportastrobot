using System;
using System.IO;
using System.Threading.Tasks;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class GameValidator
    {
        private const long MinEbootSize = 100_000_000; // ~100MB minimum
        private const string ExpectedTitleId = "PPSA21567";
        private const string ExpectedVersion = "01.019.000";

        public async Task<GameInfo> ValidateGameDumpAsync(string gamePath)
        {
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

                if (ebootInfo.Length < MinEbootSize)
                {
                    gameInfo.IsValid = false;
                    gameInfo.ValidationError = $"eboot.bin is too small ({FormatBytes(ebootInfo.Length)}). Expected at least {FormatBytes(MinEbootSize)}. This may be an incomplete dump.";
                    return gameInfo;
                }

                // Read title ID and version from param.sfx or other metadata files
                ReadGameMetadata(gamePath, gameInfo);

                // Validate title ID
                if (!string.IsNullOrEmpty(gameInfo.TitleId) && gameInfo.TitleId != ExpectedTitleId)
                {
                    gameInfo.IsValid = false;
                    gameInfo.ValidationError = $"Game title ID mismatch. Expected {ExpectedTitleId}, found {gameInfo.TitleId}. This does not appear to be Astro Bot (PPSA21567).";
                    return gameInfo;
                }

                // Any version is accepted; ones other than the tested one get a note, not a refusal.
                bool testedVersion = string.IsNullOrEmpty(gameInfo.Version) || gameInfo.Version == ExpectedVersion;

                // Check for required directories
                var requiredDirs = new[] { "sce_module", "sce_sys", "data" };
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

                gameInfo.IsValid = true;
                gameInfo.ValidationError = null;
                if (!testedVersion)
                {
                    gameInfo.Version += " (not tested; features this version does not support turn themselves off)";
                }
                return gameInfo;
            }
            catch (Exception ex)
            {
                gameInfo.IsValid = false;
                gameInfo.ValidationError = $"Validation error: {ex.Message}";
                return gameInfo;
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