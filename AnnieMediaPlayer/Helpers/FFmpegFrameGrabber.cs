using FFmpeg.AutoGen;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    public unsafe class FFmpegFrameGrabber : IDisposable
    {
        private AVFormatContext* _formatContext = null;
        private AVCodecContext* _videoCodecContext = null;
        private SwsContext* _swsContext = null;
        private SwsContext* _scaledSwsContext = null; // 리사이징
        private int _videoStreamIndex = -1;
        private readonly string _filePath;
        private int _width;
        private int _height;
        private int _previewWidth;
        private int _previewHeight;

        private AVFrame* _rgbFrame = null; // RGB 변환 프레임
        private byte* _buffer = null; // RGB 변환 버퍼
        private AVFrame* _scaledRgbFrame = null; // 리사이징된 프레임
        private byte* _scaledBuffer = null; // 리사이징된 버퍼

        public FFmpegFrameGrabber(string filePath)
        {
            _filePath = filePath;
            Initialize();
        }

        private void Initialize()
        {
            var formatContext = ffmpeg.avformat_alloc_context();
            _formatContext = formatContext;
            if (ffmpeg.avformat_open_input(&formatContext, _filePath, null, null) < 0)
                throw new ApplicationException("Error opening input file.");

            if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
            {
                ffmpeg.avformat_close_input(&formatContext);
                throw new ApplicationException("Error finding stream information.");
            }

            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                if (_formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    _videoStreamIndex = i;
                    break;
                }
            }

            if (_videoStreamIndex == -1)
            {
                ffmpeg.avformat_close_input(&formatContext);
                throw new ApplicationException("Could not find video stream.");
            }

            AVCodecParameters* codecPar = _formatContext->streams[_videoStreamIndex]->codecpar;
            AVCodec* videoCodec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);
            if (videoCodec == null)
            {
                ffmpeg.avformat_close_input(&formatContext);
                throw new ApplicationException("Unsupported codec.");
            }

            var videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
            _videoCodecContext = videoCodecContext;
            if (ffmpeg.avcodec_parameters_to_context(_videoCodecContext, codecPar) < 0)
            {
                ffmpeg.avformat_close_input(&formatContext);
                ffmpeg.avcodec_free_context(&videoCodecContext);
                throw new ApplicationException("Error copying codec parameters to context.");
            }

            if (ffmpeg.avcodec_open2(_videoCodecContext, videoCodec, null) < 0)
            {
                ffmpeg.avformat_close_input(&formatContext);
                ffmpeg.avcodec_free_context(&videoCodecContext);
                throw new ApplicationException("Error opening codec context.");
            }

            _width = _videoCodecContext->width;
            _height = _videoCodecContext->height;

            _swsContext = ffmpeg.sws_getContext(
                _width, _height, _videoCodecContext->pix_fmt,
                _width, _height, AVPixelFormat.AV_PIX_FMT_BGR24,
                ffmpeg.SWS_BILINEAR, null, null, null);

            if (_swsContext == null)
            {
                ffmpeg.avcodec_free_context(&videoCodecContext);
                ffmpeg.avformat_close_input(&formatContext);
                throw new ApplicationException("Error creating SwsContext.");
            }

            // RGB 프레임 버퍼 초기화
            _rgbFrame = ffmpeg.av_frame_alloc();
            if (_rgbFrame == null)
            {
                Dispose();
                throw new ApplicationException("Error allocating RGB frame.");
            }
            int bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGR24, _width, _height, 1);
            _buffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

            byte_ptrArray4 rgbDataArray = new byte_ptrArray4();
            int_array4 rgbLinesizeArray = new int_array4();

            ffmpeg.av_image_fill_arrays(ref rgbDataArray, ref rgbLinesizeArray, _buffer, AVPixelFormat.AV_PIX_FMT_BGR24, _width, _height, 1);

            for (uint i = 0; i < 4; i++)
            {
                _rgbFrame->data[i] = rgbDataArray[i];
                _rgbFrame->linesize[i] = rgbLinesizeArray[i];
            }
        }

        private unsafe void AllocateScaledFrame(int width, int height)
        {
            if (_scaledRgbFrame == null || _previewWidth != width || _previewHeight != height)
            {
                FreeScaledFrame(); // 기존 리소스 해제

                _previewWidth = width;
                _previewHeight = height;
                _scaledRgbFrame = ffmpeg.av_frame_alloc();
                int scaledBufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGR24, _previewWidth, _previewHeight, 1);
                _scaledBuffer = (byte*)ffmpeg.av_malloc((ulong)scaledBufferSize);

                byte_ptrArray4 scaledDataArray = new byte_ptrArray4();
                int_array4 scaledLinesizeArray = new int_array4();

                ffmpeg.av_image_fill_arrays(ref scaledDataArray, ref scaledLinesizeArray, _scaledBuffer,
                    AVPixelFormat.AV_PIX_FMT_BGR24, _previewWidth, _previewHeight, 1);

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
                {
                    // 오류 처리 필요
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

        public unsafe BitmapSource? GetFrameAt(TimeSpan targetTime, Size previewSize, bool useKeyFrame = true)
        {
            AllocateScaledFrame((int)previewSize.Width, (int)previewSize.Height);

            AVFrame* frame = ffmpeg.av_frame_alloc();
            AVPacket* packet = ffmpeg.av_packet_alloc();
            BitmapSource? result = null;
            bool frameFound = false;

            try
            {
                double timeBase = ffmpeg.av_q2d(_formatContext->streams[_videoStreamIndex]->time_base);
                long targetFramePts = (long)(targetTime.TotalSeconds / timeBase);
                int seekFlags = useKeyFrame ? ffmpeg.AVSEEK_FLAG_BACKWARD : ffmpeg.AVSEEK_FLAG_BACKWARD | ffmpeg.AVSEEK_FLAG_ANY;
                ffmpeg.av_seek_frame(_formatContext, _videoStreamIndex, targetFramePts, seekFlags);
                ffmpeg.avcodec_flush_buffers(_videoCodecContext);

                while (ffmpeg.av_read_frame(_formatContext, packet) >= 0 && !frameFound)
                {
                    if (packet->stream_index == _videoStreamIndex)
                    {
                        if (ffmpeg.avcodec_send_packet(_videoCodecContext, packet) == 0)
                        {
                            if (ffmpeg.avcodec_receive_frame(_videoCodecContext, frame) == 0)
                            {
                                if (useKeyFrame || Math.Abs((frame->pts * timeBase) - targetTime.TotalSeconds) < 0.1)
                                {
                                    ffmpeg.sws_scale(_scaledSwsContext, frame->data, frame->linesize, 0, _height,
                                        _scaledRgbFrame->data, _scaledRgbFrame->linesize);

                                    // FFmpegHelper.ConvertFrameToBitmapSource 내부에서 fixed 사용
                                    result = FFmpegHelper.ConvertFrameToBitmapSource(_scaledRgbFrame, _previewWidth, _previewHeight);
                                    frameFound = true;
                                }
                                ffmpeg.av_frame_unref(frame);
                                if (frameFound) break;
                            }
                        }
                    }
                    ffmpeg.av_packet_unref(packet);
                }
            }
            finally
            {
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);
            }
            return result;
        }

        public void Dispose()
        {
            Dispose(true);
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
            if (_swsContext != null)
            {
                ffmpeg.sws_freeContext(_swsContext);
                _swsContext = null;
            }
            FreeScaledFrame();
            if (_rgbFrame != null)
            {
                var rgbFrame = _rgbFrame;
                ffmpeg.av_frame_free(&rgbFrame);
                _rgbFrame = null;
            }
            if (_buffer != null)
            {
                ffmpeg.av_free(_buffer);
                _buffer = null;
            }
        }

        ~FFmpegFrameGrabber()
        {
            Dispose(false);
        }
    }
}