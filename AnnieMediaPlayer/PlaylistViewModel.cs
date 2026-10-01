using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using AnnieMediaPlayer.Helpers;
using AnnieMediaPlayer.Options;

namespace AnnieMediaPlayer
{
    public sealed class PlaylistItemViewModel : ViewModelBase
    {
        public PlaylistItemViewModel(string filePath)
        {
            FilePath = filePath;
            FileTypeIcon = FileTypeIconService.GetIcon(filePath);
        }

        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public BitmapSource? FileTypeIcon { get; }
        public TimeSpan Duration { get => Get(); set => Set(value); }
        public bool IsCurrent { get => Get(); internal set => Set(value); }

        private BitmapSource? _albumArt;

        public BitmapSource? AlbumArt
        {
            get => _albumArt;
            set
            {
                if (ReferenceEquals(_albumArt, value))
                    return;

                _albumArt = value;
                OnPropertyChanged();
            }
        }
    }

    public sealed class PlaylistViewModel : ViewModelBase
    {
        private PlaylistItemViewModel? _currentItem;
        private readonly List<PlaylistItemViewModel> _shuffleOrder = new();
        private readonly Random _shuffleRandom = new();
        private PlaylistPlaybackMode _playbackMode = PlaylistPlaybackMode.Sequential;
        private bool _shuffleEnabled;

        public ObservableCollection<PlaylistItemViewModel> Items { get; } = new();
        public bool HasItems => Items.Count > 0;
        public bool CanPlayPrevious => CurrentItem != null &&
            (GetAdjacentIndex(false) >= 0 || (_playbackMode == PlaylistPlaybackMode.Playlist && Items.Count > 0));
        public bool CanPlayNext => Items.Count > 0 && GetNextItem() != null;

        public PlaylistViewModel()
        {
            Items.CollectionChanged += Items_CollectionChanged;
        }

        private void Items_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_shuffleEnabled)
            {
                var playlistItems = Items.ToHashSet();
                var remaining = _shuffleOrder.Where(playlistItems.Contains).ToList();
                var knownItems = remaining.ToHashSet();
                foreach (var item in Items.Where(item => knownItems.Add(item)))
                {
                    var currentIndex = remaining.IndexOf(CurrentItem!);
                    var insertFrom = CurrentItem == null ? 0 : Math.Max(0, currentIndex + 1);
                    var insertAt = _shuffleRandom.Next(insertFrom, remaining.Count + 1);
                    remaining.Insert(insertAt, item);
                }

                _shuffleOrder.Clear();
                _shuffleOrder.AddRange(remaining);
            }

