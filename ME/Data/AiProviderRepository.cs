using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;
using ME.Services;

namespace ME.Data
{
    /// <summary>
    /// AI 供应商仓库（ai_providers.json）。
    /// </summary>
    public class AiProviderRepository
    {
        private const string FileName = "ai_providers";

        public List<AiProvider> GetAll()
        {
            return JsonStore.Load<AiProvider>(FileName).ToList();
        }

        public AiProvider GetDefault()
        {
            var providers = GetAll();
            return providers.FirstOrDefault(p => p.IsDefault) ?? providers.FirstOrDefault();
        }

        public int Insert(AiProvider provider)
        {
            var providers = JsonStore.Load<AiProvider>(FileName);
            var maxId = providers.Count > 0 ? providers.Max(p => p.Id) : 0;
            provider.Id = maxId + 1;
            providers.Add(provider);
            if (provider.IsDefault || providers.Count == 1)
            {
                foreach (var p in providers) p.IsDefault = p.Id == provider.Id;
            }
            JsonStore.Save(FileName, providers);
            return provider.Id;
        }

        public void Update(AiProvider provider)
        {
            var providers = JsonStore.Load<AiProvider>(FileName);
            var existing = providers.FirstOrDefault(p => p.Id == provider.Id);
            if (existing != null)
            {
                existing.Name = provider.Name;
                if (!string.IsNullOrEmpty(provider.EncryptedApiKey)) existing.EncryptedApiKey = provider.EncryptedApiKey;
                existing.BaseUrl = provider.BaseUrl;
                existing.Model = provider.Model;
                existing.ApiFormat = provider.ApiFormat;
                if (provider.IsDefault)
                {
                    foreach (var p in providers) p.IsDefault = p.Id == provider.Id;
                }
                JsonStore.Save(FileName, providers);
            }
        }

        public void Delete(int id)
        {
            var providers = JsonStore.Load<AiProvider>(FileName);
            var target = providers.FirstOrDefault(p => p.Id == id);
            if (target != null)
            {
                providers.Remove(target);
                JsonStore.Save(FileName, providers);
            }
        }

        /// <summary>确保至少有一个默认 DeepSeek 供应商（首次启动）</summary>
        public List<AiProvider> EnsureDefaultDeepSeek()
        {
            var providers = GetAll();
            if (providers.Count == 0)
            {
                providers = new List<AiProvider>
                {
                    new AiProvider
                    {
                        Name = "DeepSeek",
                        BaseUrl = "https://api.deepseek.com",
                        Model = "deepseek-chat",
                        ApiFormat = AiApiFormat.OpenAI,
                        IsDefault = true,
                        IsBuiltIn = true
                    }
                };
                providers[0].Id = 1;
                JsonStore.Save(FileName, providers);
            }
            return providers;
        }

        /// <summary>读取明文 API Key（解密）</summary>
        public static string GetApiKey(AiProvider p)
        {
            return SecureStore.Decrypt(p.EncryptedApiKey ?? "");
        }
    }
}
