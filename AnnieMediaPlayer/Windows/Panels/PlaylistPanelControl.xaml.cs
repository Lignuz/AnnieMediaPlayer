using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Runtime.InteropServices;
using AnnieMediaPlayer.Options;
using AnnieMediaPlayer.Windows.Info;

namespace AnnieMediaPlayer.Windows.Panels
{
    public partial class PlaylistPanelControl : UserControl
    {
        public event EventHandler? AddFilesRequested;
        public event EventHandler<IReadOnlyList<PlaylistItemViewModel>>? RemoveRequested;
        public event EventHandler? ClearRequested;
        public event EventHandler? PlayPauseRequested;
        public event EventHandler? NextRequested;
        public event EventHandler<(IReadOnlyList<PlaylistItemViewModel> Items, int TargetIndex)>? ItemMoveRequested;
        public event EventHandler<PlaylistItemViewModel>? ItemActivated;
        private Point _dragStartPoint;
        private PlaylistItemViewModel? _pressedItem;
        private IReadOnlyList<PlaylistItemViewModel> _pressedSelection = Array.Empty<PlaylistItemViewModel>();

        public IReadOnlyList<PlaylistItemViewModel> SelectedItems => PlaylistList.SelectedItems.OfType<PlaylistItemViewModel>().ToList();
        public PlaylistItemViewModel? SelectedItem => PlaylistList.SelectedItem as PlaylistItemViewModel;
        public bool IsListKeyboardFocusWithin => PlaylistList.IsKeyboardFocusWithin;

        public PlaylistPanelControl()
        {
            InitializeComponent();
            Loaded += PlaylistPanelControl_Loaded;
            Unloaded += PlaylistPanelControl_Unloaded;
            UpdatePlaybackModeButton();
        }

        private void PlaylistPanelControl_Loaded(object sender, RoutedEventArgs e)
        {
            OptionViewModel.Instance.CurrentOption.PropertyChanged += CurrentOption_PropertyChanged;
            LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
            UpdatePlaybackModeButton();
        }

        private void PlaylistPanelControl_Unloaded(object sender, RoutedEventArgs e)
        {
            OptionViewModel.Instance.CurrentOption.PropertyChanged -= CurrentOption_PropertyChanged;
            LanguageManager.LanguageChanged -= LanguageManager_LanguageChanged;
        }

