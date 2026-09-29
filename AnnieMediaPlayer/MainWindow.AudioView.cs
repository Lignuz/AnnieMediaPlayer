using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AnnieMediaPlayer.Options;

namespace AnnieMediaPlayer
{
    // 오디오 재생 화면: 기본/시각화 모드 선택, 가사 영역, 마우스를 움직이면 잠시 나타나는 화면 위 컨트롤
    public partial class MainWindow
    {
        private static readonly TimeSpan AudioChromeShowDuration = TimeSpan.FromSeconds(2.8);
        private static readonly Duration LyricsSlideDuration = TimeSpan.FromMilliseconds(380);
        private const double MaximumAlbumArtSize = 440;
        // 커버와 스펙트럼을 하나의 중심 덩어리로 보이게 하도록 커버 높이를 제한합니다.
        private const double AlbumArtHeightRatio = 0.72;
        private const double BasicSpectrumGap = 3;
        private const double BasicSpectrumVerticalMargin = 19.5;
        private readonly DispatcherTimer _audioChromeHideTimer = new() { Interval = AudioChromeShowDuration };
        private bool _isAudioChromeVisible;
        private bool _isLyricsShown;
        private bool _isLyricsSliding;
        private int _lyricsSlideVersion;
        private int _lyricsLoadVersion;
        private DispatcherOperation? _basicSpectrumBoundsUpdate;
        private bool _basicSpectrumBoundsDirty;

        // 가사 영역이 차지하는 오른쪽 폭. 왼쪽 화면(앨범 아트·시각화)은 이만큼을 비우고 가운데를 맞춥니다.
        private static readonly DependencyProperty AudioContentInsetProperty = DependencyProperty.Register(
            nameof(AudioContentInset), typeof(double), typeof(MainWindow),
            new PropertyMetadata(0d, (d, _) => ((MainWindow)d).ApplyAudioContentInset()));

        private double AudioContentInset
        {
            get => (double)GetValue(AudioContentInsetProperty);
            set => SetValue(AudioContentInsetProperty, value);
        }

        private double LyricsRegionWidth => Math.Clamp(grid_center.ActualWidth * 0.42, 300, 620);

