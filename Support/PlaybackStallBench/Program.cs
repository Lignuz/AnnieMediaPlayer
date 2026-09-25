using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using FFmpeg.AutoGen;
using Unosquare.FFME;
using Unosquare.FFME.Common;

namespace PlaybackStallBench
{
    /// <summary>
    /// 다른 프로그램이 CPU를 사용할 때 재생이 멈추는지 측정합니다.
    ///
    /// 사용법:
    ///   PlaybackStallBench.exe bench --media 파일 --variant 이름 --csv 결과.csv
    ///       [--parallel-rendering true|false] [--audio-cache 블록수] [--process-priority Normal|AboveNormal]
    ///       [--repeats 횟수] [--ui-alloc-kb 프레임당KB]
    ///       [--scenario idle|bg-load|fg-load|fg-spikes] [--load-ms 지속ms] [--load-duty 0..100]
    ///   PlaybackStallBench.exe spin 지속ms 우선순위 [부하ms 휴식ms]   (부하 프로세스, 벤치가 직접 실행)
    /// </summary>
    internal static class Program
    {
        private const string FFmpegDirectory = @"..\..\..\..\ffmpeg";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "spin")
                return Spinner.Run(args);

            var options = BenchOptions.Parse(args);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var bench = new Bench(options);
            var exitCode = 0;

            var window = new Window
            {
                Width = 360,
                Height = 200,
                Left = 0,
                Top = 0,
                ShowActivated = false,
                Title = $"Playback stall bench - {options.Variant}",
                Content = bench.Media,
            };

            window.Loaded += async (_, _) =>
            {
                try
                {
                    await bench.RunAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAILED: {ex}");
                    exitCode = 1;
                }
                finally
                {
                    bench.Media.Dispose();
                    app.Shutdown(exitCode);
                }
            };

