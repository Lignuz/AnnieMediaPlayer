using Unosquare.FFME;
using Unosquare.FFME.Common;

namespace AnnieMediaPlayer
{
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
        public MediaElement _mediaElement { get; private set; } = null!;
        
        public event EventHandler<MediaInitializingEventArgs>? OnMediaInitializing;
        public event EventHandler<MediaOpeningEventArgs>? OnMediaOpening;
        public event EventHandler<MediaOpenedEventArgs>? OnMediaOpened;
        public event EventHandler<EventArgs>? OnMediaReady;
        public event EventHandler<EventArgs>? OnMediaEnded;
        public event EventHandler<MediaFailedEventArgs>? OnMediaFailed;
        public event EventHandler<EventArgs>? OnMediaClosed;
        public event EventHandler<MediaOpeningEventArgs>? OnMediaChanging;
        public event EventHandler<MediaOpenedEventArgs>? OnMediaChanged;
        public event EventHandler<PositionChangedEventArgs>? OnPositionChanged;
        public event EventHandler<MediaStateChangedEventArgs>? OnMediaStateChanged;
        public event EventHandler<RenderingVideoEventArgs>? OnVideoFrameRendered;

        public long LastRenderedFrameNumber { get; private set; }

        public FFMEPlayer(MediaElement mediaElement)
        {
            _mediaElement = mediaElement ?? throw new ArgumentNullException(nameof(mediaElement));

            // MediaElement 이벤트 핸들러 등록
            _mediaElement.MediaInitializing += MediaElement_MediaInitializing;
            _mediaElement.MediaOpening += MediaElement_MediaOpening;
            _mediaElement.MediaOpened += MediaElement_MediaOpened;
            _mediaElement.MediaReady += MediaElement_MediaReady;
            _mediaElement.MediaEnded += MediaElement_MediaEnded;
            _mediaElement.MediaFailed += MediaElement_MediaFailed;
            _mediaElement.MediaClosed += MediaElement_MediaClosed;
            _mediaElement.MediaChanging += MediaElement_MediaChanging;
            _mediaElement.MediaChanged += MediaElement_MediaChanged;
            _mediaElement.PositionChanged += MediaElement_PositionChanged;
            _mediaElement.MediaStateChanged += MediaElement_MediaStateChanged;
            _mediaElement.RenderingVideo += MediaElement_RenderingVideo;
        }

        // 미디어 파일을 열기 합니다.
        public async Task<bool> Open(string filePath)
        {
            if (_mediaElement != null)
            {
                return await _mediaElement.Open(new Uri(filePath));
            }
            return false;
        }

        // 미디어 재생을 시작합니다.
        public async Task<bool> Play()
        {
            if (_mediaElement != null)
            {
                return await _mediaElement.Play();
            }
            return false;
        }

        // 미디어 재생을 일시정지합니다.
        public async Task<bool> Pause()
        {
            if (_mediaElement != null)
            {
                return await _mediaElement.Pause();
            }
            return false;
        }

        // 미디어 재생을 중지합니다.
        public async Task<bool> Stop()
        {
            if (_mediaElement != null)
            {
                return await _mediaElement.Stop();
            }
            return false;
        }

        // 지정된 시간으로 탐색합니다.
        public async Task<bool> Seek(TimeSpan position, bool keyFrame = true)
        {
            if (_mediaElement != null)
            {
                if (keyFrame)
                    return await _mediaElement.SeekKeyFrame(position);
                else
                    return await _mediaElement.SeekAccurate(position);
            }
            return false;
        }

        // 프레임단위로 이동합니다.
        public async Task<bool> SeekStep(bool next = true)
        {
            if (_mediaElement != null)
            {
                if (next)
                    return await _mediaElement.StepForward();
                else
                    return await _mediaElement.StepBackward();
            }
            return false;
        }

        // 재생 속도를 설정합니다.
        public void SetSpeedRatio(double speed)
        {
            if (_mediaElement != null)
            {
                _mediaElement.SpeedRatio = speed;
            }
        }

