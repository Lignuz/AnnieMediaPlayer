using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnnieMediaPlayer.CustomControls
{
    public sealed class AudioVisualizerControl : Control
    {
        private const int FftSize = 4096;
        private const int BandCount = 64;
        private readonly AudioRingBuffer _audio = new(1 << 16);
        private readonly SpectrumAnalyzer _analyzer = new();
        private readonly DispatcherTimer _timer;
        private readonly float[] _samples = new float[FftSize];
        private readonly ImageBrush _grainBrush;
        private readonly Random _random = new(12345);
        private readonly List<Particle> _particles = new();
        private SpectrumData _spectrum = new();
        private Palette _palette = Palette.Default;
        private BitmapSource? _coverTiny;
        private double _elapsed;
        private double _hudRemaining = 2.8;
        private double _particleEmitAccumulator;
        private int _mode;
        private volatile bool _acceptAudioSamples;

        public AudioVisualizerControl()
        {
            Focusable = true;
            SnapsToDevicePixels = true;
            _grainBrush = CreateGrainBrush();
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += (_, _) => RenderFrame();
            MouseMove += OnMouseMove;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

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
            if ((bool)e.NewValue)
                control.Activate();
            else
                control.Deactivate();
        }

        private static void OnAlbumArtChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AudioVisualizerControl)d;
            control.UpdatePalette((BitmapSource?)e.NewValue);
            control._coverTiny = CreateTinyCover((BitmapSource?)e.NewValue);
            control.InvalidateVisual();
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((AudioVisualizerControl)d).InvalidateVisual();

        private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AudioVisualizerControl)d;
            if (control.IsActive)
                control.ResetAudioInput();
        }

        private void Activate()
        {
            _acceptAudioSamples = true;
            _hudRemaining = 2.8;
            _timer.Start();
            ResetAudioInput();
            InvalidateVisual();
        }

        private void Deactivate()
        {
            _acceptAudioSamples = false;
            _timer.Stop();
            ResetAudioInput();
            _spectrum = new SpectrumData();
            InvalidateVisual();
        }

        private void ResetAudioInput()
        {
            _audio.Clear();
            _analyzer.Reset();
            _particles.Clear();
            _particleEmitAccumulator = 0;
        }

        public void PushAudioSamples(IReadOnlyList<byte> buffer, int bufferLength, int sampleRate, int channelCount, int bitsPerSample)
        {
            if (!_acceptAudioSamples || bitsPerSample != 16 || channelCount <= 0 || sampleRate <= 0)
                return;

            var byteCount = Math.Min(Math.Max(0, bufferLength), buffer.Count);
            var bytesPerSample = sizeof(short);
            var frameSize = channelCount * bytesPerSample;
            var frameCount = byteCount / frameSize;
            if (frameCount <= 0)
                return;

            var mono = new float[frameCount];
            _analyzer.SetSampleRate(sampleRate);
            for (var frame = 0; frame < frameCount; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channelCount; channel++)
                {
                    var offset = frame * frameSize + channel * bytesPerSample;
                    var sample = unchecked((short)(buffer[offset] | buffer[offset + 1] << 8));
                    sum += sample / 32768f;
                }
                mono[frame] = sum / channelCount;
            }

            _audio.Push(mono);
        }

        private void RenderFrame()
        {
            var dt = 1.0 / 60.0;
            _elapsed += dt;
            _hudRemaining = Math.Max(0, _hudRemaining - dt);
            if (IsPlaying)
            {
                _audio.Latest(_samples);
                _spectrum = _analyzer.Process(_samples, (float)dt);
            }
            else
            {
                _spectrum = _analyzer.ProcessSilence((float)dt);
            }
            UpdateParticles((float)dt);
            InvalidateVisual();
        }

        private void UpdateParticles(float dt)
        {
            if (_spectrum.Beat > 0.5)
                EmitParticles(26, 1.0);

            _particleEmitAccumulator += dt * (8 + 60 * _spectrum.Treble);
            while (_particleEmitAccumulator > 1)
            {
                _particleEmitAccumulator -= 1;
                EmitParticles(1, 0.6);
            }

            for (var i = _particles.Count - 1; i >= 0; i--)
            {
                var particle = _particles[i];
                particle.R += particle.Velocity * dt;
                particle.Velocity *= Math.Exp(-dt * 0.9);
                particle.Life -= dt;
                particle.Angle += particle.Spin * dt;
                if (particle.Life <= 0)
                    _particles.RemoveAt(i);
                else
                    _particles[i] = particle;
            }
        }

        private void EmitParticles(int count, double strength)
        {
            for (var i = 0; i < count && _particles.Count < 600; i++)
            {
                var particle = new Particle
                {
                    Angle = _random.NextDouble() * Math.PI * 2,
                    R = 1 + _random.NextDouble() * 0.05,
                    Velocity = (0.25 + _random.NextDouble() * 0.55) * strength,
                    MaxLife = 1.2 + _random.NextDouble() * 1.6,
                    Size = 1.2 + _random.NextDouble() * 2.4,
                    Spin = (_random.NextDouble() - 0.5) * 0.3,
                    ColorIndex = _random.NextDouble() < 0.15 ? 3 : _random.Next(0, 3)
                };
                particle.Life = particle.MaxLife;
                _particles.Add(particle);
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsActive)
                return;

            _hudRemaining = 2.8;
            InvalidateVisual();
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsActive)
                return;

            var point = e.GetPosition(this);
            var hud = GetHudBounds();
            if (point.Y < hud.Top || point.Y > hud.Bottom)
                return;

            var segmentWidth = hud.Width / 3;
            var index = (int)((point.X - hud.Left) / segmentWidth);
            if (index is >= 0 and < 3)
            {
                _mode = index;
                _hudRemaining = 2.8;
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!IsActive || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var area = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.PushClip(new RectangleGeometry(area));
            try
            {
                switch (_mode)
                {
                    case 0: DrawAura(dc, area); break;
                    case 1: DrawHalo(dc, area); break;
                    default: DrawRibbon(dc, area); break;
                }
                DrawHud(dc, area);
            }
            finally
            {
                dc.Pop();
            }
        }

        private void DrawAura(DrawingContext dc, Rect area)
        {
            dc.DrawRectangle(Brush(_palette.Deep), null, area);
            var diagonal = Math.Sqrt(area.Width * area.Width + area.Height * area.Height) * 1.25;
            var art = new Rect(area.Left + area.Width / 2 - diagonal / 2, area.Top + area.Height / 2 - diagonal / 2, diagonal, diagonal);
            dc.PushTransform(new RotateTransform(_elapsed * 2.0, area.Width / 2, area.Height / 2));
            if (_coverTiny is not null)
                dc.DrawImage(_coverTiny, art);
            dc.Pop();
            dc.DrawRectangle(Brush(_palette.Deep, 0.50), null, area);

            var energy = new[] { _spectrum.Bass, (_spectrum.Bass + _spectrum.LowMid) / 2, _spectrum.LowMid,
                _spectrum.Mid, Math.Min(1, _spectrum.Treble * 1.3), _spectrum.Level };
            for (var i = 0; i < energy.Length; i++)
            {
                var phase = i * 1.7;
                var cx = area.Width * (0.5 + 0.34 * Math.Sin(_elapsed * (0.045 + i * 0.02) * 2.1 + phase));
                var cy = area.Height * (0.5 + 0.30 * Math.Cos(_elapsed * (0.045 + i * 0.02) * 2.9 + phase * 1.3));
                var radius = Math.Min(area.Width, area.Height) * (0.42 + 0.30 * energy[i]) * (1 + 0.06 * _spectrum.Beat);
                DrawRadial(dc, new Point(cx, cy), radius * 1.25, radius, _palette.Colors[i % 4], 0.30 + 0.50 * energy[i]);
            }
            DrawVignette(dc, area, 0.85);
            DrawGrain(dc, area, 0.035);

            var coverSize = Math.Min(area.Width, area.Height) * 0.46 * (1 + 0.018 * _spectrum.Beat);
            var center = new Point(area.Width / 2, area.Height * 0.44);
            DrawShadow(dc, center, coverSize * 0.78, coverSize * 0.72, 0.35);
            DrawCover(dc, new Rect(center.X - coverSize / 2, center.Y - coverSize / 2, coverSize, coverSize), coverSize * 0.035);
            var textTop = center.Y + coverSize / 2 + 28;
            DrawText(dc, TitleOrFileName(), 30, FontWeights.SemiBold, new Rect(area.Width * 0.1, textTop, area.Width * 0.8, 40), Colors.White, TextAlignment.Center);
            DrawText(dc, ArtistOrDefault(), 18, FontWeights.Normal, new Rect(area.Width * 0.1, textTop + 40, area.Width * 0.8, 28), Color.FromArgb(158, 255, 255, 255), TextAlignment.Center);
            var lineWidth = coverSize * (0.25 + 0.75 * _spectrum.Level);
            var linePen = new Pen(ColorBrush(Color.FromArgb(140, 255, 255, 255)), 2);
            dc.DrawLine(linePen, new Point(center.X - lineWidth / 2, textTop + 84), new Point(center.X + lineWidth / 2, textTop + 84));
        }

        private void DrawHalo(DrawingContext dc, Rect area)
        {
            dc.DrawRectangle(Brush(_palette.Deep), null, area);
            var center = new Point(area.Width / 2, area.Height * 0.47);
            var radius = Math.Min(area.Width, area.Height) * 0.19 * (1 + 0.035 * _spectrum.Beat);
            DrawRadial(dc, center, radius * 3.2, radius * 3.2, _palette.Colors[0], 0.12 + 0.30 * _spectrum.Bass);
            var guide = new Pen(ColorBrush(Color.FromArgb(14, 255, 255, 255)), 1);
            dc.DrawEllipse(null, guide, center, radius * 2.05, radius * 2.05);
            dc.DrawEllipse(null, guide, center, radius * 1.55, radius * 1.55);

            const int count = BandCount * 2;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var j = 0; j < count; j++)
                {
                    var band = j < BandCount ? j : count - 1 - j;
                    var value = _spectrum.Bands is { Length: > 0 } bands && band < bands.Length
                        ? bands[band]
                        : 0;
                    var angle = -Math.PI / 2 + (j + 0.5) / count * Math.PI * 2;
                    var inner = radius * 1.10;
                    var length = radius * 0.035 + value * radius * 0.95;
                    var outer = inner + length;
                    var from = new Point(center.X + Math.Cos(angle) * inner, center.Y + Math.Sin(angle) * inner);
                    var to = new Point(center.X + Math.Cos(angle) * outer, center.Y + Math.Sin(angle) * outer);
                    var color = ColorBlend(_palette.Colors[band < BandCount / 2 ? 0 : 1], _palette.Colors[band < BandCount * 3 / 4 ? 1 : 2], (band % (BandCount / 2)) / (double)(BandCount / 2));
                    var alpha = pass == 0 ? (byte)(25 + 64 * value) : (byte)(140 + 100 * value);
                    dc.DrawLine(new Pen(ColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), pass == 0 ? 7 : 2), from, to);
                }
            }
            for (var i = 0; i < 48; i++)
            {
                var angle = i * 2.39996 + _elapsed * (0.2 + (i % 5) * 0.03);
                var distance = radius * (1.16 + 0.12 * ((i * 37) % 100) / 100.0);
                var fade = 0.25 + 0.65 * ((i * 17) % 100) / 100.0;
                dc.DrawEllipse(Brush(Color.FromArgb((byte)(220 * fade), _palette.Colors[i % 4].R, _palette.Colors[i % 4].G, _palette.Colors[i % 4].B)), null,
                    new Point(center.X + Math.Cos(angle) * distance, center.Y + Math.Sin(angle) * distance), 1.5 + i % 3, 1.5 + i % 3);
            }
            foreach (var particle in _particles)
            {
                var life = particle.Life / particle.MaxLife;
                var fade = Math.Min(1, life * 1.8) * Math.Min(1, (1 - life) * 8);
                var distance = radius * (1.12 + particle.R);
                var color = _palette.Colors[particle.ColorIndex];
                dc.DrawEllipse(Brush(Color.FromArgb((byte)(217 * fade), color.R, color.G, color.B)), null,
                    new Point(center.X + Math.Cos(particle.Angle) * distance, center.Y + Math.Sin(particle.Angle) * distance),
                    particle.Size, particle.Size);
            }

            DrawShadow(dc, center, radius * 1.35, radius * 1.35, 0.35);
            DrawCoverCircle(dc, center, radius, _elapsed * 0.25);
            dc.DrawEllipse(null, new Pen(ColorBrush(Color.FromArgb(36, 255, 255, 255)), 1.5), center, radius, radius);
            dc.DrawEllipse(Brush(_palette.Deep, 0.95), null, center, radius * 0.07, radius * 0.07);
            dc.DrawEllipse(null, new Pen(ColorBrush(Color.FromArgb(64, 255, 255, 255)), 1), center, radius * 0.07, radius * 0.07);
            DrawGrain(dc, area, 0.03);
            var textTop = area.Height - 158;
            DrawText(dc, TitleOrFileName(), 26, FontWeights.SemiBold, new Rect(area.Width * 0.1, textTop, area.Width * 0.8, 36), Colors.White, TextAlignment.Center);
            DrawText(dc, ArtistOrDefault(), 16, FontWeights.Normal, new Rect(area.Width * 0.1, textTop + 34, area.Width * 0.8, 24), Color.FromArgb(140, 255, 255, 255), TextAlignment.Center);
        }

        private void DrawRibbon(DrawingContext dc, Rect area)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5, 5, 8)), null, area);
            var x0 = area.Width * 0.04;
            var span = area.Width * 0.92;
            var y0 = area.Height * 0.56;
            var energies = new[] { _spectrum.Bass, _spectrum.LowMid, _spectrum.Mid, Math.Min(1, _spectrum.Treble * 1.4) };
            var periods = new[] { 1.15, 1.75, 2.45, 3.3 };
            var speeds = new[] { 0.9, -1.25, 1.6, -2.2 };
            for (var layer = 0; layer < 4; layer++)
            {
                var pointsUp = new Point[180];
                var pointsDown = new Point[180];
                var amplitude = area.Height * (0.025 + 0.24 * Math.Clamp(energies[layer], 0, 1)) * (layer == 0 ? 1 + 0.25 * _spectrum.Beat : 1);
                var phase = _elapsed * speeds[layer] + layer * 1.3;
                for (var i = 0; i < pointsUp.Length; i++)
                {
                    var u = i / (double)(pointsUp.Length - 1);
                    var envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * u)), 2.2);
                    var wobble = 0.35 * Math.Sin(_elapsed * 0.7 + layer * 2.1 + u * 3);
                    var y = amplitude * envelope * Math.Sin(2 * Math.PI * periods[layer] * u + phase + wobble);
                    var x = x0 + u * span;
                    pointsUp[i] = new Point(x, y0 - y);
                    pointsDown[i] = new Point(x, y0 + y);
                }
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(pointsUp[0], true, true);
                    context.PolyLineTo(pointsUp.Skip(1).ToArray(), true, true);
                    context.PolyLineTo(pointsDown.Reverse().ToArray(), true, true);
                }
                geometry.Freeze();
                var ribbonColor = ColorBlend(_palette.Colors[layer % 3], _palette.Colors[(layer + 1) % 3], 0.5);
                dc.DrawGeometry(Brush(Color.FromArgb((byte)(70 + 70 * energies[layer]), ribbonColor.R, ribbonColor.G, ribbonColor.B)), null, geometry);
                var edge = new Pen(ColorBrush(Color.FromArgb((byte)(80 + 90 * energies[layer]), ribbonColor.R, ribbonColor.G, ribbonColor.B)), 2);
                DrawPolyline(dc, pointsUp, edge);
                DrawPolyline(dc, pointsDown, edge);
            }
            dc.DrawLine(new Pen(ColorBrush(Color.FromArgb((byte)(80 + 120 * _spectrum.Level), 255, 255, 255)), 1.5), new Point(x0, y0), new Point(x0 + span, y0));
            DrawGrain(dc, area, 0.025);

            var margin = 36.0;
            var coverSize = 72.0;
            DrawCover(dc, new Rect(margin, margin, coverSize, coverSize), 10);
            DrawText(dc, TitleOrFileName(), 22, FontWeights.SemiBold, new Rect(margin + coverSize + 18, margin + 6, area.Width - margin, 32), Colors.White, TextAlignment.Left);
            DrawText(dc, ArtistOrDefault(), 15, FontWeights.Normal, new Rect(margin + coverSize + 18, margin + 38, area.Width - margin, 26), Color.FromArgb(140, 255, 255, 255), TextAlignment.Left);
        }

        private void DrawHud(DrawingContext dc, Rect area)
        {
            if (_hudRemaining <= 0)
                return;

            var opacity = Math.Clamp(_hudRemaining / 0.6, 0, 1);
            var width = Math.Min(area.Width - 20, 3 * 96.0 + 10);
            var rect = new Rect((area.Width - width) / 2, area.Height - 66, width, 38);
            dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(20 * opacity), 255, 255, 255)), new Pen(ColorBrush(Color.FromArgb((byte)(36 * opacity), 255, 255, 255)), 1), rect, 19, 19);
            var names = new[] { "1  Aura", "2  Halo", "3  Ribbon" };
            for (var i = 0; i < 3; i++)
            {
                var button = new Rect(rect.Left + 5 + 96 * i, rect.Top + 5, 96, 28);
                if (i == _mode)
                    dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(46 * opacity), 255, 255, 255)), null, button, 14, 14);
                DrawText(dc, names[i], 14, i == _mode ? FontWeights.SemiBold : FontWeights.Normal,
                    button, Color.FromArgb((byte)((i == _mode ? 242 : 128) * opacity), 255, 255, 255), TextAlignment.Center);
            }
        }

        private Rect GetHudBounds() => new((ActualWidth - Math.Min(ActualWidth - 20, 298)) / 2, ActualHeight - 66, Math.Min(ActualWidth - 20, 298), 38);

        private void DrawCover(DrawingContext dc, Rect rect, double radius)
        {
            if (AlbumArt is null)
            {
                dc.DrawRoundedRectangle(Brush(_palette.Colors[0], 0.4), null, rect, radius, radius);
                return;
            }
            dc.PushClip(new RectangleGeometry(rect, radius, radius));
            DrawImageCover(dc, AlbumArt, rect);
            dc.Pop();
        }

        private void DrawCoverCircle(DrawingContext dc, Point center, double radius, double angle)
        {
            if (AlbumArt is null)
            {
                dc.DrawEllipse(Brush(_palette.Colors[0], 0.5), null, center, radius, radius);
                return;
            }
            dc.PushClip(new EllipseGeometry(center, radius, radius));
            dc.PushTransform(new RotateTransform(angle * 180 / Math.PI, center.X, center.Y));
            DrawImageCover(dc, AlbumArt, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
            dc.Pop();
            dc.Pop();
        }

        private static void DrawImageCover(DrawingContext dc, BitmapSource source, Rect destination)
        {
            var sourceWidth = Math.Max(1, source.Width);
            var sourceHeight = Math.Max(1, source.Height);
            var scale = Math.Max(destination.Width / sourceWidth, destination.Height / sourceHeight);
            var imageWidth = sourceWidth * scale;
            var imageHeight = sourceHeight * scale;
            var imageRect = new Rect(
                destination.X + (destination.Width - imageWidth) / 2,
                destination.Y + (destination.Height - imageHeight) / 2,
                imageWidth,
                imageHeight);
            dc.PushClip(new RectangleGeometry(destination));
            dc.DrawImage(source, imageRect);
            dc.Pop();
        }

        private static BitmapSource? CreateTinyCover(BitmapSource? source)
        {
            if (source is null)
                return null;

            var target = new RenderTargetBitmap(4, 4, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawImage(source, new Rect(0, 0, 4, 4));
            target.Render(visual);
            target.Freeze();
            return target;
        }

        private void DrawRadial(DrawingContext dc, Point center, double rx, double ry, Color color, double opacity)
        {
            var brush = new RadialGradientBrush(Color.FromArgb((byte)(220 * opacity), color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
            brush.Center = new Point(0.5, 0.5);
            brush.GradientOrigin = new Point(0.5, 0.5);
            dc.DrawEllipse(brush, null, center, rx, ry);
        }

        private void DrawShadow(DrawingContext dc, Point center, double rx, double ry, double opacity) =>
            DrawRadial(dc, new Point(center.X, center.Y + ry * 0.1), rx, ry, Colors.Black, opacity);

        private void DrawVignette(DrawingContext dc, Rect area, double opacity)
        {
            var brush = new RadialGradientBrush(Color.FromArgb(0, _palette.Deep.R, _palette.Deep.G, _palette.Deep.B), Color.FromArgb((byte)(190 * opacity), _palette.Deep.R, _palette.Deep.G, _palette.Deep.B));
            dc.DrawRectangle(brush, null, area);
        }

        private void DrawGrain(DrawingContext dc, Rect area, double opacity)
        {
            _grainBrush.Opacity = opacity;
            dc.DrawRectangle(_grainBrush, null, area);
        }

        private static ImageBrush CreateGrainBrush()
        {
            const int size = 128;
            const byte alpha = 20;
            var pixels = new byte[size * size * 4];
            var state = 12345u;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                state = state * 1664525u + 1013904223u;
                var value = (byte)(state >> 24);
                var channel = (byte)(value * alpha / 255);
                pixels[i] = channel;
                pixels[i + 1] = channel;
                pixels[i + 2] = channel;
                pixels[i + 3] = alpha;
            }

            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                Viewport = new Rect(0, 0, size, size),
                ViewportUnits = BrushMappingMode.Absolute
            };
        }

        private static void DrawPolyline(DrawingContext dc, IReadOnlyList<Point> points, Pen pen)
        {
            if (points.Count < 2)
                return;

            for (var i = 1; i < points.Count; i++)
                dc.DrawLine(pen, points[i - 1], points[i]);
        }

        private void DrawText(DrawingContext dc, string text, double size, FontWeight weight, Rect rect, Color color, TextAlignment alignment)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size,
                new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                TextAlignment = alignment,
                MaxTextWidth = rect.Width,
                MaxTextHeight = rect.Height,
                Trimming = TextTrimming.CharacterEllipsis
            };
            var textTop = rect.Top + Math.Max(0, (rect.Height - formatted.Height) / 2);
            dc.DrawText(formatted, new Point(rect.Left, textTop));
        }

        private string TitleOrFileName()
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title;
            return string.IsNullOrWhiteSpace(FilePath) ? "Now Playing" : System.IO.Path.GetFileNameWithoutExtension(FilePath);
        }

        private string ArtistOrDefault() => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;

        private void UpdatePalette(BitmapSource? source)
        {
            if (source is null)
            {
                _palette = Palette.Default;
                return;
            }
            try
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                var stride = converted.PixelWidth * 4;
                var pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);
                long r = 0, g = 0, b = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    b += pixels[i];
                    g += pixels[i + 1];
                    r += pixels[i + 2];
                }
                var count = Math.Max(1, pixels.Length / 4);
                _palette = Palette.FromColor(Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count)));
            }
            catch
            {
                _palette = Palette.Default;
            }
        }

        private static SolidColorBrush Brush(Color color, double opacity = 1)
        {
            color.A = (byte)(color.A * Math.Clamp(opacity, 0, 1));
            return new SolidColorBrush(color);
        }

        private static SolidColorBrush ColorBrush(Color color) => new(color);

        private static Color ColorBlend(Color first, Color second, double amount) => Color.FromRgb(
            (byte)(first.R + (second.R - first.R) * amount),
            (byte)(first.G + (second.G - first.G) * amount),
            (byte)(first.B + (second.B - first.B) * amount));

        private struct Particle
        {
            public double Angle;
            public double R;
            public double Velocity;
            public double MaxLife;
            public double Life;
            public double Size;
            public double Spin;
            public int ColorIndex;
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            if (VisualParent is null)
                Deactivate();
        }

        private readonly record struct Palette(Color Deep, Color[] Colors)
        {
            public static Palette Default => FromColor(Color.FromRgb(120, 90, 220));

            public static Palette FromColor(Color color)
            {
                RgbToHsl(color, out var h, out var s, out _);
                if (s < 0.12) { h = 262; s = 0.55; }
                s = Math.Clamp(s, 0.55, 0.90);
                return new Palette(
                    Hsl(h, 0.30, 0.035),
                    new[] { Hsl(h, s, 0.60), Hsl(h + 38, s, 0.62), Hsl(h - 42, s * 0.95, 0.56), Hsl(h + 165, s * 0.85, 0.66) });
            }

            private static void RgbToHsl(Color c, out double h, out double s, out double l)
            {
                var r = c.R / 255.0; var g = c.G / 255.0; var b = c.B / 255.0;
                var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
                var delta = max - min; l = (max + min) / 2;
                s = delta < 0.0001 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
                if (delta < 0.0001) { h = 0; return; }
                h = max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
                if (h < 0) h += 360;
            }

            private static Color Hsl(double h, double s, double l)
            {
                h = (h % 360 + 360) % 360;
                var c = (1 - Math.Abs(2 * l - 1)) * s;
                var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
                var m = l - c / 2;
                var rgb = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
                return Color.FromRgb((byte)((rgb.Item1 + m) * 255), (byte)((rgb.Item2 + m) * 255), (byte)((rgb.Item3 + m) * 255));
            }
        }

        private sealed class AudioRingBuffer
        {
            private readonly float[] _buffer;
            private readonly object _sync = new();
            private int _write;

            public AudioRingBuffer(int size) => _buffer = new float[size];

            public void Push(float[] samples)
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

        private sealed class SpectrumAnalyzer
        {
            private readonly double[] _real = new double[FftSize];
            private readonly double[] _imaginary = new double[FftSize];
            private readonly double[] _window = new double[FftSize];
            private readonly double[] _edges = new double[BandCount + 1];
            private readonly double[] _bands = new double[BandCount];
            private readonly double[][] _cosTables = new double[12][];
            private readonly double[][] _sinTables = new double[12][];
            private double _agc = -30;
            private double _bassAverage;
            private double _sinceBeat = 1;
            private int _sampleRate = 48000;
            private bool _initialized;

            public void SetSampleRate(int sampleRate)
            {
                if (sampleRate > 0)
                    Interlocked.Exchange(ref _sampleRate, sampleRate);
            }

            public void Reset()
            {
                Array.Clear(_real);
                Array.Clear(_imaginary);
                Array.Clear(_bands);
                _agc = -30;
                _bassAverage = 0;
                _sinceBeat = 1;
            }

            public SpectrumData Process(float[] samples, float dt)
            {
                EnsureInitialized();
                for (var i = 0; i < FftSize; i++)
                {
                    _real[i] = samples[i] * _window[i];
                    _imaginary[i] = 0;
                }
                Transform();
                return Analyze(dt);
            }

            public SpectrumData ProcessSilence(float dt)
            {
                EnsureInitialized();
                for (var i = 0; i < BandCount; i++)
                    _bands[i] *= Math.Exp(-dt / 0.22);
                var result = CreateData();
                result.Beat = (float)(result.Beat * Math.Exp(-dt * 5.5));
                return result;
            }

            private void EnsureInitialized()
            {
                if (_initialized) return;
                for (var i = 0; i < FftSize; i++)
                    _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
                for (var i = 0; i <= BandCount; i++)
                    _edges[i] = 30 * Math.Pow(16000 / 30.0, (double)i / BandCount);
                var stage = 0;
                for (var length = 2; length <= FftSize; length <<= 1)
                {
                    _cosTables[stage] = new double[length / 2];
                    _sinTables[stage] = new double[length / 2];
                    var angle = -2 * Math.PI / length;
                    for (var offset = 0; offset < length / 2; offset++)
                    {
                        _cosTables[stage][offset] = Math.Cos(angle * offset);
                        _sinTables[stage][offset] = Math.Sin(angle * offset);
                    }
                    stage++;
                }
                _initialized = true;
            }

            private void Transform()
            {
                for (var i = 1; i < FftSize; i++)
                {
                    var bit = FftSize >> 1;
                    var j = 0;
                    while ((i & bit) != 0) { j ^= bit; bit >>= 1; }
                    j ^= bit;
                    if (i < j)
                    {
                        (_real[i], _real[j]) = (_real[j], _real[i]);
                        (_imaginary[i], _imaginary[j]) = (_imaginary[j], _imaginary[i]);
                    }
                }
                var stage = 0;
                for (var length = 2; length <= FftSize; length <<= 1)
                {
                    var cosTable = _cosTables[stage];
                    var sinTable = _sinTables[stage];
                    for (var start = 0; start < FftSize; start += length)
                    for (var offset = 0; offset < length / 2; offset++)
                    {
                        var cos = cosTable[offset]; var sin = sinTable[offset];
                        var even = start + offset; var odd = even + length / 2;
                        var tr = _real[odd] * cos - _imaginary[odd] * sin;
                        var ti = _real[odd] * sin + _imaginary[odd] * cos;
                        _real[odd] = _real[even] - tr; _imaginary[odd] = _imaginary[even] - ti;
                        _real[even] += tr; _imaginary[even] += ti;
                    }
                    stage++;
                }
            }

            private SpectrumData Analyze(float dt)
            {
                var raw = new double[BandCount];
                var max = -120.0;
                var sampleRate = Volatile.Read(ref _sampleRate);
                for (var band = 0; band < BandCount; band++)
                {
                    var low = _edges[band] * FftSize / sampleRate;
                    var high = _edges[band + 1] * FftSize / sampleRate;
                    var magnitude = 0.0;
                    if (high - low < 1)
                    {
                        var binCenter = Math.Clamp((low + high) * 0.5, 1, FftSize / 2.0 - 2);
                        var lowerBin = (int)binCenter;
                        var fraction = binCenter - lowerBin;
                        var first = Magnitude(lowerBin);
                        var second = Magnitude(lowerBin + 1);
                        magnitude = first + (second - first) * fraction;
                    }
                    else
                    {
                        for (var bin = Math.Max(1, (int)Math.Ceiling(low)); bin <= high && bin < FftSize / 2; bin++)
                            magnitude = Math.Max(magnitude, Magnitude(bin));
                    }
                    var center = Math.Sqrt(_edges[band] * _edges[band + 1]);
                    raw[band] = 20 * Math.Log10(magnitude * 4 / (FftSize * 0.5) + 1e-7) + 3 * Math.Log2(center / 1000);
                    max = Math.Max(max, raw[band]);
                }
                _agc = Math.Max(max, _agc - 4 * dt);
                _agc = Math.Max(_agc, -42);
                var attack = 1 - Math.Exp(-dt / 0.035); var release = 1 - Math.Exp(-dt / 0.22);
                for (var i = 0; i < BandCount; i++)
                {
                    var value = Math.Clamp((raw[i] - (_agc - 48)) / 48, 0, 1);
                    value = Math.Pow(value, 1.6);
                    _bands[i] += (value - _bands[i]) * (value > _bands[i] ? attack : release);
                }
                var currentBass = Average(0, 150);
                _bassAverage += (currentBass - _bassAverage) * (1 - Math.Exp(-dt / 0.9));
                _sinceBeat += dt;
                var beat = currentBass > _bassAverage * 1.3 + 0.04 && currentBass > 0.25 && _sinceBeat > 0.27 ? 1 : 0;
                if (beat > 0) _sinceBeat = 0;
                return CreateData((float)beat);
            }

            private double Magnitude(int bin) => Math.Sqrt(_real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]);

            private double Average(double low, double high)
            {
                var sum = 0.0; var count = 0;
                for (var i = 0; i < BandCount; i++)
                {
                    var center = Math.Sqrt(_edges[i] * _edges[i + 1]);
                    if (center >= low && center < high) { sum += _bands[i]; count++; }
                }
                return count == 0 ? 0 : sum / count;
            }

            private SpectrumData CreateData(float beat = 0)
            {
                var result = new SpectrumData { Beat = beat };
                Array.Copy(_bands, result.Bands, BandCount);
                result.Bass = Average(0, 150); result.LowMid = Average(150, 600);
                result.Mid = Average(600, 3000); result.Treble = Average(3000, 20000);
                result.Level = Average(0, 20000);
                return result;
            }
        }

        private struct SpectrumData
        {
            public double[] Bands;
            public double Bass;
            public double LowMid;
            public double Mid;
            public double Treble;
            public double Level;
            public double Beat;

            public SpectrumData()
            {
                Bands = new double[BandCount];
            }
        }
    }
}