            window.Show();
            return app.Run();
        }

        internal static string ResolveFFmpegDirectory()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("BENCH_FFMPEG_DIR");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return fromEnvironment;

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, FFmpegDirectory));
        }
    }

    internal sealed class BenchOptions
    {
        public string Media { get; private set; } = string.Empty;
        public string Variant { get; private set; } = "current";
        public string Csv { get; private set; } = "results.csv";
        public bool ParallelRendering { get; private set; }
        public int AudioCache { get; private set; }
        public ProcessPriorityClass ProcessPriority { get; private set; } = ProcessPriorityClass.Normal;
        public int Repeats { get; private set; } = 1;
        public int UiAllocKb { get; private set; } = 100;
        public string ScenarioName { get; private set; } = string.Empty;
        public int LoadMs { get; private set; }
        public int LoadDuty { get; private set; } = 100;
        public int TargetCpu { get; private set; }
        public bool ControlsOnly { get; private set; }
        public int ControlRepeats { get; private set; } = 3;
        public bool SpeedSmoke { get; private set; }

        public static BenchOptions Parse(string[] args)
        {
            var options = new BenchOptions();
            for (var i = 1; i + 1 < args.Length; i += 2)
            {
                var value = args[i + 1];
                switch (args[i])
                {
                    case "--media": options.Media = value; break;
                    case "--variant": options.Variant = value; break;
                    case "--csv": options.Csv = value; break;
                    case "--parallel-rendering": options.ParallelRendering = bool.Parse(value); break;
                    case "--audio-cache": options.AudioCache = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--process-priority": options.ProcessPriority = Enum.Parse<ProcessPriorityClass>(value); break;
                    case "--repeats": options.Repeats = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--ui-alloc-kb": options.UiAllocKb = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--scenario": options.ScenarioName = value; break;
                    case "--load-ms": options.LoadMs = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--load-duty": options.LoadDuty = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--target-cpu": options.TargetCpu = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--controls-only": options.ControlsOnly = bool.Parse(value); break;
                    case "--control-repeats": options.ControlRepeats = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--speed-smoke": options.SpeedSmoke = bool.Parse(value); break;
                    default: throw new ArgumentException($"알 수 없는 옵션: {args[i]}");
                }
            }

            if (!File.Exists(options.Media))
                throw new FileNotFoundException("미디어 파일이 없습니다.", options.Media);
            if (options.LoadDuty is < 0 or > 100 || options.LoadMs < 0 || options.TargetCpu is < 0 or > 100 || options.ControlRepeats < 1)
                throw new ArgumentOutOfRangeException(nameof(args), "부하 비율은 0~100, 지속 시간은 0 이상이어야 합니다.");

            return options;
        }
    }

    internal sealed record Scenario(string Name, string Description, int LoadMs, string Priority, int OnMs, int OffMs);

    internal sealed class Bench
    {
        // 부하가 끝난 뒤 스스로 재생이 이어지는지 지켜보는 시간
        private const int ObserveMs = 6000;

        // 관찰 시간 동안 이 이상 소리가 나오지 않으면 "멈춤"(원래 문제)으로 판정합니다.
        private const double StuckAudioSeconds = 1.0;

        private static readonly Scenario[] Scenarios =
        {
            new("idle", "부하 없음", 5000, "", 0, 0),
            new("bg-load", "백그라운드 부하: 보통 우선순위, 모든 코어 100%", 8000, "Normal", 0, 0),
            new("fg-load", "포그라운드 부하: 실제 활성 창, 모든 코어 100%", 8000, "Foreground", 0, 0),
            new("fg-spikes", "포그라운드 스파이크: 300ms 부하 / 1200ms 휴식", 12000, "Foreground", 300, 1200),
        };

        private readonly BenchOptions Options;
        private readonly ConcurrentQueue<(long Timestamp, double DurationMs)> AudioEvents = new();
        private readonly ConcurrentQueue<(long Timestamp, string Message)> LogEvents = new();

        public Bench(BenchOptions options)
        {
            Options = options;
            Library.FFmpegDirectory = Program.ResolveFFmpegDirectory();
            Library.EnableWpfMultiThreadedVideo = false;

            Media = new MediaElement
            {
                LoadedBehavior = MediaPlaybackState.Manual,
                UnloadedBehavior = MediaPlaybackState.Manual,
                Volume = 0.1,
            };

            // This event runs before the device write. Device output is measured
            // independently by WASAPI loopback in LoopbackMonitor.
            Media.RenderingAudio += (_, e) =>
                AudioEvents.Enqueue((Stopwatch.GetTimestamp(), e.Duration.TotalMilliseconds));

            Media.MessageLogged += (_, e) =>
            {
                if (e.MessageType is MediaLogMessageType.Warning or MediaLogMessageType.Error || e.Message.StartsWith("SYNC-BUFFER", StringComparison.Ordinal))
                    LogEvents.Enqueue((ToTimestamp(e.TimestampUtc), e.Message));
                if (e.Message.StartsWith("DirectSound buffer clear was delayed", StringComparison.Ordinal))
                    Console.WriteLine($"DIAGNOSTIC: {e.Message}");
            };

            Media.MediaOpening += (_, e) =>
            {
                e.Options.UseParallelRendering = Options.ParallelRendering;
                var hasVideo = e.Info.Streams.Values.Any(s =>
                    s.CodecType == AVMediaType.AVMEDIA_TYPE_VIDEO &&
                    (s.Disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0);
                if (!hasVideo && Options.AudioCache > 0)
                    e.Options.AudioBlockCache = Options.AudioCache;
            };
        }

        public MediaElement Media { get; }

        public async Task RunAsync()
        {
            using (var process = Process.GetCurrentProcess())
                process.PriorityClass = Options.ProcessPriority;

            using var loopback = new LoopbackMonitor();

            StartUiAllocationLoad();

            if (!await Media.Open(new Uri(Path.GetFullPath(Options.Media))) || !await Media.Play())
                throw new InvalidOperationException("미디어를 열거나 재생하지 못했습니다.");

            await Task.Delay(3000);

            if (Options.SpeedSmoke)
            {
                foreach (var speed in new[] { 0.5d, 2d })
                {
                    Media.SpeedRatio = speed;
                    await Task.Delay(500);
                    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
                    loopback.BeginWindow();
                    await Task.Delay(3000);
                    var output = loopback.EndWindow();
                    var allocatedMb = (GC.GetTotalAllocatedBytes(true) - allocatedBefore) / (1024d * 1024d);
                    Console.WriteLine($"SPEED {speed:0.0}x: device silence={output.MaxSilenceMs} ms, " +
                        $"repeat={output.MaxRepeatMs} ms, process allocation={allocatedMb:0.0} MiB / 3 s");
                }

                Media.SpeedRatio = 1d;
                await Task.Delay(500);
            }

            var writeHeader = !File.Exists(Options.Csv);
            using var csv = new StreamWriter(Options.Csv, append: true, new UTF8Encoding(true));
            if (writeHeader)
                csv.WriteLine("variant,media,scenario,repeat,system_cpu_percent,audio_ratio,clock_ratio,max_audio_gap_ms,device_silence_ms,device_repeat_ms,device_capture_gap_ms,device_captured_ms,output_late,audio_issues,sync_buffering,stuck,recovered_by_pause_play,gc_pause_ms");

            var scenarios = Options.ControlsOnly
                ? Array.Empty<Scenario>()
                : string.IsNullOrWhiteSpace(Options.ScenarioName)
                ? Scenarios
                : Scenarios.Where(s => s.Name == Options.ScenarioName).ToArray();
            if (scenarios.Length == 0 && !Options.ControlsOnly)
                throw new ArgumentException($"알 수 없는 시나리오: {Options.ScenarioName}");

            for (var repeat = 1; repeat <= Options.Repeats; repeat++)
            {
                foreach (var baseScenario in scenarios)
                {
                    if (Options.LoadMs > 120000 && (repeat > 1 || !ReferenceEquals(baseScenario, scenarios[0])))
                    {
                        await Media.Seek(TimeSpan.Zero);
                        await Task.Delay(1500);
                    }

                    var scenario = Options.LoadMs > 0
                        ? baseScenario with
                        {
                            LoadMs = Options.LoadMs,
                            OnMs = Options.LoadDuty,
                            OffMs = 100 - Options.LoadDuty,
                        }
                        : baseScenario;
                    var result = await RunScenarioAsync(scenario, loopback);
                    var line = string.Join(",",
                        Options.Variant,
                        Path.GetFileName(Options.Media),
                        scenario.Name,
                        repeat,
                        result.SystemCpuPercent.ToString("0.0", CultureInfo.InvariantCulture),
                        result.AudioRatio.ToString("0.00", CultureInfo.InvariantCulture),
                        result.ClockRatio.ToString("0.00", CultureInfo.InvariantCulture),
                        result.MaxAudioGapMs.ToString("0", CultureInfo.InvariantCulture),
                        result.Loopback.MaxSilenceMs,
                        result.Loopback.MaxRepeatMs,
                        result.Loopback.MaxCaptureGapMs,
                        result.Loopback.CapturedMs,
                        result.OutputLate,
                        result.AudioIssues,
                        result.SyncBuffering,
                        result.Stuck ? 1 : 0,
                        result.RecoveredByPausePlay ? 1 : 0,
                        result.GcPauseMs.ToString("0", CultureInfo.InvariantCulture));
                    csv.WriteLine(line);
                    csv.Flush();
                    Console.WriteLine(line);
                }
            }

            var controlSamples = new List<(double PauseMs, double PlayMs, double SeekMs)>();
            for (var trial = 1; trial <= Options.ControlRepeats; trial++)
            {
                var pauseStart = Stopwatch.GetTimestamp();
                await Media.Pause();
                var pauseMs = Stopwatch.GetElapsedTime(pauseStart).TotalMilliseconds;
                await Task.Delay(250);

                var playStart = Stopwatch.GetTimestamp();
                await Media.Play();
                var playMs = Stopwatch.GetElapsedTime(playStart).TotalMilliseconds;
                await Task.Delay(250);

                var seekStart = Stopwatch.GetTimestamp();
                await Media.Seek(TimeSpan.FromSeconds(trial % 2 == 0 ? 20 : 10));
                var seekMs = Stopwatch.GetElapsedTime(seekStart).TotalMilliseconds;
                controlSamples.Add((pauseMs, playMs, seekMs));
                await Task.Delay(250);
            }

            var closeStart = Stopwatch.GetTimestamp();
            await Media.Close();
            var closeMs = Stopwatch.GetElapsedTime(closeStart).TotalMilliseconds;

            var controlsCsv = Options.Csv + ".controls.csv";
            var controlsHeader = !File.Exists(controlsCsv);
            using var controls = new StreamWriter(controlsCsv, append: true, new UTF8Encoding(true));
            if (controlsHeader)
                controls.WriteLine("variant,media,trial,pause_command_ms,play_command_ms,seek_command_ms,close_command_ms");
            for (var i = 0; i < controlSamples.Count; i++)
            {
                var sample = controlSamples[i];
                controls.WriteLine(string.Join(",",
                    Options.Variant, Path.GetFileName(Options.Media), i + 1,
                    sample.PauseMs.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.PlayMs.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.SeekMs.ToString("0.0", CultureInfo.InvariantCulture),
                    i == controlSamples.Count - 1 ? closeMs.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty));
                Console.WriteLine($"CONTROL {Options.Variant} #{i + 1}: pause={sample.PauseMs:0.0} ms, play={sample.PlayMs:0.0} ms, seek={sample.SeekMs:0.0} ms");
            }

            Console.WriteLine($"CONTROL {Options.Variant}: close={closeMs:0.0} ms");
        }

        private async Task<ScenarioResult> RunScenarioAsync(Scenario scenario, LoopbackMonitor loopback)
        {
            var cpuStart = ReadSystemCpuTimes();
            var gcPauseStart = GC.GetTotalPauseDuration();
            var clockStart = Media.ActualPosition ?? TimeSpan.Zero;
            loopback.BeginWindow();
            var start = Stopwatch.GetTimestamp();

            if (scenario.LoadMs > 0 && scenario.Priority.Length > 0)
                await RunSpinnerAsync(scenario);
            else
                await Task.Delay(scenario.LoadMs);

            var loadEnd = Stopwatch.GetTimestamp();
            var cpuEnd = ReadSystemCpuTimes();
            var clockEnd = Media.ActualPosition ?? TimeSpan.Zero;
            var loadSeconds = Stopwatch.GetElapsedTime(start, loadEnd).TotalSeconds;

            // 부하가 끝난 뒤에도 소리가 이어지지 않으면 원래 문제(일시정지·재생 전까지 멈춤)로 봅니다.
            await Task.Delay(ObserveMs);
            var observeEnd = Stopwatch.GetTimestamp();
            var deviceOutput = loopback.EndWindow();
            var audioAfterLoad = AudioSecondsBetween(loadEnd, observeEnd);
            var stuck = audioAfterLoad < StuckAudioSeconds;

            var recovered = false;
            if (stuck)
            {
                // 사용자가 하던 대로 일시정지 후 다시 재생해 복구합니다.
                await Media.Pause();
                await Task.Delay(300);
                await Media.Play();
                var recoverStart = Stopwatch.GetTimestamp();
                await Task.Delay(3000);
                recovered = AudioSecondsBetween(recoverStart, Stopwatch.GetTimestamp()) >= StuckAudioSeconds;
            }

            var logs = LogEvents.Where(e => e.Timestamp >= start && e.Timestamp <= observeEnd).ToList();
            foreach (var entry in logs.Where(e =>
                e.Message.StartsWith("AUDIO OUTPUT LATE", StringComparison.Ordinal) ||
                e.Message.Contains("Audio buffer", StringComparison.Ordinal)))
            {
                Console.WriteLine($"DIAGNOSTIC {scenario.Name}: {entry.Message}");
            }
            var result = new ScenarioResult(
                SystemCpuPercent: CalculateCpuPercent(cpuStart, cpuEnd),
                AudioRatio: AudioSecondsBetween(start, loadEnd) / loadSeconds,
                ClockRatio: (clockEnd - clockStart).TotalSeconds / loadSeconds,
                MaxAudioGapMs: MaxAudioGapMs(start, observeEnd),
                Loopback: deviceOutput,
                OutputLate: logs.Count(e => e.Message.StartsWith("AUDIO OUTPUT LATE", StringComparison.Ordinal)),
                AudioIssues: logs.Count(e => e.Message.Contains("Audio buffer", StringComparison.Ordinal)),
                SyncBuffering: logs.Count(e => e.Message.StartsWith("SYNC-BUFFER: Entered", StringComparison.Ordinal)),
                Stuck: stuck,
                RecoveredByPausePlay: recovered,
                GcPauseMs: (GC.GetTotalPauseDuration() - gcPauseStart).TotalMilliseconds);

            // 다음 시나리오 전에 이전 이벤트를 정리하고 재생을 안정시킵니다.
            while (AudioEvents.TryPeek(out var first) && first.Timestamp < observeEnd)
                AudioEvents.TryDequeue(out _);
            await Task.Delay(1500);
            return result;
        }

        private async Task RunSpinnerAsync(Scenario scenario)
        {
            var arguments = $"spin {scenario.LoadMs} {scenario.Priority} {scenario.OnMs} {scenario.OffMs} {Options.TargetCpu}";
            using var spinner = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

            await spinner.WaitForExitAsync();
        }

        private double AudioSecondsBetween(long from, long to) =>
            AudioEvents.Where(e => e.Timestamp >= from && e.Timestamp <= to).Sum(e => e.DurationMs) / 1000d;

        private double MaxAudioGapMs(long from, long to)
        {
            var previous = from;
            var maxGap = 0d;
            foreach (var (timestamp, _) in AudioEvents.Where(e => e.Timestamp >= from && e.Timestamp <= to))
            {
                maxGap = Math.Max(maxGap, Stopwatch.GetElapsedTime(previous, timestamp).TotalMilliseconds);
                previous = timestamp;
            }

            return Math.Max(maxGap, Stopwatch.GetElapsedTime(previous, to).TotalMilliseconds);
        }

        // 실제 플레이어의 UI(시각화 등)처럼 매 프레임 메모리를 할당합니다.
        private void StartUiAllocationLoad()
        {
            if (Options.UiAllocKb <= 0)
                return;

            var keep = new List<byte[]>();
            System.Windows.Media.CompositionTarget.Rendering += (_, _) =>
            {
                for (var i = 0; i < Options.UiAllocKb; i++)
                    keep.Add(new byte[1024]);
                if (keep.Count > Options.UiAllocKb * 30)
                    keep.RemoveRange(0, Options.UiAllocKb * 10);
            };
        }

        private static long ToTimestamp(DateTime utc)
        {
            var age = DateTime.UtcNow - utc;
            return Stopwatch.GetTimestamp() - (long)(age.TotalSeconds * Stopwatch.Frequency);
        }

        internal static (long Idle, long Total) ReadSystemCpuTimes()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return default;
            return (AsLong(idle), AsLong(kernel) + AsLong(user));
        }

        private static long AsLong(FILETIME value) =>
            ((long)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

        internal static double CalculateCpuPercent((long Idle, long Total) start, (long Idle, long Total) end)
        {
            var elapsed = end.Total - start.Total;
            return elapsed > 0 ? 100d * (1d - (double)(end.Idle - start.Idle) / elapsed) : double.NaN;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        private sealed record ScenarioResult(
            double SystemCpuPercent,
            double AudioRatio,
            double ClockRatio,
            double MaxAudioGapMs,
            LoopbackResult Loopback,
            int OutputLate,
            int AudioIssues,
            int SyncBuffering,
            bool Stuck,
            bool RecoveredByPausePlay,
            double GcPauseMs);
    }

    /// <summary>
    /// 별도 프로세스에서 모든 코어에 CPU 부하를 줍니다.
    /// "Highest"는 포그라운드 앱의 스레드(Windows가 우선순위를 2단계 올림)와 같은 수준입니다.
    /// </summary>
    internal static class Spinner
    {
        public static int Run(string[] args)
        {
            var durationMs = int.Parse(args[1], CultureInfo.InvariantCulture);
            var foreground = args[2] == "Foreground";
            var priority = foreground ? ThreadPriority.Normal : Enum.Parse<ThreadPriority>(args[2]);
            var onMs = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 0;
            var offMs = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 0;
            var targetCpu = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 0;
            var periodMs = onMs + offMs > 0 ? onMs + offMs : 100;
            var duty = new[] { onMs <= 0 ? periodMs : onMs };
            var clock = Stopwatch.StartNew();

            Thread loadWindowThread = null;
            System.Windows.Threading.Dispatcher loadDispatcher = null;
            if (foreground)
            {
                using var ready = new ManualResetEventSlim();
                loadWindowThread = new Thread(() =>
                {
                    var window = new Window
                    {
                        Title = "Playback CPU load (foreground)",
                        Width = 320,
                        Height = 160,
                        Left = 400,
                        Top = 0,
                    };
                    window.Show();
                    window.Activate();
                    loadDispatcher = window.Dispatcher;
                    ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                }) { IsBackground = true };
                loadWindowThread.SetApartmentState(ApartmentState.STA);
                loadWindowThread.Start();
                ready.Wait();
                Thread.Sleep(500);
            }

            clock.Restart();

            var threads = Enumerable.Range(0, Environment.ProcessorCount).Select(_ => new Thread(() =>
            {
                var value = 1.0;
                while (clock.ElapsedMilliseconds < durationMs)
                {
                    var active = clock.ElapsedMilliseconds % periodMs < Volatile.Read(ref duty[0]);
                    if (active)
                        value = Math.Sqrt(value + 1);
                    else
                        Thread.Sleep(1);
                }

                GC.KeepAlive(value);
            })
            {
                Priority = priority,
                IsBackground = true,
            }).ToList();

            threads.ForEach(t => t.Start());
            Thread feedbackThread = null;
            if (targetCpu > 0)
            {
                feedbackThread = new Thread(() =>
                {
                    var previous = Bench.ReadSystemCpuTimes();
                    while (clock.ElapsedMilliseconds < durationMs)
                    {
                        Thread.Sleep(2000);
                        var current = Bench.ReadSystemCpuTimes();
                        var actualCpu = Bench.CalculateCpuPercent(previous, current);
                        previous = current;
                        if (double.IsNaN(actualCpu))
                            continue;

                        var adjustment = (int)Math.Round((targetCpu - actualCpu) * 0.7 * periodMs / 100d);
                        var next = Math.Clamp(Volatile.Read(ref duty[0]) + adjustment, 0, periodMs);
                        Volatile.Write(ref duty[0], next);
                    }
                }) { IsBackground = true };
                feedbackThread.Start();
            }

            threads.ForEach(t => t.Join());
            feedbackThread?.Join();
            if (loadDispatcher != null)
            {
                loadDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
                loadWindowThread.Join();
            }
            return 0;
        }
    }
}
