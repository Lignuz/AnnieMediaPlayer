using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Unosquare.FFME;

namespace AnnieMediaPlayer.Windows.Info
{
    public partial class FileInfoWindow : BaseWindow
    {
        private readonly FileInfoViewModel _viewModel;
        private readonly AlbumArtService _albumArtService = new();
        private readonly CancellationTokenSource _loadCancellation = new();
        private bool _loadStarted;

        public FileInfoWindow(string filePath)
        {
            InitializeComponent();
            _viewModel = new FileInfoViewModel(filePath);
            DataContext = _viewModel;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Loaded += LoadFileInfo;
        }

        protected override void OnClosed(EventArgs e)
        {
            _loadCancellation.Cancel();
            _loadCancellation.Dispose();
            _albumArtService.Dispose();
            base.OnClosed(e);
        }

        private async void LoadFileInfo(object sender, RoutedEventArgs e)
        {
            if (_loadStarted)
                return;
            _loadStarted = true;
            var token = _loadCancellation.Token;
            _viewModel.Status = Text("Text.FileInfo.Loading");
            try
            {
                // 조회용 컨테이너를 별도로 열어 현재 재생 파일과 위치를 바꾸지 않습니다.
                var snapshot = await Task.Run(() =>
                {
                    var length = new System.IO.FileInfo(_viewModel.FilePath).Length;
                    var info = Library.RetrieveMediaInfo(_viewModel.FilePath);
                    token.ThrowIfCancellationRequested();
                    return (Length: length, Info: info);
                }, token);
                token.ThrowIfCancellationRequested();

                var info = snapshot.Info;
                _viewModel.Summary.Add(new(Text("Text.FileInfo.Size"), $"{snapshot.Length:N0} bytes"));
                _viewModel.Summary.Add(new(Text("Text.FileInfo.Format"), info.Format));
                _viewModel.Summary.Add(new(Text("Text.FileInfo.Duration"), info.Duration >= TimeSpan.Zero ? $"{(int)info.Duration.TotalHours:00}:{info.Duration.Minutes:00}:{info.Duration.Seconds:00}" : "—"));
                _viewModel.Summary.Add(new(Text("Text.FileInfo.BitRate"), info.BitRate > 0 ? $"{info.BitRate / 1000.0:N0} kbps" : "—"));
                AddTags(Text("Text.FileInfo.Container"), info.Metadata);
                foreach (var stream in info.Streams.Values.OrderBy(stream => stream.StreamIndex))
                {
                    var details = new List<string>();
                    if (stream.SampleRate > 0)
                        details.Add($"{stream.SampleRate:N0} Hz / {stream.Channels} ch");
                    if (stream.PixelWidth > 0 && stream.PixelHeight > 0)
                        details.Add($"{stream.PixelWidth} × {stream.PixelHeight}");
                    if (stream.FPS > 0)
                        details.Add($"{stream.FPS.ToString("0.##", CultureInfo.CurrentCulture)} fps");
                    _viewModel.Streams.Add(new(stream.StreamIndex, stream.CodecTypeName, stream.CodecName, string.Join(" · ", details)));
                    AddTags($"#{stream.StreamIndex} {stream.CodecTypeName}", stream.Metadata);
                }
                _viewModel.Status = Text("Text.FileInfo.ReadOnly");

                var artwork = await _albumArtService.LoadAsync(_viewModel.FilePath, 360, token);
                token.ThrowIfCancellationRequested();
                _viewModel.AlbumArt = artwork.Image;
                _viewModel.AlbumArtDescription = Text($"Text.FileInfo.Artwork.{artwork.Source}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"File information could not be read: {ex}");
                if (!token.IsCancellationRequested)
                    _viewModel.Status = Text("Text.FileInfo.Failed");
            }
        }

        private void AddTags(string source, IReadOnlyDictionary<string, string> tags)
        {
            foreach (var tag in tags.OrderBy(tag => tag.Key, StringComparer.OrdinalIgnoreCase))
                _viewModel.Tags.Add(new(source, tag.Key, MetadataText.Repair(tag.Value)));
        }

        private static string Text(string key) => LanguageManager.GetResourceString(key);
    }

    internal sealed class FileInfoViewModel(string filePath) : ViewModelBase
    {
        private BitmapSource? _albumArt;
        private string _albumArtDescription = string.Empty;
        private string _status = string.Empty;
        public string FilePath { get; } = filePath;
        public string FileName => Path.GetFileName(FilePath);
        public ObservableCollection<FileInfoField> Summary { get; } = new();
        public ObservableCollection<FileMetadataEntry> Tags { get; } = new();
        public ObservableCollection<FileStreamEntry> Streams { get; } = new();
        public BitmapSource? AlbumArt { get => _albumArt; set { _albumArt = value; OnPropertyChanged(); } }
        public string AlbumArtDescription { get => _albumArtDescription; set { _albumArtDescription = value; OnPropertyChanged(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    }

    internal sealed record FileInfoField(string Name, string Value);
    internal sealed record FileMetadataEntry(string Source, string Name, string Value);
    internal sealed record FileStreamEntry(int Index, string Type, string Codec, string Details);
}
