using FFmpeg.AutoGen;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    public unsafe class FFmpegContext
    {
        public AVFormatContext* FormatContext;
        public AVCodecContext* CodecContext;
        public int VideoStreamIndex;

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

        public static unsafe void OpenVideo(string filePath, Action<BitmapSource, int, TimeSpan, TimeSpan, FFmpegContext> onFrameDecoded, CancellationToken token, Action? onCompleted = null)
        {
            var context = OpenVideoFile(filePath);
            var swsCtx = CreateSwsContext(context.CodecContext);
            AVFrame* pFrame = ffmpeg.av_frame_alloc();
            AVPacket* pPacket = ffmpeg.av_packet_alloc();
            byte* buffer;
            AVFrame* pFrameRGB = AllocateFrameWithBuffer(context.CodecContext, out buffer);

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
                    Thread.Sleep(100);
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
                        ffmpeg.av_packet_unref(pPacket);
                    }
                    else
                    {
                        ffmpeg.avcodec_send_packet(context.CodecContext, null);
                        eof = true;
                    }

                    double timeBase = ffmpeg.av_q2d(context.FormatContext->streams[context.VideoStreamIndex]->time_base);
                    while (ffmpeg.avcodec_receive_frame(context.CodecContext, pFrame) == 0)
                    {
                        TimeSpan currentTime = TimeSpan.FromSeconds(pFrame->pts * timeBase);
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

                        // 여기서 속도 제어를 위한 프레임 타이밍 조절
                        if (VideoPlayerController.SpeedIndex == 5)
                        {
                            double targetTimeInSeconds = pFrame->pts * timeBase;
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
                                    
                                    targetTimeInSeconds = pFrame->pts * timeBase;
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
                        else
                        {
                            TimeSpan delay = VideoPlayerController.PlaybackSpeeds[VideoPlayerController.SpeedIndex];
                            var sw = Stopwatch.StartNew();
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
            AVCodec* codec = null;

            for (int i = 0; i < formatContext->nb_streams; i++)
            {
                if (formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    videoStreamIndex = i;
                    codec = ffmpeg.avcodec_find_decoder(formatContext->streams[i]->codecpar->codec_id);
                    break;
                }
            }

            if (videoStreamIndex == -1 || codec == null)
                throw new ApplicationException("비디오 스트림 없음");

            AVCodecContext* codecContext = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_parameters_to_context(codecContext, formatContext->streams[videoStreamIndex]->codecpar);
            ffmpeg.avcodec_open2(codecContext, codec, null);

            return new FFmpegContext
            {
                FormatContext = formatContext,
                CodecContext = codecContext,
                VideoStreamIndex = videoStreamIndex
            };
        }

        public static unsafe SwsContext* CreateSwsContext(AVCodecContext* codecContext)
        {
            return ffmpeg.sws_getContext(
                codecContext->width, codecContext->height, codecContext->pix_fmt,
                codecContext->width, codecContext->height, AVPixelFormat.AV_PIX_FMT_BGR24,
                ffmpeg.SWS_BILINEAR, null, null, null);
        }

        public static unsafe BitmapSource ConvertFrameToBitmapSource(AVFrame* pFrameRGB, AVCodecContext* codec)
        {
            int width = codec->width;
            int height = codec->height;
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
    }
}
