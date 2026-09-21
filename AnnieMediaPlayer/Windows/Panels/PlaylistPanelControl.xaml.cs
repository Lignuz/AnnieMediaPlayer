using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AnnieMediaPlayer.Windows.Panels
{
    public partial class PlaylistPanelControl : UserControl
    {
        public event EventHandler? AddFilesRequested;
        public event EventHandler<IReadOnlyList<PlaylistItemViewModel>>? RemoveRequested;
        public event EventHandler? ClearRequested;
        public event EventHandler<(IReadOnlyList<PlaylistItemViewModel> Items, int TargetIndex)>? ItemMoveRequested;
        public event EventHandler<PlaylistItemViewModel>? ItemDoubleClicked;
        private Point _dragStartPoint;
        private PlaylistItemViewModel? _pressedItem;
        private IReadOnlyList<PlaylistItemViewModel> _pressedSelection = Array.Empty<PlaylistItemViewModel>();

        public IReadOnlyList<PlaylistItemViewModel> SelectedItems => PlaylistList.SelectedItems.OfType<PlaylistItemViewModel>().ToList();

        public PlaylistPanelControl()
        {
            InitializeComponent();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddFilesRequested?.Invoke(this, EventArgs.Empty);
        private void RemoveButton_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, SelectedItems);
        private void ClearButton_Click(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

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
            if (ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) is ListBoxItem container &&
                container.DataContext is PlaylistItemViewModel item)
                ItemDoubleClicked?.Invoke(this, item);
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
