using System;
using System.IO;
using System.Text.Json;

namespace GitNinja.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private GitNinjaSettings _settings;

        public SettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "GitNinja");

            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);

            _settingsPath = Path.Combine(appFolder, "settings.json");
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<GitNinjaSettings>(json) ?? new GitNinjaSettings();
                }
                catch
                {
                    _settings = new GitNinjaSettings();
                }
            }
            else
            {
                _settings = new GitNinjaSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { /* Silently fail if can't save */ }
        }

        public string? LastProjectPath
        {
            get => _settings.LastProjectPath;
            set
            {
                _settings.LastProjectPath = value;
                SaveSettings();
            }
        }

        public bool HasLastProject => !string.IsNullOrEmpty(LastProjectPath) && Directory.Exists(LastProjectPath);
    }

    public class GitNinjaSettings
    {
        public string? LastProjectPath { get; set; }
    }
}