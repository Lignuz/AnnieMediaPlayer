using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Unosquare.FFME;
using Microsoft.Win32;

namespace AnnieMediaPlayer.Windows.Info
{
    public partial class FileInfoWindow : BaseWindow
    {
        private readonly FileInfoViewModel _viewModel;
        private readonly AlbumArtService _albumArtService = new();
        private readonly CancellationTokenSource _loadCancellation = new();
        private bool _loadStarted;
        private MediaTagSnapshot? _tagSnapshot;
        private ArtworkEdit _artworkEdit;
        private byte[]? _replacementArtwork;
        private bool _discardChanges;
        private bool _isSaving;
        private bool _closeAfterSave;
        public event EventHandler? TagsSaved;
        public string FilePath => _viewModel.FilePath;

        public FileInfoWindow(string filePath)
        {
            InitializeComponent();
            _viewModel = new FileInfoViewModel(filePath);
            DataContext = _viewModel;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Loaded += async (_, _) => await LoadFileInfoAsync();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isSaving)
                _loadCancellation.Cancel();
            else if (!_discardChanges && _viewModel.CanSave)
            {
                var result = MessageBox.Show(this, Text("Text.FileInfo.Unsaved"), Text("Text.FileInfo"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) e.Cancel = true;
                else if (result == MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    _closeAfterSave = true;
                    _ = SaveTagsAsync();
                }
                else _discardChanges = true;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _loadCancellation.Cancel();
            _loadCancellation.Dispose();
            _albumArtService.Dispose();
            base.OnClosed(e);
        }

        private async Task LoadFileInfoAsync()
        {
            if (_loadStarted)
                return;
            _loadStarted = true;
            _viewModel.IsBusy = true;
            _viewModel.Summary.Clear();
            _viewModel.Tags.Clear();
            _viewModel.Streams.Clear();
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
                if (MediaTagService.SupportsEditing(_viewModel.FilePath))
                {
                    try
                    {
                        _tagSnapshot = await Task.Run(() => MediaTagService.Read(_viewModel.FilePath), token);
                        token.ThrowIfCancellationRequested();
                        _artworkEdit = ArtworkEdit.Keep;
                        _replacementArtwork = null;
                        _viewModel.LoadEditor(_tagSnapshot.Values);
                        _viewModel.CanEdit = true;
                        _viewModel.EditorStatus = Text("Text.FileInfo.EditHelp");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        PlayerDiagnostics.Write($"Tag editor unavailable: {ex.Message}");
                        _viewModel.CanEdit = false;
                        _viewModel.EditorStatus = Text("Text.FileInfo.Unsupported");
                    }
                }
                else _viewModel.EditorStatus = Text("Text.FileInfo.Unsupported");
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
            finally { _viewModel.IsBusy = false; }
        }

        private async void SaveTags_Click(object sender, RoutedEventArgs e) => await SaveTagsAsync();

        private async Task SaveTagsAsync()
        {
            if (!_viewModel.CanSave || _tagSnapshot is null) return;
            _isSaving = true;
            _viewModel.IsBusy = true;
            _viewModel.EditorStatus = Text("Text.FileInfo.Saving");
            var token = _loadCancellation.Token;
            try
            {
                Task Save() => MediaTagService.SaveAsync(_viewModel.FilePath, _tagSnapshot, _viewModel.EditedValues,
                    _artworkEdit, _replacementArtwork, () => VideoPlayerController.IsFileInUse(_viewModel.FilePath), token);
                // 재생 중인 파일이면 플레이어가 잠시 파일을 닫았다가 저장 후 이어서 재생합니다.
                if (Application.Current.MainWindow is MainWindow player)
                    await player.RunWithFileReleasedAsync(_viewModel.FilePath, Save);
                else
                    await Save();
                _albumArtService.ClearCache();
                _loadStarted = false;
                await LoadFileInfoAsync();
                if (token.IsCancellationRequested) return;
                _viewModel.EditorStatus = Text("Text.FileInfo.Saved");
                TagsSaved?.Invoke(this, EventArgs.Empty);
                if (_closeAfterSave) { _discardChanges = true; Close(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Tag save failed: {ex}");
                _viewModel.EditorStatus = ex switch
                {
                    ArgumentException => Text("Text.FileInfo.InvalidNumber"),
                    InvalidOperationException => Text("Text.FileInfo.InUse"),
                    _ => Text("Text.FileInfo.SaveFailed")
                };
                _closeAfterSave = false;
            }
            finally { _isSaving = false; _viewModel.IsBusy = false; }
        }

        private void ChangeArtwork_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanEditFields) return;
            var dialog = new OpenFileDialog { Filter = Text("Text.FileInfo.ImageFilter") };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (new System.IO.FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) throw new IOException("Artwork exceeds 16 MiB.");
                var bytes = File.ReadAllBytes(dialog.FileName);
                var image = CreateArtworkPreview(bytes);
                _replacementArtwork = bytes;
                _artworkEdit = ArtworkEdit.Replace;
                _viewModel.AlbumArt = image;
                _viewModel.AlbumArtDescription = Text("Text.FileInfo.Artwork.Pending");
                _viewModel.ArtworkChanged = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException
                or InvalidOperationException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException or System.Runtime.InteropServices.COMException)
            { _viewModel.EditorStatus = Text("Text.FileInfo.ImageFailed"); }
        }

