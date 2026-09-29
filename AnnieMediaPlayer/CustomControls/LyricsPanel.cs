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

        // 현재 줄을 둘 세로 위치(패널 높이 비율)
        private const double FocusRatio = 0.42;
        // 시간 정보가 없는 가사의 첫 줄 위치(패널 높이 비율)
        private const double PlainTopRatio = 0.12;

        private readonly Grid _viewport = new() { ClipToBounds = true, Background = Brushes.Transparent };
        private readonly StackPanel _lines = new() { VerticalAlignment = VerticalAlignment.Top };
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
            _viewport.Children.Add(_lines);
            _viewport.OpacityMask = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.Transparent, 0),
                    new(Colors.Black, 0.14),
                    new(Colors.Black, 0.86),
                    new(Colors.Transparent, 1),
                },
                new Point(0, 0), new Point(0, 1));

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
            _viewport.MouseWheel += OnMouseWheel;
            UpdateEmptyState();
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

            _lines.UpdateLayout();
            if (_lyrics is { IsSynced: true })
                SetCurrent(_current, animate: false, force: true);
            else
                ScrollTo(_viewport.ActualHeight * PlainTopRatio, animate: false);
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

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_lyrics is null || _lyrics.IsSynced)
                return;

            var viewport = _viewport.ActualHeight;
            var min = Math.Min(viewport * PlainTopRatio, viewport * 0.5 - _lines.ActualHeight);
            ScrollTo(Math.Clamp(_scrollTarget + e.Delta * 0.6, min, viewport * PlainTopRatio), animate: true);
            e.Handled = true;
        }

        private void ScrollTo(double y, bool animate)
        {
            _scrollTarget = y;
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
