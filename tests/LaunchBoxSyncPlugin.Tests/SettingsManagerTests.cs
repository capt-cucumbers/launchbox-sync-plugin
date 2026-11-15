using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LaunchBoxSyncPlugin.Tests
{
    public sealed class SettingsManagerTests : IDisposable
    {
        private readonly string _tempDir;

        public SettingsManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LaunchBoxSyncSettingsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Fact]
        public void SourceConfig_DefaultsAndPropertyChanged_NotifySubscribers()
        {
            var config = new SourceConfig();
            Assert.Equal(string.Empty, config.Key);
            Assert.Equal(2, config.SelectedSyncIndex);
            Assert.Equal(string.Empty, config.ManifestName);
            Assert.Equal(string.Empty, config.ManifestPath);
            Assert.Equal(string.Empty, config.SourceName);
            Assert.False(config.IsValid);
            Assert.Equal(0, config.GameCount);
            Assert.Equal(string.Empty, config.ValidationError);

            var changed = new List<string?>();
            config.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            config.Key = "steam";
            config.SelectedSyncIndex = 1;
            config.ManifestName = "Steam";
            config.ManifestPath = @"C:\manifest.json";
            config.SourceName = "Steam";
            config.IsValid = true;
            config.GameCount = 42;
            config.ValidationError = "warn";

            Assert.Equal(
                new[]
                {
                    nameof(SourceConfig.Key),
                    nameof(SourceConfig.SelectedSyncIndex),
                    nameof(SourceConfig.ManifestName),
                    nameof(SourceConfig.ManifestPath),
                    nameof(SourceConfig.SourceName),
                    nameof(SourceConfig.IsValid),
                    nameof(SourceConfig.GameCount),
                    nameof(SourceConfig.ValidationError)
                },
                changed);
        }

        [Fact]
        public void ConfigManager_LoadSources_ReadsValidConfigAndHandlesMissingOrBadFiles()
        {
            Assert.Empty(ConfigManager.LoadSources(string.Empty));
            Assert.Empty(ConfigManager.LoadSources(Path.Combine(_tempDir, "missing.json")));

            var badPath = Path.Combine(_tempDir, "bad.json");
            File.WriteAllText(badPath, "{ invalid");
            Assert.Empty(ConfigManager.LoadSources(badPath));

            var nullPath = Path.Combine(_tempDir, "null.json");
            File.WriteAllText(nullPath, "null");
            Assert.Empty(ConfigManager.LoadSources(nullPath));

            var configPath = Path.Combine(_tempDir, "config.json");
            File.WriteAllText(
                configPath,
                """
                {
                  "windows_local": {
                    "manifest_name": "Windows - Local",
                    "manifest_path": "C:\\Games\\local\\manifest.json",
                    "source_name": "Custom Local"
                  }
                }
                """);

            var sources = ConfigManager.LoadSources(configPath);
            var source = Assert.Single(sources);
            Assert.Equal("windows_local", source.Key);
            Assert.Equal("Windows - Local", source.ManifestName);
            Assert.Equal(@"C:\Games\local\manifest.json", source.ManifestPath);
            Assert.Equal("Custom Local", source.SourceName);
        }

        [Fact]
        public void ConfigManager_ValidateManifests_SetsStateWithoutUiDispatcher()
        {
            var validPath = Path.Combine(_tempDir, "valid.json");
            File.WriteAllText(validPath, """{"game_1": {"game_name": "One"}, "game_2": {"game_name": "Two"}}""");

            var badJsonPath = Path.Combine(_tempDir, "bad.json");
            File.WriteAllText(badJsonPath, "{ not valid json");

            var nullJsonPath = Path.Combine(_tempDir, "null_manifest.json");
            File.WriteAllText(nullJsonPath, "null");

            var missingPath = Path.Combine(_tempDir, "does_not_exist.json");

            var sources = new List<SourceConfig>
            {
                new SourceConfig { ManifestName = "Valid", ManifestPath = validPath },
                new SourceConfig { ManifestName = "Malformed", ManifestPath = badJsonPath },
                new SourceConfig { ManifestName = "Missing", ManifestPath = missingPath },
                new SourceConfig { ManifestName = "Unset", ManifestPath = string.Empty },
                new SourceConfig { ManifestName = "NullJson", ManifestPath = nullJsonPath }
            };

            ConfigManager.ValidateManifests(sources);

            Assert.True(sources[0].IsValid);
            Assert.Equal(2, sources[0].GameCount);
            Assert.Equal(string.Empty, sources[0].ValidationError);

            Assert.False(sources[1].IsValid);
            Assert.Equal(0, sources[1].GameCount);
            Assert.StartsWith("Validation failed:", sources[1].ValidationError);

            Assert.False(sources[2].IsValid);
            Assert.StartsWith("Manifest file not found:", sources[2].ValidationError);

            Assert.False(sources[3].IsValid);
            Assert.Equal("Manifest path is not configured.", sources[3].ValidationError);

            Assert.False(sources[4].IsValid);
            Assert.Equal("Manifest JSON is empty or invalid.", sources[4].ValidationError);
        }

        [Fact]
        public void IconHelper_SafeCalls_DoNotThrowWhenIconMissing()
        {
            _ = IconHelper.GetPluginIcon();

            Exception? staError = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    _ = IconHelper.GetBitmapIcon();
                }
                catch (Exception ex)
                {
                    staError = ex;
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            Assert.Null(staError);
        }
    }
}
