namespace SubtitleAI
{
    internal class Program
    {
        static async void Main(string[] args)
        {
            const string inputFile = "";

            SubtitleGenerator generator = new(inputFile);
            FileInfo result = await generator.GenerateSubtitleAsync();
        }
    }
}
