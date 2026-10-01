using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Data;

namespace AnnieMediaPlayer.Options
{
    public enum Languages
    {
        ko = 0,
        en = 1,
    }

    public enum Themes
    {
        Light = 0,
        Dark = 1,
    };

    public enum RotateAngle
    {
        Rotate_0 = 0,
        Rotate_90 = 90,
        Rotate_180 = 180,
        Rotate_270 = 270,
    }

    // 오디오 재생 화면: 기본(앨범 아트) 또는 시각화 모드
    public enum AudioViewMode
    {
        Basic = 0,
        Aura = 1,
        Halo = 2,
        Ribbon = 3,
    }

    public enum PlaylistPlaybackMode
    {
        Sequential = 0,
        Playlist = 1,
        Track = 2,
        CurrentTrackOnly = 3,
    }

    public sealed class WindowPlacementSettings
    {
        public bool HasBounds { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsMaximized { get; set; }
        public bool IsVisible { get; set; }
        public bool IsDocked { get; set; }
        public bool DockedToRight { get; set; }
        public bool SyncHeight { get; set; }
    }

    public class Option : ViewModelBase
    {
        [JsonIgnore]
        public List<Languages> AvailableLanguages => Enum.GetValues(typeof(Languages)).Cast<Languages>().ToList();
        [JsonIgnore]
        public List<Themes> AvailableThemes => Enum.GetValues(typeof(Themes)).Cast<Themes>().ToList();

        private static readonly string OptionFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnnieMediaPlayer", "options.json");

        public Option()
        {
            InitializeOption();
        }

        public void InitializeOption()
        {
            // 옵션 초기값 지정
            SelectedTheme = Themes.Light;
            SelectedLanguage = Languages.ko;
            UseOverlayControl = false;
            UseSeekFramePreview = false;

            UseHWAccelerator = false;
            UseOpenPlay = true;
            UseLegacyAudioOut = false;
            AudioViewMode = AudioViewMode.Aura;
            ShowLyrics = false;
            UseTransitionFade = true;
            UsePlaylistPersistence = true;
            RememberWindowPositions = true;
            MainWindowPlacement = new WindowPlacementSettings();
            PlaylistWindowPlacement = new WindowPlacementSettings();
            PlaylistPlaybackMode = PlaylistPlaybackMode.Sequential;
            ShufflePlayback = false;

            UseFlipHorizontal = false;
            UseFlipVertical = false;
            RotateAngle = RotateAngle.Rotate_0;
        }

        // 옵션 멤버 프로퍼티
        public Themes SelectedTheme { get => Get(); set => Set(value); }
        public Languages SelectedLanguage { get => Get(); set => Set(value); }
        public bool UseOverlayControl { get => Get(); set => Set(value); }
        public bool UseSeekFramePreview { get => Get(); set => Set(value); }

        public bool UseHWAccelerator { get => Get(); set => Set(value); }
        public bool UseOpenPlay { get => Get(); set => Set(value); }
        public bool UseLegacyAudioOut { get => Get(); set => Set(value); }
        public AudioViewMode AudioViewMode { get => Get(); set => Set(value); }
        public bool ShowLyrics { get => Get(); set => Set(value); }
        public bool UseTransitionFade { get => Get(); set => Set(value); }
        public bool UsePlaylistPersistence { get => Get(); set => Set(value); }
        public bool RememberWindowPositions { get => Get(); set => Set(value); }
        public WindowPlacementSettings MainWindowPlacement { get; set; } = new();
        public WindowPlacementSettings PlaylistWindowPlacement { get; set; } = new();
        public PlaylistPlaybackMode PlaylistPlaybackMode { get => Get(); set => Set(value); }
        public bool ShufflePlayback { get => Get(); set => Set(value); }

        [JsonIgnore]
        public bool UseFlipHorizontal { get => Get(); set => Set(value); }
        [JsonIgnore]
        public bool UseFlipVertical { get => Get(); set => Set(value); }
        [JsonIgnore]
        public RotateAngle RotateAngle { get => Get(); set => Set(value); }


        // 옵션 저장
        // 저장 도중 프로그램이 종료돼도 기존 파일이 깨지지 않도록 임시 파일에 쓴 뒤 교체합니다.
        // (재생목록 저장과 같은 방식)
        public void Save()
        {
            string? temporaryPath = null;
            try
            {
                var dir = Path.GetDirectoryName(OptionFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                temporaryPath = Path.Combine(dir!, $"options.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, OptionFilePath, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"옵션 저장 실패: {ex.Message}");
            }
            finally
            {
                if (temporaryPath != null && File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        // 옵션 불러오기
        public static Option Load()
        {
            try
            {
                if (File.Exists(OptionFilePath))
                {
                    var json = File.ReadAllText(OptionFilePath);
                    var option = JsonSerializer.Deserialize<Option>(json);
                    if (option != null)
                    {
                        option.MainWindowPlacement ??= new WindowPlacementSettings();
                        option.PlaylistWindowPlacement ??= new WindowPlacementSettings();
                        using var document = JsonDocument.Parse(json);
                        var root = document.RootElement;
                        // 이전 연속 재생 옵션을 새 재생 모드로 이어받습니다.
                        if (root.ValueKind == JsonValueKind.Object &&
                            !root.TryGetProperty(nameof(PlaylistPlaybackMode), out _) &&
                            root.TryGetProperty("UseContinuousPlayback", out var continuousPlayback) &&
                            (continuousPlayback.ValueKind == JsonValueKind.True || continuousPlayback.ValueKind == JsonValueKind.False))
                        {
                            option.PlaylistPlaybackMode = continuousPlayback.GetBoolean()
                                ? PlaylistPlaybackMode.Sequential
                                : PlaylistPlaybackMode.CurrentTrackOnly;
                        }

                        if (!Enum.IsDefined(option.PlaylistPlaybackMode))
                            option.PlaylistPlaybackMode = PlaylistPlaybackMode.Sequential;

                        return option;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"옵션 불러오기 실패: {ex.Message}");
            }
            // 기본값 반환
            return new Option();
        }
    }

    public class EnumToLocalizedStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Themes theme)
            {
                if (OptionViewModel.Instance.ThemeDisplayNames.TryGetValue(theme, out string? key))
                {
                    return Application.Current.Resources[key] as string;
                }
            }
            else if (value is Languages language)
            {
                if (OptionViewModel.Instance.LanguageDisplayNames.TryGetValue(language, out string? key))
                {
                    return Application.Current.Resources[key] as string;
                }
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
