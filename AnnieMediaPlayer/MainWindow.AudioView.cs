using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AnnieMediaPlayer.Options;

namespace AnnieMediaPlayer
{
    // 오디오 재생 화면: 기본/시각화 모드 선택과, 마우스를 움직이면 잠시 나타나는 화면 위 컨트롤
    public partial class MainWindow
    {
        private static readonly TimeSpan AudioChromeShowDuration = TimeSpan.FromSeconds(2.8);
        private readonly DispatcherTimer _audioChromeHideTimer = new() { Interval = AudioChromeShowDuration };
        private bool _isAudioChromeVisible;

        private void InitializeAudioView()
        {
            _audioChromeHideTimer.Tick += (_, _) => HideAudioChromeUnlessHovered();
            ApplyVisualizerMode(OptionViewModel.Instance.CurrentOption.AudioViewMode);
            UpdateAudioChromeInset();
        }

        private void AudioModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: AudioViewMode mode })
                SetAudioViewMode(mode);
        }

        private void SetAudioViewMode(AudioViewMode mode)
        {
            var option = OptionViewModel.Instance.CurrentOption;
            if (option.AudioViewMode == mode)
            {
                ShowAudioChrome();
                return;
            }

            // 기본 화면과 시각화 사이는 화면 전체가 바뀌므로 곡 전환과 같은 페이드를 쓰고,
            // 시각화 모드끼리는 시각화 컨트롤이 직접 크로스페이드합니다.
            var crossesBasic = (option.AudioViewMode == AudioViewMode.Basic) != (mode == AudioViewMode.Basic);
            var fade = crossesBasic && vm.IsAudioOnly && option.UseTransitionFade &&
                !_mediaTransitionPending && BeginMediaTransition();

            // 시각화를 켜기 전에 모드를 먼저 정해, 켜지는 순간 이전 모드가 보이지 않게 합니다.
            ApplyVisualizerMode(mode);
            option.AudioViewMode = mode;
            if (fade)
                FinishMediaTransition(true);
            ShowAudioChrome();
        }

        private void ApplyVisualizerMode(AudioViewMode mode)
        {
            if (mode != AudioViewMode.Basic)
                AudioVisualizer.VisualizerMode = mode - AudioViewMode.Aura;
        }

        private void grid_center_MouseMove(object sender, MouseEventArgs e) => ShowAudioChrome();

        private void grid_center_MouseLeave(object sender, MouseEventArgs e) => HideAudioChrome();

        private void ShowAudioChrome()
        {
            if (!vm.IsAudioOnly)
                return;

            _audioChromeHideTimer.Stop();
            _audioChromeHideTimer.Start();
            if (_isAudioChromeVisible)
                return;

            _isAudioChromeVisible = true;
            AudioModeBar.IsHitTestVisible = true;
            FadeControl(AudioModeBar, 1);
        }

        private void HideAudioChromeUnlessHovered()
        {
            // 컨트롤 위에 마우스가 있는 동안에는 계속 보여 줍니다.
            if (AudioModeBar.IsMouseOver)
                return;

            HideAudioChrome();
        }

        private void HideAudioChrome()
        {
            _audioChromeHideTimer.Stop();
            if (!_isAudioChromeVisible)
                return;

            _isAudioChromeVisible = false;
            AudioModeBar.IsHitTestVisible = false;
            FadeControl(AudioModeBar, 0);
        }

        // 컨트롤 패널을 화면 위에 겹쳐 쓸 때는 모드 선택 막대를 그 위로 올립니다.
        private void UpdateAudioChromeInset()
        {
            var inset = UseOverlayControl ? panel_control.ActualHeight : 0;
            AudioModeBar.Margin = new Thickness(0, 0, 0, Math.Max(28, inset + 12));
        }
    }
}
