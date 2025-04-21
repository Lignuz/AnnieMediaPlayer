using FFmpeg.AutoGen;
using System.Drawing;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    public static class FFmpegLoader
    {
        public static void RegisterFFmpeg()
        {
            ffmpeg.RootPath = "ffmpeg"; // 또는 DLL이 있는 폴더 경로
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();
        }
    }

    public static class FFmpegHelper
    {
        public unsafe static void OpenVideo(string filePath, Action<BitmapSource, int> onFrameDecoded, CancellationToken token)
        {
            AVFormatContext* pFormatContext = ffmpeg.avformat_alloc_context();

            if (ffmpeg.avformat_open_input(&pFormatContext, filePath, null, null) != 0)
                throw new ApplicationException("파일 열기 실패");

            if (ffmpeg.avformat_find_stream_info(pFormatContext, null) != 0)
                throw new ApplicationException("스트림 정보 찾기 실패");

            AVCodec* pCodec = null;
            int videoStreamIndex = -1;

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
                throw new ApplicationException("비디오 스트림을 찾을 수 없습니다");

            AVCodecContext* pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
            ffmpeg.avcodec_parameters_to_context(pCodecContext, pFormatContext->streams[videoStreamIndex]->codecpar);
            ffmpeg.avcodec_open2(pCodecContext, pCodec, null);

            AVFrame* pFrame = ffmpeg.av_frame_alloc();
            AVFrame* pFrameRGB = ffmpeg.av_frame_alloc();
            AVPacket* pPacket = ffmpeg.av_packet_alloc();

            int width = pCodecContext->width;
            int height = pCodecContext->height;

            SwsContext* pSwsCtx = ffmpeg.sws_getContext(width, height, pCodecContext->pix_fmt,
                                                        width, height, AVPixelFormat.AV_PIX_FMT_BGR24,
                                                        ffmpeg.SWS_BILINEAR, null, null, null);

            int bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGR24, width, height, 1);
            byte* buffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);
            //ffmpeg.av_image_fill_arrays(ref pFrameRGB->data[0], ref pFrameRGB->linesize[0], buffer, AVPixelFormat.AV_PIX_FMT_BGR24, width, height, 1);

            // FFmpeg.AutoGen에서 제공하는 4포인터 배열 구조체
            byte_ptrArray4 dataArray = new byte_ptrArray4();
            int_array4 linesizeArray = new int_array4();

            // ref로 직접 넘기기 (배열 전체 구조체를 ref로 넘기는 것이지, 인덱싱하지 않음)
            ffmpeg.av_image_fill_arrays(ref dataArray, ref linesizeArray, buffer,
                AVPixelFormat.AV_PIX_FMT_BGR24, width, height, 1);

            // 배열 복사
            for (uint i = 0; i < 4; i++)
            {
                pFrameRGB->data[i] = dataArray[i];
                pFrameRGB->linesize[i] = linesizeArray[i];
            }

            int frameCount = 0;

            while (ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0)
            {
                if (token.IsCancellationRequested)
                    break;

                if (pPacket->stream_index == videoStreamIndex)
                {
                    if (ffmpeg.avcodec_send_packet(pCodecContext, pPacket) == 0)
                    {
                        while (ffmpeg.avcodec_receive_frame(pCodecContext, pFrame) == 0)
                        {
                            ffmpeg.sws_scale(pSwsCtx, pFrame->data, pFrame->linesize, 0, height,
                                             pFrameRGB->data, pFrameRGB->linesize);

                            var bitmap = new Bitmap(width, height, pFrameRGB->linesize[0],
                                System.Drawing.Imaging.PixelFormat.Format24bppRgb,
                                (IntPtr)pFrameRGB->data[0]);

                            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                bitmap.GetHbitmap(), IntPtr.Zero, System.Windows.Int32Rect.Empty,
                                BitmapSizeOptions.FromWidthAndHeight(width, height));

                            bitmapSource.Freeze(); // UI 스레드 전송을 위해 freeze

                            bitmap.Dispose();

                            onFrameDecoded(bitmapSource, frameCount++);
                        }
                    }
                }
                ffmpeg.av_packet_unref(pPacket);
            }

            // 자원 해제
            ffmpeg.sws_freeContext(pSwsCtx);
            ffmpeg.av_free(buffer);
            ffmpeg.av_frame_free(&pFrame);
            ffmpeg.av_frame_free(&pFrameRGB);
            ffmpeg.av_packet_free(&pPacket);
            ffmpeg.avcodec_free_context(&pCodecContext);
            ffmpeg.avformat_close_input(&pFormatContext);
        }
    }
}
