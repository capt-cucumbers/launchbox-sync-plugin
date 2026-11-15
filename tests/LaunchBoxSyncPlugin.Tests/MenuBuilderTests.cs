using System;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;

namespace LaunchBoxSyncPlugin.Tests
{
    public sealed class MenuBuilderTests
    {
        private static void RunOnStaThread(Action action)
        {
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (captured != null)
            {
                throw new Xunit.Sdk.XunitException($"STA action failed: {captured}");
            }
        }

        private static MethodInfo GetMenuBuilderMethod(string name)
        {
            var method = typeof(MenuBuilder).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method!;
        }

        [Fact]
        public void HookToolsMenuAndSetupSubmenus_ReturnFalseWithoutApplicationWindow()
        {
            Assert.False(MenuBuilder.HookToolsMenu());
            Assert.False(MenuBuilder.SetupSubmenus());
        }

        [Fact]
        public void IsHeaderMatch_MatchesMnemonicsAndTextBlocksCaseInsensitively()
        {
            RunOnStaThread(() =>
            {
                var method = GetMenuBuilderMethod("IsHeaderMatch");

                Assert.False((bool)method.Invoke(null, new object?[] { null, "Tools" })!);
                Assert.True((bool)method.Invoke(null, new object?[] { "_Tools", "tools" })!);
                Assert.True((bool)method.Invoke(null, new object?[] { "  Sync External _Games ", "Sync External Games" })!);

                var tb = new TextBlock { Text = "_Tools" };
                Assert.True((bool)method.Invoke(null, new object?[] { tb, "Tools" })!);
                Assert.False((bool)method.Invoke(null, new object?[] { tb, "File" })!);
            });
        }

        [Fact]
        public void FindToolsMenuLogical_LocatesNestedToolsMenuItem()
        {
            RunOnStaThread(() =>
            {
                var method = GetMenuBuilderMethod("FindToolsMenuLogical");

                var fileItem = new MenuItem { Header = "_File" };
                var toolsItem = new MenuItem { Header = "_Tools" };
                var menu = new Menu();
                menu.Items.Add(fileItem);
                menu.Items.Add(toolsItem);

                var panel = new StackPanel();
                panel.Children.Add(menu);

                var found = method.Invoke(null, new object?[] { panel }) as MenuItem;
                Assert.Same(toolsItem, found);

                var emptyPanel = new StackPanel();
                Assert.Null(method.Invoke(null, new object?[] { emptyPanel }));
            });
        }

        [Fact]
        public void PopulateSubmenus_ClearsExistingPlaceholderAndAddsSyncAllItem()
        {
            RunOnStaThread(() =>
            {
                var method = GetMenuBuilderMethod("PopulateSubmenus");

                var pluginMenuItem = new MenuItem { Header = "Sync _External Games" };
                pluginMenuItem.Items.Add(new MenuItem { Header = "Stale Item" });

                var toolsMenu = new MenuItem { Header = "_Tools" };
                toolsMenu.Items.Add(pluginMenuItem);

                method.Invoke(null, new object?[] { toolsMenu });

                Assert.NotEmpty(pluginMenuItem.Items);
                var last = Assert.IsType<MenuItem>(pluginMenuItem.Items[pluginMenuItem.Items.Count - 1]);
                Assert.Equal("Sync ALL Games", last.Header);
                foreach (var item in pluginMenuItem.Items)
                {
                    if (item is MenuItem mi)
                    {
                        Assert.NotEqual("Stale Item", mi.Header);
                    }
                }
            });
        }
    }
}
