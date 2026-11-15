using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Threading.Tasks;

namespace LaunchBoxSyncPlugin
{
    public class SourceConfig : INotifyPropertyChanged
    {
        private string _key = string.Empty;
        private int _selectedSyncIndex = 2;
        private string _manifestName = string.Empty;
        private string _manifestPath = string.Empty;
        private string _sourceName = string.Empty;
        private bool _isValid = false;
        private int _gameCount = 0;
        private string _validationError = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [JsonIgnore]
        public string Key 
        { 
            get => _key; 
            set { _key = value; OnPropertyChanged(nameof(Key)); } 
        }

        [JsonIgnore]
        public int SelectedSyncIndex 
        { 
            get => _selectedSyncIndex; 
            set { _selectedSyncIndex = value; OnPropertyChanged(nameof(SelectedSyncIndex)); } 
        }

        [JsonPropertyName("manifest_name")]
        public string ManifestName 
        { 
            get => _manifestName; 
            set { _manifestName = value; OnPropertyChanged(nameof(ManifestName)); } 
        }

        [JsonPropertyName("manifest_path")]
        public string ManifestPath 
        { 
            get => _manifestPath; 
            set { _manifestPath = value; OnPropertyChanged(nameof(ManifestPath)); } 
        }

        [JsonPropertyName("source_name")]
        public string SourceName
        {
            get => _sourceName;
            set { _sourceName = value; OnPropertyChanged(nameof(SourceName)); }
        }

        [JsonIgnore]
        public bool IsValid
        {
            get => _isValid;
            set { _isValid = value; OnPropertyChanged(nameof(IsValid)); }
        }

        [JsonIgnore]
        public int GameCount
        {
            get => _gameCount;
            set { _gameCount = value; OnPropertyChanged(nameof(GameCount)); }
        }

        [JsonIgnore]
        public string ValidationError
        {
            get => _validationError;
            set { _validationError = value; OnPropertyChanged(nameof(ValidationError)); }
        }
    }

    public static class ConfigManager
    {
        private static readonly string ConfigFilePath;

        static ConfigManager()
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var directory = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            ConfigFilePath = Path.Combine(directory, "config.json");
        }

        public static List<SourceConfig> LoadSources()
        {
            return LoadSources(ConfigFilePath);
        }

        public static List<SourceConfig> LoadSources(string configPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                {
                    System.Diagnostics.Debug.WriteLine($"config.json not found at: {configPath}");
                    return new List<SourceConfig>();
                }

                var json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dict = JsonSerializer.Deserialize<Dictionary<string, SourceConfig>>(json, options);

                if (dict == null) return new List<SourceConfig>();

                var sources = new List<SourceConfig>();
                foreach (var kvp in dict)
                {
                    kvp.Value.Key = kvp.Key;
                    sources.Add(kvp.Value);
                }
                return sources;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load config.json: {ex.Message}");
                return new List<SourceConfig>();
            }
        }

        public static void ValidateManifests(IEnumerable<SourceConfig> sources)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            foreach (var source in sources)
            {
                bool valid = false;
                int count = 0;
                string validationError = string.Empty;

                try
                {
                    if (string.IsNullOrWhiteSpace(source.ManifestPath))
                    {
                        validationError = "Manifest path is not configured.";
                    }
                    else if (!File.Exists(source.ManifestPath))
                    {
                        validationError = $"Manifest file not found: {source.ManifestPath}";
                    }
                    else
                    {
                        var json = File.ReadAllText(source.ManifestPath);
                        var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, options);
                        if (manifest != null)
                        {
                            valid = true;
                            count = manifest.Count;
                        }
                        else
                        {
                            validationError = "Manifest JSON is empty or invalid.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    validationError = $"Validation failed: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"Validation failed for {source.ManifestName}: {ex.Message}");
                }

                source.IsValid = valid;
                source.GameCount = count;
                source.ValidationError = validationError;
            }
        }
    }

    public static class IconHelper
    {
        private static System.Drawing.Image? _cachedIcon;

        public static System.Drawing.Image? GetPluginIcon()
        {
            if (_cachedIcon != null) return _cachedIcon;

            try
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var directory = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var iconPath = Path.Combine(directory, "resources", "GameSyncIcon.png");
                if (File.Exists(iconPath))
                {
                    var bytes = File.ReadAllBytes(iconPath);
                    using var stream = new MemoryStream(bytes);
                    using var image = System.Drawing.Image.FromStream(stream);
                    _cachedIcon = new System.Drawing.Bitmap(image);
                }
            }
            catch
            {
                // ignore
            }
            return _cachedIcon;
        }

        private static System.Windows.Media.Imaging.BitmapImage? _cachedBitmapIcon;

        public static System.Windows.Media.Imaging.BitmapImage? GetBitmapIcon()
        {
            if (_cachedBitmapIcon != null) return _cachedBitmapIcon;

            try
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var directory = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var iconPath = Path.Combine(directory, "resources", "GameSyncIcon.png");
                if (File.Exists(iconPath))
                {
                    var bytes = File.ReadAllBytes(iconPath);
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _cachedBitmapIcon = bitmap;
                }
            }
            catch
            {
                // ignore
            }
            return _cachedBitmapIcon;
        }
    }
}
