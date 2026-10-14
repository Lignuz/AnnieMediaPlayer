using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AnnieMediaPlayer.Options;

namespace AnnieMediaPlayer
{
    public partial class MainWindow
    {
        private readonly DispatcherTimer _commandMessageTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
        private int _commandMessageVersion;
        private long _commandMessageFadeInTick;
        private bool _commandMessageFadingOut;
        private long _lastWindowStateMessage;

        private void InitializeCommandMessages()
        {
            _commandMessageTimer.Tick += (_, _) =>
            {
                _commandMessageTimer.Stop();
                var version = _commandMessageVersion;
                var fade = new DoubleAnimation(CommandMessageHost.Opacity, 0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, _) =>
                {
                    if (version != _commandMessageVersion)
                        return;
                    _commandMessageFadingOut = false;
                    CommandMessageHost.Visibility = Visibility.Collapsed;
                };
                _commandMessageFadingOut = true;
                CommandMessageHost.BeginAnimation(OpacityProperty, fade);
            };
            OptionViewModel.Instance.CurrentOption.PropertyChanged += CommandMessageOptionChanged;
            vm.PropertyChanged += CommandMessageViewModelChanged;
            grid_center.SizeChanged += CommandMessageLayoutChanged;
            panel_titlebar.SizeChanged += CommandMessageLayoutChanged;
            panel_control.SizeChanged += CommandMessageLayoutChanged;
            SizeChanged += CommandMessageWindowSizeChanged;
            StateChanged += CommandMessageWindowStateChanged;
            Closed += (_, _) =>
            {
                OptionViewModel.Instance.CurrentOption.PropertyChanged -= CommandMessageOptionChanged;
                vm.PropertyChanged -= CommandMessageViewModelChanged;
                grid_center.SizeChanged -= CommandMessageLayoutChanged;
                panel_titlebar.SizeChanged -= CommandMessageLayoutChanged;
                panel_control.SizeChanged -= CommandMessageLayoutChanged;
                SizeChanged -= CommandMessageWindowSizeChanged;
                StateChanged -= CommandMessageWindowStateChanged;
                HideCommandMessage();
            };
        }

