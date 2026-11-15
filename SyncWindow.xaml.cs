using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LaunchBoxSyncPlugin
{
    public partial class SyncWindow : Unbroken.LaunchBox.Windows.Desktop.DialogTheming.Controls.ThemedWindow
    {
        private List<SourceConfig> sources = new List<SourceConfig>();
        private bool _isLoading;

        public SyncWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadIcons();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            BtnRefresh.IsEnabled = false;

            try
            {
                sources = await Task.Run(() =>
                {
                    var loaded = ConfigManager.LoadSources();
                    ConfigManager.ValidateManifests(loaded);
                    return loaded;
                });
                LstSources.ItemsSource = sources;
            }
            finally
            {
                BtnRefresh.IsEnabled = true;
                _isLoading = false;
            }
        }

        private void LoadIcons()
        {
            var img = IconHelper.GetBitmapIcon();
            if (img != null)
            {
                this.Icon = img;
                ImgBtnRefresh.Source = img;
            }
        }

        internal static (bool syncGames, bool syncMedia, string optionText) ResolveSyncOptions(int selectedSyncIndex)
        {
            bool syncGames = selectedSyncIndex == 0 || selectedSyncIndex == 2;
            bool syncMedia = selectedSyncIndex == 1 || selectedSyncIndex == 2;
            string optionText = selectedSyncIndex switch
            {
                0 => "data",
                1 => "media",
                _ => "data and media"
            };
            return (syncGames, syncMedia, optionText);
        }

        private async void BtnRowSync_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SourceConfig source)
            {
                var (syncGames, syncMedia, optionText) = ResolveSyncOptions(source.SelectedSyncIndex);

                var progressWindow = new ProgressWindow($"Syncing {source.ManifestName}...", async (progress, token) =>
                {
                    await Task.Run(() => SyncEngine.SyncSource(source, syncGames, syncMedia, progress, token), token);
                });

                progressWindow.Owner = this;
                
                if (progressWindow.ShowDialog() == true)
                {
                    MessageBox.Show($"Successfully synced game {optionText} for '{source.ManifestName}'", "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (progressWindow.Error != null)
                {
                    MessageBox.Show($"Sync failed: {progressWindow.Error.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show("Sync was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }
    }
}
