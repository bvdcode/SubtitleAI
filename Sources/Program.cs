using Serilog;
using System.Diagnostics;

namespace SubtitleAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            FileInfo inputFile = new(args[0]);
            if (!inputFile.Exists)
            {
                Log.Logger.Error("Input file does not exist");
                Environment.Exit(exitCode: 1);
            }

            CancellationTokenSource cancellationTokenSource = new();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationTokenSource.Cancel();
            };

            SubtitleGenerator generator = new(inputFile.FullName, Log.Logger);
            FileInfo result = await generator.GenerateSubtitleAsync(cancellationTokenSource.Token);
            Log.Logger.Information($"Subtitle file generated at {result.FullName}");
            // ffmpeg -i S45E03.mkv -i S45E03.srt -c copy -c:s copy -map 0 -map 1 -map_metadata 0 -map_chapters 0 -metadata:s:s:1 language=en S45E03SRT.mkv
            string output = Path.ChangeExtension(inputFile.FullName, ".eng.mkv");
            ProcessStartInfo startInfo = new()
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputFile.FullName}\" -i \"{result.FullName}\" -c copy -c:s copy -map 0 -map 1 -map_metadata 0 -map_chapters 0 -metadata:s:s:0 language=eng \"{output}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // log output to console
            Process process = new()
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) Log.Logger.Information(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Log.Logger.Error(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationTokenSource.Token);
            // delete the generated subtitle file and move result to input file
            //result.Delete();
            //inputFile.Delete();
            Log.Logger.Information($"Subtitle file injected into {output}");
        }
    }
}