        private static BitmapImage CreateArtworkPreview(byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) throw new IOException("Artwork exceeds the permitted size.");
            var picture = new TagLib.Picture(new TagLib.ByteVector(bytes));
            if (picture.MimeType is not ("image/jpeg" or "image/png")) throw new IOException("Unsupported image format.");
            using var stream = new MemoryStream(bytes);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            var width = frame.PixelWidth;
            var height = frame.PixelHeight;
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
                throw new IOException("Artwork dimensions exceed 8192 pixels.");
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (width >= height) image.DecodePixelWidth = Math.Min(512, width);
            else image.DecodePixelHeight = Math.Min(512, height);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void RemoveArtwork_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanEditFields) return;
            _replacementArtwork = null;
            _artworkEdit = ArtworkEdit.Remove;
            _viewModel.AlbumArt = null;
            _viewModel.AlbumArtDescription = Text("Text.FileInfo.Artwork.RemovePending");
            _viewModel.ArtworkChanged = true;
        }

        private void AddTags(string source, IReadOnlyDictionary<string, string> tags)
        {
            foreach (var tag in tags.OrderBy(tag => tag.Key, StringComparer.OrdinalIgnoreCase))
                _viewModel.Tags.Add(new(source, tag.Key, MetadataText.Repair(tag.Value)));
        }

        private static string Text(string key) => LanguageManager.GetResourceString(key);
    }

    internal sealed class FileInfoViewModel : ViewModelBase
    {
        private BitmapSource? _albumArt;
        private string _albumArtDescription = string.Empty;
        private string _status = string.Empty;
        public string FilePath { get; }

        public FileInfoViewModel(string filePath)
        {
            FilePath = filePath;
            // 화면이 처음 값을 읽을 때 기본값이 없으면 예외가 나므로, 문자열 속성을 미리 비워 둡니다.
            EditorStatus = EditTitle = EditArtists = EditAlbumArtists = EditAlbum = EditYear = EditTrack =
                EditGenres = EditComment = EditLyrics = string.Empty;
        }

        public string FileName => Path.GetFileName(FilePath);
        public ObservableCollection<FileInfoField> Summary { get; } = new();
        public ObservableCollection<FileMetadataEntry> Tags { get; } = new();
        public ObservableCollection<FileStreamEntry> Streams { get; } = new();
        public BitmapSource? AlbumArt { get => _albumArt; set { _albumArt = value; OnPropertyChanged(); } }
        public string AlbumArtDescription { get => _albumArtDescription; set { _albumArtDescription = value; OnPropertyChanged(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        private MediaTagValues? _originalValues;
        public string EditorStatus { get => Get(); set => Set(value); }
        public string EditTitle { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditArtists { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditAlbumArtists { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditAlbum { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditYear { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditTrack { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditGenres { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditComment { get => Get(); set { Set(value); NotifyEditor(); } }
        public string EditLyrics { get => Get(); set { Set(value); NotifyEditor(); } }
        public bool ArtworkChanged { get => Get(); set { Set(value); NotifyEditor(); } }
        public bool IsBusy { get => Get(); set { Set(value); NotifyEditor(); } }
        public bool CanEdit { get => Get(); set { Set(value); NotifyEditor(); } }
        public bool CanEditFields => CanEdit && !IsBusy;
        public bool CanSave => CanEditFields && (ArtworkChanged || _originalValues != EditedValues);
        public MediaTagValues EditedValues => new(EditTitle ?? "", EditArtists ?? "", EditAlbumArtists ?? "", EditAlbum ?? "",
            EditYear ?? "", EditTrack ?? "", EditGenres ?? "", EditComment ?? "", EditLyrics ?? "");
        private void NotifyEditor() { OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanEditFields)); }
        public void LoadEditor(MediaTagValues values)
        {
            _originalValues = values;
            EditTitle = values.Title; EditArtists = values.Artists; EditAlbumArtists = values.AlbumArtists;
            EditAlbum = values.Album; EditYear = values.Year; EditTrack = values.Track; EditGenres = values.Genres;
            EditComment = values.Comment; EditLyrics = values.Lyrics; ArtworkChanged = false;
        }
    }

    internal sealed record FileInfoField(string Name, string Value);
    internal sealed record FileMetadataEntry(string Source, string Name, string Value);
    internal sealed record FileStreamEntry(int Index, string Type, string Codec, string Details);
}
