namespace Wox.Plugin.OpenAI.Translation.Models
{
    public class OpenAiError
    {
        public string Message { get; set; }
        public string Type { get; set; }
        public string Param { get; set; }
        public string Code { get; set; }
    }
}