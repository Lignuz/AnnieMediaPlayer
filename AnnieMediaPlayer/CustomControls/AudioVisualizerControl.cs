using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer.CustomControls
{
    // 오디오 시각화 컨트롤
    //   [1] Aura   : 커버 색으로 만든 컬러 필드 위에서 대역별 빛이 숨쉬듯 부풀고, 비트에 커버가 펄스
    //   [2] Halo   : 회전하는 원형 커버(바이닐) 둘레의 대칭 스펙트럼 + 비트 파티클
    //   [3] Ribbon : 저역~고역 웨이브 4겹이 가산 혼합으로 겹치는 발광 웨이브 + 하단 글로우
    //
    // 화면 구성: base(일반 합성) → 가산 레이어(AdditiveBlendEffect) → overlay(일반 합성)
    // 가산 혼합은 픽셀 셰이더로 처리하며, 매 프레임 DrawingGroup 만 다시 기록해 레이아웃 패스를 피합니다.
    public sealed class AudioVisualizerControl : Control
    {
        private const int FftSize = 4096;
        private const int BandCount = 64;
        private const int ModeCount = 3;
        private const int AdditiveLayerCount = 6;
        private const int OpacityLevels = 64;

        // 화면 주사율과 관계없이 초당 최대 약 40번만 분석·그리기를 합니다.
        private static readonly TimeSpan MinimumFrameInterval = TimeSpan.FromMilliseconds(25);
        private static readonly string[] ModeNames = { "Aura", "Halo", "Ribbon" };
        private static readonly FontFamily TextFont = new("Segoe UI Variable Display, Segoe UI");
        private static readonly CultureInfo TextCulture = CultureInfo.GetCultureInfo("ko-KR");

        // 오디오 입력과 분석
        private readonly AudioRingBuffer _audio = new(1 << 16);
        private readonly SpectrumAnalyzer _analyzer = new();
        private readonly float[] _samples = new float[FftSize];
        private float[] _monoScratch = Array.Empty<float>(); // 오디오 스레드 전용
        private volatile bool _acceptAudioSamples;

        // 레이어
        private readonly Grid _root = new();
        private readonly DrawingHost _baseHost = new();
        private readonly DrawingHost _overlayHost = new();
        private readonly Decorator _innerLayer = new();
        private readonly Decorator _outerLayer = new();
        private readonly AdditiveBlendEffect _innerEffect = new();
        private readonly AdditiveBlendEffect _outerEffect = new();
        private readonly DrawingGroup[] _additive = new DrawingGroup[AdditiveLayerCount];
        private readonly ImageBrush[] _additiveBrushes = new ImageBrush[AdditiveLayerCount];

        // 시뮬레이션 상태
        private readonly List<Particle> _particles = new();
        private int _mode;
        private float _t;
        private float _hudRemaining = 2.8f;
        private float _spin;
        private float _emitAccumulator;
        private int _lastBeatCount;
        private uint _rng = 777u;

        // 렌더링 루프
        private bool _renderingHooked;
        private TimeSpan? _lastRenderingTime;
        private Window? _window;

        // 영역·리소스
        private double _width;
        private double _height;
        private double _unit = 1;
        private double _pixelsPerDip = 1;
        private bool _paletteDirty = true;
        private bool _sizeDirty = true;
        private VizResources? _res;
        private readonly Dictionary<string, FormattedText> _textCache = new();
        private readonly Point[] _ribbonUp = new Point[RibbonPointCount];
        private readonly Point[] _ribbonDown = new Point[RibbonPointCount];
        private const int RibbonPointCount = 180;

        public AudioVisualizerControl()
        {
            Focusable = true;
            ClipToBounds = true;

            for (var i = 0; i < AdditiveLayerCount; i++)
            {
                _additive[i] = new DrawingGroup();
                // 셰이더 샘플러 입력은 ImageBrush 등만 허용되므로 DrawingImage 로 감쌉니다.
                _additiveBrushes[i] = new ImageBrush(new DrawingImage(_additive[i])) { Stretch = Stretch.Fill };
            }

            _innerEffect.Input = System.Windows.Media.Effects.Effect.ImplicitInput;
            _innerEffect.Layer1 = _additiveBrushes[0];
            _innerEffect.Layer2 = _additiveBrushes[1];
            _innerEffect.Layer3 = _additiveBrushes[2];
            _outerEffect.Input = System.Windows.Media.Effects.Effect.ImplicitInput;
            _outerEffect.Layer1 = _additiveBrushes[3];
            _outerEffect.Layer2 = _additiveBrushes[4];
            _outerEffect.Layer3 = _additiveBrushes[5];

            _innerLayer.Child = _baseHost;
            _outerLayer.Child = _innerLayer;
            _root.Children.Add(_outerLayer);
            _root.Children.Add(_overlayHost);
            AddVisualChild(_root);

            IsVisibleChanged += (_, _) => UpdateRenderingHook();
            Loaded += (_, _) =>
            {
                _window = Window.GetWindow(this);
                if (_window != null)
                    _window.StateChanged += OnWindowStateChanged;
                UpdateRenderingHook();
            };
            Unloaded += (_, _) =>
            {
                if (_window != null)
                    _window.StateChanged -= OnWindowStateChanged;
                UpdateRenderingHook();
            };
            MouseMove += OnMouseMove;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        #region 의존성 프로퍼티

        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(AudioVisualizerControl),
            new FrameworkPropertyMetadata(false, OnIsActiveChanged));

        public static readonly DependencyProperty AlbumArtProperty = DependencyProperty.Register(
            nameof(AlbumArt), typeof(BitmapSource), typeof(AudioVisualizerControl),
            new FrameworkPropertyMetadata(null, OnAlbumArtChanged));

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(AudioVisualizerControl),
            new FrameworkPropertyMetadata(string.Empty, OnContentChanged));

        public static readonly DependencyProperty ArtistProperty = DependencyProperty.Register(
            nameof(Artist), typeof(string), typeof(AudioVisualizerControl),
            new FrameworkPropertyMetadata(string.Empty, OnContentChanged));

        public static readonly DependencyProperty AlbumProperty = DependencyProperty.Register(
            nameof(Album), typeof(string), typeof(AudioVisualizerControl),
            new FrameworkPropertyMetadata(string.Empty, OnContentChanged));

        public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
            nameof(IsPlaying), typeof(bool), typeof(AudioVisualizerControl),
            new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
            nameof(FilePath), typeof(string), typeof(AudioVisualizerControl),
            new FrameworkPropertyMetadata(string.Empty, OnFilePathChanged));

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public BitmapSource? AlbumArt
        {
            get => (BitmapSource?)GetValue(AlbumArtProperty);
            set => SetValue(AlbumArtProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Artist
        {
            get => (string)GetValue(ArtistProperty);
            set => SetValue(ArtistProperty, value);
        }

        public string Album
        {
            get => (string)GetValue(AlbumProperty);
            set => SetValue(AlbumProperty, value);
        }

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        public string FilePath
        {
            get => (string)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        public int Mode => _mode;

        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AudioVisualizerControl)d;
            control._acceptAudioSamples = (bool)e.NewValue;
            control._hudRemaining = 2.8f;
            control.ResetAudioInput();
            control.UpdateRenderingHook();
            control.RenderNow();
        }

        private static void OnAlbumArtChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AudioVisualizerControl)d;
            control._paletteDirty = true;
            control.RenderNow();
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AudioVisualizerControl)d;
            control._textCache.Clear();
            control.RenderNow();
        }

        private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AudioVisualizerControl)d;
            if (control.IsActive)
                control.ResetAudioInput();
        }

        #endregion

        #region 레이아웃

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) =>
            index == 0 ? _root : throw new ArgumentOutOfRangeException(nameof(index));

        protected override Size MeasureOverride(Size constraint)
        {
            _root.Measure(constraint);
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            _root.Arrange(new Rect(arrangeSize));
            if (arrangeSize.Width != _width || arrangeSize.Height != _height)
            {
                _width = arrangeSize.Width;
                _height = arrangeSize.Height;
                _unit = Math.Min(_width, _height) / 800.0;
                _sizeDirty = true;
                RenderNow();
            }

            return arrangeSize;
        }

        #endregion

        #region 오디오 입력

        public void PushAudioSamples(IReadOnlyList<byte> buffer, int bufferLength, int sampleRate, int channelCount, int bitsPerSample)
        {
            if (!_acceptAudioSamples || bitsPerSample != 16 || channelCount <= 0 || sampleRate <= 0)
                return;

            var frameSize = channelCount * sizeof(short);
            var frameCount = Math.Min(Math.Max(0, bufferLength), buffer.Count) / frameSize;
            if (frameCount <= 0)
                return;

            if (_monoScratch.Length < frameCount)
                _monoScratch = new float[frameCount];
            var mono = _monoScratch;

            if (buffer is byte[] bytes)
            {
                var pcm = MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, frameCount * frameSize));
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var sum = 0f;
                    for (var channel = 0; channel < channelCount; channel++)
                        sum += pcm[frame * channelCount + channel];
                    mono[frame] = sum / (32768f * channelCount);
                }
            }
            else
            {
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var sum = 0f;
                    for (var channel = 0; channel < channelCount; channel++)
                    {
                        var offset = frame * frameSize + channel * sizeof(short);
                        sum += unchecked((short)(buffer[offset] | buffer[offset + 1] << 8));
                    }
                    mono[frame] = sum / (32768f * channelCount);
                }
            }

            _analyzer.SetSampleRate(sampleRate);
            _audio.Push(mono.AsSpan(0, frameCount));
        }

        private void ResetAudioInput()
        {
            _audio.Clear();
            _analyzer.Reset();
            _particles.Clear();
            _emitAccumulator = 0;
            _lastBeatCount = _analyzer.BeatCount;
        }

        #endregion

        #region 렌더링 루프

        private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateRenderingHook();

        private void UpdateRenderingHook()
        {
            // 최소화 중에는 렌더링 이벤트 구독을 해제해 WPF가 매 프레임 깨어나지 않게 합니다.
            var wanted = IsActive && IsVisible && _window?.WindowState != WindowState.Minimized;
            if (wanted && !_renderingHooked)
            {
                _lastRenderingTime = null;
                CompositionTarget.Rendering += OnRendering;
                _renderingHooked = true;
            }
            else if (!wanted && _renderingHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderingHooked = false;
            }
        }

        // 화면 주사율에 맞춰 호출되며, 실제 경과 시간으로 시뮬레이션합니다.
        private void OnRendering(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs args || args.RenderingTime == _lastRenderingTime)
                return; // 같은 프레임에서 여러 번 호출되는 경우

            // 주사율이 높은 화면에서도 분석과 그리기 비용이 늘지 않도록 프레임을 건너뜁니다.
            if (_lastRenderingTime is TimeSpan previous && args.RenderingTime - previous < MinimumFrameInterval)
                return;

            var dt = _lastRenderingTime is TimeSpan last ? (args.RenderingTime - last).TotalSeconds : 1.0 / 60.0;
            _lastRenderingTime = args.RenderingTime;

            // 창이 최소화되어 보이지 않으면 분석·그리기를 쉽니다.
            if (_window?.WindowState == WindowState.Minimized)
                return;

            AdvanceFrame(Math.Clamp(dt, 0, 0.1));
            RenderNow();
        }

        private void AdvanceFrame(double dt)
        {
            // 일시정지 중에는 무음을 분석해 화면이 자연스럽게 잦아들게 합니다.
            if (IsPlaying)
                _audio.Latest(_samples);
            else
                Array.Clear(_samples);

            var spectrum = _analyzer.Process(_samples, (float)dt);
            Update(spectrum, (float)dt);
        }

        private void Update(Spectrum s, float dt)
        {
            _t += dt;
            _hudRemaining = Math.Max(0, _hudRemaining - dt);
            _spin += dt * (0.25f + 0.6f * s.Level); // 에너지가 크면 조금 빨리 회전

            // 비트마다 파티클 버스트 + 고역 에너지에 비례한 상시 방출
            if (_analyzer.BeatCount != _lastBeatCount)
            {
                _lastBeatCount = _analyzer.BeatCount;
                Emit(26, 1f);
            }

            _emitAccumulator += dt * (8f + 60f * s.Treble);
            while (_emitAccumulator > 1f)
            {
                _emitAccumulator -= 1f;
                Emit(1, 0.6f);
            }

            for (var i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.R += p.Velocity * dt;
                p.Velocity *= MathF.Exp(-dt * 0.9f);
                p.Life -= dt;
                p.Angle += p.Spin * dt;
                if (p.Life <= 0)
                    _particles.RemoveAt(i);
                else
                    _particles[i] = p;
            }
        }

        private void Emit(int count, float strength)
        {
            for (var i = 0; i < count && _particles.Count < 600; i++)
            {
                var p = new Particle
                {
                    Angle = Rand() * 2 * MathF.PI,
                    R = 1f + Rand() * 0.05f, // 반경 단위: 커버 반지름
                    Velocity = (0.25f + Rand() * 0.55f) * strength,
                    Size = 1.2f + Rand() * 2.4f,
                    Spin = (Rand() - 0.5f) * 0.3f
                };
                p.MaxLife = p.Life = 1.2f + Rand() * 1.6f;
                p.Color = Rand() < 0.15f ? 3 : (int)(Rand() * 3);
                _particles.Add(p);
            }
        }

        private float Rand()
        {
            _rng = _rng * 1664525u + 1013904223u;
            return (_rng >> 8) / 16777216f;
        }

        #endregion

        #region 마우스 (HUD)

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (IsActive)
                _hudRemaining = Math.Max(_hudRemaining, 1.5f);
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsActive)
                return;

            var point = e.GetPosition(this);
            var hit = HudHit(point.X, point.Y);
            if (hit >= 0)
            {
                SetMode(hit);
                e.Handled = true;
            }
        }

        private void SetMode(int mode)
        {
            _mode = (mode + ModeCount) % ModeCount;
            _hudRemaining = 2.8f;
            RenderNow();
        }

        private Rect HudRect(int index)
        {
            double segment = 96 * _unit, height = 38 * _unit, pad = 5 * _unit;
            var width = segment * ModeCount + pad * 2;
            var rect = new Rect(_width / 2 - width / 2, _height - height - 28 * _unit, width, height);
            return index < 0 ? rect : new Rect(rect.Left + pad + segment * index, rect.Top + pad, segment, height - pad * 2);
        }

        private int HudHit(double x, double y)
        {
            if (_hudRemaining <= 0.05f)
                return -1;

            for (var i = 0; i < ModeCount; i++)
            {
                if (HudRect(i).Contains(x, y))
                    return i;
            }

            return -1;
        }

        #endregion

        #region 그리기

        private void RenderNow()
        {
            if (!IsActive || _width < 16 || _height < 16)
            {
                ClearLayers();
                return;
            }

            EnsureResources();
            var res = _res!;
            var s = _analyzer.Spectrum;

            _innerLayer.Effect = _mode == 0 ? null : _innerEffect;
            _outerLayer.Effect = _mode == 2 ? _outerEffect : null;
            // 효과가 켜진 레이어의 입력은 모두 채웁니다 (빈 이미지를 셰이더 입력으로 넘기지 않도록 Ribbon 의 6번째도 투명 레이어로 유지).
            var usedLayers = _mode switch { 0 => 0, 1 => 3, _ => 6 };
            for (var i = usedLayers; i < AdditiveLayerCount; i++)
            {
                if (_additive[i].Children.Count > 0)
                    _additive[i].Children.Clear();
            }

            var add = new DrawingContext[usedLayers];
            for (var i = 0; i < usedLayers; i++)
                add[i] = _additive[i].Open();
            var baseDc = _baseHost.Drawing.Open();
            var overlay = _overlayHost.Drawing.Open();
            try
            {
                // 가산 레이어 이미지가 항상 요소 영역과 같은 크기·위치가 되도록 영역으로 자르고 투명 사각형으로 채웁니다.
                var full = new Rect(0, 0, _width, _height);
                foreach (var dc in add)
                {
                    dc.PushClip(new RectangleGeometry(full));
                    dc.DrawRectangle(Brushes.Transparent, null, full);
                }

                // 바탕도 영역으로 잘라야 셰이더 입력(바탕)과 가산 레이어 이미지의 영역이 일치합니다.
                baseDc.PushClip(new RectangleGeometry(full));

                switch (_mode)
                {
                    case 0: DrawAura(res, s, baseDc); break;
                    case 1: DrawHalo(res, s, baseDc, add, overlay); break;
                    default: DrawRibbon(res, s, baseDc, add, overlay); break;
                }
                DrawHud(overlay);
            }
            finally
            {
                foreach (var dc in add)
                {
                    dc.Pop();
                    dc.Close();
                }
                baseDc.Pop();
                baseDc.Close();
                overlay.Close();
            }
        }

        private void ClearLayers()
        {
            _baseHost.Drawing.Children.Clear();
            _overlayHost.Drawing.Children.Clear();
            foreach (var group in _additive)
                group.Children.Clear();
            _innerLayer.Effect = null;
            _outerLayer.Effect = null;
        }

        // [1] Aura — 앨범 색 메시 그라디언트가 음악에 맞춰 호흡
        private void DrawAura(VizResources res, Spectrum s, DrawingContext dc)
        {
            double w = _width, h = _height, u = _unit;
            var full = new Rect(0, 0, w, h);
            dc.DrawRectangle(res.Deep, null, full);

            // (a) 커버 컬러 필드: 4x4 커버를 화면 대각선보다 크게, 아주 천천히 회전 (톤 다운까지 미리 합성됨)
            if (res.AuraField is not null)
            {
                var side = Math.Sqrt(w * w + h * h) * 1.25;
                dc.PushTransform(new RotateTransform(_t * 2.0, w / 2, h / 2));
                dc.DrawImage(res.AuraField, new Rect(w / 2 - side / 2, h / 2 - side / 2, side, side));
                dc.Pop();
            }

            // (b) 대역별 블롭: 저역은 크고 느리게, 고역은 작고 빠르게
            Span<float> energy = stackalloc float[6] { s.Bass, s.Bass * 0.5f + s.LowMid * 0.5f, s.LowMid, s.Mid, s.Treble * 1.3f, s.Level };
            var m = Math.Min(w, h);
            for (var i = 0; i < 6; i++)
            {
                double speed = 0.045 + 0.02 * i, phase = i * 1.7;
                var cx = w * (0.5 + 0.34 * Math.Sin(_t * speed * 2.1 + phase));
                var cy = h * (0.5 + 0.30 * Math.Cos(_t * speed * 2.9 + phase * 1.3));
                var r = m * (0.42 + 0.30 * energy[i]) * (1 + 0.06 * s.Beat);
                FillRadial(dc, res.Blobs[i], cx, cy, r * 1.25, r, 0.30 + 0.50 * Clamp01(energy[i]));
            }

            Vignette(dc, res, 0.85);
            Grain(dc, res, 1.0);

            // 커버: 부드러운 그림자 + 비트 펄스
            var cs = m * 0.46 * (1 + 0.018 * s.Beat);
            double coverX = w / 2, coverY = h * 0.44;
            FillRadial(dc, res.Shadow, coverX, coverY + cs * 0.10, cs * 0.78, cs * 0.72, 0.9);
            var rc = new Rect(coverX - cs / 2, coverY - cs / 2, cs, cs);
            DrawCoverRect(dc, res, rc, cs * 0.035);
            dc.DrawRoundedRectangle(null, res.CoverOutline, rc, cs * 0.035, cs * 0.035);

            var ty = rc.Bottom + 28 * u;
            DrawText(dc, TitleOrFileName(), 30 * u, FontWeights.SemiBold, TextAlignment.Center,
                new Rect(w * 0.1, ty, w * 0.8, 40 * u), WithAlpha(Colors.White, 0.96f));
            DrawText(dc, ArtistLine(), 18 * u, FontWeights.Normal, TextAlignment.Center,
                new Rect(w * 0.1, ty + 40 * u, w * 0.8, 26 * u), WithAlpha(Colors.White, 0.62f));

            // 커버 아래 미니 레벨 라인
            var lineWidth = cs * (0.25 + 0.75 * s.Level);
            var ly = ty + 84 * u;
            dc.DrawLine(res.LevelLine, new Point(coverX - lineWidth / 2, ly), new Point(coverX + lineWidth / 2, ly));
        }

        // [2] Halo — 원형 커버 + 대칭 캡슐 스펙트럼 + 비트 파티클
        private void DrawHalo(VizResources res, Spectrum s, DrawingContext dc, DrawingContext[] add, DrawingContext overlay)
        {
            double w = _width, h = _height, u = _unit;
            var m = Math.Min(w, h);
            double cx = w / 2, cy = h * 0.47;
            var radius = m * 0.19 * (1 + 0.035 * s.Beat); // 커버 반지름
            var center = new Point(cx, cy);

            dc.DrawRectangle(res.Deep, null, new Rect(0, 0, w, h));
            Vignette(dc, res, 0.7);
            dc.DrawEllipse(null, res.GuideRing, center, radius * 2.05, radius * 2.05);
            dc.DrawEllipse(null, res.GuideRing, center, radius * 1.55, radius * 1.55);

            // 가산: 중앙 글로우
            FillRadial(add[0], res.Glow, cx, cy, radius * 3.2, radius * 3.2, 0.12 + 0.30 * s.Bass);

            // 가산: 대칭 캡슐 바 (위쪽이 저역, 좌우 대칭으로 아래에서 고역이 만남). 0: 글로우, 1: 코어
            const int bars = BandCount * 2;
            var r0 = radius * 1.10;
            var maxLength = radius * 0.95;
            for (var j = 0; j < bars; j++)
            {
                var band = j < BandCount ? j : bars - 1 - j;
                var v = s.Bands[band];
                var angle = -Math.PI / 2 + (j + 0.5) / bars * 2 * Math.PI;
                var length = radius * 0.035 + v * maxLength;
                double ca = Math.Cos(angle), sa = Math.Sin(angle);
                var from = new Point(cx + ca * r0, cy + sa * r0);
                var to = new Point(cx + ca * (r0 + length), cy + sa * (r0 + length));
                add[1].DrawLine(res.BarPen(0, band, v), from, to);
                add[2].DrawLine(res.BarPen(1, band, v), from, to);
            }

            // 가산: 파티클
            foreach (var p in _particles)
            {
                var life = p.Life / p.MaxLife;
                var fade = Math.Min(1f, life * 1.8f) * Math.Min(1f, (1 - life) * 8f);
                var distance = radius * (1.12 + p.R);
                add[2].DrawEllipse(res.ParticleBrush(p.Color, 0.85 * fade), null,
                    new Point(cx + Math.Cos(p.Angle) * distance, cy + Math.Sin(p.Angle) * distance), p.Size * u, p.Size * u);
            }

            // 커버 (바이닐처럼 천천히 회전) + 테두리 + 중앙 홀
            FillRadial(overlay, res.Shadow, cx, cy + radius * 0.12, radius * 1.35, radius * 1.35, 0.8);
            DrawCoverCircle(overlay, res, cx, cy, radius, _spin);
            overlay.DrawEllipse(null, res.CoverRim, center, radius, radius);
            overlay.DrawEllipse(res.DeepHole, null, center, radius * 0.07, radius * 0.07);
            overlay.DrawEllipse(null, res.HoleRim, center, radius * 0.07, radius * 0.07);

            Grain(overlay, res, 0.8);
            var ty = h - 158 * u;
            DrawText(overlay, TitleOrFileName(), 26 * u, FontWeights.SemiBold, TextAlignment.Center,
                new Rect(w * 0.1, ty, w * 0.8, 36 * u), WithAlpha(Colors.White, 0.95f));
            DrawText(overlay, ArtistLine(), 16 * u, FontWeights.Normal, TextAlignment.Center,
                new Rect(w * 0.1, ty + 34 * u, w * 0.8, 24 * u), WithAlpha(Colors.White, 0.55f));
        }

        // [3] Ribbon — 겹쳐진 발광 웨이브 (가산 혼합)
        private void DrawRibbon(VizResources res, Spectrum s, DrawingContext dc, DrawingContext[] add, DrawingContext overlay)
        {
            double w = _width, h = _height, u = _unit;
            double x0 = res.RibbonX0, span = res.RibbonSpan, y0 = h * 0.56;
            dc.DrawRectangle(res.RibbonBackground, null, new Rect(0, 0, w, h));

            // 가산: 하단 엣지 글로우 + 중심 코어 라인
            add[0].DrawRectangle(res.EdgeGlow.Get(0.35 + 0.5 * s.Bass), null, new Rect(0, h * 0.55, w, h * 0.45));
            var coreLine = new Pen(res.WhiteFade.Get(0.25 + 0.5 * s.Level), 1.5 * u);
            coreLine.Freeze();
            add[0].DrawLine(coreLine, new Point(x0, y0), new Point(x0 + span, y0));

            // 가산: 웨이브 4겹 (레이어마다 별도 입력이라 서로 더해짐)
            Span<float> energy = stackalloc float[4] { s.Bass, s.LowMid, s.Mid, s.Treble * 1.4f };
            ReadOnlySpan<float> periods = stackalloc float[4] { 1.15f, 1.75f, 2.45f, 3.3f };
            ReadOnlySpan<float> speeds = stackalloc float[4] { 0.9f, -1.25f, 1.6f, -2.2f };
            for (var layer = 0; layer < 4; layer++)
            {
                var amp = h * (0.025 + 0.24 * Clamp01(energy[layer])) * (layer == 0 ? 1 + 0.25 * s.Beat : 1);
                var phase = _t * speeds[layer] + layer * 1.3;
                for (var i = 0; i < RibbonPointCount; i++)
                {
                    var t = i / (double)(RibbonPointCount - 1);
                    var envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), 2.2); // 양끝 테이퍼
                    var wobble = 0.35 * Math.Sin(_t * 0.7 + layer * 2.1 + t * 3);
                    var y = amp * envelope * Math.Sin(2 * Math.PI * periods[layer] * t + phase + wobble);
                    var x = x0 + t * span;
                    _ribbonUp[i] = new Point(x, y0 - y);
                    _ribbonDown[i] = new Point(x, y0 + y);
                }

                // 채움: 위 곡선 → 아래 곡선(역순)으로 닫힌 렌즈 모양
                var fill = new StreamGeometry();
                using (var ctx = fill.Open())
                {
                    ctx.BeginFigure(_ribbonUp[0], true, true);
                    for (var i = 1; i < RibbonPointCount; i++)
                        ctx.LineTo(_ribbonUp[i], false, false);
                    for (var i = RibbonPointCount - 1; i >= 0; i--)
                        ctx.LineTo(_ribbonDown[i], false, false);
                }
                fill.Freeze();

                // 윤곽선: 넓은 글로우 + 얇은 코어
                var edge = new StreamGeometry();
                using (var ctx = edge.Open())
                {
                    ctx.BeginFigure(_ribbonUp[0], false, false);
                    for (var i = 1; i < RibbonPointCount; i++)
                        ctx.LineTo(_ribbonUp[i], true, true);
                    ctx.BeginFigure(_ribbonDown[0], false, false);
                    for (var i = 1; i < RibbonPointCount; i++)
                        ctx.LineTo(_ribbonDown[i], true, true);
                }
                edge.Freeze();

                var target = add[layer + 1];
                target.DrawGeometry(res.Ribbons[layer].Get(0.28 + 0.30 * Clamp01(energy[layer])), null, fill);
                target.DrawGeometry(null, res.RibbonGlowPens[layer], edge);
                target.DrawGeometry(null, res.RibbonCorePens[layer], edge);
            }

            Grain(overlay, res, 0.7);

            // 좌상단 Now Playing 카드
            double margin = 36 * u, size = 72 * u;
            var rc = new Rect(margin, margin, size, size);
            FillRadial(overlay, res.Shadow, margin + size / 2, margin + size / 2 + 6 * u, size * 0.8, size * 0.8, 0.7);
            DrawCoverRect(overlay, res, rc, 10 * u);
            var textLeft = rc.Right + 18 * u;
            var textWidth = Math.Max(0, w - margin - textLeft);
            DrawText(overlay, TitleOrFileName(), 22 * u, FontWeights.SemiBold, TextAlignment.Left,
                new Rect(textLeft, margin + 6 * u, textWidth, 32 * u), WithAlpha(Colors.White, 0.95f));
            DrawText(overlay, ArtistLine(), 15 * u, FontWeights.Normal, TextAlignment.Left,
                new Rect(textLeft, margin + 38 * u, textWidth, 26 * u), WithAlpha(Colors.White, 0.55f));
        }

        // HUD: 모드 전환 시 잠깐 나타나는 글래스 필
        private void DrawHud(DrawingContext dc)
        {
            var alpha = Clamp01(_hudRemaining / 0.6f);
            if (alpha <= 0)
                return;

            alpha = Math.Round(alpha * 32) / 32; // 텍스트 캐시가 늘어나지 않도록 단계화
            var rect = HudRect(-1);
            var radius = rect.Height / 2;
            dc.DrawRoundedRectangle(Solid(WithAlpha(Colors.White, (0.08 * alpha))),
                new Pen(Solid(WithAlpha(Colors.White, (0.14 * alpha))), 1), rect, radius, radius);
            for (var i = 0; i < ModeCount; i++)
            {
                var segment = HudRect(i);
                if (i == _mode)
                {
                    var r = segment.Height / 2;
                    dc.DrawRoundedRectangle(Solid(WithAlpha(Colors.White, (0.18 * alpha))), null, segment, r, r);
                }

                DrawText(dc, $"{i + 1}  {ModeNames[i]}", 14 * _unit, i == _mode ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment.Center, segment, WithAlpha(Colors.White, ((i == _mode ? 0.95 : 0.5) * alpha)));
            }
        }

        private static void FillRadial(DrawingContext dc, OpacityBrushCache brush, double cx, double cy, double rx, double ry, double opacity) =>
            dc.DrawEllipse(brush.Get(opacity), null, new Point(cx, cy), rx, ry);

        private void Vignette(DrawingContext dc, VizResources res, double strength)
        {
            var r = Math.Sqrt(_width * _width + _height * _height) * 0.62;
            FillRadial(dc, res.Vignette, _width / 2, _height / 2, r, r, strength);
        }

        // 필름 그레인: 초당 24번 타일 위치를 옮겨 움직이는 입자처럼 보이게 합니다.
        private void Grain(DrawingContext dc, VizResources res, double opacity)
        {
            var step = (int)(_t * 24);
            dc.DrawRectangle(res.GrainBrush(step % 7, step % 5, opacity), null, new Rect(0, 0, _width, _height));
        }

        private static void DrawCoverRect(DrawingContext dc, VizResources res, Rect rect, double radius) =>
            dc.DrawRoundedRectangle((Brush?)res.Cover ?? res.CoverFallback, null, rect, radius, radius);

        private static void DrawCoverCircle(DrawingContext dc, VizResources res, double cx, double cy, double radius, double angle)
        {
            dc.PushTransform(new RotateTransform(angle * 180 / Math.PI, cx, cy));
            dc.DrawEllipse((Brush?)res.Cover ?? res.CoverFallback, null, new Point(cx, cy), radius, radius);
            dc.Pop();
        }

        private void DrawText(DrawingContext dc, string text, double size, FontWeight weight, TextAlignment alignment, Rect rect, Color color)
        {
            if (string.IsNullOrWhiteSpace(text) || rect.Width <= 1 || size <= 1)
                return;

            var key = $"{text}\u0001{size:F1}\u0001{weight}\u0001{alignment}\u0001{rect.Width:F0}\u0001{color}";
            if (!_textCache.TryGetValue(key, out var formatted))
            {
                if (_textCache.Count > 256)
                    _textCache.Clear();

                formatted = new FormattedText(text, TextCulture, FlowDirection.LeftToRight,
                    new Typeface(TextFont, FontStyles.Normal, weight, FontStretches.Normal), size, Solid(color), _pixelsPerDip)
                {
                    TextAlignment = alignment,
                    MaxTextWidth = rect.Width,
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                _textCache[key] = formatted;
            }

            dc.DrawText(formatted, new Point(rect.Left, rect.Top + (rect.Height - formatted.Height) / 2));
        }

        private string TitleOrFileName()
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title;
            return string.IsNullOrWhiteSpace(FilePath) ? "Now Playing" : System.IO.Path.GetFileNameWithoutExtension(FilePath);
        }

        private string ArtistLine()
        {
            var artist = string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;
            return string.IsNullOrWhiteSpace(Album) ? artist : $"{artist}  ·  {Album}";
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        private static SolidColorBrush Solid(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        #endregion

        #region 리소스

        private void EnsureResources()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.PixelsPerDip != _pixelsPerDip)
            {
                _pixelsPerDip = dpi.PixelsPerDip;
                _textCache.Clear();
                _sizeDirty = true;
            }

            if (_res is null || _paletteDirty)
            {
                _res = new VizResources(AlbumArt);
                _paletteDirty = false;
                _sizeDirty = true;
            }

            if (_sizeDirty)
            {
                _res.UpdateSize(_width, _height, _unit, dpi.DpiScaleX);
                _textCache.Clear();
                _sizeDirty = false;
            }
        }

        // 커버·팔레트·영역에 따라 만드는 브러시와 펜 (모두 Freeze 하여 매 프레임 재사용)
        private sealed class VizResources
        {
            private readonly Color[] _barColors = new Color[BandCount];
            private readonly Pen?[,,] _barPens = new Pen?[2, BandCount, 33];
            private readonly SolidColorBrush?[,] _particleBrushes = new SolidColorBrush?[4, 33];
            private readonly Dictionary<(int, int, int), ImageBrush> _grainBrushes = new();
            private readonly BitmapSource _grainTile;
            private double _barWidth = 2;
            private double _grainTileSize = 256;

            public VizResources(BitmapSource? cover)
            {
                Color? vibrant = null;
                try
                {
                    vibrant = CoverColorExtractor.GetVibrantColor(cover);
                }
                catch (Exception ex)
                {
                    PlayerDiagnostics.Write($"Visualizer palette extraction failed: {ex.Message}");
                }
                Palette = VizPalette.From(vibrant ?? Color.FromRgb(120, 90, 220));

                Deep = Solid(Palette.Deep);
                DeepHole = Solid(WithAlpha(Palette.Deep, 0.95));
                RibbonBackground = Solid(Color.FromRgb(5, 5, 8));
                CoverFallback = Solid(WithAlpha(Palette.Colors[0], 0.4));

                for (var i = 0; i < 6; i++)
                    Blobs[i] = new OpacityBrushCache(Radial(Palette.Colors[i == 5 ? 0 : i % 4], (0, 1), (0.35, 0.75), (0.7, 0.25), (1, 0)));
                Vignette = new OpacityBrushCache(Radial(Palette.Deep, (0, 0), (0.55, 0.1), (1, 0.9)));
                Glow = new OpacityBrushCache(Radial(Palette.Colors[0], (0, 0.9), (0.4, 0.35), (1, 0)));
                Shadow = new OpacityBrushCache(Radial(Colors.Black, (0, 0.55), (0.6, 0.2), (1, 0)));
                EdgeGlow = new OpacityBrushCache(Linear(new Point(0.5, 1), new Point(0.5, 0), BrushMappingMode.RelativeToBoundingBox,
                    (0, WithAlpha(Palette.Colors[0], 0.55)), (0.45, WithAlpha(Palette.Colors[1], 0.12)), (1, WithAlpha(Palette.Colors[1], 0))));

                for (var band = 0; band < BandCount; band++)
                {
                    var t = band / (double)(BandCount - 1); // 0 저역 → 1 고역
                    _barColors[band] = t < 0.5
                        ? Mix(Palette.Colors[0], Palette.Colors[1], t * 2)
                        : Mix(Palette.Colors[1], Palette.Colors[2], (t - 0.5) * 2);
                }

                if (cover is not null)
                {
                    // 앨범 이미지는 백그라운드 스레드에서 디코딩되어 디코더가 그 스레드에 묶여 있으므로
                    // 브러시를 Freeze 하면 스레드 예외가 납니다. 그리기에는 문제가 없어 Freeze 하지 않습니다.
                    var brush = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
                    RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                    Cover = brush;
                    try
                    {
                        AuraField = CreateAuraField(cover, Palette.Deep);
                    }
                    catch (Exception ex)
                    {
                        PlayerDiagnostics.Write($"Visualizer aura field creation failed: {ex.Message}");
                    }
                }

                _grainTile = CreateGrainTile();
                CoverOutline = FrozenPen(Solid(WithAlpha(Colors.White, 0.10f)), 1);
                GuideRing = FrozenPen(Solid(WithAlpha(Colors.White, 0.05f)), 1);
                HoleRim = FrozenPen(Solid(WithAlpha(Colors.White, 0.25f)), 1);
            }

            public VizPalette Palette { get; }
            public SolidColorBrush Deep { get; }
            public SolidColorBrush DeepHole { get; }
            public SolidColorBrush RibbonBackground { get; }
            public SolidColorBrush CoverFallback { get; }
            public ImageBrush? Cover { get; }
            public BitmapSource? AuraField { get; }
            public OpacityBrushCache[] Blobs { get; } = new OpacityBrushCache[6];
            public OpacityBrushCache Vignette { get; }
            public OpacityBrushCache Glow { get; }
            public OpacityBrushCache Shadow { get; }
            public OpacityBrushCache EdgeGlow { get; }
            public Pen CoverOutline { get; }
            public Pen GuideRing { get; }
            public Pen HoleRim { get; }

            // 영역 의존 리소스
            public Pen CoverRim { get; private set; } = null!;
            public Pen LevelLine { get; private set; } = null!;
            public OpacityBrushCache WhiteFade { get; private set; } = null!;
            public OpacityBrushCache[] Ribbons { get; } = new OpacityBrushCache[4];
            public Pen[] RibbonGlowPens { get; } = new Pen[4];
            public Pen[] RibbonCorePens { get; } = new Pen[4];
            public double RibbonX0 { get; private set; }
            public double RibbonSpan { get; private set; }

            public void UpdateSize(double width, double height, double unit, double dpiScale)
            {
                RibbonX0 = width * 0.04;
                RibbonSpan = width * 0.92;
                CoverRim = FrozenPen(Solid(WithAlpha(Colors.White, 0.14f)), 1.5 * unit);
                LevelLine = FrozenPen(Solid(WithAlpha(Colors.White, 0.55f)), 2 * unit, PenLineCap.Round);

                var start = new Point(RibbonX0, 0);
                var end = new Point(RibbonX0 + RibbonSpan, 0);
                WhiteFade = new OpacityBrushCache(Linear(start, end, BrushMappingMode.Absolute,
                    (0, WithAlpha(Colors.White, 0)), (0.5, Colors.White), (1, WithAlpha(Colors.White, 0))));

                // 레이어별 수평 그라디언트: 양끝 투명 → 가운데 발광, 인접색으로 색 이동 (보색 c[3]은 파티클에만)
                for (var layer = 0; layer < 4; layer++)
                {
                    Color a = Palette.Colors[layer % 3], b = Palette.Colors[(layer + 1) % 3];
                    Ribbons[layer] = new OpacityBrushCache(Linear(start, end, BrushMappingMode.Absolute,
                        (0, WithAlpha(a, 0)), (0.18, WithAlpha(a, 0.35)), (0.5, Mix(a, b, 0.5)), (0.82, WithAlpha(b, 0.35)), (1, WithAlpha(b, 0))));
                    RibbonGlowPens[layer] = FrozenPen(Ribbons[layer].Get(0.25), 7 * unit, PenLineCap.Round);
                    RibbonCorePens[layer] = FrozenPen(Ribbons[layer].Get(0.9), 1.6 * unit, PenLineCap.Round);
                }

                // 캡슐 바 폭은 커버 둘레에서 계산 (비트 펄스에 따른 미세한 변화는 무시)
                var r0 = Math.Min(width, height) * 0.19 * 1.10;
                _barWidth = Math.Max(2, 2 * Math.PI * r0 / (BandCount * 2) * 0.46);
                Array.Clear(_barPens);

                // 그레인 타일 1 픽셀이 화면 1 픽셀이 되도록 DPI 를 반영합니다.
                _grainTileSize = 256 / Math.Max(0.5, dpiScale);
                _grainBrushes.Clear();
            }

            public Pen BarPen(int pass, int band, double value)
            {
                var level = (int)Math.Round(Clamp01(value) * 32);
                var pen = _barPens[pass, band, level];
                if (pen is not null)
                    return pen;

                var v = level / 32.0;
                var color = _barColors[band];
                if (pass == 0)
                    color = WithAlpha(color, 0.10 + 0.25 * v);
                else
                    color = WithAlpha(Mix(color, Colors.White, 0.25 * v), 0.55 + 0.45 * v);

                pen = FrozenPen(Solid(color), pass == 0 ? _barWidth * 3.2 : _barWidth, PenLineCap.Round);
                _barPens[pass, band, level] = pen;
                return pen;
            }

            public SolidColorBrush ParticleBrush(int colorIndex, double opacity)
            {
                var level = (int)Math.Round(Clamp01(opacity) * 32);
                return _particleBrushes[colorIndex, level] ??= Solid(WithAlpha(Palette.Colors[colorIndex], level / 32.0));
            }

            public ImageBrush GrainBrush(int stepX, int stepY, double opacity)
            {
                var key = (stepX, stepY, (int)Math.Round(opacity * 100));
                if (_grainBrushes.TryGetValue(key, out var brush))
                    return brush;

                var scale = _grainTileSize / 256;
                brush = new ImageBrush(_grainTile)
                {
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.Fill,
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(stepX * 37 * scale, stepY * 53 * scale, _grainTileSize, _grainTileSize),
                    Opacity = opacity
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                brush.Freeze();
                _grainBrushes[key] = brush;
                return brush;
            }

            // 필름 그레인 (256x256 타일, premultiplied)
            private static BitmapSource CreateGrainTile()
            {
                const int size = 256;
                const uint alpha = 20;
                var pixels = new byte[size * size * 4];
                var state = 12345u;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    state = state * 1664525u + 1013904223u;
                    var channel = (byte)((state >> 24) * alpha / 255);
                    pixels[i] = channel;
                    pixels[i + 1] = channel;
                    pixels[i + 2] = channel;
                    pixels[i + 3] = (byte)alpha;
                }

                var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
                bitmap.Freeze();
                return bitmap;
            }

            // Aura 배경: 커버를 4x4 로 영역 평균한 뒤 부드럽게(바이큐빅) 늘린 컬러 필드.
            // 필드를 불투명도 0.9 로 그린 뒤 배경색으로 50% 톤 다운한 결과를 미리 합성합니다: 결과 = 0.45 * 필드 + 0.55 * 배경
            private static BitmapSource CreateAuraField(BitmapSource cover, Color deep)
            {
                const int grid = 4;
                const int size = 128;
                var tiny = CoverColorExtractor.SampleBgra(cover, grid, grid);
                var pixels = new byte[size * size * 4];
                for (var y = 0; y < size; y++)
                {
                    var sy = (y + 0.5) * grid / size - 0.5;
                    for (var x = 0; x < size; x++)
                    {
                        var sx = (x + 0.5) * grid / size - 0.5;
                        var o = (y * size + x) * 4;
                        for (var c = 0; c < 3; c++)
                        {
                            var value = Bicubic(tiny, grid, sx, sy, c);
                            var background = c == 0 ? deep.B : c == 1 ? deep.G : deep.R;
                            pixels[o + c] = (byte)Math.Clamp(Math.Round(0.45 * value + 0.55 * background), 0, 255);
                        }
                        pixels[o + 3] = 255;
                    }
                }

                var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
                bitmap.Freeze();
                return bitmap;
            }

            private static double Bicubic(byte[] bgra, int grid, double x, double y, int channel)
            {
                int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
                double fx = x - ix, fy = y - iy, sum = 0;
                for (var m = -1; m <= 2; m++)
                {
                    var wy = CatmullRom(m - fy);
                    var py = Math.Clamp(iy + m, 0, grid - 1);
                    for (var n = -1; n <= 2; n++)
                    {
                        var px = Math.Clamp(ix + n, 0, grid - 1);
                        sum += bgra[(py * grid + px) * 4 + channel] * CatmullRom(n - fx) * wy;
                    }
                }

                return sum;
            }

            private static double CatmullRom(double x)
            {
                x = Math.Abs(x);
                if (x < 1) return 1.5 * x * x * x - 2.5 * x * x + 1;
                if (x < 2) return -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2;
                return 0;
            }

            // 반경·중심을 도형 경계에 맞추는 원형 그라디언트 (FillEllipse 영역 = 브러시 영역)
            private static RadialGradientBrush Radial(Color color, params (double Offset, double Alpha)[] stops)
            {
                var brush = new RadialGradientBrush();
                foreach (var (offset, alpha) in stops)
                    brush.GradientStops.Add(new GradientStop(WithAlpha(color, alpha), offset));
                brush.Freeze();
                return brush;
            }

            private static LinearGradientBrush Linear(Point start, Point end, BrushMappingMode mapping, params (double Offset, Color Color)[] stops)
            {
                var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end, MappingMode = mapping };
                foreach (var (offset, color) in stops)
                    brush.GradientStops.Add(new GradientStop(color, offset));
                brush.Freeze();
                return brush;
            }

            private static Pen FrozenPen(Brush brush, double thickness, PenLineCap cap = PenLineCap.Flat)
            {
                var pen = new Pen(brush, thickness) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
                pen.Freeze();
                return pen;
            }
        }

        // 불투명도를 64 단계로 나눠 미리 만든(Freeze) 브러시를 재사용합니다.
        private sealed class OpacityBrushCache
        {
            private readonly Brush _template;
            private readonly Brush?[] _levels = new Brush?[OpacityLevels + 1];

            public OpacityBrushCache(Brush template) => _template = template;

            public Brush Get(double opacity)
            {
                var level = (int)Math.Round(Clamp01(opacity) * OpacityLevels);
                var brush = _levels[level];
                if (brush is null)
                {
                    brush = _template.CloneCurrentValue();
                    brush.Opacity = level / (double)OpacityLevels;
                    brush.Freeze();
                    _levels[level] = brush;
                }

                return brush;
            }
        }

        // 팔레트: 커버 대표색 → 유사색 + 보색 포인트
        private sealed record VizPalette(Color Deep, Color[] Colors)
        {
            public static VizPalette From(Color color)
            {
                RgbToHsl(color, out var h, out var s);
                if (s < 0.12) { h = 262; s = 0.55; } // 무채색 커버 → 기본 바이올렛
                s = Math.Clamp(s, 0.55, 0.90);
                return new VizPalette(
                    Hsl(h, 0.30, 0.035),
                    new[] { Hsl(h, s, 0.60), Hsl(h + 38, s, 0.62), Hsl(h - 42, s * 0.95, 0.56), Hsl(h + 165, s * 0.85, 0.66) });
            }

            private static void RgbToHsl(Color c, out double h, out double s)
            {
                double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
                var l = (max + min) / 2;
                s = delta < 1e-4 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
                h = 0;
                if (delta < 1e-4) return;
                h = max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
                if (h < 0) h += 360;
            }

            private static Color Hsl(double h, double s, double l)
            {
                h = (h % 360 + 360) % 360;
                var c = (1 - Math.Abs(2 * l - 1)) * s;
                var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
                var m = l - c / 2;
                var (r, g, b) = h switch
                {
                    < 60 => (c, x, 0d),
                    < 120 => (x, c, 0d),
                    < 180 => (0d, c, x),
                    < 240 => (0d, x, c),
                    < 300 => (x, 0d, c),
                    _ => (c, 0d, x)
                };
                static byte To8(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
                return Color.FromRgb(To8(r + m), To8(g + m), To8(b + m));
            }
        }

        private static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), color.R, color.G, color.B);

        private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

        #endregion

        #region 분석기

        private sealed class Spectrum
        {
            public readonly float[] Bands = new float[BandCount]; // 0..1 (스무딩됨)
            public float Bass, LowMid, Mid, Treble, Level;
            public float Beat; // 비트 순간 1 → 지수 감쇠
        }

        // FFT(4096) → 64 로그 밴드 → AGC → 어택/릴리즈 스무딩 → 비트 검출
        private sealed class SpectrumAnalyzer
        {
            private readonly float[] _window = new float[FftSize];
            private readonly float[] _re = new float[FftSize];
            private readonly float[] _im = new float[FftSize];
            private readonly float[] _twiddleCos = new float[FftSize / 2];
            private readonly float[] _twiddleSin = new float[FftSize / 2];
            private readonly float[] _edges = new float[BandCount + 1];
            private readonly float[] _centers = new float[BandCount];
            private readonly float[] _raw = new float[BandCount];
            private float _agc = -30f, _bassAverage, _sinceBeat = 1;
            private int _sampleRate = 48000;

            public SpectrumAnalyzer()
            {
                for (var i = 0; i < FftSize; i++)
                    _window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (FftSize - 1)); // Hann
                for (var k = 0; k < FftSize / 2; k++)
                {
                    var angle = -2.0 * Math.PI * k / FftSize;
                    _twiddleCos[k] = (float)Math.Cos(angle);
                    _twiddleSin[k] = (float)Math.Sin(angle);
                }
                const float low = 30f, high = 16000f;
                for (var b = 0; b <= BandCount; b++)
                    _edges[b] = low * MathF.Pow(high / low, (float)b / BandCount);
                for (var b = 0; b < BandCount; b++)
                    _centers[b] = MathF.Sqrt(_edges[b] * _edges[b + 1]);
            }

            public Spectrum Spectrum { get; } = new();
            public int BeatCount { get; private set; }

            public void SetSampleRate(int sampleRate)
            {
                if (sampleRate > 0)
                    Volatile.Write(ref _sampleRate, sampleRate);
            }

            public void Reset()
            {
                Array.Clear(Spectrum.Bands);
                Spectrum.Bass = Spectrum.LowMid = Spectrum.Mid = Spectrum.Treble = Spectrum.Level = Spectrum.Beat = 0;
                _agc = -30f;
                _bassAverage = 0;
                _sinceBeat = 1;
            }

            public Spectrum Process(float[] x, float dt)
            {
                float sampleRate = Volatile.Read(ref _sampleRate);
                for (var i = 0; i < FftSize; i++)
                {
                    _re[i] = x[i] * _window[i];
                    _im[i] = 0;
                }
                Fft();

                const float norm = 2f / (FftSize * 0.5f); // Hann coherent gain 보정
                var frameMax = -120f;
                for (var b = 0; b < BandCount; b++)
                {
                    float kLo = _edges[b] * FftSize / sampleRate, kHi = _edges[b + 1] * FftSize / sampleRate;
                    float magnitude;
                    if (kHi - kLo < 1f)
                    {
                        // 저역: 빈보다 좁은 밴드 → 보간
                        var kc = 0.5f * (kLo + kHi);
                        var k0 = (int)kc;
                        var fraction = kc - k0;
                        magnitude = Magnitude(k0) + (Magnitude(k0 + 1) - Magnitude(k0)) * fraction;
                    }
                    else
                    {
                        magnitude = 0;
                        for (var k = (int)MathF.Ceiling(kLo); k <= (int)kHi && k < FftSize / 2; k++)
                            magnitude = Math.Max(magnitude, Magnitude(k));
                    }

                    // +3 dB/oct 틸트: 핑크 스펙트럼을 평평하게 보이도록 (고역이 죽어 보이지 않게)
                    var db = 20f * MathF.Log10(magnitude * norm + 1e-7f) + 3f * MathF.Log2(_centers[b] / 1000f);
                    _raw[b] = db;
                    frameMax = Math.Max(frameMax, db);
                }

                // AGC: 최근 최대 레벨 기준으로 48dB 창을 사용 (조용한 곡도 화면이 살아있게)
                _agc = Math.Max(frameMax, _agc - 4f * dt);
                _agc = Math.Max(_agc, -42f); // 무음에서 노이즈 증폭 방지
                float attack = 1f - MathF.Exp(-dt / 0.035f), release = 1f - MathF.Exp(-dt / 0.22f);
                float bassNow = 0;
                var bassCount = 0;
                var bands = Spectrum.Bands;
                for (var b = 0; b < BandCount; b++)
                {
                    var v = Math.Clamp((_raw[b] - (_agc - 48f)) / 48f, 0f, 1f);
                    v = MathF.Pow(v, 1.6f); // 대비 강화
                    bands[b] += (v - bands[b]) * (v > bands[b] ? attack : release);
                    if (_centers[b] < 150f)
                    {
                        bassNow += v;
                        bassCount++;
                    }
                }

                Spectrum.Bass = Average(0, 150);
                Spectrum.LowMid = Average(150, 600);
                Spectrum.Mid = Average(600, 3000);
                Spectrum.Treble = Average(3000, 20000);
                Spectrum.Level = Average(0, 20000);

                // 비트: 순간 저역 에너지가 이동 평균보다 확실히 클 때
                bassNow = bassCount > 0 ? bassNow / bassCount : 0;
                _bassAverage += (bassNow - _bassAverage) * (1f - MathF.Exp(-dt / 0.9f));
                _sinceBeat += dt;
                if (bassNow > _bassAverage * 1.3f + 0.04f && bassNow > 0.25f && _sinceBeat > 0.27f)
                {
                    Spectrum.Beat = 1f;
                    _sinceBeat = 0;
                    BeatCount++;
                }
                Spectrum.Beat *= MathF.Exp(-dt * 5.5f);
                return Spectrum;
            }

            private float Magnitude(int k) => MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);

            private float Average(float low, float high)
            {
                float sum = 0;
                var count = 0;
                for (var b = 0; b < BandCount; b++)
                {
                    if (_centers[b] >= low && _centers[b] < high)
                    {
                        sum += Spectrum.Bands[b];
                        count++;
                    }
                }
                return count > 0 ? sum / count : 0f;
            }

            private void Fft()
            {
                // 비트 반전 순서 재배치
                for (int i = 1, j = 0; i < FftSize; i++)
                {
                    var bit = FftSize >> 1;
                    for (; (j & bit) != 0; bit >>= 1)
                        j ^= bit;
                    j ^= bit;
                    if (i < j)
                    {
                        (_re[i], _re[j]) = (_re[j], _re[i]);
                        (_im[i], _im[j]) = (_im[j], _im[i]);
                    }
                }

                for (var length = 2; length <= FftSize; length <<= 1)
                {
                    var half = length / 2;
                    var stride = FftSize / length;
                    for (var k = 0; k < half; k++)
                    {
                        float wr = _twiddleCos[k * stride], wi = _twiddleSin[k * stride];
                        for (var i = 0; i < FftSize; i += length)
                        {
                            int even = i + k, odd = even + half;
                            var tr = _re[odd] * wr - _im[odd] * wi;
                            var ti = _re[odd] * wi + _im[odd] * wr;
                            _re[odd] = _re[even] - tr;
                            _im[odd] = _im[even] - ti;
                            _re[even] += tr;
                            _im[even] += ti;
                        }
                    }
                }
            }
        }

        private sealed class AudioRingBuffer
        {
            private readonly float[] _buffer;
            private readonly object _sync = new();
            private int _write;

            public AudioRingBuffer(int size) => _buffer = new float[size];

            public void Push(ReadOnlySpan<float> samples)
            {
                lock (_sync)
                {
                    foreach (var sample in samples)
                    {
                        _buffer[_write] = sample;
                        _write = (_write + 1) % _buffer.Length;
                    }
                }
            }

            public void Latest(float[] output)
            {
                lock (_sync)
                {
                    var start = (_write + _buffer.Length - output.Length) % _buffer.Length;
                    for (var i = 0; i < output.Length; i++)
                        output[i] = _buffer[(start + i) % _buffer.Length];
                }
            }

            public void Clear()
            {
                lock (_sync)
                {
                    Array.Clear(_buffer);
                    _write = 0;
                }
            }
        }

        #endregion

        private struct Particle
        {
            public float Angle;
            public float R;
            public float Velocity;
            public float MaxLife;
            public float Life;
            public float Size;
            public float Spin;
            public int Color;
        }

        // DrawingGroup 하나를 그리는 가벼운 요소. 내용은 DrawingGroup.Open() 으로 바꾸므로 InvalidateVisual 이 필요 없습니다.
        private sealed class DrawingHost : FrameworkElement
        {
            public DrawingGroup Drawing { get; } = new();

            protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawDrawing(Drawing);
        }
    }
}
