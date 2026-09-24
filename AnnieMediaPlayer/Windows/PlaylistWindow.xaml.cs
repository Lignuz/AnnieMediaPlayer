using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using AnnieMediaPlayer.Windows.Panels;

namespace AnnieMediaPlayer.Windows
{
    public partial class PlaylistWindow : AnnieMediaPlayer.BaseWindow
    {
        public event EventHandler? AddFilesRequested;
        public event EventHandler<IReadOnlyList<PlaylistItemViewModel>>? RemoveRequested;
        public event EventHandler? ClearRequested;
        public event EventHandler? PlayPauseRequested;
        public event EventHandler<(IReadOnlyList<PlaylistItemViewModel> Items, int TargetIndex)>? ItemMoveRequested;
        public event EventHandler<PlaylistItemViewModel>? ItemActivated;
        public event EventHandler? MoveCompleted;
        public event EventHandler? ToggleRequested;
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

            PlaylistTitleBar.CloseRequested += (_, e) => ToggleRequested?.Invoke(this, e);

            PreviewKeyDown += PlaylistWindow_PreviewKeyDown;

            var panel = PlaylistPanel;
            panel.AddFilesRequested += (_, e) => AddFilesRequested?.Invoke(this, e);
            panel.RemoveRequested += (_, e) => RemoveRequested?.Invoke(this, e);
            panel.ClearRequested += (_, e) => ClearRequested?.Invoke(this, e);
            panel.PlayPauseRequested += (_, e) => PlayPauseRequested?.Invoke(this, e);
            panel.ItemMoveRequested += (_, move) => ItemMoveRequested?.Invoke(this, move);
            panel.ItemActivated += (_, item) => ItemActivated?.Invoke(this, item);
            StateChanged += (_, _) =>
            {
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
            };
        }

        private void PlaylistWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 목록의 포커스 이동과 버튼의 기본 키 동작을 우선합니다.
            if (e.Key == Key.Tab)
                return;

            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.None &&
                    PlaylistPanel.IsListKeyboardFocusWithin && PlaylistPanel.SelectedItem is { } item)
                {
                    ItemActivated?.Invoke(this, item);
                    e.Handled = true;
                }
                return;
            }

            if (Owner is not MainWindow mainWindow)
                return;

            var modifiers = Keyboard.Modifiers;
            var listFocused = PlaylistPanel.IsListKeyboardFocusWithin;
            var isFunctionShortcut = modifiers == ModifierKeys.None &&
                (e.Key == Key.F4 || e.Key == Key.F5 || e.Key == Key.F8);
            var isSpaceShortcut = e.Key == Key.Space && Keyboard.FocusedElement is not ButtonBase &&
                (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Control);
            var isTransformShortcut = (modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (modifiers & ModifierKeys.Alt) == ModifierKeys.None &&
                (e.Key == Key.R || e.Key == Key.H || e.Key == Key.V);
            var isWindowShortcut = modifiers == ModifierKeys.None &&
                (e.Key == Key.OemTilde || e.Key == Key.D1 || e.Key == Key.D2 ||
                 e.Key == Key.Z || e.Key == Key.X || e.Key == Key.C ||
                 (!listFocused && (e.Key == Key.Left || e.Key == Key.Right)));

            if (!isFunctionShortcut && !isSpaceShortcut && !isTransformShortcut && !isWindowShortcut)
                return;

            KeyboardInputHandler.HandleKeyDown(mainWindow, e);
            e.Handled = true;
        }
    }
}