        private void InitializeAudioView()
        {
            _audioChromeHideTimer.Tick += (_, _) => HideAudioChromeUnlessHovered();
            ApplyVisualizerMode(OptionViewModel.Instance.CurrentOption.AudioViewMode);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsAudioOnly))
                {
                    UpdateLyricsLayout(animate: false);
                    UpdateBasicSpectrumLayout();
                }
                else if (e.PropertyName == nameof(MainViewModel.CurrentAlbumArt))
                {
                    ScheduleBasicSpectrumBoundsUpdate();
                }
            };
            grid_center.SizeChanged += (_, _) =>
            {
                UpdateLyricsLayout(animate: false);
                UpdateBasicSpectrumLayout();
            };
            AudioAlbumArtImage.SizeChanged += (_, _) => ScheduleBasicSpectrumBoundsUpdate();
            AudioVisualizer.SizeChanged += (_, _) => ScheduleBasicSpectrumBoundsUpdate();
            AudioVisualizer.LayoutUpdated += (_, _) =>
            {
                // 창 크기를 끌어 바꾸는 동안에도 스펙트럼이 커버를 놓치지 않도록, 레이아웃이 끝난 같은 프레임에 좌표를 맞춥니다.
                if (_basicSpectrumBoundsDirty)
                    UpdateBasicSpectrumBounds();
            };
            UpdateBasicSpectrumLayout();
            UpdateAudioChromeInset();
        }

        #region 모드 선택

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
            if (mode == AudioViewMode.Basic)
                AudioVisualizer.BasicSpectrumBounds = Rect.Empty;
            ApplyVisualizerMode(mode);
            option.AudioViewMode = mode;
            if (mode == AudioViewMode.Basic)
                UpdateBasicSpectrumLayout();
            if (fade)
                FinishMediaTransition(true);
            ShowAudioChrome();
        }

        private void ApplyVisualizerMode(AudioViewMode mode)
        {
            if (mode != AudioViewMode.Basic)
                AudioVisualizer.VisualizerMode = mode - AudioViewMode.Aura;
            AudioVisualizer.IsBasicSpectrumMode = mode == AudioViewMode.Basic;
        }

        private void UpdateBasicSpectrumLayout()
        {
            var availableHeight = grid_center.ActualHeight;
            if (availableHeight <= 0)
                return;

            var maxCoverHeight = Math.Min(MaximumAlbumArtSize, availableHeight * AlbumArtHeightRatio);
            var maxHeightWithMargins = availableHeight - 2 * BasicSpectrumVerticalMargin - BasicSpectrumGap -
                GetBasicSpectrumHeight(maxCoverHeight);
            maxCoverHeight = Math.Min(maxCoverHeight, Math.Max(64, maxHeightWithMargins));

            if (Math.Abs(AudioAlbumArtImage.MaxHeight - maxCoverHeight) > 0.5)
                AudioAlbumArtImage.MaxHeight = maxCoverHeight;

            if (AudioAlbumArtImage.RenderTransform is TranslateTransform shift)
                shift.Y = -(GetBasicSpectrumHeight(maxCoverHeight) + BasicSpectrumGap) / 2;

            ScheduleBasicSpectrumBoundsUpdate();
        }

        private static double GetBasicSpectrumHeight(double coverHeight) => Math.Clamp(coverHeight * 0.14, 20, 48);

        private void ScheduleBasicSpectrumBoundsUpdate()
        {
            _basicSpectrumBoundsDirty = true;
            if (_basicSpectrumBoundsUpdate?.Status == DispatcherOperationStatus.Pending)
                return;

            // 레이아웃 패스가 없는 변경(커버 교체 등)은 이 예약으로 처리합니다.
            _basicSpectrumBoundsUpdate = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _basicSpectrumBoundsUpdate = null;
                UpdateBasicSpectrumBounds();
            }));
        }

        private void UpdateBasicSpectrumBounds()
        {
            _basicSpectrumBoundsDirty = false;
            if (!vm.IsAudioOnly || !AudioVisualizer.IsBasicSpectrumMode ||
                AudioVisualizer.ActualWidth <= 0 || AudioVisualizer.ActualHeight <= 0)
                return;

            Rect spectrumBounds;

            if (AudioAlbumArtImage.Source is System.Windows.Media.Imaging.BitmapSource source &&
                AudioAlbumArtImage.RenderSize.Width > 0 && AudioAlbumArtImage.RenderSize.Height > 0)
            {
                var scale = Math.Min(AudioAlbumArtImage.RenderSize.Width / source.Width,
                    AudioAlbumArtImage.RenderSize.Height / source.Height);
                var imageWidth = source.Width * scale;
                var imageHeight = source.Height * scale;
                var imageContent = new Rect(
                    (AudioAlbumArtImage.RenderSize.Width - imageWidth) / 2,
                    (AudioAlbumArtImage.RenderSize.Height - imageHeight) / 2,
                    imageWidth,
                    imageHeight);
                var spectrumHeight = GetBasicSpectrumHeight(imageContent.Height);
                if (AudioAlbumArtImage.RenderTransform is TranslateTransform shift)
                    shift.Y = -(spectrumHeight + BasicSpectrumGap) / 2;

                try
                {
                    var imageBounds = AudioAlbumArtImage.TransformToVisual(AudioVisualizer).TransformBounds(imageContent);
                    spectrumBounds = new Rect(imageBounds.Left, imageBounds.Bottom + BasicSpectrumGap, imageBounds.Width, spectrumHeight);
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
            else
            {
                var width = Math.Min(320, Math.Max(100, AudioVisualizer.ActualWidth * 0.45));
                var spectrumHeight = GetBasicSpectrumHeight(Math.Min(MaximumAlbumArtSize, grid_center.ActualHeight * 0.7));
                spectrumBounds = new Rect((AudioVisualizer.ActualWidth - width) / 2,
                    AudioVisualizer.ActualHeight * 0.66, width, spectrumHeight);
            }

            if (!AudioVisualizer.BasicSpectrumBounds.Equals(spectrumBounds))
                AudioVisualizer.BasicSpectrumBounds = spectrumBounds;
        }

        #endregion

        #region 가사

        private void LyricsToggle_Click(object sender, RoutedEventArgs e)
        {
            var option = OptionViewModel.Instance.CurrentOption;
            option.ShowLyrics = !option.ShowLyrics;
            UpdateLyricsLayout(animate: option.UseTransitionFade); // 전환 효과를 끈 경우에는 바로 나타나고 사라집니다.
            ShowAudioChrome();
        }

        private void LyricsView_LineClicked(object? sender, TimeSpan time) => _ = VideoPlayerController.Seek(time);

        // 가사를 켜면 가사 영역이 오른쪽에서 밀려 나오고, 왼쪽 화면은 남은 영역의 가운데로 옮겨 갑니다.
        private void UpdateLyricsLayout(bool animate)
        {
            var show = vm.IsAudioOnly && OptionViewModel.Instance.CurrentOption.ShowLyrics && grid_center.ActualWidth > 0;
            var width = LyricsRegionWidth;
            LyricsRegion.Width = width;

            if (show == _isLyricsShown)
            {
                // 창 크기만 바뀐 경우: 애니메이션 없이 폭을 맞춥니다.
                if (show && !_isLyricsSliding)
                    AudioContentInset = width;
                return;
            }

            _isLyricsShown = show;
            var version = ++_lyricsSlideVersion;
            if (show)
            {
                LyricsRegion.Visibility = Visibility.Visible;
                LyricsView.UpdatePosition(vm.Position);
            }

            var slide = width * 0.6;
            if (!animate)
            {
                StopLyricsSlide();
                LyricsRegion.Opacity = show ? 1 : 0;
                LyricsRegionShift.X = show ? 0 : slide;
                AudioContentInset = show ? width : 0;
                if (!show)
                    LyricsRegion.Visibility = Visibility.Collapsed;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var inset = new DoubleAnimation(show ? width : 0, LyricsSlideDuration) { EasingFunction = ease };
            inset.Completed += (_, _) =>
            {
                if (version != _lyricsSlideVersion)
                    return;

                // 애니메이션이 끝나면 값을 고정합니다. 도중에 창 크기가 바뀌었을 수 있으므로 지금 폭으로 맞춥니다.
                _isLyricsSliding = false;
                BeginAnimation(AudioContentInsetProperty, null);
                AudioContentInset = _isLyricsShown ? LyricsRegionWidth : 0;
                if (!_isLyricsShown)
                    LyricsRegion.Visibility = Visibility.Collapsed;
            };
            _isLyricsSliding = true;
            BeginAnimation(AudioContentInsetProperty, inset);
            LyricsRegion.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, LyricsSlideDuration) { EasingFunction = ease });
            LyricsRegionShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(show ? slide : 0, show ? 0 : slide, LyricsSlideDuration) { EasingFunction = ease });
        }

        private void StopLyricsSlide()
        {
            _isLyricsSliding = false;
            BeginAnimation(AudioContentInsetProperty, null);
            LyricsRegion.BeginAnimation(OpacityProperty, null);
            LyricsRegionShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        }

        private void ApplyAudioContentInset()
        {
            var inset = AudioContentInset;
            AudioVisualizer.ContentRightInset = inset;

            // 가사가 보이면 앨범 아트가 왼쪽 영역을 꽉 채우지 않도록 여백을 함께 늘립니다.
            var margin = 40 * Math.Min(1, inset / 300);
            AudioAlbumArtImage.Margin = new Thickness(margin, margin, inset + margin, margin);
            ScheduleBasicSpectrumBoundsUpdate();
            UpdateAudioChromeInset();
        }

        // 곡을 열 때 가사를 찾습니다. 파일을 읽으므로 UI 스레드 밖에서 처리합니다.
        private async void LoadLyrics(string source, IReadOnlyDictionary<string, string>? containerMetadata,
            IReadOnlyDictionary<string, string>? streamMetadata)
        {
            var version = ++_lyricsLoadVersion;
            LyricsView.Lyrics = null;
            LyricsView.IsLoading = true;

            Lyrics? lyrics = null;
            try
            {
                lyrics = await Task.Run(() => LyricsLoader.Load(source, containerMetadata, streamMetadata));
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Lyrics load failed: {ex.Message}");
            }

            if (version != _lyricsLoadVersion)
                return;

            LyricsView.IsLoading = false;
            LyricsView.Lyrics = lyrics;
            LyricsView.UpdatePosition(vm.Position);
        }

        private void ClearLyrics()
        {
            _lyricsLoadVersion++;
            LyricsView.IsLoading = false;
            LyricsView.Lyrics = null;
        }

        private void UpdateLyricsPosition(TimeSpan position)
        {
            if (_isLyricsShown)
                LyricsView.UpdatePosition(position);
        }

        #endregion

        #region 화면 위 컨트롤 (모드 선택·가사 버튼)

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
            LyricsToggleBar.IsHitTestVisible = true;
            FadeControl(AudioModeBar, 1);
            FadeControl(LyricsToggleBar, 1);
        }

        private void HideAudioChromeUnlessHovered()
        {
            // 컨트롤 위에 마우스가 있는 동안에는 계속 보여 줍니다.
            if (AudioModeBar.IsMouseOver || LyricsToggleBar.IsMouseOver)
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
            LyricsToggleBar.IsHitTestVisible = false;
            FadeControl(AudioModeBar, 0);
            FadeControl(LyricsToggleBar, 0);
        }

        // 컨트롤 패널·제목 표시줄을 화면 위에 겹쳐 쓸 때는 그만큼 비켜 두고,
        // 가사가 보이면 모드 선택 막대를 왼쪽 화면의 가운데로 옮깁니다.
        private void UpdateAudioChromeInset()
        {
            var top = UseOverlayControl ? panel_titlebar.ActualHeight : 0;
            var bottom = UseOverlayControl ? panel_control.ActualHeight : 0;
            AudioModeBar.Margin = new Thickness(0, 0, AudioContentInset, Math.Max(16, bottom + 12)); // 앨범 모드의 스펙트럼과 겹치지 않도록 화면 아래에 가깝게 둡니다.
            LyricsToggleBar.Margin = new Thickness(0, top + 16, 24, 0);
            LyricsView.Margin = new Thickness(0, top + 68, 24, bottom + 24);
        }

        #endregion
    }
}
