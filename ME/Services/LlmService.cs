using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    /// <summary>
    /// 通用大模型调用服务，支持 OpenAI 兼容 / Anthropic 格式。
    /// 供应商配置见 ai_providers.json（AiProviderRepository）。
    /// </summary>
    public static class LlmService
    {
        public static async Task<string> ChatAsync(AiProvider provider, string systemPrompt, string userContent, double temperature = 0.7)
        {
            if (provider == null)
                throw new InvalidOperationException("未配置 AI 供应商，请到 设置 → AI 分析 中添加并设置 API Key。");
            var apiKey = AiProviderRepository.GetApiKey(provider);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException($"供应商「{provider.Name}」未填写 API Key，请到 设置 → AI 分析 中填写。");
            if (string.IsNullOrWhiteSpace(provider.BaseUrl))
                throw new InvalidOperationException($"供应商「{provider.Name}」未填写请求地址。");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(90);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            if (provider.ApiFormat == AiApiFormat.Anthropic)
                return await CallAnthropicAsync(client, provider, systemPrompt, userContent, temperature);
            return await CallOpenAIAsync(client, provider, systemPrompt, userContent, temperature);
        }

        private static string NormalizeOpenAIEndpoint(AiProvider provider)
        {
            var url = provider.BaseUrl.Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url;
            if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                return url + "/chat/completions";
            return url + "/chat/completions";
        }

        private static async Task<string> CallOpenAIAsync(HttpClient client, AiProvider provider, string systemPrompt, string userContent, double temperature)
        {
            var payload = new
            {
                model = provider.Model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = temperature,
                stream = false
            };
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(NormalizeOpenAIEndpoint(provider), body);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var err = text.Length > 300 ? text.Substring(0, 300) + "…" : text;
                throw new InvalidOperationException($"「{provider.Name}」请求失败（{(int)resp.StatusCode}）：{err}");
            }
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
            {
                return content.GetString()?.Trim() ?? "(无返回内容)";
            }
            throw new InvalidOperationException($"「{provider.Name}」返回格式异常（非 OpenAI 兼容结构）");
        }

        private static async Task<string> CallAnthropicAsync(HttpClient client, AiProvider provider, string systemPrompt, string userContent, double temperature)
        {
            var baseUrl = provider.BaseUrl.Trim().TrimEnd('/');
            var endpoint = baseUrl.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase)
                ? baseUrl : baseUrl + "/v1/messages";
            var payload = new
            {
                model = provider.Model,
                max_tokens = 2048,
                system = systemPrompt,
                temperature = temperature,
                messages = new object[]
                {
                    new { role = "user", content = userContent }
                }
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(endpoint, body);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var err = text.Length > 300 ? text.Substring(0, 300) + "…" : text;
                throw new InvalidOperationException($"「{provider.Name}」请求失败（{(int)resp.StatusCode}）：{err}");
            }
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("content", out var contentArr) &&
                contentArr.GetArrayLength() > 0 &&
                contentArr[0].TryGetProperty("text", out var txt))
            {
                return txt.GetString()?.Trim() ?? "(无返回内容)";
            }
            throw new InvalidOperationException($"「{provider.Name}」返回格式异常（非 Anthropic 结构）");
        }
    }
}
