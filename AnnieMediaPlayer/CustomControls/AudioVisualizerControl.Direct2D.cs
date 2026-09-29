using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using DxgiFormat = Vortice.DXGI.Format;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using DWriteTextAlignment = Vortice.DirectWrite.TextAlignment;

namespace AnnieMediaPlayer.CustomControls
{
    public sealed partial class AudioVisualizerControl
    {
        // GPU 경로: Direct2D 로 공유 텍스처에 그리고 D3DImage 로 WPF 화면에 합성합니다.
        // 사용할 수 없는 환경(원격 데스크톱, 소프트웨어 렌더링, 초기화 실패)에서는 WPF 렌더러를 사용합니다.
        private readonly D3DImage _gpuImageSource = new();
        private readonly System.Windows.Controls.Image _gpuImage = new()
        {
            Stretch = Stretch.Fill,
            Visibility = Visibility.Collapsed
        };
        private Direct2DRenderer? _gpu;
        private bool _gpuUnavailable;
        private bool _gpuBackBufferSet;
        private static int s_pendingGpuSignals;

        // 장치 손실은 보통 한 번 다시 만들면 복구되지만, 실패가 이어지면 매 프레임 장치를 다시 만들게 되므로
        // 짧은 시간 안에 여러 번 실패하면 GPU 경로를 끄고 WPF 렌더러를 사용합니다.
        private const int GpuFailureLimit = 3;
        private static readonly TimeSpan GpuFailureWindow = TimeSpan.FromSeconds(5);
        private int _gpuFailureCount;
        private long _gpuFirstFailureTimestamp;

        private static readonly TimeSpan FrontBufferRetryInitialDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan FrontBufferRetryMaxDelay = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan FrontBufferRetryGrace = TimeSpan.FromSeconds(1);
        private long _frontBufferLostTimestamp;
        private long _frontBufferRetryStartTimestamp;
        private TimeSpan _frontBufferRetryDelay;

        private void InitializeGpuLayer()
        {
            _gpuImage.Source = _gpuImageSource;
            _gpuImageSource.IsFrontBufferAvailableChanged += (_, _) =>
            {
                // 장치가 손실되면 표면을 버리고, 다시 사용할 수 있게 되면 다음 프레임에서 새로 만듭니다.
                ReleaseGpu();
                RenderNow();
            };
        }

        private static bool IsGpuRenderingSupported(Visual visual)
        {
            if (SystemParameters.IsRemoteSession || RenderCapability.Tier >> 16 < 2 ||
                RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly)
                return false;

            return PresentationSource.FromVisual(visual) is HwndSource { CompositionTarget.RenderMode: RenderMode.Default };
        }

        // GPU 로 그렸으면 true, WPF 렌더러로 그려야 하면 false 를 반환합니다.
        private bool TryRenderGpu(VizResources res, Spectrum s)
        {
            if (_gpuUnavailable)
            {
                if (_gpu is not null)
                    ReleaseGpu();
                return false;
            }

            if (!IsGpuRenderingSupported(this))
            {
                ReleaseGpu();
                return false;
            }

            if (!IsFrontBufferReady())
                return false;

            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var pixelWidth = Math.Max(1, (int)Math.Ceiling(_width * dpi.DpiScaleX));
                var pixelHeight = Math.Max(1, (int)Math.Ceiling(_height * dpi.DpiScaleY));

                if (_gpu is null)
                {
                    // 이전 렌더러의 GPU 작업이 끝나지 않았으면 완료될 때까지 WPF 렌더러를 사용합니다.
                    if (Volatile.Read(ref s_pendingGpuSignals) > 0)
                        return false;

                    var hwnd = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
                    if (hwnd == IntPtr.Zero)
                        return false;

                    _gpu = new Direct2DRenderer(hwnd);
                }

                var result = _gpu.Render(this, res, s, pixelWidth, pixelHeight, (float)dpi.DpiScaleX, (float)dpi.DpiScaleY);
                if (result is GpuFrameResult.DeviceLost or GpuFrameResult.Stalled)
                {
                    // 다음 프레임에 장치를 다시 만들고, 이번 프레임은 WPF 렌더러로 그립니다.
                    ReleaseGpu();
                    RecordGpuFailure(result == GpuFrameResult.DeviceLost ? "Direct2D EndDraw failed" : "GPU did not finish drawing in time");
                    return false;
                }

                if (result == GpuFrameResult.Skipped)
                {
                    // GPU 화면이 보이는 중이면 이전 프레임을 유지하고,
                    // 아직 한 번도 표시하지 않았으면(새로 만든 렌더러) 이번 프레임은 WPF 렌더러로 그립니다.
                    return _gpuImage.Visibility == Visibility.Visible;
                }

                if (_gpuImage.Visibility != Visibility.Visible)
                {
                    ClearLayers();
                    ClearModeTransition();
                    _gpuImage.Visibility = Visibility.Visible;
                }

                return true;
            }
            catch (Exception ex)
            {
                ReleaseGpu();
                RecordGpuFailure(ex.ToString());
                return false;
            }
        }

        // GPU 전면 버퍼를 쓸 수 없는 동안(장치 손실 등)에는 WPF 렌더러로 그립니다.
        // GPU 이미지를 화면에서 내리면 복구 알림(IsFrontBufferAvailableChanged)이 오지 않을 수 있으므로,
        // 간격을 늘려 가며 한 번씩 다시 시도하고, 시도한 직후에는 알림이 도착할 때까지 잠시 상태 값을 무시합니다.
        private bool IsFrontBufferReady()
        {
            if (_gpuImageSource.IsFrontBufferAvailable)
            {
                _frontBufferLostTimestamp = 0;
                _frontBufferRetryStartTimestamp = 0;
                _frontBufferRetryDelay = TimeSpan.Zero;
                return true;
            }

            if (_frontBufferRetryStartTimestamp != 0)
            {
                // 다시 시도한 렌더러가 살아 있으면 복구 알림을 잠시 기다립니다.
                if (_gpu is not null && Stopwatch.GetElapsedTime(_frontBufferRetryStartTimestamp) < FrontBufferRetryGrace)
                    return true;

                // 시도가 실패해 렌더러가 해제됐거나, 기다려도 복구되지 않았으면 이번 시도는 끝냅니다.
                // 실패한 시도마다 새 렌더러를 만들지 않도록 다음 시도까지 간격을 둡니다.
                ScheduleFrontBufferRetry();
            }
            else if (_frontBufferLostTimestamp == 0)
            {
                ScheduleFrontBufferRetry();
            }

            if (Stopwatch.GetElapsedTime(_frontBufferLostTimestamp) < _frontBufferRetryDelay)
                return false;

            _frontBufferRetryStartTimestamp = Stopwatch.GetTimestamp();
            return true;
        }

