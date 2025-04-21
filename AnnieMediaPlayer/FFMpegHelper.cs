using FFmpeg.AutoGen;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows;

namespace AnnieMediaPlayer
{
    public unsafe class FFmpegContext
    {
        public AVFormatContext* FormatContext;
        public AVCodecContext* CodecContext;
        public int VideoStreamIndex;
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
        public unsafe static void OpenVideo(string filePath, Action<BitmapSource, int, TimeSpan, TimeSpan, FFmpegContext> onFrameDecoded, CancellationToken token, Action? onCompleted = null)
        {
            AVFormatContext* pFormatContext = ffmpeg.avformat_alloc_context();
            if (ffmpeg.avformat_open_input(&pFormatContext, filePath, null, null) != 0)
                throw new ApplicationException("파일 열기 실패");

            if (ffmpeg.avformat_find_stream_info(pFormatContext, null) != 0)
                throw new ApplicationException("스트림 정보 실패");

            int videoStreamIndex = -1;
            AVCodec* pCodec = null;

            for (int i = 0; i < pFormatContext->nb_streams; i++)
            {
                if (pFormatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    videoStreamIndex = i;
                    pCodec = ffmpeg.avcodec_find_decoder(pFormatContext->streams[i]->codecpar->codec_id);
                    break;
                }
            }

            if (videoStreamIndex == -1 || pCodec == null)
                throw new ApplicationException("비디오 스트림 없음");

            AVCodecContext* pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
            ffmpeg.avcodec_parameters_to_context(pCodecContext, pFormatContext->streams[videoStreamIndex]->codecpar);
            ffmpeg.avcodec_open2(pCodecContext, pCodec, null);

            SwsContext* pSwsCtx = ffmpeg.sws_getContext(
                pCodecContext->width, pCodecContext->height, pCodecContext->pix_fmt,
                pCodecContext->width, pCodecContext->height, AVPixelFormat.AV_PIX_FMT_BGR24,
                ffmpeg.SWS_BILINEAR, null, null, null);

            AVFrame* pFrame = ffmpeg.av_frame_alloc();
            AVFrame* pFrameRGB = ffmpeg.av_frame_alloc();
            AVPacket* pPacket = ffmpeg.av_packet_alloc();

            int bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGR24, pCodecContext->width, pCodecContext->height, 1);
            byte* buffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

            byte_ptrArray4 dataArray = new byte_ptrArray4();
            int_array4 linesizeArray = new int_array4();

            ffmpeg.av_image_fill_arrays(ref dataArray, ref linesizeArray, buffer, AVPixelFormat.AV_PIX_FMT_BGR24,
                pCodecContext->width, pCodecContext->height, 1);

            for (uint i = 0; i < 4; i++)
            {
                pFrameRGB->data[i] = dataArray[i];
                pFrameRGB->linesize[i] = linesizeArray[i];
            }

            var context = new FFmpegContext
            {
                FormatContext = pFormatContext,
                CodecContext = pCodecContext,
                VideoStreamIndex = videoStreamIndex
            };

            bool eof = false;
            while (!token.IsCancellationRequested)
            {
                if (!eof && ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0)
                {
                    if (pPacket->stream_index == videoStreamIndex)
                    {
                        ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                    }
                    ffmpeg.av_packet_unref(pPacket);
                }
                else
                {
                    ffmpeg.avcodec_send_packet(pCodecContext, null);
                    eof = true;
                }

                while (ffmpeg.avcodec_receive_frame(pCodecContext, pFrame) == 0)
                {
                    ffmpeg.sws_scale(pSwsCtx, pFrame->data, pFrame->linesize, 0, pCodecContext->height,
                        pFrameRGB->data, pFrameRGB->linesize);

                    using var bitmap = new System.Drawing.Bitmap(pCodecContext->width, pCodecContext->height,
                        pFrameRGB->linesize[0], System.Drawing.Imaging.PixelFormat.Format24bppRgb,
                        (IntPtr)pFrameRGB->data[0]);

                    IntPtr hBitmap = bitmap.GetHbitmap();
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(pCodecContext->width, pCodecContext->height));

                    bitmapSource.Freeze();
                    DeleteObject(hBitmap);

                    TimeSpan currentTime = TimeSpan.FromSeconds(pFrame->pts * ffmpeg.av_q2d(pFormatContext->streams[videoStreamIndex]->time_base));
                    TimeSpan totalTime = TimeSpan.FromSeconds(pFormatContext->duration / (double)ffmpeg.AV_TIME_BASE);

                    int frameIndex = (int)(currentTime.TotalSeconds * pCodecContext->framerate.num / (double)pCodecContext->framerate.den);
                    onFrameDecoded(bitmapSource, frameIndex, currentTime, totalTime, context);
                }

                if (eof)
                {
                    onCompleted?.Invoke();
                    break;
                }
            }

            ffmpeg.av_free(buffer);
            ffmpeg.sws_freeContext(pSwsCtx);
            ffmpeg.av_frame_free(&pFrame);
            ffmpeg.av_frame_free(&pFrameRGB);
            ffmpeg.av_packet_free(&pPacket);
            ffmpeg.avcodec_free_context(&pCodecContext);
            ffmpeg.avformat_close_input(&pFormatContext);
        }

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
