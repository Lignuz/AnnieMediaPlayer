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

        private void InitializeCommandMessages()
        {
            _commandMessageTimer.Tick += (_, _) =>
            {
                _commandMessageTimer.Stop();
                var version = _commandMessageVersion;
                var fade = new DoubleAnimation(CommandMessageHost.Opacity, 0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, _) =>
                {
                    if (version == _commandMessageVersion)
                        CommandMessageHost.Visibility = Visibility.Collapsed;
                };
                CommandMessageHost.BeginAnimation(OpacityProperty, fade);
            };
            OptionViewModel.Instance.CurrentOption.PropertyChanged += CommandMessageOptionChanged;
            grid_center.SizeChanged += CommandMessageLayoutChanged;
            panel_titlebar.SizeChanged += CommandMessageLayoutChanged;
            panel_control.SizeChanged += CommandMessageLayoutChanged;
            LyricsToggleBar.SizeChanged += CommandMessageLayoutChanged;
            LyricsToggleBar.IsVisibleChanged += CommandMessageUpperControlsChanged;
            Closed += (_, _) =>
            {
                OptionViewModel.Instance.CurrentOption.PropertyChanged -= CommandMessageOptionChanged;
                grid_center.SizeChanged -= CommandMessageLayoutChanged;
                panel_titlebar.SizeChanged -= CommandMessageLayoutChanged;
                panel_control.SizeChanged -= CommandMessageLayoutChanged;
                LyricsToggleBar.SizeChanged -= CommandMessageLayoutChanged;
                LyricsToggleBar.IsVisibleChanged -= CommandMessageUpperControlsChanged;
                HideCommandMessage();
            };
        }

        private void CommandMessageOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Option.ShowCommandMessages) && !OptionViewModel.Instance.CurrentOption.ShowCommandMessages)
                HideCommandMessage();
            if (e.PropertyName == nameof(Option.UseOverlayControl))
                UpdateCommandMessageLayout();
        }

        private void CommandMessageLayoutChanged(object sender, SizeChangedEventArgs e) => UpdateCommandMessageLayout();
        private void CommandMessageUpperControlsChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateCommandMessageLayout();

        private void UpdateCommandMessageLayout()
        {
            // 숨겨진 컨트롤의 자리도 확보해 페이드 중 겹침과 메시지 위치의 급변을 막습니다.
            var top = UseOverlayControl ? panel_titlebar.ActualHeight : 0;
            var bottom = UseOverlayControl ? panel_control.ActualHeight : 0;
            if (LyricsToggleBar.IsVisible)
                top = Math.Max(top, LyricsToggleBar.Margin.Top + LyricsToggleBar.ActualHeight);
            CommandMessageHost.Margin = new Thickness(16, top + 16, 16, bottom + 16);
            CommandMessageHost.MaxHeight = Math.Max(0, grid_center.ActualHeight - top - bottom - 32);
        }

        private void HideCommandMessage()
        {
            _commandMessageVersion++;
            _commandMessageTimer.Stop();
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
            CommandMessageHost.Visibility = Visibility.Visible;
            CommandMessageHost.BeginAnimation(OpacityProperty,
                new DoubleAnimation(CommandMessageHost.Opacity, 1, TimeSpan.FromMilliseconds(150)));
            _commandMessageTimer.Start();
        }
    }
}