        private void ScheduleFrontBufferRetry()
        {
            ReleaseGpu();
            _frontBufferRetryDelay = _frontBufferRetryDelay == TimeSpan.Zero
                ? FrontBufferRetryInitialDelay
                : TimeSpan.FromTicks(Math.Min(_frontBufferRetryDelay.Ticks * 2, FrontBufferRetryMaxDelay.Ticks));
            _frontBufferLostTimestamp = Stopwatch.GetTimestamp();
            _frontBufferRetryStartTimestamp = 0;
        }

        private void RecordGpuFailure(string reason)
        {
            var now = Stopwatch.GetTimestamp();
            if (_gpuFailureCount == 0 || Stopwatch.GetElapsedTime(_gpuFirstFailureTimestamp, now) > GpuFailureWindow)
            {
                _gpuFailureCount = 0;
                _gpuFirstFailureTimestamp = now;
            }

            _gpuFailureCount++;
            PlayerDiagnostics.Write($"Visualizer GPU rendering failed ({_gpuFailureCount}/{GpuFailureLimit}): {reason}");
            if (_gpuFailureCount >= GpuFailureLimit)
            {
                _gpuUnavailable = true;
                PlayerDiagnostics.Write("Visualizer GPU rendering disabled; using WPF renderer.");
            }
        }

        private enum GpuFrameResult
        {
            Presented,
            Skipped,    // GPU 가 앞 프레임을 아직 끝내지 못해 이전 화면을 유지함
            DeviceLost,
            Stalled     // GPU 지연이 연속으로 이어짐
        }

        private bool IsGpuActive =>_gpu is not null && _gpuImage.Visibility == Visibility.Visible;

        // 모드 전환 화면을 보관하는 중 장치 오류가 나도 UI 이벤트로 예외가 전파되지 않게 합니다.
        private void BeginGpuTransition()
        {
            try
            {
                _gpu!.BeginTransition();
            }
            catch (Exception ex)
            {
                ReleaseGpu();
                RecordGpuFailure(ex.ToString());
            }
        }

        private void ReleaseGpu()
        {
            // GPU 를 쓰지 않는 환경에서는 매 프레임 호출되므로, 해제할 것이 있을 때만 D3DImage 를 건드립니다.
            if (_gpu is null && !_gpuBackBufferSet)
                return;

            _gpu?.Dispose();
            _gpu = null;
            _gpuImage.Visibility = Visibility.Collapsed;
            _gpuImageSource.Lock();
            try
            {
                _gpuImageSource.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                _gpuBackBufferSet = false;
            }
            finally
            {
                _gpuImageSource.Unlock();
            }
        }

        // Direct2D 렌더러. 그리기 순서와 수치는 WPF 렌더러와 같고, 가산 혼합은 Direct2D 의 PrimitiveBlend.Add 를 사용합니다.
        private sealed class Direct2DRenderer : IDisposable
        {
            private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(150);
            private static readonly TimeSpan GpuWaitTimeout = TimeSpan.FromMilliseconds(100);
            private const int GpuTimeoutLimit = 3;
            private static readonly TimeSpan GpuStallLimit = TimeSpan.FromMilliseconds(500);

            private readonly ID2D1Factory1 _d2dFactory;
            private readonly IDWriteFactory _dwriteFactory;
            private readonly string _fontFamily;
            private readonly ID3D11Device _d3d11;
            private readonly IDXGIDevice2 _dxgiDevice;
            private readonly AutoResetEvent _gpuDone = new(false);
            private bool _gpuWaitPending;
            private int _gpuTimeouts;
            private long _gpuSubmitTimestamp;
            private readonly IDirect3D9Ex _d3d9;
            private readonly IDirect3DDevice9Ex _d3d9Device;
            private readonly ID2D1Device _d2dDevice;
            private readonly ID2D1DeviceContext _dc;
            private readonly ID2D1StrokeStyle _round;
            private readonly Dictionary<(int Size, DWriteFontWeight Weight, DWriteTextAlignment Align), IDWriteTextFormat> _formats = new();

            // 크기 의존
            private ID3D11Texture2D? _texture;
            private IDirect3DTexture9? _texture9;
            private IDirect3DSurface9? _surface9;
            private ID2D1Bitmap1? _target;
            private int _pixelWidth;
            private int _pixelHeight;

            // 내용(커버·팔레트) 의존
            private VizResources? _content;
            private ID2D1Bitmap? _cover;
            private ID2D1Bitmap? _coverTiny;
            private ID2D1BitmapBrush? _coverBrush;
            private ID2D1BitmapBrush? _grain;
            private ID2D1SolidColorBrush? _solid;
            private readonly ID2D1RadialGradientBrush?[] _blobs = new ID2D1RadialGradientBrush?[6];
            private ID2D1RadialGradientBrush? _vignette, _glow, _shadow;
            private readonly ID2D1LinearGradientBrush?[] _ribbons = new ID2D1LinearGradientBrush?[4];
            private ID2D1LinearGradientBrush? _whiteFade, _edgeGlow;

            // 모드 전환 크로스페이드
            private ID2D1Bitmap1? _transitionFrame;
            private long _transitionStart;

            private readonly Vector2[] _up = new Vector2[RibbonPointCount];
            private readonly Vector2[] _down = new Vector2[RibbonPointCount];

