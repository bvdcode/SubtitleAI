using Serilog;
using Whisper.net;
using Xabe.FFmpeg;
using Whisper.net.Ggml;
using SubtitleAI.Helpers;
using System.Diagnostics;
using Xabe.FFmpeg.Downloader;

namespace SubtitleAI
{
    internal class SubtitleGenerator(string inputFile, ILogger logger)
    {
        private const string _workingDirectory = ".subtitle-ai-cache";
        private const GgmlType _ggmlType = GgmlType.LargeV3;
        private readonly string _inputFile = inputFile;
        private readonly ILogger _logger = logger;

        internal async Task<FileInfo> GenerateSubtitleAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_workingDirectory))
            {
                var createdFolder = Directory.CreateDirectory(_workingDirectory);
                createdFolder.Attributes = FileAttributes.Directory | FileAttributes.Hidden;
            }
            _logger.Information("Checking libraries...");
            await CheckFfmpegAsync();
            var processor = await CreateWhisperAsync(cancellationToken);

            return new FileInfo("");
        }

        private async Task<WhisperProcessor> CreateWhisperAsync(CancellationToken token)
        {
            _logger.Information("Creating WhisperProcessor...");
            string modelName = $"ggml-{_ggmlType.ToString().ToLower()}.bin";

            if (!Directory.Exists(_workingDirectory))
            {
                Directory.CreateDirectory(_workingDirectory);
            }

            string filePath = Path.Combine(_workingDirectory, modelName);
            FileInfo fileInfo = new(filePath);
            using var modelStream = await ModelHelpers.GetModel(_ggmlType);
            long totalBytes = modelStream.Content.Headers.ContentLength ?? 0;
            if (fileInfo.Exists && fileInfo.Length != totalBytes)
            {
                _logger.Information("Model size mismatch - deleting model");
                fileInfo.Delete();
            }
            if (!fileInfo.Exists)
            {
                _logger.Information("Downloading model: {_ggmlType}", _ggmlType);
                using var fileWriter = fileInfo.Create();
                var source = await modelStream.Content.ReadAsStreamAsync(token);
                StartProgress(fileWriter, totalBytes, token);
                await source.CopyToAsync(fileWriter, token);
                _logger.Information("Model downloaded: {filePath}", filePath);
            }
            else
            {
                _logger.Information("Model already exists: {filePath}", filePath);
            }
            try
            {
                WhisperFactory whisperFactory = WhisperFactory.FromPath(filePath);
                return whisperFactory
                    .CreateBuilder()
                    .WithLanguage("auto")
                    .Build();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while creating WhisperProcessor");
                throw;
            }
        }

        private void StartProgress(Stream modelStream, long totalBytes, CancellationToken token)
        {
            Task.Run(async () =>
            {
                double previousProgress = 0;

                while (!token.IsCancellationRequested)
                {
                    double progress = modelStream.Position / (double)totalBytes;
                    double roundedProgress = Math.Round(progress, 4);
                    if (roundedProgress != previousProgress)
                    {
                        _logger.Information("Downloading model: {progress:P}", roundedProgress);
                        previousProgress = roundedProgress;
                    }
                    await Task.Delay(1000, token);
                }
            }, token);
        }

        private async Task CheckFfmpegAsync()
        {
            string ffmpegPath = Path.Combine(_workingDirectory, "ffmpeg");
            if (!Directory.Exists(ffmpegPath))
            {
                Directory.CreateDirectory(ffmpegPath);
            }
            FFmpeg.SetExecutablesPath(ffmpegPath);
            _logger.Information("Checking FFmpeg...");
            if (Directory.GetFiles(ffmpegPath).Length == 0)
            {
                _logger.Information("FFmpeg not found - downloading...");
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, FFmpeg.ExecutablesPath, new FFMpegDownloadingProgress(Log.Logger));
                _logger.Information("FFmpeg downloaded");
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    Exec("chmod +x " + Path.Combine(ffmpegPath, "ffmpeg"));
                    Exec("chmod +x " + Path.Combine(ffmpegPath, "ffprobe"));
                }
            }
        }

        private void Exec(string cmd)
        {
            var escapedArgs = cmd.Replace("\"", "\\\"");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\""
                }
            };

            try
            {
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while executing command: {cmd}", cmd);
            }
        }
    }
}