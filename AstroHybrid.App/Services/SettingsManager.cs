using System;
using System.IO;
using System.Text.Json;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class SettingsManager
    {
        private readonly string _settingsPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private AppSettings? _cachedSettings;

        public SettingsManager(string rootDirectory)
        {
            _settingsPath = Path.Combine(rootDirectory, "Build", "astrohybrid-settings.json");
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        public AppSettings Load()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
                    return _cachedSettings;
                }
                catch
                {
                    _cachedSettings = new AppSettings();
                    return _cachedSettings;
                }
            }

            _cachedSettings = new AppSettings();
            return _cachedSettings;
        }

        public void Save(AppSettings settings)
        {
            _cachedSettings = settings;
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }

        public string GetSettingsPath() => _settingsPath;
    }
}