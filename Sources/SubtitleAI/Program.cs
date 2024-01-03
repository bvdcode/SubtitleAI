using Serilog;

namespace SubtitleAI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
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
        }
    }
}