using System.Collections.ObjectModel;
using System.IO;

namespace AnnieMediaPlayer
{
    public sealed class PlaylistItemViewModel : ViewModelBase
    {
        public PlaylistItemViewModel(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public TimeSpan Duration { get => Get(); set => Set(value); }
    }

    public sealed class PlaylistViewModel : ViewModelBase
    {
        private PlaylistItemViewModel? _currentItem;

        public ObservableCollection<PlaylistItemViewModel> Items { get; } = new();
        public bool HasItems => Items.Count > 0;
        public bool CanPlayPrevious => CurrentItem != null && Items.IndexOf(CurrentItem) > 0;
        public bool CanPlayNext => Items.Count > 0 &&
            (CurrentItem == null || Items.IndexOf(CurrentItem) < Items.Count - 1);

        public PlaylistViewModel()
        {
            Items.CollectionChanged += (_, _) => NotifyPlaylistStateChanged();
        }

        public PlaylistItemViewModel? CurrentItem
        {
            get => _currentItem;
            private set
            {
                if (ReferenceEquals(_currentItem, value))
                    return;

                _currentItem = value;
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

            foreach (var item in selected)
                Items.Remove(item);

            for (var index = 0; index < selected.Count; index++)
                Items.Insert(adjustedTargetIndex + index, selected[index]);

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

        public PlaylistItemViewModel? GetNextItem()
        {
            if (CurrentItem == null)
                return Items.FirstOrDefault();

            var currentIndex = Items.IndexOf(CurrentItem);
            return currentIndex >= 0 && currentIndex + 1 < Items.Count
                ? Items[currentIndex + 1]
                : null;
        }

        public PlaylistItemViewModel? GetPreviousItem()
        {
            if (CurrentItem == null)
                return null;

            var currentIndex = Items.IndexOf(CurrentItem);
            return currentIndex > 0 ? Items[currentIndex - 1] : null;
        }
    }
}