        public double GetSpeedRatio()
        {
            if (_mediaElement != null)
            {
                return _mediaElement.SpeedRatio;
            }
            return 1.0;
        }

        // 볼륨을 설정합니다.
        public void SetVolume(double volume)
        {
            if (_mediaElement != null)
            {
                _mediaElement.Volume = volume;
            }
        }

        public double GetVolume()
        {
            if (_mediaElement != null)
            {
                return _mediaElement.Volume;
            }
            return 0.0;
        }

        ///////////////////
        // 이벤트 핸들러 //
        ///////////////////

        private void MediaElement_MediaInitializing(object? sender, MediaInitializingEventArgs e)
        {
            OnMediaInitializing?.Invoke(this, e);
        }

        private void MediaElement_MediaOpening(object? sender, MediaOpeningEventArgs e)
        {
            OnMediaOpening?.Invoke(this, e);
        }

        private void MediaElement_MediaOpened(object? sender, MediaOpenedEventArgs e)
        {
            OnMediaOpened?.Invoke(this, e);
        }

        private void MediaElement_MediaReady(object? sender, EventArgs e)
        {
            OnMediaReady?.Invoke(this, e);
        }

        private void MediaElement_MediaEnded(object? sender, EventArgs e)
        {
            OnMediaEnded?.Invoke(this, e);
        }

        private void MediaElement_MediaFailed(object? sender, MediaFailedEventArgs e)
        {
            OnMediaFailed?.Invoke(this, e);
        }

        private void MediaElement_MediaClosed(object? sender, EventArgs e)
        {
            OnMediaClosed?.Invoke(this, e);
        }

        private void MediaElement_MediaChanging(object? sender, MediaOpeningEventArgs e)
        {
            OnMediaChanging?.Invoke(this, e);
        }

        private void MediaElement_MediaChanged(object? sender, MediaOpenedEventArgs e)
        {
            OnMediaChanged?.Invoke(this, e);
        }

        private void MediaElement_PositionChanged(object? sender, PositionChangedEventArgs e)
        {
            OnPositionChanged?.Invoke(this, e);
        }

        private void MediaElement_MediaStateChanged(object? sender, MediaStateChangedEventArgs e)
        {
            if (e.MediaState == MediaPlaybackState.Close || 
                e.MediaState == MediaPlaybackState.Stop)
            {
                LastRenderedFrameNumber = 0;
            }
            OnMediaStateChanged?.Invoke(this, e);
        }

        private void MediaElement_RenderingVideo(object? sender, RenderingVideoEventArgs e)
        {
            LastRenderedFrameNumber = e.PictureNumber - 1;
            OnVideoFrameRendered?.Invoke(this, e);
        }

        private bool _isDisposed = false;
        public async Task DisposeAsync()
        {
            if (_isDisposed || _mediaElement == null)
                return;

            _mediaElement.MediaInitializing -= MediaElement_MediaInitializing;
            _mediaElement.MediaOpening -= MediaElement_MediaOpening;
            _mediaElement.MediaOpened -= MediaElement_MediaOpened;
            _mediaElement.MediaReady -= MediaElement_MediaReady;
            _mediaElement.MediaEnded -= MediaElement_MediaEnded;
            _mediaElement.MediaFailed -= MediaElement_MediaFailed;
            _mediaElement.MediaClosed -= MediaElement_MediaClosed;
            _mediaElement.MediaChanging -= MediaElement_MediaChanging;
            _mediaElement.MediaChanged -= MediaElement_MediaChanged;
            _mediaElement.PositionChanged -= MediaElement_PositionChanged;
            _mediaElement.MediaStateChanged -= MediaElement_MediaStateChanged;
            _mediaElement.RenderingVideo -= MediaElement_RenderingVideo;
            await _mediaElement.Close();

            _isDisposed = true;
        }
    }
}
