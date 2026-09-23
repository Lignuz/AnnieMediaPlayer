using AnnieMediaPlayer.Options;
using System.Windows;
using System.Windows.Media.Imaging;
using Unosquare.FFME;

namespace AnnieMediaPlayer
{
    public class MainViewModel : ViewModelBase
    {
        public MainViewModel()
        {
            // 초기화
            IsPropertiesPanelOpen = false;
            FilePath = string.Empty;
            IsOpened = false;
            IsAudioOnly = false;
            AudioTitle = string.Empty;
            AudioArtist = string.Empty;
            AudioAlbum = string.Empty;
            Duration = TimeSpan.Zero;
            Position = TimeSpan.Zero;
            FrameIndex = 0;
            FPS = 0.0;

            IsPlaying = false;
            PlayPauseButtonText = "Play";
            PlayStateText = "Play";

            IsNormalSpeed = true;

            PropertyChanged += MainViewModel_PropertyChanged;
            LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
        }

        private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IsPlaying))
            {
                UpdateTextResources();
            }
        }

        private void LanguageManager_LanguageChanged(object? sender, EventArgs e)
        {
            UpdateTextResources();
        }

        void UpdateTextResources()
        {
            string playPauseButtonResourceText = IsPlaying ? "Text.Pause" : "Text.Play";
            string playStateTextResourceText = IsPlaying ? "Text.Playing" : "Text.Pause";
            PlayPauseButtonText = LanguageManager.GetResourceString(playPauseButtonResourceText);
            PlayStateText = string.Format($"[{LanguageManager.GetResourceString(playStateTextResourceText)}]");
        }


        // 기본 미디어 엘리먼트
        private MediaElement? m_MediaElement;
        public MediaElement? MediaElement
        {
            get
            {
                if (m_MediaElement == null)
                    m_MediaElement = (App.Current.MainWindow as MainWindow)?.ffmeMediaElement;

                return m_MediaElement;
            }
        }

        // 현재 옵션 
        public Option CurrentOption => OptionViewModel.Instance.CurrentOption;

        // 프로퍼티 패널 열림 여부
        public bool IsPropertiesPanelOpen { get => Get(); set => Set(value); }
        public PlaylistViewModel Playlist { get; } = new();

        // 미디어 상태 관련 프로퍼티 모음
        public string FilePath { get => Get(); set => Set(value); }
        public bool IsOpened { get => Get(); set => Set(value); }
        public bool IsAudioOnly { get => Get(); set => Set(value); }
        public string AudioTitle { get => Get(); set => Set(value); }
        public string AudioArtist { get => Get(); set => Set(value); }
        public string AudioAlbum { get => Get(); set => Set(value); }

        // 현재 재생 중인 곡의 앨범 이미지 (시각화 화면용 고해상도). 없을 수 있으므로 필드로 보관합니다.
        private BitmapSource? _currentAlbumArt;
        public BitmapSource? CurrentAlbumArt
        {
            get => _currentAlbumArt;
            set
            {
                if (ReferenceEquals(_currentAlbumArt, value))
                    return;

                _currentAlbumArt = value;
                OnPropertyChanged();
            }
        }
        public TimeSpan Duration { get => Get(); set => Set(value); }
        public TimeSpan Position { get => Get(); set => Set(value); }
        public long FrameIndex { get => Get(); set => Set(value); }
        public double FPS { get => Get(); set => Set(value); }

        public bool IsPlaying { get => Get(); set => Set(value); }
        public string PlayPauseButtonText { get => Get(); private set => Set(value); }
        public string PlayStateText { get => Get(); private set => Set(value); }
        public bool IsNormalSpeed { get => Get(); set => Set(value); }
    }
}
