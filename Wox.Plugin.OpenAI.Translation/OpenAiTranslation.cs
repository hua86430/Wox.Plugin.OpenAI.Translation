using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Wox.Plugin.OpenAI.Translation
{
    public class OpenAiTranslation : IPlugin
    {
        private static readonly string TokenFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openai_token.txt");

        public void Init(PluginInitContext context)
        {
        }

        public List<Result> Query(Query query)
        {
            var results = new List<Result>();
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
            }
            // 檢查是否有已經保存的 token
            else if (File.Exists(TokenFilePath))
            {
                var token = File.ReadAllText(TokenFilePath);
                var inputText = query.Search.TrimStart("tr ".ToCharArray());

                // 檢查翻譯文本是否非空
                if (!string.IsNullOrEmpty(inputText))
                {
                    // 修改語言檢測邏輯，將 "zh" 保留為中文，其他語言統一翻譯為中文
                    var sourceLanguage = DetectLanguage(inputText);
                    var targetLanguage = sourceLanguage == "zh" ? "en" : "zh"; // 中文翻譯成英文，其他語言翻譯成中文
                    var translatedText = TranslateText(inputText, targetLanguage, token).Result;

                    results.Add(new Result
                    {
                        Title = translatedText,
                        SubTitle = "Translated text",
                        IcoPath = "Images\\icon.png"
                    });
                }
                else
                {
                    results.Add(new Result
                    {
                        Title = "Please enter text to translate.",
                        IcoPath = "Images\\icon.png"
                    });
                }
            }
            // 如果沒有 token，提醒用戶進行 token 設定
            else
            {
                results.Add(new Result
                {
                    Title = "Please enter ［tr auth {token}］ to save your OpenAI token",
                    SubTitle = "OpenAI token is required for translation.",
                    IcoPath = "Images\\icon.png"
                });
            }

            return results;
        }

        private async Task<string> TranslateText(string text, string targetLanguage, string token)
        {
            var apiUrl = "https://api.openai.com/v1/chat/completions";
            var prompt = targetLanguage == "en"
                ? $"Translate the following text to English:\n\n{text}"
                : $"Translate the following text to Traditional Chinese:\n\n{text}";

            var requestBody = new
            {
                model = "gpt-3.5-turbo",
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

                var response = await client.PostAsync(apiUrl, content);
                var responseString = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<OpenAIResponse>(responseString);
                return responseObject.choices[0].message.content.Trim();
            }
        }

        private string DetectLanguage(string text)
        {
            var chineseCount = text.Count(c => c >= 0x4e00 && c <= 0x9fff); // 中文字符數
            var otherCount = text.Length - chineseCount;

            // 如果主要是中文字符，則判定為中文，否則為其他語言
            return chineseCount > otherCount ? "zh" : "other";
        }


        public class Choice
        {
            public Message message { get; set; }
        }

        public class Message
        {
            public string content { get; set; }
        }

        public class OpenAIResponse
        {
            public List<Choice> choices { get; set; }
        }
    }
}