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

namespace Wox.Plugin.OpenAI.Translation
{
    public class OpenAiTranslation : IPlugin
    {
        private static readonly string TokenFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openai_token.txt");

        private static readonly string ResponseLogFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "openai_response_log.txt");

        private static CancellationTokenSource debounceCts = new CancellationTokenSource();

        public void Init(PluginInitContext context)
        {
        }

        // 同步的 Query 方法，使用 Task.Result 獲取異步結果
        public List<Result> Query(Query query)
        {
            return QueryAsync(query).Result; // 同步調用異步邏輯
        }

        public async Task<List<Result>> QueryAsync(Query query)
        {
            var results = new List<Result>();
            debounceCts.Cancel(); // 取消之前的請求
            debounceCts = new CancellationTokenSource();

            var parameters = query.Search.Split(' ');

            // 檢查是否是 auth token 的操作
            if (parameters.Length > 1 && parameters[0].Equals("auth", StringComparison.OrdinalIgnoreCase))
            {
                var token = parameters[1];

                // 返回提示，並用 Lambda 表達式來執行 Token 儲存操作
                results.Add(new Result
                {
                    Title = "Press Enter to save OpenAI token.",
                    SubTitle = $"Token: {token}",
                    IcoPath = "Images\\icon.png",
                    Action = context =>
                    {
                        // 儲存 Token 到本地檔案
                        File.WriteAllText(TokenFilePath, token);
                        return true;
                    }
                });
                return results;
            }

            // 檢查是否有已經保存的 token
            var inputText = query.Search.TrimStart("tr ".ToCharArray());

            if (!string.IsNullOrEmpty(inputText) && File.Exists(TokenFilePath))
            {
                var token = File.ReadAllText(TokenFilePath);
                try
                {
                    // 等待 1 秒，允許新的翻譯請求
                    await Task.Delay(1000, debounceCts.Token);

                    var sourceLanguage = DetectLanguage(inputText);
                    var targetLanguage = sourceLanguage == "zh" ? "en" : "zh";
                    var translatedText = await TranslateText(inputText, targetLanguage, token);

                    // 返回翻譯結果，並在按下 Enter 時複製到剪貼板
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
                        // 解析錯誤訊息
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
            var chineseCount = text.Count(c => c >= 0x4e00 && c <= 0x9fff); // 中文字符數
            var otherCount = text.Length - chineseCount;

            // 如果主要是中文字符，則判定為中文，否則為其他語言
            return chineseCount > otherCount ? "zh" : "other";
        }

        public class OpenAiErrorResponse
        {
            public OpenAiError Error { get; set; }
        }

        public class OpenAiError
        {
            public string Message { get; set; }
            public string Type { get; set; }
            public string Param { get; set; }
            public string Code { get; set; }
        }

        public class Choice
        {
            public Message Message { get; set; }
        }

        public class Message
        {
            public string Content { get; set; }
        }

        public class OpenAiResponse
        {
            public List<Choice> Choices { get; set; }
        }
    }
}