            public Direct2DRenderer(IntPtr hwnd)
            {
                // 생성 도중 실패하면 호출한 쪽은 이 객체를 받지 못하므로, 그때까지 만든 장치를 여기서 해제합니다.
                try
                {
                    _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded, DebugLevel.None);
                    _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
                    _fontFamily = FontExists(_dwriteFactory, "Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";

                    // WPF 가 창을 그리는 GPU 와 같은 어댑터에서 장치를 만들어야 표면을 공유할 수 있습니다.
                    _d3d9 = D3D9.Direct3DCreate9Ex();
                    var adapter9 = FindD3D9Adapter(_d3d9, hwnd);
                    var luid = _d3d9.GetAdapterLuid(adapter9);
                    _d3d11 = CreateD3D11Device(luid);
                    _dxgiDevice = _d3d11.QueryInterface<IDXGIDevice2>();

                    var presentParameters = new Vortice.Direct3D9.PresentParameters
                    {
                        Windowed = true,
                        SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                        DeviceWindowHandle = hwnd,
                        PresentationInterval = PresentInterval.Immediate,
                        BackBufferWidth = 1,
                        BackBufferHeight = 1,
                        BackBufferFormat = Vortice.Direct3D9.Format.Unknown
                    };
                    const CreateFlags flags = CreateFlags.Multithreaded | CreateFlags.FpuPreserve;
                    try
                    {
                        _d3d9Device = _d3d9.CreateDeviceEx(adapter9, DeviceType.Hardware, hwnd, flags | CreateFlags.HardwareVertexProcessing, presentParameters);
                    }
                    catch
                    {
                        _d3d9Device = _d3d9.CreateDeviceEx(adapter9, DeviceType.Hardware, hwnd, flags | CreateFlags.SoftwareVertexProcessing, presentParameters);
                    }

                    _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
                    _dc = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
                    _round = _d2dFactory.CreateStrokeStyle(new StrokeStyleProperties
                    {
                        StartCap = CapStyle.Round,
                        EndCap = CapStyle.Round,
                        DashCap = CapStyle.Round,
                        LineJoin = LineJoin.Round
                    });
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public GpuFrameResult Render(AudioVisualizerControl owner, VizResources res, Spectrum s, int pixelWidth, int pixelHeight, float dpiScaleX, float dpiScaleY)
            {
                // 앞 프레임이 GPU 에서 아직 끝나지 않았으면 기다리지 않고 이번 프레임을 건너뜁니다.
                // 제출한 지 오래되도록 끝나지 않으면 GPU 가 멈춘 것으로 봅니다.
                if (_gpuWaitPending)
                {
                    if (!_gpuDone.WaitOne(0))
                        return Stopwatch.GetElapsedTime(_gpuSubmitTimestamp) >= GpuStallLimit ? GpuFrameResult.Stalled : GpuFrameResult.Skipped;
                    _gpuWaitPending = false;
                }

                // WPF 가 이전 프레임을 복사하는 동안 표면에 그리지 않도록 표면 준비와 그리기를 잠금 안에서 수행합니다.
                var image = owner._gpuImageSource;
                image.Lock();
                try
                {
                    EnsureTarget(owner, pixelWidth, pixelHeight);
                    EnsureContent(res);

                    var w = (float)owner._width;
                    var h = (float)owner._height;
                    var u = (float)owner._unit;
                    _dc.Target = _target;
                    _dc.SetDpi(96f * dpiScaleX, 96f * dpiScaleY);
                    _dc.BeginDraw();
                    _dc.Transform = Matrix3x2.Identity;
                    switch (owner._mode)
                    {
                        case 0: DrawAura(owner, res, s, w, h, u, dpiScaleX); break;
                        case 1: DrawHalo(owner, res, s, w, h, u, dpiScaleX); break;
                        default: DrawRibbon(owner, res, s, w, h, u, dpiScaleX); break;
                    }
                    DrawTransition(w, h);
                    if (_dc.EndDraw().Failure)
                        return GpuFrameResult.DeviceLost;

                    // 공유 표면은 장치 사이에 자동으로 동기화되지 않으므로, GPU 가 그리기를 마친 뒤에 WPF 에 알려야
                    // 덜 그려진 화면이 복사되지 않습니다. 명령을 GPU 로 보내고 완료 이벤트를 기다리므로 CPU 를 쓰지 않습니다.
                    _gpuSubmitTimestamp = Stopwatch.GetTimestamp();
                    _dxgiDevice.EnqueueSetEvent(_gpuDone.SafeWaitHandle.DangerousGetHandle());
                    if (!_gpuDone.WaitOne(GpuWaitTimeout))
                    {
                        // 늦게 끝나는 프레임은 표시하지 않고, 완료 신호는 다음 프레임 전에 확인합니다.
                        _gpuWaitPending = true;
                        return ++_gpuTimeouts >= GpuTimeoutLimit ? GpuFrameResult.Stalled : GpuFrameResult.Skipped;
                    }

                    _gpuTimeouts = 0;
                    image.AddDirtyRect(new Int32Rect(0, 0, _pixelWidth, _pixelHeight));
                    return GpuFrameResult.Presented;
                }
                finally
                {
                    image.Unlock();
                }
            }

            // 현재 화면을 보관해 두고 다음 프레임부터 새 모드 위에서 서서히 사라지게 합니다.
            public void BeginTransition()
            {
                if (_target is null)
                    return;

                ClearTransition();
                _transitionFrame = _dc.CreateBitmap(new SizeI(_pixelWidth, _pixelHeight), IntPtr.Zero, 0,
                    new BitmapProperties1(new D2DPixelFormat(DxgiFormat.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), _dc.Dpi.Width, _dc.Dpi.Height));
                _transitionFrame.CopyFromBitmap(_target);
                _transitionStart = Stopwatch.GetTimestamp();
            }

            public void ClearTransition()
            {
                _transitionFrame?.Dispose();
                _transitionFrame = null;
            }

            private void DrawTransition(float w, float h)
            {
                if (_transitionFrame is null)
                    return;

                var progress = Stopwatch.GetElapsedTime(_transitionStart) / TransitionDuration;
                if (progress >= 1)
                {
                    ClearTransition();
                    return;
                }

                _dc.PrimitiveBlend = PrimitiveBlend.SourceOver;
                _dc.DrawBitmap(_transitionFrame, new RawRectF(0, 0, w, h), (float)(1 - progress), Vortice.Direct2D1.InterpolationMode.Linear, null, null);
            }

            #region 장치·표면

            private static uint FindD3D9Adapter(IDirect3D9Ex d3d9, IntPtr hwnd)
            {
                var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
                for (uint i = 0; i < d3d9.AdapterCount; i++)
                {
                    if (d3d9.GetAdapterMonitor(i) == monitor)
                        return i;
                }

                return 0;
            }

            private static ID3D11Device CreateD3D11Device(Vortice.Direct3D9.Luid luid)
            {
                var levels = new[]
                {
                    Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1, Vortice.Direct3D.FeatureLevel.Level_10_0,
                    Vortice.Direct3D.FeatureLevel.Level_9_3, Vortice.Direct3D.FeatureLevel.Level_9_2, Vortice.Direct3D.FeatureLevel.Level_9_1
                };

                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
                {
                    using (adapter)
                    {
                        var description = adapter.Description1;
                        if (description.Luid.LowPart != luid.LowPart || description.Luid.HighPart != luid.HighPart)
                            continue;

                        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, levels, out var device).CheckError();
                        return device!;
                    }
                }

                // 다른 GPU 에 만든 텍스처는 공유가 실패하거나 느려질 수 있으므로 GPU 경로를 쓰지 않습니다.
                throw new NotSupportedException("No Direct3D 11 adapter matches the adapter presenting the window.");
            }

            private void EnsureTarget(AudioVisualizerControl owner, int pixelWidth, int pixelHeight)
            {
                if (_target is not null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight)
                    return;

                ClearTransition();
                ReleaseTarget();
                _pixelWidth = pixelWidth;
                _pixelHeight = pixelHeight;

                // D3D9Ex 와 공유하려면 이전 방식의 공유 핸들(ResourceOptionFlags.Shared)을 사용해야 합니다.
                _texture = _d3d11.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)pixelWidth,
                    Height = (uint)pixelHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = DxgiFormat.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None,
                    MiscFlags = ResourceOptionFlags.Shared
                });

