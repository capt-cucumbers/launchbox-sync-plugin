using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LaunchBoxSyncPlugin
{
    public partial class ProgressWindow : Unbroken.LaunchBox.Windows.Desktop.DialogTheming.Controls.ThemedWindow, INotifyPropertyChanged
    {
        private readonly Func<IProgress<string>, CancellationToken, Task> _workTask;
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        private string _mainStatus = "Syncing...";
        public string MainStatus
        {
            get => _mainStatus;
            set
            {
                _mainStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MainStatus)));
            }
        }

        private string _subStatus = "Starting...";
        public string SubStatus
        {
            get => _subStatus;
            set
            {
                _subStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubStatus)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Exception? Error { get; private set; }
        public bool IsCancelled { get; private set; }

        public ProgressWindow(string title, Func<IProgress<string>, CancellationToken, Task> workTask)
        {
            InitializeComponent();
            DataContext = this;

            MainStatus = title;
            _workTask = workTask;
            _cts = new CancellationTokenSource();
            
            var img = IconHelper.GetBitmapIcon();
            if (img != null)
            {
                this.Icon = img;
            }
        }

        internal static async Task<(bool Succeeded, bool IsCancelled, Exception? Error)> ExecuteWorkAsync(
            Func<IProgress<string>, CancellationToken, Task> workTask,
            IProgress<string> progress,
            CancellationToken token)
        {
            bool isCancelled = false;
            Exception? error = null;

            try
            {
                await workTask(progress, token);
            }
            catch (OperationCanceledException)
            {
                isCancelled = true;
            }
            catch (Exception ex)
            {
                error = ex;
            }

            return (error == null && !isCancelled, isCancelled, error);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var progress = new Progress<string>(message =>
            {
                SubStatus = message;
            });

            var result = await ExecuteWorkAsync(_workTask, progress, _cts.Token);
            IsCancelled = result.IsCancelled;
            Error = result.Error;

            try
            {
                DialogResult = result.Succeeded;
            }
            catch (InvalidOperationException)
            {
                // Window may already be closing or not shown modally
            }
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_disposed) return;

            SubStatus = "Cancelling...";
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already closed and disposed
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed
                }
                _cts.Dispose();
            }
            base.OnClosed(e);
        }
    }
}
