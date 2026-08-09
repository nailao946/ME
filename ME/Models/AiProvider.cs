namespace ME.Models
{
    /// <summary>API 格式</summary>
    public enum AiApiFormat
    {
        OpenAI,     // OpenAI 兼容 /chat/completions
        Anthropic,  // Anthropic /v1/messages
        Custom      // 自定义（仅 OpenAI 兼容结构）
    }

    /// <summary>
    /// 第三方 AI 供应商配置，存于 ai_providers.json。
    /// API Key 用 DPAPI 加密后存储。
    /// </summary>
    public class AiProvider
    {
        public int Id { get; set; }

        /// <summary>供应商名称，如"DeepSeek""通义千问""智谱"</summary>
        public string Name { get; set; }

        /// <summary>DPAPI 加密后的 API Key</summary>
        public string EncryptedApiKey { get; set; }

        /// <summary>请求地址，如 https://api.deepseek.com（可含 /chat/completions）</summary>
        public string BaseUrl { get; set; }

        /// <summary>模型名称，如 deepseek-chat</summary>
        public string Model { get; set; }

        public AiApiFormat ApiFormat { get; set; } = AiApiFormat.OpenAI;

        public bool IsDefault { get; set; }

        public bool IsBuiltIn { get; set; }
    }
}
