using System;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;

namespace LaunchBoxSyncPlugin.Tests
{
    public sealed class PluginEntryTests
    {
        [Fact]
        public void ManifestSyncPlugin_PropertiesMatchExpectedConfiguration()
        {
            var plugin = new ManifestSyncPlugin();

            Assert.Equal("Sync External Games", plugin.Caption);
            Assert.True(plugin.ShowInLaunchBox);
            Assert.False(plugin.ShowInBigBox);
            Assert.False(plugin.AllowInBigBoxWhenLocked);
            _ = plugin.IconImage;
        }

        [Fact]
        public void SystemEventsPlugin_OnEventRaised_SafeWithoutWpfApplication()
        {
            var eventsPlugin = new SystemEventsPlugin();

            eventsPlugin.OnEventRaised("PluginInitialized");
            eventsPlugin.OnEventRaised("LaunchBoxStartupCompleted");
            eventsPlugin.OnEventRaised("SelectionChanged");
        }

        [Fact]
        public void GameMenuItemPlugin_GetMenuItems_FiltersNullEmptyAndUnsyncedGames()
        {
            var plugin = new GameMenuItemPlugin();

            Assert.Empty(plugin.GetMenuItems(null!));
            Assert.Empty(plugin.GetMenuItems(Array.Empty<IGame>()));

            var unsynced = LaunchBoxFakes.CreateGame(id: "lb-1", title: "Unsynced");
            Assert.Empty(plugin.GetMenuItems(new[] { unsynced }));

            var synced = LaunchBoxFakes.CreateGame(id: "lb-2", title: "Synced");
            var gameIdField = synced.AddNewCustomField();
            gameIdField.Name = "game id";
            gameIdField.Value = "ext-2";

            var items = plugin.GetMenuItems(new[] { unsynced, synced }).ToList();
            var parent = Assert.Single(items);

            Assert.Equal("Sync External Game", parent.Caption);
            Assert.True(parent.Enabled);

            var children = parent.Children.ToList();
            Assert.Equal(
                new[] { "Sync Game Data", "Sync Game Media", "Sync Game Data & Media" },
                children.Select(c => c.Caption).ToArray());
        }

        [Fact]
        public void SyncGameMenuItem_InvokesCallbackOnlyWhenLeafAndSupportsChildren()
        {
            IGame[]? captured = null;
            var leaf = new SyncGameMenuItem("Leaf Action", games => captured = games, showIcon: false);

            Assert.Equal("Leaf Action", leaf.Caption);
            Assert.Empty(leaf.Children);
            Assert.True(leaf.Enabled);
            Assert.Null(leaf.Icon);

            var testGames = new[] { LaunchBoxFakes.CreateGame(id: "lb-9", title: "Target") };
            leaf.OnSelect(testGames);
            Assert.Same(testGames, captured);

            var parent = new SyncGameMenuItem("Parent Menu", new[] { leaf }, showIcon: false);
            Assert.Single(parent.Children);
            parent.OnSelect(testGames);
        }
    }
}
