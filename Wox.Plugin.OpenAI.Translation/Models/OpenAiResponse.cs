using System.Collections.Generic;

namespace Wox.Plugin.OpenAI.Translation.Models
{
    public class OpenAiResponse
    {
        public List<Choice> Choices { get; set; }
    }
}