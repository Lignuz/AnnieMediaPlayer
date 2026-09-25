using System.Diagnostics;
using NAudio.Wave;

namespace PlaybackStallBench;

/// <summary>
/// Observes the default output device after mixing. The generated benchmark media
/// must contain continuous, non-periodic audio and other applications must be quiet.
/// A rendering event before the device write cannot establish audible continuity.
/// </summary>
internal sealed class LoopbackMonitor : IDisposable
{
    private const int FrameMilliseconds = 10;
    private const int SilenceThreshold = 64;
    private const ulong HashOffset = 14695981039346656037UL;
    private const ulong HashPrime = 1099511628211UL;

    private readonly object gate = new();
    private readonly WasapiLoopbackCapture capture = new();
    private readonly byte[] frame;
    private readonly ulong[] hashes = new ulong[100];
    private readonly bool[] valid = new bool[100];
    private readonly int frameBytes;
    private int frameLength;
    private int frameIndex;
    private int silentRun;
    private int repeatRun100;
    private int repeatRun200;
    private int maxSilentFrames;
    private int maxRepeatFrames;
    private long capturedFrames;
    private long lastCaptureTimestamp;
    private double maxCaptureGapMs;
    private Exception captureError;
    private bool measuring;

    public LoopbackMonitor()
    {
        capture.WaveFormat = new WaveFormat(48000, 16, 2);
        frameBytes = capture.WaveFormat.AverageBytesPerSecond * FrameMilliseconds / 1000;
        frameBytes -= frameBytes % capture.WaveFormat.BlockAlign;
        frame = new byte[frameBytes];
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += (_, e) => captureError = e.Exception;
        capture.StartRecording();
    }

    public void BeginWindow()
    {
        lock (gate)
        {
            frameLength = 0;
            frameIndex = 0;
            silentRun = 0;
            repeatRun100 = 0;
            repeatRun200 = 0;
            maxSilentFrames = 0;
            maxRepeatFrames = 0;
            capturedFrames = 0;
            lastCaptureTimestamp = 0;
            maxCaptureGapMs = 0;
            Array.Clear(valid);
            measuring = true;
        }
    }

    public LoopbackResult EndWindow()
    {
        lock (gate)
        {
            measuring = false;
            if (captureError != null)
                throw new InvalidOperationException("WASAPI loopback capture stopped.", captureError);

            return new LoopbackResult(
                maxSilentFrames * FrameMilliseconds,
                maxRepeatFrames * FrameMilliseconds,
                capturedFrames * FrameMilliseconds,
                (int)Math.Ceiling(maxCaptureGapMs));
        }
    }

    public void Dispose()
    {
        capture.StopRecording();
        capture.Dispose();
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        lock (gate)
        {
            if (!measuring)
                return;

            var now = Stopwatch.GetTimestamp();
            if (lastCaptureTimestamp != 0)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(lastCaptureTimestamp, now).TotalMilliseconds;
                var suppliedMs = 1000d * e.BytesRecorded / capture.WaveFormat.AverageBytesPerSecond;
                maxCaptureGapMs = Math.Max(maxCaptureGapMs, elapsedMs - suppliedMs);
            }

            lastCaptureTimestamp = now;

            for (var offset = 0; offset < e.BytesRecorded;)
            {
                var copyLength = Math.Min(frameBytes - frameLength, e.BytesRecorded - offset);
                Buffer.BlockCopy(e.Buffer, offset, frame, frameLength, copyLength);
                frameLength += copyLength;
                offset += copyLength;
                if (frameLength != frameBytes)
                    continue;

                AnalyzeFrame();
                frameLength = 0;
            }
        }
    }

    private void AnalyzeFrame()
    {
        var peak = 0;
        var hash = HashOffset;
        for (var i = 0; i < frameBytes; i += 2)
        {
            var sample = (short)(frame[i] | (frame[i + 1] << 8));
            peak = Math.Max(peak, Math.Abs((int)sample));
            hash = (hash ^ frame[i]) * HashPrime;
            hash = (hash ^ frame[i + 1]) * HashPrime;
        }

        var silent = peak <= SilenceThreshold;
        silentRun = silent ? silentRun + 1 : 0;
        maxSilentFrames = Math.Max(maxSilentFrames, silentRun);

        // A 200 ms DirectSound buffer repeats when the writer misses an entire
        // cycle. Continuous random-noise input makes matching 30 ms fingerprints
        // at 100/200 ms offsets extremely unlikely during normal playback.
        var current = frameIndex % hashes.Length;
        var match100 = !silent && capturedFrames >= 10 && valid[(frameIndex + 90) % 100] &&
            hash == hashes[(frameIndex + 90) % 100];
        var match200 = !silent && capturedFrames >= 20 && valid[(frameIndex + 80) % 100] &&
            hash == hashes[(frameIndex + 80) % 100];
        repeatRun100 = match100 ? repeatRun100 + 1 : 0;
        repeatRun200 = match200 ? repeatRun200 + 1 : 0;
        if (repeatRun100 >= 3 || repeatRun200 >= 3)
            maxRepeatFrames = Math.Max(maxRepeatFrames, Math.Max(repeatRun100, repeatRun200));

        hashes[current] = hash;
        valid[current] = true;
        frameIndex++;
        capturedFrames++;
    }
}

internal sealed record LoopbackResult(int MaxSilenceMs, int MaxRepeatMs, long CapturedMs, int MaxCaptureGapMs);
