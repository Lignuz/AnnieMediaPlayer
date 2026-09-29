using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnnieMediaPlayer.CustomControls
{
    // 가사 패널: 시간 정보가 있는 가사는 현재 줄을 강조하며 자동으로 스크롤하고,
    // 시간 정보가 없는 가사는 마우스 휠로 스크롤합니다. 위아래 가장자리는 흐리게 사라집니다.
    public sealed class LyricsPanel : UserControl
    {
        private static readonly Duration ScrollDuration = TimeSpan.FromMilliseconds(450);
        private static readonly Duration HighlightDuration = TimeSpan.FromMilliseconds(220);
        private static readonly Color CurrentText = Colors.White;
        private static readonly Color UpcomingText = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
        private static readonly Color PastText = Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF);
        private static readonly Color PlainText = Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF);
        private static readonly Color CurrentBand = Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF);
        private static readonly Color HoverBand = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF);
        private static readonly Duration ScrollBarFadeDuration = TimeSpan.FromMilliseconds(180);
        private static readonly Brush ThumbBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush ThumbHoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));

        // 현재 줄을 둘 세로 위치(패널 높이 비율)
        private const double FocusRatio = 0.42;
        // 시간 정보가 없는 가사의 첫 줄 위치와, 끝까지 스크롤했을 때 마지막 줄의 아래쪽 위치(패널 높이 비율, 흐림이 시작되는 곳)
        private const double PlainTopRatio = 0.12;
        private const double PlainBottomRatio = 0.86;

        private readonly Grid _viewport = new() { ClipToBounds = true, Background = Brushes.Transparent };
        private readonly LinearGradientBrush _edgeFade;

        // 스크롤 막대: 시간 정보가 없는 가사에서 마우스를 올렸을 때만 나타나며, 끌어서 스크롤할 수 있습니다.
        private readonly Canvas _scrollBar = new()
        {
            Width = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 4, 18),
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        private readonly Border _thumb = new() { Width = 4, CornerRadius = new CornerRadius(2), Background = ThumbBrush };
        private bool _isDraggingThumb;
        private double _dragStartY;
        private double _dragStartScroll;
        // 가사 줄 목록은 패널보다 훨씬 길 수 있습니다. Grid 에 바로 두면 패널 높이로 배치되어 넘치는 줄이 잘리므로,
        // 높이 제한 없이 배치되는 Canvas 에 두고 폭만 맞춥니다. 영역 밖은 _viewport 가 가립니다.
        private readonly Canvas _linesHost = new();
        private readonly StackPanel _lines = new();
        private readonly TranslateTransform _scroll = new();
        private readonly StackPanel _empty = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 0) };
        private readonly List<LineView> _views = new();
        private Lyrics? _lyrics;
        private int _current = -1;
        private double _scrollTarget;

        public event EventHandler<TimeSpan>? LineClicked;

        public LyricsPanel()
        {
            _lines.RenderTransform = _scroll;
            _linesHost.Children.Add(_lines);
            _viewport.Children.Add(_linesHost);

            // 위아래 가장자리를 흐리게 합니다. 상대 좌표로 두면 보이는 영역이 아니라 긴 줄 목록 전체를 기준으로
            // 흐려지므로, 가사 영역 높이를 기준으로 한 절대 좌표를 씁니다(끝점은 Relayout 에서 맞춤).
            _edgeFade = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.Transparent, 0),
                    new(Colors.Black, 0.14),
                    new(Colors.Black, PlainBottomRatio),
                    new(Colors.Transparent, 1),
                },
                new Point(0, 0), new Point(0, 1))
            {
                MappingMode = BrushMappingMode.Absolute,
            };
            _viewport.OpacityMask = _edgeFade;

            Canvas.SetLeft(_thumb, 4);
            _scrollBar.Children.Add(_thumb);
            _thumb.MouseEnter += (_, _) => _thumb.Background = ThumbHoverBrush;
            _thumb.MouseLeave += (_, _) => _thumb.Background = _isDraggingThumb ? ThumbHoverBrush : ThumbBrush;
            _thumb.MouseLeftButtonDown += OnThumbMouseDown;
            _thumb.MouseMove += OnThumbMouseMove;
            _thumb.MouseLeftButtonUp += (_, e) =>
            {
                _thumb.ReleaseMouseCapture();
                e.Handled = true;
            };
            _thumb.LostMouseCapture += (_, _) =>
            {
                _isDraggingThumb = false;
                _thumb.Background = _thumb.IsMouseOver ? ThumbHoverBrush : ThumbBrush;
                UpdateScrollBarVisibility();
            };

            var emptyTitle = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            emptyTitle.SetResourceReference(TextBlock.TextProperty, "Text.Lyrics.None");
            var emptyHint = new TextBlock
            {
                FontSize = 13,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            emptyHint.SetResourceReference(TextBlock.TextProperty, "Text.Lyrics.NoneHint");
            _empty.Children.Add(emptyTitle);
            _empty.Children.Add(emptyHint);

            var content = new Grid();
            content.Children.Add(_viewport);
            content.Children.Add(_empty);
            content.Children.Add(_scrollBar);

            Content = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(0x9E, 0x0E, 0x0E, 0x13)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = content,
            };

            FontFamily = new FontFamily("Segoe UI Variable Display, Malgun Gothic, Segoe UI");
            SizeChanged += (_, _) => Relayout();
            MouseEnter += (_, _) => UpdateScrollBarVisibility();
            MouseLeave += (_, _) => UpdateScrollBarVisibility();
            MouseWheel += OnMouseWheel; // 스크롤 막대 위에서도 휠이 동작하도록 패널 전체에서 받습니다.
            UpdateEmptyState();
        }

        private static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        // 표시할 가사. 가사를 찾지 못했으면 안내 문구를 보여 줍니다.
        public Lyrics? Lyrics
        {
            get => _lyrics;
            set
            {
                if (ReferenceEquals(_lyrics, value))
                    return;

                _lyrics = value;
                Rebuild();
            }
        }

        // 가사를 찾는 중이면 안내 문구를 숨깁니다.
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                UpdateEmptyState();
            }
        }
        private bool _isLoading;

        public void UpdatePosition(TimeSpan position)
        {
            if (_lyrics is not { IsSynced: true })
                return;

            var index = _lyrics.IndexAt(position);
            if (index != _current)
                SetCurrent(index, animate: true);
        }

        private void Rebuild()
        {
            _lines.Children.Clear();
            _views.Clear();
            _current = -1;
            _scroll.BeginAnimation(TranslateTransform.YProperty, null);

            if (_lyrics is not null)
            {
                foreach (var line in _lyrics.Lines)
                {
                    var view = new LineView(line, _lyrics.IsSynced);
                    if (_lyrics.IsSynced && line.Time is TimeSpan time)
                    {
                        view.Border.Cursor = Cursors.Hand;
                        view.Border.MouseEnter += (_, _) => view.SetHover(true, _views.IndexOf(view) == _current);
                        view.Border.MouseLeave += (_, _) => view.SetHover(false, _views.IndexOf(view) == _current);
                        view.Border.MouseLeftButtonUp += (_, e) =>
                        {
                            LineClicked?.Invoke(this, time);
                            e.Handled = true;
                        };
                    }

                    _views.Add(view);
                    _lines.Children.Add(view.Border);
                }
            }

            UpdateEmptyState();
            Relayout();
        }

        private void UpdateEmptyState() =>
            _empty.Visibility = _lyrics is null && !_isLoading ? Visibility.Visible : Visibility.Collapsed;

        // 크기가 바뀌면 글자 크기를 맞춥니다. 시간 정보가 있는 가사는 현재 줄로, 없는 가사는 첫 줄로 바로 옮깁니다.
        private void Relayout()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var fontSize = Math.Clamp(ActualWidth / 19, 15, 24);
            foreach (var view in _views)
                view.SetFontSize(fontSize);

            _lines.Width = _viewport.ActualWidth;
            _edgeFade.EndPoint = new Point(0, _viewport.ActualHeight);
            _lines.UpdateLayout();
            if (_lyrics is { IsSynced: true })
                SetCurrent(_current, animate: false, force: true);
            else
                ScrollTo(PlainScrollTop, animate: false);
            UpdateScrollBarVisibility();
        }

        private void SetCurrent(int index, bool animate, bool force = false)
        {
            if (index == _current && !force)
                return;

            _current = index;
            for (var i = 0; i < _views.Count; i++)
            {
                var state = i == index ? LineState.Current : i < index ? LineState.Past : LineState.Upcoming;
                _views[i].SetState(state, animate);
            }

            if (_views.Count == 0)
                return;

            // 첫 줄이 시작되기 전에는 첫 줄을 현재 줄 자리에 둡니다.
            var focus = _views[Math.Max(0, index)].Border;
            var top = focus.TranslatePoint(new Point(0, 0), _lines).Y;
            ScrollTo(_viewport.ActualHeight * FocusRatio - (top + focus.ActualHeight / 2), animate);
        }

        // 시간 정보가 없는 가사의 스크롤 범위: 첫 줄이 위쪽 흐림 바로 아래에 오는 위치부터,
        // 마지막 줄이 아래쪽 흐림이 시작되는 곳에 닿는 위치까지 (가사가 짧으면 스크롤하지 않음)
        private double PlainScrollTop => _viewport.ActualHeight * PlainTopRatio;
        private double PlainScrollBottom => Math.Min(PlainScrollTop, _viewport.ActualHeight * PlainBottomRatio - _lines.ActualHeight);
        private bool IsScrollable => _lyrics is { IsSynced: false } && PlainScrollBottom < PlainScrollTop - 1;

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_lyrics is null || _lyrics.IsSynced)
                return;

            ScrollTo(Math.Clamp(_scrollTarget + e.Delta * 0.6, PlainScrollBottom, PlainScrollTop), animate: true);
            e.Handled = true;
        }

        private void OnThumbMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingThumb = true;
            _dragStartY = e.GetPosition(_scrollBar).Y;
            _dragStartScroll = _scrollTarget;
            _thumb.CaptureMouse();
            e.Handled = true;
        }

        private void OnThumbMouseMove(object sender, MouseEventArgs e)
        {
            var travel = _scrollBar.ActualHeight - _thumb.Height;
            if (!_isDraggingThumb || travel <= 0)
                return;

            // 막대를 움직인 거리만큼 스크롤 범위 안에서 비례해 옮깁니다.
            var range = PlainScrollTop - PlainScrollBottom;
            var y = _dragStartScroll - (e.GetPosition(_scrollBar).Y - _dragStartY) * range / travel;
            ScrollTo(Math.Clamp(y, PlainScrollBottom, PlainScrollTop), animate: false);
        }

        private void ScrollTo(double y, bool animate)
        {
            _scrollTarget = y;
            UpdateThumb(y, animate);
            if (!animate)
            {
                _scroll.BeginAnimation(TranslateTransform.YProperty, null);
                _scroll.Y = y;
                return;
            }

            var animation = new DoubleAnimation(y, ScrollDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            _scroll.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        // 스크롤 막대의 길이와 위치를 스크롤 범위에 맞춥니다.
        private void UpdateThumb(double y, bool animate)
        {
            var track = _scrollBar.ActualHeight;
            if (!IsScrollable || track <= 0)
                return;

            double top = PlainScrollTop, range = top - PlainScrollBottom, viewport = _viewport.ActualHeight;
            _thumb.Height = Math.Max(24, track * viewport / (viewport + range));
            var thumbTop = (top - y) / range * (track - _thumb.Height);
            if (!animate)
            {
                _thumb.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetTop(_thumb, thumbTop);
                return;
            }

            _thumb.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(thumbTop, ScrollDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }, HandoffBehavior.SnapshotAndReplace);
        }

        private void UpdateScrollBarVisibility()
        {
            var visible = (IsMouseOver || _isDraggingThumb) && IsScrollable;
            _scrollBar.IsHitTestVisible = visible;
            _scrollBar.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, ScrollBarFadeDuration));
        }

        private enum LineState
        {
            Upcoming,
            Current,
            Past,
        }

        // 가사 한 줄: 현재 줄 강조 띠(배경) + 글자
        private sealed class LineView
        {
            private readonly SolidColorBrush _band = new(Colors.Transparent);
            private readonly SolidColorBrush _text;
            private readonly TextBlock _block;

            public LineView(LyricLine line, bool synced)
            {
                _text = new SolidColorBrush(synced ? UpcomingText : PlainText);

                // 시간 정보가 있는 가사의 빈 줄은 간주 표시, 없는 가사의 빈 줄은 문단 여백으로 둡니다.
                var text = line.Text.Length > 0 ? line.Text : synced ? "♪" : " ";
                _block = new TextBlock
                {
                    Text = text,
                    Foreground = _text,
                    FontWeight = FontWeights.Medium,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                };
                Border = new Border
                {
                    Background = _band,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 7, 16, 7),
                    Margin = new Thickness(14, 1, 14, 1),
                    Child = _block,
                };
            }

            public Border Border { get; }

            public void SetFontSize(double size)
            {
                _block.FontSize = size;
                _block.LineHeight = size * 1.35;
            }

            public void SetState(LineState state, bool animate)
            {
                var text = state switch
                {
                    LineState.Current => CurrentText,
                    LineState.Past => PastText,
                    _ => UpcomingText,
                };
                Animate(_text, text, animate);
                Animate(_band, state == LineState.Current ? CurrentBand : Colors.Transparent, animate);
            }

            public void SetHover(bool hover, bool isCurrent)
            {
                if (!isCurrent)
                    Animate(_band, hover ? HoverBand : Colors.Transparent, animate: true);
            }

            private static void Animate(SolidColorBrush brush, Color to, bool animate)
            {
                if (!animate)
                {
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    brush.Color = to;
                    return;
                }

                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, HighlightDuration));
            }
        }
    }
}
