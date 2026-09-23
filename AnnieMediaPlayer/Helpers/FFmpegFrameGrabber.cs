using FFmpeg.AutoGen;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    public unsafe class FFmpegFrameGrabber : IDisposable
    {
        private AVFormatContext* _formatContext = null;
        private AVCodecContext* _videoCodecContext = null;
        private SwsContext* _scaledSwsContext = null; // 리사이징
        private int _videoStreamIndex = -1;
        private readonly string _filePath;
        private int _width;
        private int _height;
        private int _previewWidth;
        private int _previewHeight;

        private AVFrame* _scaledRgbFrame = null; // 리사이징된 프레임
        private byte* _scaledBuffer = null; // 리사이징된 버퍼
        private readonly object _sync = new();
        private bool _isDisposed;
        private int _decodeErrorLogged;

        public int Width => _width;
        public int Height => _height;

        public FFmpegFrameGrabber(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            _filePath = filePath;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                var formatContext = ffmpeg.avformat_alloc_context();
                if (formatContext == null)
                    throw new ApplicationException("Error allocating format context.");

                if (ffmpeg.avformat_open_input(&formatContext, _filePath, null, null) < 0)
                {
                    if (formatContext != null)
                        ffmpeg.avformat_close_input(&formatContext);
                    throw new ApplicationException("Error opening input file.");
                }

                _formatContext = formatContext;
                if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
                    throw new ApplicationException("Error finding stream information.");

                for (int i = 0; i < _formatContext->nb_streams; i++)
                {
                    // 내장 앨범 이미지 스트림은 영상이 아니므로 건너뜁니다. (HasVideo 판단과 동일한 기준)
                    var stream = _formatContext->streams[i];
                    if (stream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO &&
                        (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                    {
                        _videoStreamIndex = i;
                        break;
                    }
                }

                if (_videoStreamIndex == -1)
                    throw new ApplicationException("Could not find video stream.");

                AVCodecParameters* codecPar = _formatContext->streams[_videoStreamIndex]->codecpar;
                AVCodec* videoCodec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);
                if (videoCodec == null)
                    throw new ApplicationException("Unsupported codec.");

                var videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
                if (videoCodecContext == null)
                    throw new ApplicationException("Error allocating codec context.");

                _videoCodecContext = videoCodecContext;
                if (ffmpeg.avcodec_parameters_to_context(_videoCodecContext, codecPar) < 0)
                    throw new ApplicationException("Error copying codec parameters to context.");

                if (ffmpeg.avcodec_open2(_videoCodecContext, videoCodec, null) < 0)
                    throw new ApplicationException("Error opening codec context.");

                _width = _videoCodecContext->width;
                _height = _videoCodecContext->height;
                if (_width <= 0 || _height <= 0)
                    throw new ApplicationException("Invalid video dimensions.");

                // 미리보기 출력용 변환 버퍼는 크기가 정해질 때 AllocateScaledFrame 에서 생성합니다.
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private unsafe void AllocateScaledFrame(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Preview dimensions must be greater than zero.");

            if (_scaledRgbFrame == null || _previewWidth != width || _previewHeight != height)
            {
                try
                {
                    FreeScaledFrame(); // 기존 리소스 해제

                    _previewWidth = width;
                    _previewHeight = height;
                    _scaledRgbFrame = ffmpeg.av_frame_alloc();
                    if (_scaledRgbFrame == null)
                        throw new ApplicationException("Error allocating scaled frame.");

                    int scaledBufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGR24, _previewWidth, _previewHeight, 1);
                    if (scaledBufferSize <= 0)
                        throw new ApplicationException("Error calculating scaled buffer size.");

                    _scaledBuffer = (byte*)ffmpeg.av_malloc((ulong)scaledBufferSize);
                    if (_scaledBuffer == null)
                        throw new ApplicationException("Error allocating scaled buffer.");

                    byte_ptrArray4 scaledDataArray = new byte_ptrArray4();
                    int_array4 scaledLinesizeArray = new int_array4();

                    if (ffmpeg.av_image_fill_arrays(ref scaledDataArray, ref scaledLinesizeArray, _scaledBuffer,
                        AVPixelFormat.AV_PIX_FMT_BGR24, _previewWidth, _previewHeight, 1) < 0)
                        throw new ApplicationException("Error initializing scaled buffer.");

                    for (uint i = 0; i < 4; i++)
                    {
                        _scaledRgbFrame->data[i] = scaledDataArray[i];
                        _scaledRgbFrame->linesize[i] = scaledLinesizeArray[i];
                    }

                    _scaledRgbFrame->width = _previewWidth;
                    _scaledRgbFrame->height = _previewHeight;
                    _scaledRgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGR24;

                    _scaledSwsContext = ffmpeg.sws_getContext(
                        _width, _height, _videoCodecContext->pix_fmt,
                        _previewWidth, _previewHeight, AVPixelFormat.AV_PIX_FMT_BGR24,
                        ffmpeg.SWS_BILINEAR, null, null, null);

                    if (_scaledSwsContext == null)
                        throw new ApplicationException("Error creating scaled SwsContext.");
                }
                catch
                {
                    FreeScaledFrame();
                    throw;
                }
            }
        }

        private unsafe void FreeScaledFrame()
        {
            var scaledRgbFrame = _scaledRgbFrame;
            ffmpeg.av_frame_free(&scaledRgbFrame);
            ffmpeg.av_free(_scaledBuffer);
            ffmpeg.sws_freeContext(_scaledSwsContext);
            _scaledRgbFrame = null;
            _scaledBuffer = null;
            _scaledSwsContext = null;
        }

        public unsafe BitmapSource? GetFrameAt(TimeSpan targetTime, Size previewSize, out TimeSpan currentTime, bool useKeyFrame = true)
        {
            lock (_sync)
            {
                if (_isDisposed || _formatContext == null || _videoCodecContext == null)
                {
                    currentTime = TimeSpan.Zero;
                    return null;
                }

                return GetFrameAtCore(targetTime, previewSize, out currentTime, useKeyFrame);
            }
        }

        private unsafe BitmapSource? GetFrameAtCore(TimeSpan targetTime, Size previewSize, out TimeSpan currentTime, bool useKeyFrame)
        {
            currentTime = TimeSpan.Zero;

            AllocateScaledFrame((int)previewSize.Width, (int)previewSize.Height);

            AVFrame* frame = ffmpeg.av_frame_alloc();
            AVPacket* packet = ffmpeg.av_packet_alloc();
            BitmapSource? result = null;
            bool frameFound = false;

            try
            {
                if (frame == null || packet == null)
                    throw new ApplicationException("Error allocating frame buffers.");

                double timeBase = ffmpeg.av_q2d(_formatContext->streams[_videoStreamIndex]->time_base);
                long targetFramePts = (long)(targetTime.TotalSeconds / timeBase);
                int seekFlags = useKeyFrame ? ffmpeg.AVSEEK_FLAG_BACKWARD : ffmpeg.AVSEEK_FLAG_BACKWARD | ffmpeg.AVSEEK_FLAG_ANY;
                ffmpeg.av_seek_frame(_formatContext, _videoStreamIndex, targetFramePts, seekFlags);
                ffmpeg.avcodec_flush_buffers(_videoCodecContext);

                while (!frameFound)
                {
                    if (ffmpeg.av_read_frame(_formatContext, packet) < 0)
                    {
                        // 파일 끝에서는 디코더에 남아 있는 프레임을 꺼냅니다.
                        int flushResult = ffmpeg.avcodec_send_packet(_videoCodecContext, null);
                        if (flushResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            frameFound = TryReceiveFrame(frame, timeBase, targetTime, useKeyFrame, ref currentTime, ref result);
                            if (!frameFound)
                            {
                                flushResult = ffmpeg.avcodec_send_packet(_videoCodecContext, null);
                                if (flushResult < 0 && flushResult != ffmpeg.AVERROR_EOF)
                                    LogDecodeErrorOnce("flush retry", flushResult);
                            }
                        }
                        else if (flushResult < 0 && flushResult != ffmpeg.AVERROR_EOF)
                        {
                            LogDecodeErrorOnce("flush", flushResult);
                        }

                        if (!frameFound && flushResult >= 0)
                            frameFound = TryReceiveFrame(frame, timeBase, targetTime, useKeyFrame, ref currentTime, ref result);
                        break;
                    }

                    try
                    {
                        if (packet->stream_index != _videoStreamIndex)
                            continue;

                        // 한 패킷에서 여러 프레임이 나올 수 있으므로 받을 수 있는 프레임을 모두 확인합니다.
                        int sendResult = ffmpeg.avcodec_send_packet(_videoCodecContext, packet);
                        if (sendResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            // 디코더 입력이 가득 찬 경우 출력 큐를 비운 뒤 같은 패킷을 다시 보냅니다.
                            frameFound = TryReceiveFrame(frame, timeBase, targetTime, useKeyFrame, ref currentTime, ref result);
                            if (!frameFound)
                            {
                                int retryResult = ffmpeg.avcodec_send_packet(_videoCodecContext, packet);
                                if (retryResult < 0)
                                    LogDecodeErrorOnce("packet retry", retryResult);
                                else
                                    frameFound = TryReceiveFrame(frame, timeBase, targetTime, useKeyFrame, ref currentTime, ref result);
                            }
                        }
                        else if (sendResult < 0)
                        {
                            LogDecodeErrorOnce("packet send", sendResult);
                        }
                        else
                        {
                            frameFound = TryReceiveFrame(frame, timeBase, targetTime, useKeyFrame, ref currentTime, ref result);
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(packet);
                    }
                }
            }
            finally
            {
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);
            }
            return result;
        }

        private void LogDecodeErrorOnce(string operation, int errorCode)
        {
            if (Interlocked.Exchange(ref _decodeErrorLogged, 1) == 0)
                PlayerDiagnostics.Write($"Preview decoder {operation} failed (FFmpeg error {errorCode}): {_filePath}");
        }

        // 디코더에서 받을 수 있는 프레임을 확인하여 조건에 맞는 프레임을 미리보기 이미지로 변환합니다.
        private unsafe bool TryReceiveFrame(AVFrame* frame, double timeBase, TimeSpan targetTime, bool useKeyFrame,
            ref TimeSpan currentTime, ref BitmapSource? result)
        {
            int receiveResult;
            while ((receiveResult = ffmpeg.avcodec_receive_frame(_videoCodecContext, frame)) == 0)
            {
                try
                {
                    long pts = frame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE ? frame->best_effort_timestamp : frame->pts;
                    double frameSeconds = pts == ffmpeg.AV_NOPTS_VALUE ? targetTime.TotalSeconds : pts * timeBase;

                    if (useKeyFrame || Math.Abs(frameSeconds - targetTime.TotalSeconds) < 0.1)
                    {
                        currentTime = TimeSpan.FromSeconds(Math.Max(0, frameSeconds));

                        ffmpeg.sws_scale(_scaledSwsContext, frame->data, frame->linesize, 0, _height,
                            _scaledRgbFrame->data, _scaledRgbFrame->linesize);

                        result = ConvertFrameToBitmapSource(_scaledRgbFrame, _previewWidth, _previewHeight);
                        return true;
                    }
                }
                finally
                {
                    ffmpeg.av_frame_unref(frame);
                }
            }

            if (receiveResult < 0 && receiveResult != ffmpeg.AVERROR(ffmpeg.EAGAIN) && receiveResult != ffmpeg.AVERROR_EOF)
                LogDecodeErrorOnce("frame receive", receiveResult);

            return false;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_isDisposed)
                    return;

                Dispose(true);
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_formatContext != null)
            {
                var formatContext = _formatContext;
                ffmpeg.avformat_close_input(&formatContext);
                _formatContext = null;
            }

            if (_videoCodecContext != null)
            {
                var videoCodecContext = _videoCodecContext;
                ffmpeg.avcodec_free_context(&videoCodecContext);
                _videoCodecContext = null;
            }
            FreeScaledFrame();
        }

        ~FFmpegFrameGrabber()
        {
            lock (_sync)
            {
                if (_isDisposed)
                    return;

                Dispose(false);
                _isDisposed = true;
            }
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
    }
}
