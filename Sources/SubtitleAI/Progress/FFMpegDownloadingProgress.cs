using Serilog;
using Xabe.FFmpeg;

namespace SubtitleAI.Progress
{
    internal class FFMpegDownloadingProgress(ILogger _logger) : IProgress<ProgressInfo>
    {
        public void Report(ProgressInfo value)
        {
            _logger.Information("Progress: {progress:P}", value.DownloadedBytes / value.TotalBytes);
        }
    }
}