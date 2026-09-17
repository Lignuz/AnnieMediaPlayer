using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using AnnieMediaPlayer.Windows.Panels;

namespace AnnieMediaPlayer.Windows
{
    public partial class PlaylistWindow : Window
    {
        public event EventHandler? AddFilesRequested;
        public event EventHandler? RemoveRequested;
        public event EventHandler? ClearRequested;
        public event EventHandler<PlaylistItemViewModel>? ItemDoubleClicked;
        public event EventHandler? MoveCompleted;
        public event EventHandler? ToggleRequested;
        public PlaylistItemViewModel? SelectedItem => ((PlaylistPanelControl)Content).SelectedItem;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_EXITSIZEMOVE = 0x0232;
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MAXIMIZE = 0xF030;
            if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MAXIMIZE)
            {
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_EXITSIZEMOVE)
                MoveCompleted?.Invoke(this, EventArgs.Empty);

            return IntPtr.Zero;
        }

        public PlaylistWindow()
        {
            InitializeComponent();

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.F8)
                    return;

                ToggleRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            var panel = (PlaylistPanelControl)Content;
            panel.AddFilesRequested += (_, e) => AddFilesRequested?.Invoke(this, e);
            panel.RemoveRequested += (_, e) => RemoveRequested?.Invoke(this, e);
            panel.ClearRequested += (_, e) => ClearRequested?.Invoke(this, e);
            panel.ItemDoubleClicked += (_, item) => ItemDoubleClicked?.Invoke(this, item);
            StateChanged += (_, _) =>
            {
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
            };
        }
    }
}
