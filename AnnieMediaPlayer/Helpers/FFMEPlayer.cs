using Unosquare.FFME;
using Unosquare.FFME.Common;

namespace AnnieMediaPlayer
{
    // 미디어 상태 정보 
    public class FFMEPlayerInfo
    {
        public TimeSpan CurrentPosition { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public long CurrentFrameNumber { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public MediaPlaybackState PlaybackState { get; set; }
    }

    // FFMpeg 라이브러리 초기화
    public static class FFMELoader
    {
        public static void Initialize()
        {
            if (Library.IsInitialized == false)
            {
                Library.FFmpegDirectory = "ffmpeg";
                Library.EnableWpfMultiThreadedVideo = false;

                Library.LoadFFmpegAsync();
            }
        }
    }

    internal class FFMEPlayer
    {
        public MediaElement? _mediaElement { get; private set; }

        public event EventHandler<FFMEPlayerInfo>? OnMediaStatusChanged;
        public event EventHandler? OnMediaOpened;
        public event EventHandler? OnMediaEnded;
        public event EventHandler<string>? OnMediaFailed;
        public event EventHandler<long>? OnVideoFrameRendered;

        public long LastRenderedFrameNumber { get; private set; }

        public FFMEPlayer(MediaElement mediaElement)
        {
            _mediaElement = mediaElement ?? throw new ArgumentNullException(nameof(mediaElement));

            // MediaElement 이벤트 핸들러 등록
            _mediaElement.MediaOpened += MediaElement_MediaOpened;
            _mediaElement.MediaFailed += MediaElement_MediaFailed;
            _mediaElement.MediaEnded += MediaElement_MediaEnded;
            _mediaElement.PositionChanged += MediaElement_PositionChanged;
            _mediaElement.MediaStateChanged += MediaElement_MediaStateChanged;
            _mediaElement.RenderingVideo += MediaElement_RenderingVideo;
        }

        // 미디어 파일을 열고 재생합니다.
        public async Task OpenAndPlay(string filePath)
        {
            if (_mediaElement == null) return;

            try
            {
                await _mediaElement.Open(new Uri(filePath));
                await _mediaElement.Play();
                UpdateMediaStatus();
            }
            catch (Exception ex)
            {
                OnMediaFailed?.Invoke(this, $"OpenAndPlay: {ex.Message}");
            }
        }

        // 미디어 재생을 시작합니다.
        public async Task Play()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Play();
                UpdateMediaStatus();
            }
        }

        // 미디어 재생을 일시정지합니다.
        public async Task Pause()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Pause();
                UpdateMediaStatus();
            }
        }

        // 미디어 재생을 중지합니다.
        public async Task Stop()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Stop();
                UpdateMediaStatus();
            }
        }

        // 지정된 시간으로 탐색합니다.
        public async Task Seek(TimeSpan position, bool keyFrame = true)
        {
            if (_mediaElement == null)
                return;

            bool wasPlaying = !_mediaElement.IsPaused;

            if (wasPlaying)
                await _mediaElement.Pause();

            if (keyFrame)
                await _mediaElement.SeekKeyFrame(position);
            else
                await _mediaElement.SeekAccurate(position);

            if (wasPlaying)
                await _mediaElement.Play();

            UpdateMediaStatus();
        }

        // 재생 속도를 설정합니다.
        public void SetSpeedRatio(double speed)
        {
            if (_mediaElement != null)
            {
                _mediaElement.SpeedRatio = speed;
            }
        }

        // 볼륨을 설정합니다.
        public void SetVolume(double volume)
        {
            if (_mediaElement != null)
            {
                _mediaElement.Volume = volume;
            }
        }

        // 미디어 상태를 업데이트하고 OnMediaStausChanged 이벤트를 발생시킵니다.
        private void UpdateMediaStatus()
        {
            if (_mediaElement == null) return;

            var info = new FFMEPlayerInfo
            {
                CurrentPosition = _mediaElement.Position,
                TotalDuration = _mediaElement.NaturalDuration.HasValue ? _mediaElement.NaturalDuration.Value : TimeSpan.Zero,
                IsPlaying = _mediaElement.MediaState == MediaPlaybackState.Play,
                IsPaused = _mediaElement.MediaState == MediaPlaybackState.Pause,
                PlaybackState = _mediaElement.MediaState,
                CurrentFrameNumber = LastRenderedFrameNumber,
            };
            OnMediaStatusChanged?.Invoke(this, info);
        }

        private void MediaElement_MediaOpened(object? sender, MediaOpenedEventArgs e)
        {
            OnMediaOpened?.Invoke(this, EventArgs.Empty);
            UpdateMediaStatus();
        }

        private void MediaElement_MediaEnded(object? sender, EventArgs e)
        {
            OnMediaEnded?.Invoke(this, EventArgs.Empty);
            UpdateMediaStatus();
        }

        private void MediaElement_MediaFailed(object? sender, MediaFailedEventArgs e)
        {
            OnMediaFailed?.Invoke(this, e.ErrorException.Message);
            UpdateMediaStatus();
        }

        private void MediaElement_PositionChanged(object? sender, PositionChangedEventArgs e)
        {
            UpdateMediaStatus();
        }

        private void MediaElement_MediaStateChanged(object? sender, MediaStateChangedEventArgs e)
        {
            UpdateMediaStatus();
        }

        private void MediaElement_RenderingVideo(object? sender, RenderingVideoEventArgs e)
        {
            LastRenderedFrameNumber = e.PictureNumber;
            OnVideoFrameRendered?.Invoke(this, e.PictureNumber);
        }

        public async Task DisposeAsync()
        {
            if (_mediaElement != null)
            {
                _mediaElement.MediaOpened -= MediaElement_MediaOpened;
                _mediaElement.MediaEnded -= MediaElement_MediaEnded;
                _mediaElement.MediaFailed -= MediaElement_MediaFailed;
                _mediaElement.PositionChanged -= MediaElement_PositionChanged;
                _mediaElement.MediaStateChanged -= MediaElement_MediaStateChanged;
                _mediaElement.RenderingVideo -= MediaElement_RenderingVideo;

                await _mediaElement.Close();
                _mediaElement = null;
            }
        }
    }
}