                IntPtr sharedHandle;
                using (var resource = _texture.QueryInterface<IDXGIResource>())
                    sharedHandle = resource.SharedHandle;

                _texture9 = _d3d9Device.CreateTexture((uint)pixelWidth, (uint)pixelHeight, 1, Vortice.Direct3D9.Usage.RenderTarget,
                    Vortice.Direct3D9.Format.A8R8G8B8, Pool.Default, ref sharedHandle);
                _surface9 = _texture9.GetSurfaceLevel(0);

                using (var surface = _texture.QueryInterface<IDXGISurface>())
                {
                    _target = _dc.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
                        new D2DPixelFormat(DxgiFormat.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96,
                        BitmapOptions.Target | BitmapOptions.CannotDraw));
                }

                // Render 가 D3DImage 를 잠근 상태에서 호출됩니다.
                // 설정 도중 실패해도 ReleaseGpu 가 백버퍼를 비우도록 먼저 표시해 둡니다.
                // 원격 데스크톱 등으로 전환되어도 표시되도록 소프트웨어 대체를 허용합니다.
                owner._gpuBackBufferSet = true;
                owner._gpuImageSource.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface9.NativePointer, true);
            }

            private void ReleaseTarget()
            {
                if (_dc is not null)
                    _dc.Target = null;
                _target?.Dispose();
                _target = null;
                _surface9?.Dispose();
                _surface9 = null;
                _texture9?.Dispose();
                _texture9 = null;
                _texture?.Dispose();
                _texture = null;
            }

            #endregion

            #region 커버·팔레트 리소스

            private void EnsureContent(VizResources res)
            {
                if (ReferenceEquals(_content, res))
                    return;

                ReleaseContent();
                _content = res;
                var palette = res.Palette;

                if (res.CoverSource is BitmapSource cover)
                {
                    _cover = CreateBitmap(cover);
                    var tiny = CoverColorExtractor.SampleBgra(cover, 4, 4);
                    Premultiply(tiny);
                    _coverTiny = CreateBitmap(4, 4, tiny);
                }
                else
                {
                    // 커버가 없으면 팔레트 색으로 채운 작은 이미지를 사용합니다.
                    var c = palette.Colors[0];
                    var pixels = new byte[4 * 4 * 4];
                    for (var i = 0; i < pixels.Length; i += 4)
                    {
                        pixels[i] = c.B;
                        pixels[i + 1] = c.G;
                        pixels[i + 2] = c.R;
                        pixels[i + 3] = 255;
                    }
                    _cover = CreateBitmap(4, 4, pixels);
                    _coverTiny = CreateBitmap(4, 4, pixels);
                }

                _coverBrush = _dc.CreateBitmapBrush(_cover, new BitmapBrushProperties1(ExtendMode.Clamp, ExtendMode.Clamp, Vortice.Direct2D1.InterpolationMode.Linear), null);
                _solid = _dc.CreateSolidColorBrush(new Color4(1, 1, 1, 1));

                // 필름 그레인 (256x256 타일)
                const int grainSize = 256;
                var grain = new byte[grainSize * grainSize * 4];
                var state = 12345u;
                for (var i = 0; i < grain.Length; i += 4)
                {
                    state = state * 1664525u + 1013904223u;
                    var channel = (byte)((state >> 24) * 20 / 255);
                    grain[i] = channel;
                    grain[i + 1] = channel;
                    grain[i + 2] = channel;
                    grain[i + 3] = 20;
                }
                using (var grainBitmap = CreateBitmap(grainSize, grainSize, grain))
                    _grain = _dc.CreateBitmapBrush(grainBitmap, new BitmapBrushProperties1(ExtendMode.Wrap, ExtendMode.Wrap, Vortice.Direct2D1.InterpolationMode.NearestNeighbor), null);

                (float, float)[] blobStops = { (0, 1), (0.35f, 0.75f), (0.7f, 0.25f), (1, 0) };
                for (var i = 0; i < 6; i++)
                    _blobs[i] = Radial(palette.Colors[i == 5 ? 0 : i % 4], blobStops);
                _vignette = Radial(palette.Deep, (0, 0), (0.55f, 0.1f), (1, 0.9f));
                _glow = Radial(palette.Colors[0], (0, 0.9f), (0.4f, 0.35f), (1, 0));
                _shadow = Radial(WpfColors.Black, (0, 0.55f), (0.6f, 0.2f), (1, 0));

                // Ribbon: 레이어별 수평 그라디언트 (양끝 투명 → 가운데 발광, 인접색으로 색 이동)
                for (var layer = 0; layer < 4; layer++)
                {
                    WpfColor a = palette.Colors[layer % 3], b = palette.Colors[(layer + 1) % 3];
                    _ribbons[layer] = Linear(
                        (0, ToColor4(a, 0)), (0.18f, ToColor4(a, 0.35f)), (0.5f, ToColor4(Mix(a, b, 0.5), 1)),
                        (0.82f, ToColor4(b, 0.35f)), (1, ToColor4(b, 0)));
                }
                _whiteFade = Linear((0, new Color4(1, 1, 1, 0)), (0.5f, new Color4(1, 1, 1, 1)), (1, new Color4(1, 1, 1, 0)));
                _edgeGlow = Linear((0, ToColor4(palette.Colors[0], 0.55f)), (0.45f, ToColor4(palette.Colors[1], 0.12f)), (1, ToColor4(palette.Colors[1], 0)));
            }

            private void ReleaseContent()
            {
                _cover?.Dispose();
                _coverTiny?.Dispose();
                _coverBrush?.Dispose();
                _grain?.Dispose();
                _solid?.Dispose();
                for (var i = 0; i < _blobs.Length; i++)
                {
                    _blobs[i]?.Dispose();
                    _blobs[i] = null;
                }
                _vignette?.Dispose();
                _glow?.Dispose();
                _shadow?.Dispose();
                for (var i = 0; i < _ribbons.Length; i++)
                {
                    _ribbons[i]?.Dispose();
                    _ribbons[i] = null;
                }
                _whiteFade?.Dispose();
                _edgeGlow?.Dispose();
                _cover = _coverTiny = null;
                _coverBrush = _grain = null;
                _solid = null;
                _vignette = _glow = _shadow = null;
                _whiteFade = _edgeGlow = null;
                _content = null;
            }

            private ID2D1Bitmap CreateBitmap(BitmapSource source)
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                var width = converted.PixelWidth;
                var height = converted.PixelHeight;
                var pixels = new byte[width * height * 4];
                converted.CopyPixels(pixels, width * 4, 0);
                return CreateBitmap(width, height, pixels);
            }

            private ID2D1Bitmap CreateBitmap(int width, int height, byte[] premultipliedBgra)
            {
                var handle = GCHandle.Alloc(premultipliedBgra, GCHandleType.Pinned);
                try
                {
                    return _dc.CreateBitmap(new SizeI(width, height), handle.AddrOfPinnedObject(), (uint)(width * 4),
                        new BitmapProperties1(new D2DPixelFormat(DxgiFormat.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96));
                }
                finally
                {
                    handle.Free();
                }
            }

            private static void Premultiply(byte[] bgra)
            {
                for (var i = 0; i < bgra.Length; i += 4)
                {
                    var a = bgra[i + 3];
                    bgra[i] = (byte)(bgra[i] * a / 255);
                    bgra[i + 1] = (byte)(bgra[i + 1] * a / 255);
                    bgra[i + 2] = (byte)(bgra[i + 2] * a / 255);
                }
            }

            // 중심/반경/투명도는 매 프레임 갱신합니다.
            private ID2D1RadialGradientBrush Radial(WpfColor color, params (float Position, float Alpha)[] stops)
            {
                var gradientStops = stops.Select(stop => new Vortice.Direct2D1.GradientStop(stop.Position, ToColor4(color, stop.Alpha))).ToArray();
                using var collection = _dc.CreateGradientStopCollection(gradientStops);
                return _dc.CreateRadialGradientBrush(new RadialGradientBrushProperties(Vector2.Zero, Vector2.Zero, 1, 1), collection);
            }

            private ID2D1LinearGradientBrush Linear(params (float Position, Color4 Color)[] stops)
            {
                var gradientStops = stops.Select(stop => new Vortice.Direct2D1.GradientStop(stop.Position, stop.Color)).ToArray();
                using var collection = _dc.CreateGradientStopCollection(gradientStops);
                return _dc.CreateLinearGradientBrush(new LinearGradientBrushProperties(Vector2.Zero, new Vector2(1, 0)), collection);
            }

            #endregion

            #region 그리기

            private void Blend(bool additive) => _dc.PrimitiveBlend = additive ? PrimitiveBlend.Add : PrimitiveBlend.SourceOver;

            private void FillRadial(ID2D1RadialGradientBrush brush, float cx, float cy, float rx, float ry, float opacity)
            {
                brush.Center = new Vector2(cx, cy);
                brush.RadiusX = rx;
                brush.RadiusY = ry;
                brush.Opacity = opacity;
                _dc.FillEllipse(new Ellipse(new Vector2(cx, cy), rx, ry), brush);
            }

            private void Solid(WpfColor color, float alpha)
            {
                _solid!.Color = ToColor4(color, alpha);
                _solid.Opacity = 1;
            }

            private void White(float alpha)
            {
                _solid!.Color = new Color4(1, 1, 1, alpha);
                _solid.Opacity = 1;
            }

            // 필름 그레인: 초당 24번 타일 위치를 옮기고, 그레인 1 픽셀이 화면 1 픽셀이 되도록 배율을 되돌립니다.
            private void Grain(float t, float w, float h, float opacity, float dpiScale)
            {
                var step = (int)(t * 24);
                _grain!.Opacity = opacity;
                _grain.Transform = Matrix3x2.CreateScale(1 / dpiScale) *
                                   Matrix3x2.CreateTranslation(step % 7 * 37 / dpiScale, step % 5 * 53 / dpiScale);
                _dc.FillRectangle(new RawRectF(0, 0, w, h), _grain);
            }

            private void Vignette(float w, float h, float strength)
            {
                var r = MathF.Sqrt(w * w + h * h) * 0.62f;
                FillRadial(_vignette!, w / 2, h / 2, r, r, strength);
            }

            // 커버 (둥근 사각 / 원, 중앙 크롭, 회전)
            private void CoverRect(RawRectF rc, float radius)
            {
                var size = _cover!.Size;
                float rw = rc.Right - rc.Left, rh = rc.Bottom - rc.Top, k = Math.Max(rw / size.Width, rh / size.Height);
                _coverBrush!.Transform = Matrix3x2.CreateTranslation(-size.Width / 2, -size.Height / 2) *
                                         Matrix3x2.CreateScale(k) *
                                         Matrix3x2.CreateTranslation(rc.Left + rw / 2, rc.Top + rh / 2);
                _coverBrush.Opacity = 1;
                _dc.FillRoundedRectangle(new RoundedRectangle(rc, radius, radius), _coverBrush);
            }

            private void CoverCircle(float cx, float cy, float radius, float angle)
            {
                var size = _cover!.Size;
                var k = 2 * radius / Math.Min(size.Width, size.Height);
                _coverBrush!.Transform = Matrix3x2.CreateTranslation(-size.Width / 2, -size.Height / 2) *
                                         Matrix3x2.CreateScale(k) *
                                         Matrix3x2.CreateRotation(angle) *
                                         Matrix3x2.CreateTranslation(cx, cy);
                _coverBrush.Opacity = 1;
                _dc.FillEllipse(new Ellipse(new Vector2(cx, cy), radius, radius), _coverBrush);
            }

            private IDWriteTextFormat Format(float px, DWriteFontWeight weight, DWriteTextAlignment alignment)
            {
                var key = ((int)MathF.Round(px), weight, alignment);
                if (_formats.TryGetValue(key, out var format))
                    return format;

                format = _dwriteFactory.CreateTextFormat(_fontFamily, null, weight, Vortice.DirectWrite.FontStyle.Normal, DWriteFontStretch.Normal, Math.Max(1, key.Item1), "ko-kr");
                format.TextAlignment = alignment;
                format.ParagraphAlignment = ParagraphAlignment.Center;
                format.WordWrapping = WordWrapping.NoWrap;
                using (var ellipsis = _dwriteFactory.CreateEllipsisTrimmingSign(format))
                    format.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsis);
                _formats[key] = format;
                return format;
            }

            private void Text(string text, float px, DWriteFontWeight weight, DWriteTextAlignment alignment, RawRectF rect, float alpha)
            {
                if (string.IsNullOrEmpty(text) || rect.Right - rect.Left <= 1)
                    return;

                White(alpha);
                _dc.DrawText(text, Format(px, weight, alignment), rect, _solid!);
            }

            // [1] Aura — 앨범 색 메시 그라디언트가 음악에 맞춰 호흡
            private void DrawAura(AudioVisualizerControl owner, VizResources res, Spectrum s, float w, float h, float u, float dpiScale)
            {
                var t = owner._t;
                var deep = res.Palette.Deep;
                _dc.Clear(ToColor4(deep, 1));

                // (a) 커버 컬러 필드: 4x4 커버를 화면 대각선보다 크게, 아주 천천히 회전
                var side = MathF.Sqrt(w * w + h * h) * 1.25f;
                _dc.Transform = Matrix3x2.CreateRotation(t * 2f * MathF.PI / 180f, new Vector2(w / 2, h / 2));
                _dc.DrawBitmap(_coverTiny!, new RawRectF(w / 2 - side / 2, h / 2 - side / 2, w / 2 + side / 2, h / 2 + side / 2),
                    0.9f, Vortice.Direct2D1.InterpolationMode.Cubic, null, null);
                _dc.Transform = Matrix3x2.Identity;
                Solid(deep, 0.5f); // 톤 다운 → 커버와 텍스트가 돋보이게
                _dc.FillRectangle(new RawRectF(0, 0, w, h), _solid!);

                // (b) 대역별 블롭: 저역은 크고 느리게, 고역은 작고 빠르게
                Span<float> e = stackalloc float[6] { s.Bass, s.Bass * 0.5f + s.LowMid * 0.5f, s.LowMid, s.Mid, s.Treble * 1.3f, s.Level };
                var m = Math.Min(w, h);
                for (var i = 0; i < 6; i++)
                {
                    float speed = 0.045f + 0.02f * i, phase = i * 1.7f;
                    var cx = w * (0.5f + 0.34f * MathF.Sin(t * speed * 2.1f + phase));
                    var cy = h * (0.5f + 0.30f * MathF.Cos(t * speed * 2.9f + phase * 1.3f));
                    var r = m * (0.42f + 0.30f * e[i]) * (1f + 0.06f * s.Beat);
                    FillRadial(_blobs[i]!, cx, cy, r * 1.25f, r, 0.30f + 0.50f * (float)Clamp01(e[i]));
                }
                Vignette(w, h, 0.85f);
                Grain(t, w, h, 1f, dpiScale);

                // 커버: 부드러운 그림자 + 비트 펄스
                var cw = (float)owner.ContentWidth;
                var cs = Math.Min(cw, h) * 0.46f * (1f + 0.018f * s.Beat);
                float coverX = cw / 2, coverY = h * 0.44f;
                FillRadial(_shadow!, coverX, coverY + cs * 0.10f, cs * 0.78f, cs * 0.72f, 0.9f);
                var rc = new RawRectF(coverX - cs / 2, coverY - cs / 2, coverX + cs / 2, coverY + cs / 2);
                CoverRect(rc, cs * 0.035f);
                White(0.10f);
                _dc.DrawRoundedRectangle(new RoundedRectangle(rc, cs * 0.035f, cs * 0.035f), _solid!, 1f);

                var ty = rc.Bottom + 28 * u;
                Text(owner.TitleOrFileName(), 30 * u, DWriteFontWeight.SemiBold, DWriteTextAlignment.Center,
                    new RawRectF(cw * 0.1f, ty, cw * 0.9f, ty + 40 * u), 0.96f);
                Text(owner.ArtistLine(), 18 * u, DWriteFontWeight.Normal, DWriteTextAlignment.Center,
                    new RawRectF(cw * 0.1f, ty + 40 * u, cw * 0.9f, ty + 66 * u), 0.62f);

                // 커버 아래 미니 레벨 라인
                float lineWidth = cs * (0.25f + 0.75f * s.Level), ly = ty + 84 * u;
                White(0.55f);
                _dc.DrawLine(new Vector2(coverX - lineWidth / 2, ly), new Vector2(coverX + lineWidth / 2, ly), _solid!, 2f * u, _round);
            }

            // [2] Halo — 원형 커버 + 대칭 캡슐 스펙트럼 + 비트 파티클
            private void DrawHalo(AudioVisualizerControl owner, VizResources res, Spectrum s, float w, float h, float u, float dpiScale)
            {
                var palette = res.Palette;
                _dc.Clear(ToColor4(palette.Deep, 1));
                var cw = (float)owner.ContentWidth;
                var m = Math.Min(cw, h);
                float cx = cw / 2, cy = h * 0.47f;
                var radius = m * 0.19f * (1f + 0.035f * s.Beat); // 커버 반지름

                Blend(true);
                FillRadial(_glow!, cx, cy, radius * 3.2f, radius * 3.2f, 0.12f + 0.30f * s.Bass);
                Blend(false);
                Vignette(w, h, 0.7f);

                // 가이드 링
                White(0.05f);
                _dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), radius * 2.05f, radius * 2.05f), _solid!, 1f);
                _dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), radius * 1.55f, radius * 1.55f), _solid!, 1f);

                // 대칭 캡슐 바: 위쪽이 저역, 좌우 대칭으로 아래에서 고역이 만남
                const int bars = BandCount * 2;
                float r0 = radius * 1.10f, maxLength = radius * 0.95f;
                var barWidth = Math.Max(2f, 2 * MathF.PI * r0 / bars * 0.46f);
                Blend(true);
                for (var pass = 0; pass < 2; pass++) // 0: 글로우, 1: 코어
                {
                    for (var j = 0; j < bars; j++)
                    {
                        var band = j < BandCount ? j : bars - 1 - j;
                        var v = s.Bands[band];
                        var angle = -MathF.PI / 2 + (j + 0.5f) / bars * 2 * MathF.PI;
                        var length = radius * 0.035f + v * maxLength;
                        float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
                        var bandT = band / (float)(BandCount - 1); // 0 저역 → 1 고역
                        var color = bandT < 0.5f
                            ? Mix(palette.Colors[0], palette.Colors[1], bandT * 2)
                            : Mix(palette.Colors[1], palette.Colors[2], (bandT - 0.5f) * 2);
                        if (pass == 0)
                            Solid(color, 0.10f + 0.25f * v);
                        else
                            Solid(Mix(color, WpfColors.White, 0.25 * v), 0.55f + 0.45f * v);

                        _dc.DrawLine(new Vector2(cx + ca * r0, cy + sa * r0), new Vector2(cx + ca * (r0 + length), cy + sa * (r0 + length)),
                            _solid!, pass == 0 ? barWidth * 3.2f : barWidth, _round);
                    }
                }

                // 파티클
                foreach (var p in owner._particles)
                {
                    var life = p.Life / p.MaxLife;
                    var fade = Math.Min(1f, life * 1.8f) * Math.Min(1f, (1 - life) * 8f);
                    var distance = radius * (1.12f + p.R);
                    Solid(palette.Colors[p.Color], 0.85f * fade);
                    _dc.FillEllipse(new Ellipse(new Vector2(cx + MathF.Cos(p.Angle) * distance, cy + MathF.Sin(p.Angle) * distance), p.Size * u, p.Size * u), _solid!);
                }
                Blend(false);

                // 커버 (바이닐처럼 천천히 회전) + 테두리 + 중앙 홀
                FillRadial(_shadow!, cx, cy + radius * 0.12f, radius * 1.35f, radius * 1.35f, 0.8f);
                CoverCircle(cx, cy, radius, owner._spin);
                White(0.14f);
                _dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), radius, radius), _solid!, 1.5f * u);
                Solid(palette.Deep, 0.95f);
                _dc.FillEllipse(new Ellipse(new Vector2(cx, cy), radius * 0.07f, radius * 0.07f), _solid!);
                White(0.25f);
                _dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), radius * 0.07f, radius * 0.07f), _solid!, 1f);

                Grain(owner._t, w, h, 0.8f, dpiScale);
                var ty = h - 158 * u;
                Text(owner.TitleOrFileName(), 26 * u, DWriteFontWeight.SemiBold, DWriteTextAlignment.Center,
                    new RawRectF(cw * 0.1f, ty, cw * 0.9f, ty + 36 * u), 0.95f);
                Text(owner.ArtistLine(), 16 * u, DWriteFontWeight.Normal, DWriteTextAlignment.Center,
                    new RawRectF(cw * 0.1f, ty + 34 * u, cw * 0.9f, ty + 58 * u), 0.55f);
            }

            // [3] Ribbon — 겹쳐진 발광 웨이브 (가산 혼합)
            private void DrawRibbon(AudioVisualizerControl owner, VizResources res, Spectrum s, float w, float h, float u, float dpiScale)
            {
                var t = owner._t;
                _dc.Clear(new Color4(0.02f, 0.02f, 0.03f, 1));
                var cw = (float)owner.ContentWidth;
                float x0 = cw * 0.04f, span = cw * 0.92f, y0 = h * 0.56f;

                // 하단 엣지 글로우
                Blend(true);
                _edgeGlow!.StartPoint = new Vector2(0, h);
                _edgeGlow.EndPoint = new Vector2(0, h * 0.55f);
                _edgeGlow.Opacity = 0.35f + 0.5f * s.Bass;
                _dc.FillRectangle(new RawRectF(0, h * 0.55f, w, h), _edgeGlow);

                Span<float> e = stackalloc float[4] { s.Bass, s.LowMid, s.Mid, s.Treble * 1.4f };
                ReadOnlySpan<float> periods = stackalloc float[4] { 1.15f, 1.75f, 2.45f, 3.3f };
                ReadOnlySpan<float> speeds = stackalloc float[4] { 0.9f, -1.25f, 1.6f, -2.2f };
                for (var layer = 0; layer < 4; layer++)
                {
                    var amp = h * (0.025f + 0.24f * (float)Clamp01(e[layer])) * (layer == 0 ? 1f + 0.25f * s.Beat : 1f);
                    var phase = t * speeds[layer] + layer * 1.3f;
                    for (var i = 0; i < RibbonPointCount; i++)
                    {
                        var x = i / (float)(RibbonPointCount - 1);
                        var envelope = MathF.Pow(Math.Max(0f, MathF.Sin(MathF.PI * x)), 2.2f); // 양끝 테이퍼
                        var wobble = 0.35f * MathF.Sin(t * 0.7f + layer * 2.1f + x * 3f);
                        var y = amp * envelope * MathF.Sin(2 * MathF.PI * periods[layer] * x + phase + wobble);
                        var px = x0 + x * span;
                        _up[i] = new Vector2(px, y0 - y);
                        _down[i] = new Vector2(px, y0 + y);
                    }

                    // 채움: 위 곡선 → 아래 곡선(역순)으로 닫힌 렌즈 모양
                    using var fill = _d2dFactory.CreatePathGeometry();
                    using (var sink = fill.Open())
                    {
                        sink.BeginFigure(_up[0], FigureBegin.Filled);
                        for (var i = 1; i < RibbonPointCount; i++)
                            sink.AddLine(_up[i]);
                        for (var i = RibbonPointCount - 1; i >= 0; i--)
                            sink.AddLine(_down[i]);
                        sink.EndFigure(FigureEnd.Closed);
                        sink.Close();
                    }

                    var brush = _ribbons[layer]!;
                    brush.StartPoint = new Vector2(x0, 0);
                    brush.EndPoint = new Vector2(x0 + span, 0);
                    brush.Opacity = 0.28f + 0.30f * (float)Clamp01(e[layer]);
                    _dc.FillGeometry(fill, brush);

                    // 윤곽선: 넓은 글로우 + 얇은 코어
                    using var edge = _d2dFactory.CreatePathGeometry();
                    using (var sink = edge.Open())
                    {
                        sink.BeginFigure(_up[0], FigureBegin.Hollow);
                        for (var i = 1; i < RibbonPointCount; i++)
                            sink.AddLine(_up[i]);
                        sink.EndFigure(FigureEnd.Open);
                        sink.BeginFigure(_down[0], FigureBegin.Hollow);
                        for (var i = 1; i < RibbonPointCount; i++)
                            sink.AddLine(_down[i]);
                        sink.EndFigure(FigureEnd.Open);
                        sink.Close();
                    }
                    brush.Opacity = 0.25f;
                    _dc.DrawGeometry(edge, brush, 7f * u, _round);
                    brush.Opacity = 0.9f;
                    _dc.DrawGeometry(edge, brush, 1.6f * u, _round);
                }

                // 중심 코어 라인
                _whiteFade!.StartPoint = new Vector2(x0, 0);
                _whiteFade.EndPoint = new Vector2(x0 + span, 0);
                _whiteFade.Opacity = 0.25f + 0.5f * s.Level;
                _dc.DrawLine(new Vector2(x0, y0), new Vector2(x0 + span, y0), _whiteFade, 1.5f * u);
                Blend(false);
                Grain(t, w, h, 0.7f, dpiScale);

                // 좌상단 Now Playing 카드
                float margin = 36 * u, size = 72 * u;
                var rc = new RawRectF(margin, margin, margin + size, margin + size);
                FillRadial(_shadow!, margin + size / 2, margin + size / 2 + 6 * u, size * 0.8f, size * 0.8f, 0.7f);
                CoverRect(rc, 10 * u);
                Text(owner.TitleOrFileName(), 22 * u, DWriteFontWeight.SemiBold, DWriteTextAlignment.Leading,
                    new RawRectF(rc.Right + 18 * u, margin + 6 * u, Math.Max(rc.Right + 18 * u, cw - margin), margin + 38 * u), 0.95f);
                Text(owner.ArtistLine(), 15 * u, DWriteFontWeight.Normal, DWriteTextAlignment.Leading,
                    new RawRectF(rc.Right + 18 * u, margin + 38 * u, Math.Max(rc.Right + 18 * u, cw - margin), margin + 64 * u), 0.55f);
            }

            #endregion

            private static Color4 ToColor4(WpfColor color, float alpha) =>
                new(color.R / 255f, color.G / 255f, color.B / 255f, alpha);

            private static bool FontExists(IDWriteFactory factory, string family)
            {
                using var collection = factory.GetSystemFontCollection(false);
                return collection.FindFamilyName(family, out _);
            }

            [DllImport("user32.dll")]
            private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

            public void Dispose()
            {
                ClearTransition();
                ReleaseContent();
                ReleaseTarget();
                foreach (var format in _formats.Values)
                    format.Dispose();
                _formats.Clear();
                _round?.Dispose();
                _dc?.Dispose();
                _d2dDevice?.Dispose();
                _d3d9Device?.Dispose();
                _d3d9?.Dispose();
                _dxgiDevice?.Dispose();

                // GPU 자원은 진행 중인 GPU 작업이 끝날 때까지 드라이버가 유지하지만, 완료 신호를 받을 이벤트는
                // 앱이 열어 두어야 합니다. 신호가 아직 오지 않았다면 신호를 받은 뒤에 닫도록 넘깁니다.
                if (_gpuWaitPending && !_gpuDone.WaitOne(0))
                {
                    var done = _gpuDone;
                    Interlocked.Increment(ref s_pendingGpuSignals);
                    ThreadPool.RegisterWaitForSingleObject(done, (_, _) =>
                    {
                        try
                        {
                            done.Dispose();
                        }
                        finally
                        {
                            Interlocked.Decrement(ref s_pendingGpuSignals);
                        }
                    }, null, Timeout.Infinite, executeOnlyOnce: true);
                }
                else
                {
                    _gpuDone.Dispose();
                }
                _d3d11?.Dispose();
                _dwriteFactory?.Dispose();
                _d2dFactory?.Dispose();
            }
        }
    }
}
