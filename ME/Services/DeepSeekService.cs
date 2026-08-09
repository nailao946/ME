using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ME.Services
{
    /// <summary>
    /// DeepSeek 大模型调用（OpenAI 兼容接口）。
    /// 用于健康数据 AI 分析；API Key 存在设置中（SettingsKeys.DeepSeekApiKey）。
    /// </summary>
    public static class DeepSeekService
    {
        private const string Endpoint = "https://api.deepseek.com/chat/completions";

        public static async Task<string> ChatAsync(string apiKey, string systemPrompt, string userContent, double temperature = 0.7)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("未配置 DeepSeek API Key，请在 设置 → AI 分析 中填写。");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            var payload = new
            {
                model = "deepseek-chat",
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = temperature,
                stream = false
            };
            var body = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var resp = await client.PostAsync(Endpoint, body);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var err = text.Length > 300 ? text.Substring(0, 300) + "…" : text;
                throw new InvalidOperationException($"DeepSeek 请求失败（{(int)resp.StatusCode}）：{err}");
            }

            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
            {
                return content.GetString()?.Trim() ?? "(无返回内容)";
            }
            throw new InvalidOperationException("DeepSeek 返回格式异常");
        }
    }
}