            NotifyPlaylistStateChanged();
        }

        public void ConfigurePlayback(PlaylistPlaybackMode playbackMode, bool shuffleEnabled)
        {
            _playbackMode = Enum.IsDefined(playbackMode) ? playbackMode : PlaylistPlaybackMode.Sequential;
            if (_shuffleEnabled != shuffleEnabled)
            {
                _shuffleEnabled = shuffleEnabled;
                RebuildShuffleOrder();
            }
            NotifyPlaylistStateChanged();
        }

        private void RebuildShuffleOrder()
        {
            _shuffleOrder.Clear();
            if (!_shuffleEnabled)
                return;

            var remaining = Items.Where(item => !ReferenceEquals(item, CurrentItem)).ToList();
            for (var index = remaining.Count - 1; index > 0; index--)
            {
                var swapIndex = _shuffleRandom.Next(index + 1);
                (remaining[index], remaining[swapIndex]) = (remaining[swapIndex], remaining[index]);
            }

            if (CurrentItem != null && Items.Contains(CurrentItem))
                _shuffleOrder.Add(CurrentItem);
            _shuffleOrder.AddRange(remaining);
        }

        public PlaylistItemViewModel? CurrentItem
        {
            get => _currentItem;
            private set
            {
                if (ReferenceEquals(_currentItem, value))
                    return;

                if (_currentItem != null)
                    _currentItem.IsCurrent = false;
                _currentItem = value;
                if (_currentItem != null)
                    _currentItem.IsCurrent = true;
                OnPropertyChanged();
                NotifyPlaylistStateChanged();
            }
        }

        private void NotifyPlaylistStateChanged()
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(CanPlayPrevious));
            OnPropertyChanged(nameof(CanPlayNext));
        }

        public void AddFiles(IEnumerable<string> filePaths)
        {
            foreach (var filePath in filePaths)
                AddFile(filePath);
        }

        public PlaylistItemViewModel? AddFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            var fullPath = Path.GetFullPath(filePath);
            var existingItem = Items.FirstOrDefault(item =>
                string.Equals(item.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existingItem != null)
                return existingItem;

            var newItem = new PlaylistItemViewModel(fullPath);
            Items.Add(newItem);
            return newItem;
        }

        public void Remove(PlaylistItemViewModel? item)
        {
            if (item == null)
                return;

            var removedIndex = Items.IndexOf(item);
            if (removedIndex < 0)
                return;

            Items.RemoveAt(removedIndex);

            if (ReferenceEquals(CurrentItem, item))
                CurrentItem = null;
        }

        public void Clear()
        {
            Items.Clear();
            CurrentItem = null;
        }

        public bool MoveManyTo(IEnumerable<PlaylistItemViewModel> items, int targetIndex)
        {
            var selected = Items.Where(items.Contains).ToList();
            if (selected.Count == 0)
                return false;

            var originalTargetIndex = Math.Clamp(targetIndex, 0, Items.Count);
            var itemsBeforeTarget = selected.Count(item => Items.IndexOf(item) < originalTargetIndex);
            var remaining = Items.Where(item => !selected.Contains(item)).ToList();
            var adjustedTargetIndex = Math.Clamp(originalTargetIndex - itemsBeforeTarget, 0, remaining.Count);

            var ordered = remaining;
            ordered.InsertRange(adjustedTargetIndex, selected);

            // 제거·삽입 대신 Move 를 사용하여 항목이 목록에서 빠졌다가 다시 들어온 것으로 처리되지 않게 합니다.
            for (var index = 0; index < ordered.Count; index++)
            {
                var currentIndex = Items.IndexOf(ordered[index]);
                if (currentIndex != index)
                    Items.Move(currentIndex, index);
            }

            return true;
        }

        public void SetCurrent(PlaylistItemViewModel? item)
        {
            if (item != null && !Items.Contains(item))
                return;

            if (ReferenceEquals(CurrentItem, item))
                return;

            CurrentItem = item;
        }

        public PlaylistItemViewModel? GetNextItem(bool automaticAdvance = false)
        {
            if (automaticAdvance && _playbackMode == PlaylistPlaybackMode.CurrentTrackOnly)
                return null;

            if (automaticAdvance && _playbackMode == PlaylistPlaybackMode.Track && CurrentItem != null)
                return CurrentItem;

            if (CurrentItem == null)
                return GetPlaybackOrder().FirstOrDefault();

            var order = GetPlaybackOrder();
            var nextIndex = GetAdjacentIndex(true);
            if (nextIndex >= 0)
                return order[nextIndex];

            return _playbackMode == PlaylistPlaybackMode.Playlist && order.Count > 0
                ? order[0]
                : null;
        }

        public PlaylistItemViewModel? GetPreviousItem()
        {
            if (CurrentItem == null)
                return null;

            var order = GetPlaybackOrder();
            var previousIndex = GetAdjacentIndex(false);
            if (previousIndex >= 0)
                return order[previousIndex];

            return _playbackMode == PlaylistPlaybackMode.Playlist && order.Count > 0
                ? order[^1]
                : null;
        }

        private IList<PlaylistItemViewModel> GetPlaybackOrder() =>
            _shuffleEnabled ? _shuffleOrder : Items;

        private int GetAdjacentIndex(bool forward)
        {
            if (CurrentItem == null)
                return -1;

            var order = GetPlaybackOrder();
            var currentIndex = order.IndexOf(CurrentItem);
            if (currentIndex < 0)
                return -1;

            var adjacentIndex = currentIndex + (forward ? 1 : -1);
            return adjacentIndex >= 0 && adjacentIndex < order.Count ? adjacentIndex : -1;
        }
    }
}
