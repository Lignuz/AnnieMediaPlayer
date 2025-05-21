using System.Diagnostics;
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
        
        public event EventHandler<MediaOpenedEventArgs>? OnMediaOpened;
        public event EventHandler<EventArgs>? OnMediaEnded;
        public event EventHandler<MediaFailedEventArgs>? OnMediaFailed;
        public event EventHandler<PositionChangedEventArgs>? OnPositionChanged;
        public event EventHandler<MediaStateChangedEventArgs>? OnMediaStateChanged;
        public event EventHandler<RenderingVideoEventArgs>? OnVideoFrameRendered;

        public long LastRenderedFrameNumber { get; private set; }

        public FFMEPlayer(MediaElement mediaElement)
        {
            _mediaElement = mediaElement ?? throw new ArgumentNullException(nameof(mediaElement));

            // MediaElement 이벤트 핸들러 등록
            _mediaElement.MediaOpened += MediaElement_MediaOpened;
            _mediaElement.MediaEnded += MediaElement_MediaEnded;
            _mediaElement.MediaFailed += MediaElement_MediaFailed;
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
                bool ret = await _mediaElement.Open(new Uri(filePath));
                if (ret)
                {
                    await _mediaElement.Play();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenAndPlay: {ex}");
            }
        }

        // 미디어 재생을 시작합니다.
        public async Task Play()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Play();
            }
        }

        // 미디어 재생을 일시정지합니다.
        public async Task Pause()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Pause();
            }
        }

        // 미디어 재생을 중지합니다.
        public async Task Stop()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Stop();
            }
        }

        // 지정된 시간으로 탐색합니다.
        public async Task Seek(TimeSpan position, bool keyFrame = true)
        {
            if (_mediaElement == null)
                return;

            if (keyFrame)
                await _mediaElement.SeekKeyFrame(position);
            else
                await _mediaElement.SeekAccurate(position);
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

        private void MediaElement_MediaOpened(object? sender, MediaOpenedEventArgs e)
        {
            OnMediaOpened?.Invoke(this, e);
        }

        private void MediaElement_MediaEnded(object? sender, EventArgs e)
        {
            OnMediaEnded?.Invoke(this, e);
        }

        private void MediaElement_MediaFailed(object? sender, MediaFailedEventArgs e)
        {
            OnMediaFailed?.Invoke(this, e);
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
            LastRenderedFrameNumber = e.PictureNumber;
            OnVideoFrameRendered?.Invoke(this, e);
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
#pragma warning disable CS8625 // Null 리터럴을 null을 허용하지 않는 참조 형식으로 변환할 수 없습니다.
                _mediaElement = null;
#pragma warning restore CS8625 // Null 리터럴을 null을 허용하지 않는 참조 형식으로 변환할 수 없습니다.
            }
        }
    }
}
