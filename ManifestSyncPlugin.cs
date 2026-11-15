using System;
using System.Drawing;
using System.Windows;
using Unbroken.LaunchBox.Plugins;

namespace LaunchBoxSyncPlugin
{
    public class ManifestSyncPlugin : ISystemMenuItemPlugin
    {
        public string Caption => "Sync External Games";
        
        public Image IconImage => IconHelper.GetPluginIcon()!;

        public bool ShowInLaunchBox => true;

        public bool ShowInBigBox => false;

        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected()
        {
            try
            {
                // Ensure submenus are set up and open the management window as a click fallback
                MenuBuilder.SetupSubmenus();

                var window = new SyncWindow();
                if (Application.Current != null && Application.Current.MainWindow != null)
                {
                    window.Owner = Application.Current.MainWindow;
                }
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open plugin window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class SystemEventsPlugin : ISystemEventsPlugin
    {
        public void OnEventRaised(string eventType)
        {
            if (eventType == "PluginInitialized" || 
                eventType == "LaunchBoxStartupCompleted")
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.InvokeAsync(() => {
                        MenuBuilder.Initialize();
                    });
                }
            }
        }
    }
}
