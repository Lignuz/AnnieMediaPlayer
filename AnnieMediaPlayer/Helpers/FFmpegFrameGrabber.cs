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
        private int _videoStreamIndex = -1;
        private readonly string _filePath;
        private int _width;
        private int _height;

        public FFmpegFrameGrabber(string filePath)
        {
            _filePath = filePath;
            Initialize();
        }

        private void Initialize()
        {
            _formatContext = ffmpeg.avformat_alloc_context();
            AVFormatContext* pFormatContext = _formatContext;
            if (ffmpeg.avformat_open_input(&pFormatContext, _filePath, null, null) < 0)
                throw new ApplicationException("Error opening input file.");

            if (ffmpeg.avformat_find_stream_info(pFormatContext, null) < 0)
            {
                ffmpeg.avformat_close_input(&pFormatContext);
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
                ffmpeg.avformat_close_input(&pFormatContext);
                throw new ApplicationException("Could not find video stream.");
            }

            AVCodecParameters* codecPar = _formatContext->streams[_videoStreamIndex]->codecpar;
            AVCodec* videoCodec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);
            if (videoCodec == null)
            {
                ffmpeg.avformat_close_input(&pFormatContext);
                throw new ApplicationException("Unsupported codec.");
            }

            _videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
            AVCodecContext* pVideoCodecContext = _videoCodecContext;
            if (ffmpeg.avcodec_parameters_to_context(pVideoCodecContext, codecPar) < 0)
            {
                ffmpeg.avformat_close_input(&pFormatContext);
                ffmpeg.avcodec_free_context(&pVideoCodecContext);
                throw new ApplicationException("Error copying codec parameters to context.");
            }

            if (ffmpeg.avcodec_open2(_videoCodecContext, videoCodec, null) < 0)
            {
                ffmpeg.avformat_close_input(&pFormatContext);
                ffmpeg.avcodec_free_context(&pVideoCodecContext);
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
                ffmpeg.avcodec_free_context(&pVideoCodecContext);
                ffmpeg.avformat_close_input(&pFormatContext);
                throw new ApplicationException("Error creating SwsContext.");
            }
        }

        public unsafe BitmapSource? GetFrameAt(TimeSpan targetTime, Size previewSize, bool useKeyFrame = true)
        {
            AVFrame* frame = null;
            AVFrame* rgbFrame = null;
            AVFrame* scaledRgbFrame = null; // 리사이징된 프레임
            AVPacket* packet = null;
            byte* buffer = null;
            byte* scaledBuffer = null; // 리사이징된 버퍼
            BitmapSource? result = null;
            bool frameFound = false;
            SwsContext* scaledSwsContext = null; // 리사이징을 위한 SwsContext

            int _previewWidth = (int)previewSize.Width;
            int _previewHeight = (int)previewSize.Height;

            try
            {
                frame = ffmpeg.av_frame_alloc();
                rgbFrame = FFmpegHelper.AllocateFrameWithBuffer(_videoCodecContext, out buffer);
                packet = ffmpeg.av_packet_alloc();

                // 리사이징된 프레임 할당 및 버퍼 설정
                scaledRgbFrame = ffmpeg.av_frame_alloc();
                int scaledBufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGR24, _previewWidth, _previewHeight, 1);
                scaledBuffer = (byte*)ffmpeg.av_malloc((ulong)scaledBufferSize);
                byte_ptrArray4 scaledDataArray = new byte_ptrArray4();
                int_array4 scaledLinesizeArray = new int_array4();
                ffmpeg.av_image_fill_arrays(ref scaledDataArray, ref scaledLinesizeArray, scaledBuffer,
                    AVPixelFormat.AV_PIX_FMT_BGR24, _previewWidth, _previewHeight, 1);
                for (uint i = 0; i < 4; i++)
                {
                    scaledRgbFrame->data[i] = scaledDataArray[i];
                    scaledRgbFrame->linesize[i] = scaledLinesizeArray[i];
                }
                scaledRgbFrame->width = _previewWidth;
                scaledRgbFrame->height = _previewHeight;
                scaledRgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGR24;

                // 리사이징을 위한 SwsContext 생성
                scaledSwsContext = ffmpeg.sws_getContext(
                    _videoCodecContext->width, _videoCodecContext->height, _videoCodecContext->pix_fmt,
                    _previewWidth, _previewHeight, AVPixelFormat.AV_PIX_FMT_BGR24,
                    ffmpeg.SWS_BILINEAR, null, null, null);

                double timeBase = ffmpeg.av_q2d(_formatContext->streams[_videoStreamIndex]->time_base);
                long targetFramePts = (long)(targetTime.TotalSeconds / timeBase);
                ffmpeg.av_seek_frame(_formatContext, _videoStreamIndex, targetFramePts, ffmpeg.AVSEEK_FLAG_BACKWARD | ffmpeg.AVSEEK_FLAG_ANY);
                ffmpeg.avcodec_flush_buffers(_videoCodecContext);

                while (ffmpeg.av_read_frame(_formatContext, packet) >= 0 && !frameFound)
                {
                    if (packet->stream_index == _videoStreamIndex)
                    {
                        if (ffmpeg.avcodec_send_packet(_videoCodecContext, packet) == 0)
                        {
                            if (useKeyFrame)
                            {
                                if (ffmpeg.avcodec_receive_frame(_videoCodecContext, frame) == 0)
                                {
                                    ffmpeg.sws_scale(scaledSwsContext, frame->data, frame->linesize, 0, _videoCodecContext->height,
                                        scaledRgbFrame->data, scaledRgbFrame->linesize);

                                    result = FFmpegHelper.ConvertFrameToBitmapSource(scaledRgbFrame, _previewWidth, _previewHeight);
                                    ffmpeg.av_frame_unref(frame);
                                    frameFound = true;
                                }
                            }
                            else
                            {
                                while (ffmpeg.avcodec_receive_frame(_videoCodecContext, frame) == 0)
                                {
                                    double frameTimeInSeconds = frame->pts * timeBase;
                                    if (frameTimeInSeconds >= targetTime.TotalSeconds)
                                    {
                                        ffmpeg.sws_scale(scaledSwsContext, frame->data, frame->linesize, 0, _videoCodecContext->height,
                                            scaledRgbFrame->data, scaledRgbFrame->linesize);

                                        result = FFmpegHelper.ConvertFrameToBitmapSource(scaledRgbFrame, _previewWidth, _previewHeight);
                                        ffmpeg.av_frame_unref(frame);
                                        frameFound = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    ffmpeg.av_packet_unref(packet);
                }
            }
            finally
            {
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_frame_free(&rgbFrame);
                ffmpeg.av_packet_free(&packet);
                if (buffer != null) ffmpeg.av_free(buffer);
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
                var pFormatContext = _formatContext;
                ffmpeg.avformat_close_input(&pFormatContext);
                _formatContext = null;
            }
            if (_videoCodecContext != null)
            {
                var pVideoCodecContext = _videoCodecContext;
                ffmpeg.avcodec_free_context(&pVideoCodecContext);
                _videoCodecContext = null;
            }
            if (_swsContext != null)
            {
                ffmpeg.sws_freeContext(_swsContext);
                _swsContext = null;
            }
        }

        ~FFmpegFrameGrabber()
        {
            Dispose(false);
        }
    }
}