        private void CurrentOption_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Option.PlaylistPlaybackMode) or nameof(Option.ShufflePlayback))
                UpdatePlaybackModeButton();
        }

        private void LanguageManager_LanguageChanged(object? sender, EventArgs e) => UpdatePlaybackModeButton();

        private void UpdatePlaybackModeButton()
        {
            var mode = OptionViewModel.Instance.CurrentOption.PlaylistPlaybackMode;
            RepeatModeButton.IsChecked = mode != PlaylistPlaybackMode.Sequential;
            RepeatModeBadge.Text = mode switch
            {
                PlaylistPlaybackMode.CurrentTrackOnly => "1x",
                PlaylistPlaybackMode.Track => "1",
                PlaylistPlaybackMode.Playlist => "∞",
                _ => "x",
            };

            var resourceKey = mode switch
            {
                PlaylistPlaybackMode.Playlist => "Text.Playback.RepeatPlaylist",
                PlaylistPlaybackMode.Track => "Text.Playback.RepeatTrack",
                PlaylistPlaybackMode.CurrentTrackOnly => "Text.Playback.CurrentTrackOnly",
                _ => "Text.Playback.Sequential",
            };
            var label = LanguageManager.GetResourceString(resourceKey);
            RepeatModeButton.ToolTip = label;
            AutomationProperties.SetName(RepeatModeButton, label);

            var shuffleEnabled = OptionViewModel.Instance.CurrentOption.ShufflePlayback;
            ShufflePlaybackIcon.Data = (Geometry)FindResource(shuffleEnabled
                ? "PlaybackShuffleIconData"
                : "PlaybackSequentialIconData");
            var shuffleResourceKey = shuffleEnabled
                ? "Text.Playback.Shuffle.On"
                : "Text.Playback.Shuffle.Off";
            var shuffleLabel = LanguageManager.GetResourceString(shuffleResourceKey);
            ShufflePlaybackButton.ToolTip = shuffleLabel;
            AutomationProperties.SetName(ShufflePlaybackButton, shuffleLabel);
        }

        private void RepeatModeButton_Click(object sender, RoutedEventArgs e)
        {
            var option = OptionViewModel.Instance.CurrentOption;
            option.PlaylistPlaybackMode = option.PlaylistPlaybackMode switch
            {
                PlaylistPlaybackMode.CurrentTrackOnly => PlaylistPlaybackMode.Track,
                PlaylistPlaybackMode.Track => PlaylistPlaybackMode.Sequential,
                PlaylistPlaybackMode.Sequential => PlaylistPlaybackMode.Playlist,
                _ => PlaylistPlaybackMode.CurrentTrackOnly,
            };
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddFilesRequested?.Invoke(this, EventArgs.Empty);
        private void RemoveButton_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, SelectedItems);
        private void ClearButton_Click(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);
        private void NextButton_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);

        private void CurrentItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.Playlist.CurrentItem is { } item && PlaylistList.Items.Contains(item))
            {
                PlaylistList.SelectedItems.Clear();
                PlaylistList.SelectedItem = item;
                PlaylistList.ScrollIntoView(item);
                PlaylistList.Focus();
            }
        }

        private void PlaylistList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) is ListBoxItem container)
            {
                if (!container.IsSelected)
                {
                    PlaylistList.SelectedItems.Clear();
                    container.IsSelected = true;
                }
                container.Focus();
            }
        }

        private void FileInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { DataContext: PlaylistItemViewModel item })
            {
                var window = new FileInfoWindow(item.FilePath) { Owner = Window.GetWindow(this) };
                window.ShowDialog();
            }
        }

        private async void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: PlaylistItemViewModel item })
                return;
            try
            {
                await FileExplorer.ShowFileAsync(item.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
            {
                PlayerDiagnostics.Write($"Could not select file in Explorer: {ex}");
                if (IsLoaded)
                    MessageBox.Show(Window.GetWindow(this), LanguageManager.GetResourceString("Text.Playlist.OpenInExplorerFailed"),
                        LanguageManager.GetResourceString("Text.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PlaylistList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) as ListBoxItem;
            _pressedItem = container?.DataContext as PlaylistItemViewModel;
            _pressedSelection = SelectedItems;
            _dragStartPoint = e.GetPosition(PlaylistList);
        }

        private void PlaylistList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pressedItem == null)
                return;

            var currentPoint = e.GetPosition(PlaylistList);
            if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var draggedItems = _pressedSelection.Contains(_pressedItem)
                ? _pressedSelection.ToList()
                : new List<PlaylistItemViewModel> { _pressedItem };
            _pressedItem = null;
            _pressedSelection = Array.Empty<PlaylistItemViewModel>();
            DragDrop.DoDragDrop(PlaylistList, draggedItems, DragDropEffects.Move);
            HideDropIndicator();
        }

        private void PlaylistList_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(List<PlaylistItemViewModel>)))
            {
                HideDropIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = UpdateDropIndicator(e) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void PlaylistList_DragLeave(object sender, DragEventArgs e) => HideDropIndicator();

        private void PlaylistList_Drop(object sender, DragEventArgs e)
        {
            HideDropIndicator();
            if (!e.Data.GetDataPresent(typeof(List<PlaylistItemViewModel>)))
                return;

            var draggedItems = e.Data.GetData(typeof(List<PlaylistItemViewModel>)) as List<PlaylistItemViewModel>;
            var target = ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) as ListBoxItem;
            var targetItem = target?.DataContext as PlaylistItemViewModel;
            if (draggedItems == null || draggedItems.Count == 0 || target == null || targetItem == null || draggedItems.Contains(targetItem))
                return;

            var targetIndex = GetTargetIndex(target, e.GetPosition(target));
            ItemMoveRequested?.Invoke(this, (draggedItems, targetIndex));
            PlaylistList.SelectedItems.Clear();
            foreach (var item in draggedItems)
                PlaylistList.SelectedItems.Add(item);
            e.Handled = true;
        }

        private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveButton.IsEnabled = SelectedItems.Count > 0;
        }

        private void PlaylistList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                e.Handled = true;
                return;
            }

            if (ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) is ListBoxItem container &&
                container.DataContext is PlaylistItemViewModel item)
                ItemActivated?.Invoke(this, item);
        }

        private bool UpdateDropIndicator(DragEventArgs e)
        {
            var target = ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) as ListBoxItem;
            var draggedItems = e.Data.GetData(typeof(List<PlaylistItemViewModel>)) as List<PlaylistItemViewModel>;
            if (target?.DataContext is not PlaylistItemViewModel targetItem || draggedItems == null || draggedItems.Contains(targetItem))
            {
                HideDropIndicator();
                return false;
            }

            var targetIndex = GetTargetIndex(target, e.GetPosition(target));
            var targetPoint = target.TransformToAncestor(PlaylistHost).Transform(new Point(0, 0));
            DropIndicator.Width = PlaylistList.ActualWidth;
            DropIndicatorTransform.Y = targetPoint.Y + (targetIndex > PlaylistList.Items.IndexOf(targetItem) ? target.ActualHeight : 0);
            DropIndicator.Visibility = Visibility.Visible;
            return true;
        }

        private int GetTargetIndex(ListBoxItem target, Point targetPoint)
        {
            var targetIndex = PlaylistList.Items.IndexOf(target.DataContext);
            if (targetPoint.Y > target.ActualHeight / 2)
                targetIndex++;

            return Math.Clamp(targetIndex, 0, PlaylistList.Items.Count);
        }

        private void HideDropIndicator()
        {
            DropIndicator.Visibility = Visibility.Collapsed;
        }
    }
}
