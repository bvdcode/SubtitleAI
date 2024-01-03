using Serilog;
using Xabe.FFmpeg;

namespace SubtitleAI.Progress
{
    internal class FFMpegDownloadingProgress(ILogger _logger) : IProgress<ProgressInfo>
    {
        private double _progress;

        public void Report(ProgressInfo value)
        {
            double progress = Math.Round((double)value.DownloadedBytes / value.TotalBytes, 4);
            if (progress <= _progress)
            {
                return;
            }
            _progress = progress;
            _logger.Information("Downloading FFMPEG: {progress:P}", _progress);
        }
    }
}