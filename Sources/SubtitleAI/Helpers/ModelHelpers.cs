using Whisper.net.Ggml;

namespace SubtitleAI.Helpers
{
    public static class ModelHelpers
    {
        public static async Task<HttpResponseMessage> GetModel(GgmlType type)
        {
            string modelName = GetModelName(type);
            string requestUri = $"https://huggingface.co/sandrohanea/whisper.net/resolve/v2/classic/{modelName}.bin";
            HttpClient http = new();
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }

        private static string GetModelName(GgmlType type)
        {
            return type switch
            {
                GgmlType.Tiny => "ggml-tiny",
                GgmlType.TinyEn => "ggml-tiny.en",
                GgmlType.Base => "ggml-base",
                GgmlType.BaseEn => "ggml-base.en",
                GgmlType.Small => "ggml-small",
                GgmlType.SmallEn => "ggml-small.en",
                GgmlType.Medium => "ggml-medium",
                GgmlType.MediumEn => "ggml-medium.en",
                GgmlType.LargeV1 => "ggml-large-v1",
                GgmlType.LargeV2 => "ggml-large-v2",
                GgmlType.LargeV3 => "ggml-large-v3",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }
    }
}