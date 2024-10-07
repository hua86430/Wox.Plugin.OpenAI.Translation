using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Wox.Plugin.OpenAI.Translation.Models;

namespace Wox.Plugin.OpenAI.Translation
{
    public class OpenAiTranslation : IPlugin
    {
        private static readonly string TokenFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openai_token.txt");

        private static readonly string ResponseLogFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "openai_response_log.txt");

        private static CancellationTokenSource _debounceCts = new CancellationTokenSource();

        public void Init(PluginInitContext context)
        {
        }

        public List<Result> Query(Query query)
        {
            return QueryAsync(query).Result;
        }

        public async Task<List<Result>> QueryAsync(Query query)
        {
            var results = new List<Result>();
            _debounceCts.Cancel();
            _debounceCts = new CancellationTokenSource();

            var parameters = query.Search.Split(' ');

            if (parameters.Length > 1 && parameters[0].Equals("auth", StringComparison.OrdinalIgnoreCase))
            {
                var token = parameters[1];

                results.Add(new Result
                {
                    Title = "Press Enter to save OpenAI token.",
                    SubTitle = $"Token: {token}",
                    IcoPath = "Images\\icon.png",
                    Action = context =>
                    {
                        File.WriteAllText(TokenFilePath, token);
                        return true;
                    }
                });
                return results;
            }

            var inputText = query.Search.TrimStart("tr ".ToCharArray());

            if (!string.IsNullOrEmpty(inputText) && File.Exists(TokenFilePath))
            {
                var token = File.ReadAllText(TokenFilePath);
                try
                {
                    await Task.Delay(1000, _debounceCts.Token);

                    var sourceLanguage = DetectLanguage(inputText);
                    var targetLanguage = sourceLanguage == "zh" ? "en" : "zh";
                    var translatedText = await TranslateText(inputText, targetLanguage, token);

                    results.Add(new Result
                    {
                        Title = translatedText,
                        SubTitle = "Press Enter to copy to clipboard.",
                        IcoPath = "Images\\icon.png",
                        Action = context =>
                        {
                            Clipboard.SetText(translatedText);
                            return true;
                        }
                    });
                }
                catch (TaskCanceledException)
                {
                    results.Add(new Result
                    {
                        Title = "Got Error: Please see 'openai_response_log.txt' for more details.",
                        SubTitle = "Press Enter to copy to clipboard.",
                        IcoPath = "Images\\icon.png"
                    });
                }
            }
            else
            {
                results.Add(new Result
                {
                    Title = "Please enter text to translate.",
                    IcoPath = "Images\\icon.png"
                });
            }

            return results;
        }

        private async Task<string> TranslateText(string text, string targetLanguage, string token)
        {
            if (text == null) return string.Empty;

            var apiUrl = "https://api.openai.com/v1/chat/completions";
            var prompt = targetLanguage == "en"
                ? $"Translate the following text to English, and do not mention anything about training data or knowledge cutoff date:\n\n{text}"
                : $"Translate the following text to Traditional Chinese, and do not mention anything about training data or knowledge cutoff date:\n\n{text}";
            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = prompt }
                },
                max_tokens = 1000,
                temperature = 0.2
            };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8,
                    "application/json");

                try
                {
                    var response = await client.PostAsync(apiUrl, content);
                    var responseString = await response.Content.ReadAsStringAsync();
                    File.AppendAllText(ResponseLogFilePath, responseString);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorResponse = JsonConvert.DeserializeObject<OpenAiErrorResponse>(responseString);
                        return $"Error: {errorResponse.Error.Message}";
                    }

                    var responseObject = JsonConvert.DeserializeObject<OpenAiResponse>(responseString);
                    return responseObject.Choices[0].Message.Content.Trim();
                }
                catch (HttpRequestException ex)
                {
                    return $"Request failed: {ex.Message}";
                }
            }
        }

        private string DetectLanguage(string text)
        {
            var chineseCount = text.Count(c => c >= 0x4e00 && c <= 0x9fff);
            var otherCount = text.Length - chineseCount;

            return chineseCount > otherCount ? "zh" : "other";
        }
    }
}