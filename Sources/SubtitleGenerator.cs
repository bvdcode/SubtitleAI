using Serilog;
using Whisper.net;
using System.Text;
using Xabe.FFmpeg;
using Whisper.net.Ggml;
using SubtitleAI.Helpers;
using System.Diagnostics;
using SubtitleAI.Progress;
using Xabe.FFmpeg.Downloader;

namespace SubtitleAI
{
    internal class SubtitleGenerator(string inputFile, ILogger logger)
    {
        private const string _workingDirectory = ".subtitle-ai-cache";
        private const GgmlType _ggmlType = GgmlType.LargeV3Turbo;
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
            _logger.Information("Generating subtitles...");
            return await GenerateSubtitleAsync(processor, cancellationToken);
        }

        private async Task<FileInfo> GenerateSubtitleAsync(WhisperProcessor processor, CancellationToken cancellationToken)
        {
            _logger.Information("Converting media to wave...");
            var waveStream = await ConvertFromMediaToWaveAsync(_inputFile, cancellationToken);
            _logger.Information("Recognizing speech...");
            IEnumerable<SegmentData> speech = await RecognizeAsync(processor, waveStream, cancellationToken);
            _logger.Information("Generating subtitle...");
            string subtitles = GenerateSubtitles(speech);
            string subtitleFile = Path.ChangeExtension(_inputFile, ".srt");
            File.WriteAllText(subtitleFile, subtitles);
            return new FileInfo(subtitleFile);
        }

        private static string GenerateSubtitles(IEnumerable<SegmentData> speech)
        {
            StringBuilder sb = new();
            int index = 1;
            foreach (var segment in speech.DistinctBy(x => x.Text))
            {
                if (string.IsNullOrWhiteSpace(segment.Text))
                {
                    continue;
                }
                sb.AppendLine(index.ToString());
                sb.AppendLine($"{segment.Start:hh\\:mm\\:ss\\,fff} --> {segment.End:hh\\:mm\\:ss\\,fff}");
                sb.AppendLine(segment.Text.Trim());
                sb.AppendLine();
                index++;
            }
            return sb.ToString();
        }

        private async Task<IEnumerable<SegmentData>> RecognizeAsync(WhisperProcessor processor, MemoryStream waveStream, CancellationToken cancellationToken)
        {
            List<SegmentData> resultData = [];
            var processed = processor.ProcessAsync(waveStream, cancellationToken);
            await foreach (var result in processed)
            {
                resultData.Add(result);
                _logger.Information("[{lang}]: {from} -> {to} - {resultText}", result.Language, result.Start, result.End, result.Text);
            }
            return resultData;
        }

        private async Task<MemoryStream> ConvertFromMediaToWaveAsync(string sourceFile, CancellationToken cancellationToken, bool keepTempFiles = false)
        {
            string targetFile = Path.Combine(Environment.CurrentDirectory, _workingDirectory, Guid.NewGuid() + ".wav");
            var conversion = await FFmpeg.Conversions.FromSnippet.ExtractAudio($"\"{sourceFile}\"", $"\"{targetFile}\"");
            conversion.AddParameter("-ar 16000", ParameterPosition.PostInput);
            conversion.AddParameter("-err_detect ignore_err", ParameterPosition.PreInput);
            conversion.OnProgress += (sender, args) =>
            {
                _logger.Information("Converting media to wave: {percent}%", args.Percent);
            };
            var result = await conversion.Start(cancellationToken);
            _logger.Information("Converted - elapsed: {result}", result.Duration);
            var bytes = File.ReadAllBytes(targetFile);
            MemoryStream ms = new(bytes);
            if (!keepTempFiles)
            {
                File.Delete(targetFile);
            }
            return ms;
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
                    await Task.Delay(25, token);
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