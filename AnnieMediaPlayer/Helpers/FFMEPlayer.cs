using Unosquare.FFME;
using Unosquare.FFME.Common;

namespace AnnieMediaPlayer
{
    public class FFMEPlayerInfo
    {
        public TimeSpan CurrentPosition { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public MediaPlaybackState PlaybackState { get; set; }
    }

    internal class FFMEPlayer
    {
        private MediaElement? _mediaElement;

        public event EventHandler<FFMEPlayerInfo>? OnMediaStatusChanged;
        public event EventHandler? OnMediaOpened;
        public event EventHandler? OnMediaEnded;
        public event EventHandler<string>? OnMediaFailed;

        public FFMEPlayer(MediaElement mediaElement)
        {
            _mediaElement = mediaElement ?? throw new ArgumentNullException(nameof(mediaElement));
            _mediaElement.MediaOpened += MediaElement_MediaOpened;
            _mediaElement.MediaFailed += MediaElement_MediaFailed;
            _mediaElement.MediaEnded += MediaElement_MediaEnded;
        }

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

        public async Task Play()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Play();
                UpdateMediaStatus();
            }
        }

        public async Task Pause()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Pause();
                UpdateMediaStatus();
            }
        }

        public async Task Stop()
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Stop();
                UpdateMediaStatus();
            }
        }

        public async Task Seek(TimeSpan position)
        {
            if (_mediaElement != null)
            {
                await _mediaElement.Seek(position);
                UpdateMediaStatus();
            }
        }

        public void SetSpeedRatio(double speed)
        {
            if (_mediaElement != null)
            {
                _mediaElement.SpeedRatio = speed;
            }
        }

        public void SetVolume(double volume)
        {
            if (_mediaElement != null)
            {
                _mediaElement.Volume = volume;
            }
        }

        private void UpdateMediaStatus()
        {
            if (_mediaElement == null) return;

            var info = new FFMEPlayerInfo
            {
                CurrentPosition = _mediaElement.Position,
                TotalDuration = _mediaElement.NaturalDuration.HasValue ? _mediaElement.NaturalDuration.Value : TimeSpan.Zero,
                IsPlaying = _mediaElement.MediaState == MediaPlaybackState.Play,
                IsPaused = _mediaElement.MediaState == MediaPlaybackState.Pause,
                PlaybackState = _mediaElement.MediaState
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

        public async Task DisposeAsync()
        {
            if (_mediaElement != null)
            {
                _mediaElement.MediaOpened -= MediaElement_MediaOpened;
                _mediaElement.MediaEnded -= MediaElement_MediaEnded;
                _mediaElement.MediaFailed -= MediaElement_MediaFailed;
                _mediaElement.PositionChanged -= MediaElement_PositionChanged;
                _mediaElement.MediaStateChanged -= MediaElement_MediaStateChanged;

                await _mediaElement.Close(); 
                _mediaElement = null; 
            }
        }
    }
}
