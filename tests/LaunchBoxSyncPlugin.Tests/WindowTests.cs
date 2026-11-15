using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchBoxSyncPlugin.Tests
{
    public sealed class WindowTests
    {
        [Theory]
        [InlineData(0, true, false, "data")]
        [InlineData(1, false, true, "media")]
        [InlineData(2, true, true, "data and media")]
        [InlineData(-1, false, false, "data and media")]
        public void SyncWindow_ResolveSyncOptions_MapsIndices(
            int selectedIndex,
            bool expectedGames,
            bool expectedMedia,
            string expectedText)
        {
            var (syncGames, syncMedia, optionText) = SyncWindow.ResolveSyncOptions(selectedIndex);

            Assert.Equal(expectedGames, syncGames);
            Assert.Equal(expectedMedia, syncMedia);
            Assert.Equal(expectedText, optionText);
        }

        [Fact]
        public async Task ProgressWindow_ExecuteWorkAsync_ReportsProgressOnSuccess()
        {
            var reported = new List<string>();
            var progress = new SyncProgressCollector(reported);

            var result = await ProgressWindow.ExecuteWorkAsync(
                (p, _) =>
                {
                    p.Report("Step 1");
                    p.Report("Step 2");
                    return Task.CompletedTask;
                },
                progress,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.False(result.IsCancelled);
            Assert.Null(result.Error);
            Assert.Equal(new[] { "Step 1", "Step 2" }, reported);
        }

        [Fact]
        public async Task ProgressWindow_ExecuteWorkAsync_CapturesCancellation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var progress = new SyncProgressCollector(new List<string>());

            var result = await ProgressWindow.ExecuteWorkAsync(
                (_, token) => Task.FromCanceled(token),
                progress,
                cts.Token);

            Assert.False(result.Succeeded);
            Assert.True(result.IsCancelled);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task ProgressWindow_ExecuteWorkAsync_CapturesExceptionWithoutThrowing()
        {
            var progress = new SyncProgressCollector(new List<string>());

            var result = await ProgressWindow.ExecuteWorkAsync(
                (_, _) => Task.FromException(new InvalidOperationException("boom")),
                progress,
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.False(result.IsCancelled);
            var error = Assert.IsType<InvalidOperationException>(result.Error);
            Assert.Equal("boom", error.Message);
        }

        [Fact]
        public void WindowTypes_ExposeExpectedContractsWithoutLaunchBoxHost()
        {
            Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(ProgressWindow)));
            Assert.NotNull(typeof(ProgressWindow).GetProperty(nameof(ProgressWindow.MainStatus)));
            Assert.NotNull(typeof(ProgressWindow).GetProperty(nameof(ProgressWindow.SubStatus)));
            Assert.NotNull(typeof(ProgressWindow).GetProperty(nameof(ProgressWindow.Error)));
            Assert.NotNull(typeof(ProgressWindow).GetProperty(nameof(ProgressWindow.IsCancelled)));
            Assert.NotNull(typeof(SyncWindow).GetConstructor(Type.EmptyTypes));
        }

        private sealed class SyncProgressCollector : IProgress<string>
        {
            private readonly List<string> _messages;

            public SyncProgressCollector(List<string> messages)
            {
                _messages = messages;
            }

            public void Report(string value)
            {
                _messages.Add(value);
            }
        }
    }
}
