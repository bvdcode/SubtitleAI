

namespace SubtitleAI
{
    internal class SubtitleGenerator
    {
        private string inputFile;

        public SubtitleGenerator(string inputFile)
        {
            this.inputFile = inputFile;
        }

        internal async Task<FileInfo> GenerateSubtitleAsync()
        {
            await CheckLibrariesAsync();
        }

        private Task CheckLibrariesAsync()
        {
            throw new NotImplementedException();
        }
    }
}