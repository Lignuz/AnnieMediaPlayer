using FFmpeg.AutoGen;
using NAudio.Wave;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    public unsafe class FFmpegContext
    {
        public AVFormatContext* FormatContext;
        public AVCodecContext* CodecContext; // 비디오 코덱 컨텍스트
        public AVCodecContext* AudioCodecContext; // 오디오 코덱 컨텍스트
        public int VideoStreamIndex;
        public int AudioStreamIndex; // 오디오 스트림 인덱스

        public double Fps
        {
            get
            {
                var stream = FormatContext->streams[VideoStreamIndex];
                AVRational fr = stream->r_frame_rate.num != 0
                    ? stream->r_frame_rate
                    : stream->avg_frame_rate;
                return fr.den != 0 ? fr.num / (double)fr.den : 0;
            }
        }
    }

    public static class FFmpegLoader
    {
        public static void RegisterFFmpeg()
        {
            ffmpeg.RootPath = "ffmpeg";
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();
        }
    }

    public static class FFmpegHelper
    {
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        private static WaveOutEvent? _waveOut;
        private static BufferedWaveProvider? _waveProvider;
        private static int _audioStreamIndex = -1;
        private static AVSampleFormat _audioSampleFormat;
        private static int _audioChannels;
        private static int _audioSampleRate;

        public static unsafe void OpenVideo(string filePath, Action<BitmapSource, int, TimeSpan, TimeSpan, FFmpegContext> onFrameDecoded, CancellationToken token, Action? onCompleted = null)
        {
            var context = OpenVideoFile(filePath);
            var swsCtx = CreateSwsContext(context.CodecContext);
            AVFrame* pFrame = ffmpeg.av_frame_alloc();
            AVPacket* pPacket = ffmpeg.av_packet_alloc();
            byte* buffer;
            AVFrame* pFrameRGB = AllocateFrameWithBuffer(context.CodecContext, out buffer);

            double videoTimeBase = ffmpeg.av_q2d(context.FormatContext->streams[context.VideoStreamIndex]->time_base);
            double audioTimeBase = (context.AudioStreamIndex != -1) ? ffmpeg.av_q2d(context.FormatContext->streams[context.AudioStreamIndex]->time_base) : 0;
            long videoStartPts = context.FormatContext->streams[context.VideoStreamIndex]->start_time;
            long audioStartPts = (context.AudioStreamIndex != -1) ? context.FormatContext->streams[context.AudioStreamIndex]->start_time : 0;

            // 오디오 스트림 열기 및 재생 초기화
            if (OpenAudioStream(context))
            {
                InitializeAudioPlayback();
            }

            bool eof = false;
            while (!token.IsCancellationRequested)
            {
                // 탐색할 때 잠깐 멈춤
                while (VideoPlayerController.PauseForSeeking)
                {
                    Thread.Sleep(10);
                    if (token.IsCancellationRequested) return;
                }

                // 일시 정지 상태 확인 및 대기
                while (VideoPlayerController.IsPaused && !token.IsCancellationRequested)
                {
                    if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
                    {
                        _waveOut.Pause();
                    }
                    Thread.Sleep(100);
                }
                if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Paused && !VideoPlayerController.IsPaused)
                {
                    _waveOut.Play();
                }

                if (token.IsCancellationRequested) return;

                try
                {
                    if (!eof && ffmpeg.av_read_frame(context.FormatContext, pPacket) >= 0)
                    {
                        if (pPacket->stream_index == context.VideoStreamIndex)
                        {
                            ffmpeg.avcodec_send_packet(context.CodecContext, pPacket);
                        }
                        else if (pPacket->stream_index == _audioStreamIndex && VideoPlayerController.IsNormalSpeed) // 정상 속도일때만 오디오 처리
                        {
                            // 오디오 디코딩 및 재생 처리
                            DecodeAndPlayAudio(context, pPacket);
                        }
                        ffmpeg.av_packet_unref(pPacket);
                    }
                    else
                    {
                        ffmpeg.avcodec_send_packet(context.CodecContext, null);
                        eof = true;
                    }

                    while (ffmpeg.avcodec_receive_frame(context.CodecContext, pFrame) == 0)
                    {
                        double framePtsInSeconds = (pFrame->pts - videoStartPts) * videoTimeBase;
                        TimeSpan frameDisplayTime = TimeSpan.FromSeconds(framePtsInSeconds);

                        TimeSpan currentTime = frameDisplayTime;
                        if (VideoPlayerController.PauseForSeeking && VideoPlayerController.CurrentVideoTime != currentTime)
                        {
                            ffmpeg.av_frame_unref(pFrame);
                            continue;
                        } 

                        TimeSpan totalTime = TimeSpan.FromSeconds(context.FormatContext->duration / (double)ffmpeg.AV_TIME_BASE);
                        int frameNumber = GetFrameNumber(context, pFrame);

                        ffmpeg.sws_scale(swsCtx, pFrame->data, pFrame->linesize, 0, context.CodecContext->height, pFrameRGB->data, pFrameRGB->linesize);
                        var bitmapSource = ConvertFrameToBitmapSource(pFrameRGB, context.CodecContext);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            onFrameDecoded(bitmapSource, frameNumber, currentTime, totalTime, context);
                        });

                        if (VideoPlayerController.IsNormalSpeed) // 정상속도
                        {
                            double targetTimeInSeconds = pFrame->pts * videoTimeBase;
                            TimeSpan targetTime = TimeSpan.FromSeconds(targetTimeInSeconds);
                            DateTime playbackStartTime = VideoPlayerController.PlaybackStartTime;
                            TimeSpan elapsedTime = DateTime.UtcNow - playbackStartTime;
                            TimeSpan requiredDelay = targetTime - elapsedTime;

                            if (requiredDelay.TotalMilliseconds > 0)
                            {
                                // 정확한 딜레이를 위한 대기
                                Stopwatch sw = Stopwatch.StartNew();
                                while (sw.Elapsed.TotalMilliseconds < requiredDelay.TotalMilliseconds)
                                {
                                    if (token.IsCancellationRequested) return;
                                    Thread.SpinWait(10);
                                    
                                    targetTimeInSeconds = pFrame->pts * videoTimeBase;
                                    targetTime = TimeSpan.FromSeconds(targetTimeInSeconds);
                                    playbackStartTime = VideoPlayerController.PlaybackStartTime;
                                    elapsedTime = DateTime.UtcNow - playbackStartTime;
                                    requiredDelay = targetTime - elapsedTime;

                                    if (VideoPlayerController.WaitForPauseForSeeking)
                                    {
                                        VideoPlayerController.WaitForPauseForSeeking = false;
                                        requiredDelay = TimeSpan.Zero;
                                    }
                                }
                            }
                        }
                        else // 다른 속도
                        {
                            TimeSpan delay = VideoPlayerController.PlaybackSpeeds[VideoPlayerController.SpeedIndex];
                            Stopwatch sw = Stopwatch.StartNew();
                            while (sw.Elapsed < delay)
                            {
                                if (token.IsCancellationRequested)
                                    return;
                                Thread.SpinWait(10); // 정밀한 대기를 위해 SpinWait 사용
                                delay = VideoPlayerController.PlaybackSpeeds[VideoPlayerController.SpeedIndex];

                                if (VideoPlayerController.WaitForPauseForSeeking)
                                {
                                    VideoPlayerController.WaitForPauseForSeeking = false;
                                    delay = TimeSpan.Zero;
                                }
                            }
                        }

                        ffmpeg.av_frame_unref(pFrame);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during video decoding: {ex.Message}");
                    // To prevent the loop from getting stuck on a problematic packet,
                    // we'll break the inner while loop and try to read the next packet.
                    break;
                }

                if (eof)
                {
                    onCompleted?.Invoke();
                    break;
                }
            }

            ffmpeg.av_free(buffer);
            ffmpeg.sws_freeContext(swsCtx);
            ffmpeg.av_frame_free(&pFrame);
            ffmpeg.av_frame_free(&pFrameRGB);
            ffmpeg.av_packet_free(&pPacket);

            AVCodecContext* codecCtx = context.CodecContext;
            AVFormatContext* formatCtx = context.FormatContext;
            ffmpeg.avcodec_free_context(&codecCtx);
            ffmpeg.avformat_close_input(&formatCtx);

            StopAudioPlayback(); // 비디오 재생 종료 시 오디오 정리
        }

        public static unsafe int GetFrameNumber(FFmpegContext context, AVFrame* frame)
        {
            AVStream* stream = context.FormatContext->streams[context.VideoStreamIndex];
            long startPts = stream->start_time;
            AVRational timeBase = stream->time_base;
            double timeInSec = (frame->pts - startPts) * ffmpeg.av_q2d(timeBase);
            int frameNumber = (int)Math.Round(timeInSec * context.Fps);
            return frameNumber;
        }

        public static unsafe BitmapSource? SeekAndDecodeFrame(FFmpegContext context, long seekTarget, out int frameNumber, out TimeSpan currentTime, bool useKeyFrame = true)
        {
            frameNumber = -1;
            currentTime = TimeSpan.Zero;

            ffmpeg.av_seek_frame(context.FormatContext, context.VideoStreamIndex, seekTarget, ffmpeg.AVSEEK_FLAG_BACKWARD);
            ffmpeg.avcodec_flush_buffers(context.CodecContext);
            if (context.AudioCodecContext != null)
            {
                ffmpeg.avcodec_flush_buffers(context.AudioCodecContext);
            }

            AVPacket* pkt = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();
            byte* buffer;
            AVFrame* rgbFrame = AllocateFrameWithBuffer(context.CodecContext, out buffer);
            SwsContext* swsCtx = CreateSwsContext(context.CodecContext);

            BitmapSource? result = null;
            double timeBase = ffmpeg.av_q2d(context.FormatContext->streams[context.VideoStreamIndex]->time_base);
            double expectedTime = seekTarget * timeBase;

            bool find = false;
            while (ffmpeg.av_read_frame(context.FormatContext, pkt) >= 0 && !find)
            {
                if (pkt->stream_index == context.VideoStreamIndex)
                {
                    if (ffmpeg.avcodec_send_packet(context.CodecContext, pkt) == 0)
                    {
                        if (useKeyFrame)
                        {
                            if (ffmpeg.avcodec_receive_frame(context.CodecContext, frame) == 0)
                            {
                                currentTime = TimeSpan.FromSeconds(frame->pts * timeBase);
                                frameNumber = GetFrameNumber(context, frame);
                                ffmpeg.sws_scale(swsCtx, frame->data, frame->linesize, 0, context.CodecContext->height,
                                    rgbFrame->data, rgbFrame->linesize);
                                result = ConvertFrameToBitmapSource(rgbFrame, context.CodecContext);
                                find = true;
                            }
                        }
                        else
                        {
                            while (ffmpeg.avcodec_receive_frame(context.CodecContext, frame) == 0)
                            {
                                double actualTime = frame->pts * timeBase;
                                if (actualTime >= expectedTime)
                                {
                                    currentTime = TimeSpan.FromSeconds(actualTime);
                                    frameNumber = GetFrameNumber(context, frame);
                                    ffmpeg.sws_scale(swsCtx, frame->data, frame->linesize, 0, context.CodecContext->height,
                                        rgbFrame->data, rgbFrame->linesize);
                                    result = ConvertFrameToBitmapSource(rgbFrame, context.CodecContext);
                                    find = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                ffmpeg.av_packet_unref(pkt);
            }

            ffmpeg.av_packet_free(&pkt);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_frame_free(&rgbFrame);
            ffmpeg.sws_freeContext(swsCtx);
            ffmpeg.av_free(buffer);

            return result;
        }

        public static unsafe FFmpegContext OpenVideoFile(string filePath)
        {
            AVFormatContext* formatContext = ffmpeg.avformat_alloc_context();
            if (ffmpeg.avformat_open_input(&formatContext, filePath, null, null) != 0)
                throw new ApplicationException("파일 열기 실패");

            if (ffmpeg.avformat_find_stream_info(formatContext, null) != 0)
                throw new ApplicationException("스트림 정보 실패");

            int videoStreamIndex = -1;
            int audioStreamIndex = -1;
            AVCodec* videoCodec = null;
            AVCodec* audioCodec = null;

            for (int i = 0; i < formatContext->nb_streams; i++)
            {
                if (formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    videoStreamIndex = i;
                    videoCodec = ffmpeg.avcodec_find_decoder(formatContext->streams[i]->codecpar->codec_id);
                }
                else if (formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    audioStreamIndex = i;
                    audioCodec = ffmpeg.avcodec_find_decoder(formatContext->streams[i]->codecpar->codec_id);
                }
            }

            if (videoStreamIndex == -1 || videoCodec == null)
                throw new ApplicationException("비디오 스트림 없음");

            AVCodecContext* videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
            ffmpeg.avcodec_parameters_to_context(videoCodecContext, formatContext->streams[videoStreamIndex]->codecpar);
            ffmpeg.avcodec_open2(videoCodecContext, videoCodec, null);

            AVCodecContext* audioCodecContext = null;
            if (audioStreamIndex != -1 && audioCodec != null)
            {
                audioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
                ffmpeg.avcodec_parameters_to_context(audioCodecContext, formatContext->streams[audioStreamIndex]->codecpar);
                ffmpeg.avcodec_open2(audioCodecContext, audioCodec, null);
            }

            return new FFmpegContext
            {
                FormatContext = formatContext,
                CodecContext = videoCodecContext,
                VideoStreamIndex = videoStreamIndex,
                AudioStreamIndex = audioStreamIndex,
                AudioCodecContext = audioCodecContext // FFmpegContext에 AudioCodecContext 추가
            };
        }

        public static unsafe SwsContext* CreateSwsContext(AVCodecContext* codecContext)
        {
            return ffmpeg.sws_getContext(
                codecContext->width, codecContext->height, codecContext->pix_fmt,
                codecContext->width, codecContext->height, AVPixelFormat.AV_PIX_FMT_BGR24,
                ffmpeg.SWS_BILINEAR, null, null, null);
        }

        public static unsafe BitmapSource ConvertFrameToBitmapSource(AVFrame* pFrameRGB, int width, int height)
        {
            int stride = pFrameRGB->linesize[0];
            IntPtr pixelData = (IntPtr)pFrameRGB->data[0];

            var bitmapSource = new WriteableBitmap(
                width,
                height,
                96, // DpiX (adjust as needed)
                96, // DpiY (adjust as needed)
                System.Windows.Media.PixelFormats.Bgr24, // Or whichever format matches pFrameRGB->data
                null
            );

            bitmapSource.WritePixels(
                new Int32Rect(0, 0, width, height),
                pixelData,
                height * stride,
                stride
            );

            bitmapSource.Freeze();
            return bitmapSource;
        }

        // 기존 ConvertFrameToBitmapSource는 내부적으로 새로운 오버로드 호출
        public static unsafe BitmapSource ConvertFrameToBitmapSource(AVFrame* pFrameRGB, AVCodecContext* codec)
        {
            return ConvertFrameToBitmapSource(pFrameRGB, codec->width, codec->height);
        }

        public static unsafe AVFrame* AllocateFrameWithBuffer(AVCodecContext* codecContext, out byte* buffer)
        {
            AVFrame* frameRGB = ffmpeg.av_frame_alloc();

            int bufferSize = ffmpeg.av_image_get_buffer_size(
                AVPixelFormat.AV_PIX_FMT_BGR24, codecContext->width, codecContext->height, 1);
            buffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

            byte_ptrArray4 dataArray = new byte_ptrArray4();
            int_array4 linesizeArray = new int_array4();

            ffmpeg.av_image_fill_arrays(ref dataArray, ref linesizeArray, buffer,
                AVPixelFormat.AV_PIX_FMT_BGR24, codecContext->width, codecContext->height, 1);

            for (uint i = 0; i < 4; i++)
            {
                frameRGB->data[i] = dataArray[i];
                frameRGB->linesize[i] = linesizeArray[i];
            }

            return frameRGB;
        }

        public static unsafe bool OpenAudioStream(FFmpegContext context)
        {
            _audioStreamIndex = -1;
            for (int i = 0; i < context.FormatContext->nb_streams; i++)
            {
                if (context.FormatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    _audioStreamIndex = i;
                    var audioStream = context.FormatContext->streams[_audioStreamIndex];
                    var audioCodecPar = audioStream->codecpar;

                    _audioSampleFormat = (AVSampleFormat)audioCodecPar->format;
                    _audioChannels = audioCodecPar->ch_layout.nb_channels;
                    _audioSampleRate = audioCodecPar->sample_rate;

                    // 지원하는 샘플 포맷인지 확인 (예: float, short)
                    if (_audioSampleFormat == AVSampleFormat.AV_SAMPLE_FMT_FLTP ||
                        _audioSampleFormat == AVSampleFormat.AV_SAMPLE_FMT_S16)
                    {
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine($"지원하지 않는 오디오 샘플 포맷: {_audioSampleFormat}");
                        return false;
                    }
                }
            }
            return false; // 오디오 스트림을 찾지 못함
        }

        public static void InitializeAudioPlayback()
        {
            if (_audioStreamIndex != -1 && _audioChannels > 0 && _audioSampleRate > 0)
            {
                WaveFormat? waveFormat = null;
                if (_audioSampleFormat == AVSampleFormat.AV_SAMPLE_FMT_FLTP)
                {
                    waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_audioSampleRate, _audioChannels);
                }
                else if (_audioSampleFormat == AVSampleFormat.AV_SAMPLE_FMT_S16)
                {
                    waveFormat = new WaveFormat(_audioSampleRate, 16, _audioChannels);
                    // 또는 더 명시적으로:
                    // waveFormat = WaveFormat.CreateCustomFormat(
                    //     WaveFormatEncoding.Pcm, _audioSampleRate, _audioChannels,
                    //     2 * _audioSampleRate * _audioChannels, 2 * _audioChannels, 16);
                }

                if (waveFormat != null)
                {
                    _waveOut = new WaveOutEvent();
                    _waveProvider = new BufferedWaveProvider(waveFormat);
                    _waveOut.Init(_waveProvider);
                    _waveOut.Play();
                }
                else
                {
                    Debug.WriteLine($"지원하지 않는 NAudio WaveFormat: {_audioSampleFormat}");
                }
            }
        }

        public static void StopAudioPlayback()
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
            _waveProvider?.ClearBuffer();
            _waveProvider = null;
            _audioStreamIndex = -1;
        }

        private static unsafe void DecodeAndPlayAudio(FFmpegContext context, AVPacket* packet)
        {
            if (context.AudioCodecContext == null || packet->stream_index != context.AudioStreamIndex)
                return;

            AVFrame* audioFrame = ffmpeg.av_frame_alloc();

            // PTS 확인
            if (packet->pts == ffmpeg.AV_NOPTS_VALUE)
            {
                Debug.WriteLine("Warning: Audio packet has no PTS.");
                ffmpeg.av_packet_unref(packet); // 해당 패킷 처리 종료
                ffmpeg.av_frame_free(&audioFrame);
                return;
            }

            int ret = ffmpeg.avcodec_send_packet(context.AudioCodecContext, packet);
            if (ret < 0)
            {
                Debug.WriteLine($"Error sending audio packet for decoding (code: {ret})");
                ffmpeg.av_frame_free(&audioFrame);
                return;
            }

            while ((ret = ffmpeg.avcodec_receive_frame(context.AudioCodecContext, audioFrame)) == 0)
            {
                if (_waveProvider != null)
                {
                    byte[]? audioData = ConvertAudioFrameToByteArray(audioFrame, _audioSampleFormat, _audioChannels);
                    if (audioData != null && audioData.Length > 0)
                    {
                        _waveProvider.AddSamples(audioData, 0, audioData.Length);
                    }
                }
                ffmpeg.av_frame_unref(audioFrame);
            }
            ffmpeg.av_frame_free(&audioFrame);
        }

        private static unsafe byte[]? ConvertAudioFrameToByteArray(AVFrame* frame, AVSampleFormat sampleFormat, int channels)
        {
            if (frame->data[0] == null || frame->linesize[0] <= 0)
                return null;

            int dataSize = frame->linesize[0];
            byte[] audioBytes = new byte[dataSize];

            if (sampleFormat == AVSampleFormat.AV_SAMPLE_FMT_FLTP && channels > 1)
            {
                // Planar FLTP 데이터를 인터리브
                int sampleCount = frame->nb_samples;
                int floatSize = sizeof(float);
                byte[] interleavedBytes = new byte[sampleCount * channels * floatSize];

                for (int i = 0; i < sampleCount; i++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        Marshal.Copy((IntPtr)frame->data[(uint)ch] + i * floatSize, interleavedBytes, (i * channels + ch) * floatSize, floatSize);
                    }
                }
                return interleavedBytes;
            }
            else if (sampleFormat == AVSampleFormat.AV_SAMPLE_FMT_S16)
            {
                Marshal.Copy((IntPtr)frame->data[0], audioBytes, 0, dataSize);
                return audioBytes;
            }
            else
            {
                Debug.WriteLine($"지원하지 않는 오디오 샘플 포맷 변환: {sampleFormat}");
                return null;
            }
        }
    }
}
