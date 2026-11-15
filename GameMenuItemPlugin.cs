using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LaunchBoxSyncPlugin
{
    public class GameMenuItemPlugin : IGameMultiMenuItemPlugin
    {
        private bool GetIsValidForGame(IGame game)
        {
            var fields = game.GetAllCustomFields() ?? Array.Empty<ICustomField>();
            return fields.Any(cf => string.Equals(cf.Name, "Game ID", StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<IGameMenuItem> GetMenuItems(IGame[] selectedGames)
        {
            if (selectedGames == null || selectedGames.Length == 0) return Enumerable.Empty<IGameMenuItem>();

            var validGames = selectedGames.Where(GetIsValidForGame).ToArray();
            if (validGames.Length == 0) return Enumerable.Empty<IGameMenuItem>();

            var syncDataSubItem = new SyncGameMenuItem(
                "Sync Game Data",
                games => SyncAction(games, syncGames: true, syncMedia: false)
            );

            var syncMediaSubItem = new SyncGameMenuItem(
                "Sync Game Media",
                games => SyncAction(games, syncGames: false, syncMedia: true)
            );

            var syncBothSubItem = new SyncGameMenuItem(
                "Sync Game Data & Media",
                games => SyncAction(games, syncGames: true, syncMedia: true)
            );

            var parentMenuItem = new SyncGameMenuItem(
                "Sync External Game",
                new[] { syncDataSubItem, syncMediaSubItem, syncBothSubItem }
            );

            return new[] { parentMenuItem };
        }

        private static void SyncAction(IGame[] games, bool syncGames, bool syncMedia)
        {
            string targetName = games.Length == 1 ? $"'{games[0].Title}'" : $"{games.Length} games";
            var sources = ConfigManager.LoadSources();

            var progressWindow = new ProgressWindow($"Syncing {targetName}...", async (progress, token) =>
            {
                await System.Threading.Tasks.Task.Run(() => SyncEngine.SyncSelectedGames(games, sources, syncGames, syncMedia, progress, token), token);
            });

            if (Application.Current != null && Application.Current.MainWindow != null)
            {
                progressWindow.Owner = Application.Current.MainWindow;
            }
            
            if (progressWindow.ShowDialog() == true)
            {
                MessageBox.Show($"Successfully synced game data for {targetName}", "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (progressWindow.Error != null)
            {
                MessageBox.Show($"Sync failed: {progressWindow.Error.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class SyncGameMenuItem : IGameMenuItem
    {
        private readonly Action<IGame[]>? _onSelectedAction;
        private readonly IGameMenuItem[] _children;

        public string Caption { get; }
        public IEnumerable<IGameMenuItem> Children => _children;
        public bool Enabled => true;
        public System.Drawing.Image Icon { get; }

        public SyncGameMenuItem(string caption, Action<IGame[]> onSelectedAction, bool showIcon = false)
        {
            Caption = caption;
            _onSelectedAction = onSelectedAction;
            _children = Array.Empty<IGameMenuItem>();
            Icon = showIcon ? IconHelper.GetPluginIcon()! : null!;
        }

        public SyncGameMenuItem(string caption, IGameMenuItem[] children, bool showIcon = true)
        {
            Caption = caption;
            _onSelectedAction = null;
            _children = children;
            Icon = showIcon ? IconHelper.GetPluginIcon()! : null!;
        }

        public void OnSelect(IGame[] games)
        {
            _onSelectedAction?.Invoke(games);
        }
    }
}
