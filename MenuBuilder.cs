using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LaunchBoxSyncPlugin
{
    public static class MenuBuilder
    {
        private static readonly Brush SubmenuBackgroundBrush = CreateSubmenuBackgroundBrush();
        private static System.Windows.Threading.DispatcherTimer? timer;
        private static int attempts = 0;
        private static bool isToolsHooked = false;

        private static Brush CreateSubmenuBackgroundBrush()
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D2D"));
            brush.Freeze();
            return brush;
        }

        public static void Initialize()
        {
            if (isToolsHooked) return;

            timer?.Stop();
            timer = null;

            attempts = 0;
            timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => {
                attempts++;
                if (HookToolsMenu() || attempts > 30)
                {
                    if (timer != null)
                    {
                        timer.Stop();
                        timer = null;
                    }
                }
            };
            timer.Start();
        }

        public static bool HookToolsMenu()
        {
            if (isToolsHooked) return true;

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return false;

            // Find the top-level "Tools" MenuItem in the main window
            var toolsMenu = FindToolsMenu(mainWindow) ?? FindToolsMenuLogical(mainWindow);
            if (toolsMenu == null) return false;

            // Subscribe to SubmenuOpened so we can dynamically populate the submenus
            // when the Tools menu is actually hovered/clicked and WPF generates the containers.
            toolsMenu.SubmenuOpened += ToolsMenu_SubmenuOpened;
            isToolsHooked = true;
            return true;
        }

        private static void ToolsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem toolsMenu)
            {
                // Defer execution slightly to allow WPF to generate child containers
                Application.Current.Dispatcher.InvokeAsync(() => {
                    PopulateSubmenus(toolsMenu);
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        public static bool SetupSubmenus()
        {
            // Fallback for direct clicks/fallback window setup
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return false;

            var toolsMenu = FindToolsMenu(mainWindow) ?? FindToolsMenuLogical(mainWindow);
            if (toolsMenu == null) return false;

            PopulateSubmenus(toolsMenu);
            return true;
        }

        private static void PopulateSubmenus(MenuItem toolsMenu)
        {
            MenuItem? pluginMenuItem = null;

            // 1. Locate the "Sync External Games" MenuItem container inside the Tools menu
            foreach (var item in toolsMenu.Items)
            {
                // Case A: The item is already a MenuItem control
                if (item is MenuItem mi && IsHeaderMatch(mi.Header, "Sync External Games"))
                {
                    pluginMenuItem = mi;
                    break;
                }

                // Case B: The item is a viewmodel/wrapper, get its generated WPF container
                if (toolsMenu.ItemContainerGenerator.ContainerFromItem(item) is MenuItem container &&
                    IsHeaderMatch(container.Header, "Sync External Games"))
                {
                    pluginMenuItem = container;
                    break;
                }
            }

            if (pluginMenuItem == null)
            {
                return;
            }

            try
            {
                // 2. Clear and dynamically rebuild the submenu items
                pluginMenuItem.Items.Clear();

                var sources = ConfigManager.LoadSources();
                foreach (var source in sources)
                {
                    var sourceItem = new MenuItem
                    {
                        Header = $"Sync {source.ManifestName} Games",
                        Background = SubmenuBackgroundBrush,
                        Foreground = Brushes.White
                    };
                    var capturedSource = source;
                    sourceItem.Click += (s, e) => {
                        var progressWindow = new ProgressWindow($"Syncing {capturedSource.ManifestName} Games...", async (progress, token) =>
                        {
                            await Task.Run(() => SyncEngine.SyncSource(capturedSource, true, true, progress, token), token);
                        });

                        if (Application.Current != null && Application.Current.MainWindow != null)
                        {
                            progressWindow.Owner = Application.Current.MainWindow;
                        }
                        
                        if (progressWindow.ShowDialog() == true)
                        {
                            MessageBox.Show($"Successfully synced source '{capturedSource.ManifestName}'!", "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else if (progressWindow.Error != null)
                        {
                            MessageBox.Show($"Sync failed: {progressWindow.Error.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };
                    pluginMenuItem.Items.Add(sourceItem);
                }

                if (sources.Count > 0)
                {
                    pluginMenuItem.Items.Add(new Separator());
                }

                var syncAllItem = new MenuItem
                {
                    Header = "Sync ALL Games",
                    Background = SubmenuBackgroundBrush,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold
                };
                syncAllItem.Click += (s, e) => {
                    var progressWindow = new ProgressWindow("Syncing ALL Games...", async (progress, token) =>
                    {
                        await Task.Run(() => SyncEngine.SyncAll(sources, progress, token), token);
                    });

                    if (Application.Current != null && Application.Current.MainWindow != null)
                    {
                        progressWindow.Owner = Application.Current.MainWindow;
                    }
                    
                    if (progressWindow.ShowDialog() == true)
                    {
                        MessageBox.Show("Successfully synced all sources!", "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (progressWindow.Error != null)
                    {
                        MessageBox.Show($"Sync failed: {progressWindow.Error.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                pluginMenuItem.Items.Add(syncAllItem);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error building submenus: {ex.Message}");
            }
        }

        private static MenuItem? FindToolsMenu(DependencyObject parent)
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is MenuItem menuItem && IsHeaderMatch(menuItem.Header, "Tools"))
                {
                    return menuItem;
                }

                var found = FindToolsMenu(child);
                if (found != null) return found;
            }

            return null;
        }

        private static MenuItem? FindToolsMenuLogical(DependencyObject parent)
        {
            if (parent == null) return null;

            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is DependencyObject depObj)
                {
                    if (depObj is MenuItem menuItem && IsHeaderMatch(menuItem.Header, "Tools"))
                    {
                        return menuItem;
                    }

                    var found = FindToolsMenuLogical(depObj);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private static bool IsHeaderMatch(object? header, string targetText)
        {
            if (header == null) return false;

            string headerStr;
            if (header is TextBlock tb)
            {
                headerStr = tb.Text;
            }
            else
            {
                headerStr = header.ToString() ?? "";
            }

            // Strip mnemonics (underscores like "_Tools" or "Sync _Source Games")
            headerStr = headerStr.Replace("_", "");

            return string.Equals(headerStr.Trim(), targetText.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
