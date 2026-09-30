using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AnnieMediaPlayer
{
    internal sealed class TaskbarMediaControls : IDisposable
    {
        public const int PreviousButtonId = 1;
        public const int PlayPauseButtonId = 2;
        public const int NextButtonId = 3;
        public static readonly uint TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");

        private const uint ThumbnailButtonClicked = 0x1800;
        private const uint ButtonMaskIcon = 0x2;
        private const uint ButtonMaskTooltip = 0x4;
        private const uint ButtonMaskFlags = 0x8;
        private const uint ButtonDisabled = 0x1;
        private const int IconSize = 32;

        private IntPtr _previousIcon;
        private IntPtr _playIcon;
        private IntPtr _pauseIcon;
        private IntPtr _nextIcon;
        private IntPtr _playingOverlayIcon;
        private IntPtr _pausedOverlayIcon;
        private ITaskbarList3? _taskbar;
        private IntPtr _window;
        private bool _buttonsAdded;
        private bool _disposed;

        public TaskbarMediaControls()
        {
            try
            {
                _previousIcon = CreateIcon(DrawPrevious);
                _playIcon = CreateIcon(DrawPlay);
                _pauseIcon = CreateIcon(DrawPause);
                _nextIcon = CreateIcon(DrawNext);
            }
            catch (Exception ex)
            {
                DestroyIcons();
                PlayerDiagnostics.Write($"Taskbar media icon creation failed: {ex.Message}");
            }

            try
            {
                _playingOverlayIcon = CreateStatusOverlayIcon(isPlaying: true);
                _pausedOverlayIcon = CreateStatusOverlayIcon(isPlaying: false);
            }
            catch (Exception ex)
            {
                DestroyIcon(ref _playingOverlayIcon);
                DestroyIcon(ref _pausedOverlayIcon);
                PlayerDiagnostics.Write($"Taskbar playback status icon creation failed: {ex.Message}");
            }
        }

        public bool IsInitialized => !_disposed && _buttonsAdded && _taskbar is not null;

        public bool Initialize(IntPtr window, bool canPlay, bool isPlaying, bool canPlayPrevious, bool canPlayNext)
        {
            if (_disposed || window == IntPtr.Zero ||
                _previousIcon == IntPtr.Zero || _playIcon == IntPtr.Zero ||
                _pauseIcon == IntPtr.Zero || _nextIcon == IntPtr.Zero)
                return false;

            ReleaseTaskbar();
            _window = window;

            try
            {
                _taskbar = (ITaskbarList3)new TaskbarList();
                Check(_taskbar.HrInit());
                var buttons = CreateButtons(canPlay, isPlaying, canPlayPrevious, canPlayNext);
                Check(_taskbar.ThumbBarAddButtons(window, (uint)buttons.Length, buttons));
                _buttonsAdded = true;
                return true;
            }
            catch (Exception ex)
            {
                RecordFailure("initialize", ex);
                return false;
            }
        }

        public void UpdateButtons(bool canPlay, bool isPlaying, bool canPlayPrevious, bool canPlayNext)
        {
            if (!IsInitialized)
                return;

            try
            {
                var buttons = CreateButtons(canPlay, isPlaying, canPlayPrevious, canPlayNext);
                Check(_taskbar!.ThumbBarUpdateButtons(_window, (uint)buttons.Length, buttons));
            }
            catch (Exception ex)
            {
                RecordFailure("update thumbnail buttons", ex);
            }
        }

        public void UpdateProgress(bool isOpened, bool isPlaying, TimeSpan position, TimeSpan duration)
        {
            if (!IsInitialized)
                return;

            try
            {
                if (!isOpened)
                {
                    Check(_taskbar!.SetProgressState(_window, TaskbarProgressState.NoProgress));
                    return;
                }

                if (duration <= TimeSpan.Zero || duration == TimeSpan.MaxValue)
                {
                    var unknownDurationState = isPlaying
                        ? TaskbarProgressState.Indeterminate
                        : TaskbarProgressState.NoProgress;
                    Check(_taskbar!.SetProgressState(_window, unknownDurationState));
                    return;
                }

                var progressState = isPlaying ? TaskbarProgressState.Normal : TaskbarProgressState.Paused;
                Check(_taskbar!.SetProgressState(_window, progressState));
                var completed = (ulong)Math.Clamp(position.Ticks, 0, duration.Ticks);
                Check(_taskbar.SetProgressValue(_window, completed, (ulong)duration.Ticks));
            }
            catch (Exception ex)
            {
                RecordFailure("update playback progress", ex);
            }
        }

        public void UpdateStatusOverlay(bool isOpened, bool isPlaying)
        {
            if (!IsInitialized || (_playingOverlayIcon == IntPtr.Zero && _pausedOverlayIcon == IntPtr.Zero))
                return;

            try
            {
                var icon = !isOpened
                    ? IntPtr.Zero
                    : isPlaying ? _playingOverlayIcon : _pausedOverlayIcon;
                var description = !isOpened
                    ? string.Empty
                    : GetTooltip(isPlaying ? "Text.Playing" : "Text.Pause", isPlaying ? "Playing" : "Paused");
                Check(_taskbar!.SetOverlayIcon(_window, icon, description));
            }
            catch (Exception ex)
            {
                RecordFailure("update playback status overlay", ex);
            }
        }

        public static bool TryGetButtonId(IntPtr wParam, out int buttonId)
        {
            var packed = unchecked((ulong)wParam.ToInt64());
            var notification = (uint)((packed >> 16) & 0xFFFF);
            buttonId = (int)(packed & 0xFFFF);
            return notification == ThumbnailButtonClicked &&
                buttonId is PreviousButtonId or PlayPauseButtonId or NextButtonId;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _buttonsAdded = false;
            ReleaseTaskbar();
            DestroyIcons();
        }

        private ThumbButton[] CreateButtons(bool canPlay, bool isPlaying, bool canPlayPrevious, bool canPlayNext) =>
        [
            CreateButton(PreviousButtonId, _previousIcon,
                GetTooltip("Text.Playlist.Previous", "Play previous item"), canPlayPrevious),
            CreateButton(PlayPauseButtonId, isPlaying ? _pauseIcon : _playIcon,
                GetTooltip(isPlaying ? "Text.Pause" : "Text.Play", isPlaying ? "Pause" : "Play"), canPlay),
            CreateButton(NextButtonId, _nextIcon,
                GetTooltip("Text.Playlist.Next", "Play next item"), canPlayNext)
        ];

        private static ThumbButton CreateButton(int id, IntPtr icon, string tooltip, bool enabled) => new()
        {
            Mask = ButtonMaskIcon | ButtonMaskTooltip | ButtonMaskFlags,
            Id = (uint)id,
            Icon = icon,
            ToolTip = tooltip,
            Flags = enabled ? 0 : ButtonDisabled
        };

        private static string GetTooltip(string key, string fallback)
        {
            var value = LanguageManager.GetResourceString(key);
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }

        private static IntPtr CreateIcon(Action<Graphics, Brush> draw)
        {
            using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                draw(graphics, brush);
            }

            return bitmap.GetHicon();
        }

        private static IntPtr CreateStatusOverlayIcon(bool isPlaying)
        {
            using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            using var background = new SolidBrush(isPlaying
                ? Color.FromArgb(255, 43, 156, 82)
                : Color.FromArgb(255, 193, 112, 35));
            using var outline = new Pen(Color.FromArgb(245, 255, 255, 255), 1.5f);
            using var symbol = new SolidBrush(Color.White);

            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillEllipse(background, 1, 1, IconSize - 2, IconSize - 2);
            graphics.DrawEllipse(outline, 1, 1, IconSize - 2, IconSize - 2);

            if (isPlaying)
                graphics.FillPolygon(symbol, new Point[] { new(12, 8), new(24, 16), new(12, 24) });
            else
            {
                graphics.FillRectangle(symbol, 9, 8, 5, 16);
                graphics.FillRectangle(symbol, 18, 8, 5, 16);
            }

            return bitmap.GetHicon();
        }

        private static void DrawPrevious(Graphics graphics, Brush brush)
        {
            graphics.FillRectangle(brush, 4, 6, 3, 20);
            graphics.FillPolygon(brush, new Point[] { new(27, 6), new(12, 16), new(27, 26) });
        }

        private static void DrawPlay(Graphics graphics, Brush brush) =>
            graphics.FillPolygon(brush, new Point[] { new(9, 5), new(26, 16), new(9, 27) });

        private static void DrawPause(Graphics graphics, Brush brush)
        {
            graphics.FillRectangle(brush, 7, 6, 6, 20);
            graphics.FillRectangle(brush, 19, 6, 6, 20);
        }

        private static void DrawNext(Graphics graphics, Brush brush)
        {
            graphics.FillPolygon(brush, new Point[] { new(5, 6), new(20, 16), new(5, 26) });
            graphics.FillRectangle(brush, 25, 6, 3, 20);
        }

        private void RecordFailure(string operation, Exception exception)
        {
            PlayerDiagnostics.Write($"Taskbar media integration failed to {operation}: {exception.Message}");
            _buttonsAdded = false;
            ReleaseTaskbar();
        }

        private void ReleaseTaskbar()
        {
            _buttonsAdded = false;
            if (_taskbar is not null && Marshal.IsComObject(_taskbar))
            {
                try
                {
                    Marshal.FinalReleaseComObject(_taskbar);
                }
                catch (Exception ex)
                {
                    PlayerDiagnostics.Write($"Taskbar media integration cleanup failed: {ex.Message}");
                }
            }

            _taskbar = null;
        }

        private void DestroyIcons()
        {
            DestroyIcon(ref _previousIcon);
            DestroyIcon(ref _playIcon);
            DestroyIcon(ref _pauseIcon);
            DestroyIcon(ref _nextIcon);
            DestroyIcon(ref _playingOverlayIcon);
            DestroyIcon(ref _pausedOverlayIcon);
        }

        private static void DestroyIcon(ref IntPtr icon)
        {
            if (icon != IntPtr.Zero)
            {
                DestroyIconNative(icon);
                icon = IntPtr.Zero;
            }
        }

        private static void Check(int result)
        {
            if (result < 0)
                Marshal.ThrowExceptionForHR(result);
        }

        [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIconNative(IntPtr icon);

        [ComImport]
        [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class TaskbarList
        {
        }

        [ComImport]
        [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr window);
            [PreserveSig] int DeleteTab(IntPtr window);
            [PreserveSig] int ActivateTab(IntPtr window);
            [PreserveSig] int SetActiveAlt(IntPtr window);
            [PreserveSig] int MarkFullscreenWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            [PreserveSig] int SetProgressValue(IntPtr window, ulong completed, ulong total);
            [PreserveSig] int SetProgressState(IntPtr window, TaskbarProgressState state);
            [PreserveSig] int RegisterTab(IntPtr tab, IntPtr owner);
            [PreserveSig] int UnregisterTab(IntPtr tab);
            [PreserveSig] int SetTabOrder(IntPtr tab, IntPtr insertBefore);
            [PreserveSig] int SetTabActive(IntPtr tab, IntPtr owner, uint reserved);
            [PreserveSig] int ThumbBarAddButtons(IntPtr window, uint buttonCount,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ThumbButton[] buttons);
            [PreserveSig] int ThumbBarUpdateButtons(IntPtr window, uint buttonCount,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ThumbButton[] buttons);
            [PreserveSig] int ThumbBarSetImageList(IntPtr window, IntPtr imageList);
            [PreserveSig] int SetOverlayIcon(IntPtr window, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
            [PreserveSig] int SetThumbnailTooltip(IntPtr window, [MarshalAs(UnmanagedType.LPWStr)] string tooltip);
            [PreserveSig] int SetThumbnailClip(IntPtr window, IntPtr clipRectangle);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ThumbButton
        {
            public uint Mask;
            public uint Id;
            public uint Bitmap;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? ToolTip;
            public uint Flags;
        }

        private enum TaskbarProgressState : uint
        {
            NoProgress = 0,
            Indeterminate = 1,
            Normal = 2,
            Error = 4,
            Paused = 8
        }
    }
}