        private void CommandMessageOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Option.ShowCommandMessages) && !OptionViewModel.Instance.CurrentOption.ShowCommandMessages)
                HideCommandMessage();
            if (e.PropertyName == nameof(Option.UseOverlayControl))
                UpdateCommandMessageLayout();
            var option = OptionViewModel.Instance.CurrentOption;
            switch (e.PropertyName)
            {
                case nameof(Option.AudioViewMode):
                    ShowCommandMessage("Text.Message.AudioMode", option.AudioViewMode == AudioViewMode.Basic
                        ? LanguageManager.GetResourceString("Text.AudioViewMode.Basic") : option.AudioViewMode.ToString());
                    break;
                case nameof(Option.ShowLyrics): ShowCommandMessage("Text.Message.Lyrics", MessageToggle(option.ShowLyrics)); break;
                case nameof(Option.ShufflePlayback): ShowCommandMessage(option.ShufflePlayback ? "Text.Playback.Shuffle.On" : "Text.Playback.Shuffle.Off"); break;
                case nameof(Option.PlaylistPlaybackMode):
                    ShowCommandMessage(option.PlaylistPlaybackMode switch
                    {
                        PlaylistPlaybackMode.Playlist => "Text.Playback.RepeatPlaylist",
                        PlaylistPlaybackMode.Track => "Text.Playback.RepeatTrack",
                        PlaylistPlaybackMode.CurrentTrackOnly => "Text.Playback.CurrentTrackOnly",
                        _ => "Text.Playback.Sequential"
                    });
                    break;
                case nameof(Option.RotateAngle): ShowCommandMessage("Text.Message.Rotation", (int)option.RotateAngle); break;
                case nameof(Option.UseFlipHorizontal): ShowCommandMessage("Text.Message.FlipHorizontal", MessageToggle(option.UseFlipHorizontal)); break;
                case nameof(Option.UseFlipVertical): ShowCommandMessage("Text.Message.FlipVertical", MessageToggle(option.UseFlipVertical)); break;
                case nameof(Option.UseOverlayControl): ShowCommandMessage("Text.Message.Overlay", MessageToggle(option.UseOverlayControl)); break;
                case nameof(Option.SelectedTheme): ShowCommandMessage("Text.Message.Theme", LanguageManager.GetResourceString($"Text.Themes.{option.SelectedTheme}")); break;
                case nameof(Option.SelectedLanguage):
                    ShowCommandMessage("Text.Message.Setting", LanguageManager.GetResourceString("Text.Languages"),
                        LanguageManager.GetResourceString(option.SelectedLanguage == Languages.ko ? "Text.Languages.Korean" : "Text.Languages.English"));
                    break;
                case nameof(Option.UseTransitionFade): ShowOptionToggle("Text.UseTransitionFade", option.UseTransitionFade); break;
                case nameof(Option.UsePlaylistPersistence): ShowOptionToggle("Text.UsePlaylistPersistence", option.UsePlaylistPersistence); break;
                case nameof(Option.RememberWindowPositions): ShowOptionToggle("Text.RememberWindowPositions", option.RememberWindowPositions); break;
                case nameof(Option.UseSeekFramePreview): ShowOptionToggle("Text.SeekFramePreview", option.UseSeekFramePreview); break;
                case nameof(Option.UseHWAccelerator): ShowOptionToggle("Text.UseHWAccelerator", option.UseHWAccelerator); break;
                case nameof(Option.UseOpenPlay): ShowOptionToggle("Text.UseOpenPlay", option.UseOpenPlay); break;
                case nameof(Option.UseLegacyAudioOut): ShowOptionToggle("Text.UseLegacyAudioOut", option.UseLegacyAudioOut); break;
            }
        }

        private void CommandMessageViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsPropertiesPanelOpen))
                ShowCommandMessage("Text.Message.Properties", MessageToggle(vm.IsPropertiesPanelOpen));
        }

        private void ShowOptionToggle(string key, bool enabled) => ShowCommandMessage("Text.Message.Setting", LanguageManager.GetResourceString(key), MessageToggle(enabled));

        private static string MessageToggle(bool enabled) => LanguageManager.GetResourceString(enabled ? "Text.Message.On" : "Text.Message.Off");

        private void CommandMessageWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || WindowState != WindowState.Normal || _isClosing ||
                !OptionViewModel.Instance.CurrentOption.ShowCommandMessages || Environment.TickCount64 - _lastWindowStateMessage < 350)
                return;
            // 크기를 바꾸는 동안 바뀐 크기를 바로 표시합니다.
            ShowCommandMessage("Text.Message.WindowSize", Math.Round(ActualWidth), Math.Round(ActualHeight));
        }

        private void CommandMessageWindowStateChanged(object? sender, EventArgs e)
        {
            _lastWindowStateMessage = Environment.TickCount64;
            if (WindowState != WindowState.Minimized)
                ShowCommandMessage(WindowState == WindowState.Maximized ? "Text.Message.Maximized" : "Text.Message.Restored");
        }

        public void NotifySpeedChanged()
        {
            if (VideoPlayerController.IsNormalSpeed || vm.IsAudioOnly)
                ShowCommandMessage("Text.Message.Speed", VideoPlayerController.GetSpeedRatio().ToString("0.##", CultureInfo.CurrentCulture));
            else
                ShowCommandMessage("Text.Message.FrameInterval", VideoPlayerController.PlaybackSpeeds[VideoPlayerController.SpeedIndex].TotalSeconds.ToString("0.###", CultureInfo.CurrentCulture));
        }

        public async Task SeekWithMessageAsync(TimeSpan position)
        {
            if (await VideoPlayerController.Seek(position))
                ShowCommandMessage("Text.Message.Seek", MessagePosition(position));
        }

        public async Task SeekStepWithMessageAsync(bool next)
        {
            if (await VideoPlayerController.SeekStep(next))
                ShowCommandMessage("Text.Message.Seek", MessagePosition(VideoPlayerController.CurrentPosition));
        }

        private static string MessagePosition(TimeSpan position) => $"{(int)position.TotalHours:00}:{position.Minutes:00}:{position.Seconds:00}";

        private void CommandMessageLayoutChanged(object sender, SizeChangedEventArgs e) => UpdateCommandMessageLayout();

        private void UpdateCommandMessageLayout()
        {
            // 제목 표시줄과 하단 컨트롤 패널의 자리는 숨김 중에도 확보해 페이드 중 겹침을 막습니다.
            var top = UseOverlayControl ? panel_titlebar.ActualHeight : 0;
            var bottom = UseOverlayControl ? panel_control.ActualHeight : 0;
            CommandMessageHost.Margin = new Thickness(16, top + 16, 16, bottom + 16);
            CommandMessageHost.MaxHeight = Math.Max(0, grid_center.ActualHeight - top - bottom - 32);
        }

        private void HideCommandMessage()
        {
            _commandMessageVersion++;
            _commandMessageTimer.Stop();
            _commandMessageFadingOut = false;
            CommandMessageHost.BeginAnimation(OpacityProperty, null);
            CommandMessageHost.Visibility = Visibility.Collapsed;
        }

        private void ShowCommandMessage(string resourceKey, params object[] values)
        {
            if (!IsLoaded || _isClosing || !OptionViewModel.Instance.CurrentOption.ShowCommandMessages)
                return;

            // 연속된 볼륨 조절은 최신 값으로 교체하고 표시 시간을 연장합니다.
            _commandMessageVersion++;
            _commandMessageTimer.Stop();
            var text = LanguageManager.GetResourceString(resourceKey);
            CommandMessageText.Text = values.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, values);
            UpdateCommandMessageLayout();
            // 페이드 인이 진행 중이거나 이미 완전히 보이면 다시 시작하지 않아, 크기 변경처럼 잦은 갱신에서도 흐려지거나 깜빡이지 않습니다.
            var fadeInRunning = CommandMessageHost.Visibility == Visibility.Visible &&
                Environment.TickCount64 - _commandMessageFadeInTick < 150;
            CommandMessageHost.Visibility = Visibility.Visible;
            // 사라지는 중에 새 메시지가 오면, 아직 투명도가 높더라도 사라지는 애니메이션을 나타나는 애니메이션으로 바꿉니다.
            if (_commandMessageFadingOut || (CommandMessageHost.Opacity < 0.99 && !fadeInRunning))
            {
                _commandMessageFadingOut = false;
                _commandMessageFadeInTick = Environment.TickCount64;
                CommandMessageHost.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(CommandMessageHost.Opacity, 1, TimeSpan.FromMilliseconds(150)));
            }
            _commandMessageTimer.Start();
        }
    }
}
