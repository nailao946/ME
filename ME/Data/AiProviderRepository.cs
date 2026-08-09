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
            if (providers.Count == 0) return null;
            // 优先返回"已填 API Key"的供应商：默认有 key → 其次任意有 key → 再默认 → 再第一个。
            // 避免出现"选了带 Key 的供应商，但健康页却命中无 Key 的内置 DeepSeek"的情况
            var withKey = providers.Where(p => !string.IsNullOrWhiteSpace(GetApiKey(p))).ToList();
            var dfltWithKey = withKey.FirstOrDefault(p => p.IsDefault);
            if (dfltWithKey != null) return dfltWithKey;
            if (withKey.Count > 0) return withKey[0];
            return providers.FirstOrDefault(p => p.IsDefault) ?? providers[0];
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
                // 无条件同步"设为默认"，允许取消默认；同时保证至少有一个默认
                existing.IsDefault = provider.IsDefault;
                if (!providers.Any(p => p.IsDefault))
                    existing.IsDefault = true;
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

        /// <summary>确保至少有一个默认 DeepSeek 供应商（首次启动），并迁移旧版 DeepSeekApiKey 设置</summary>
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

                // 迁移旧版 DeepSeek API Key（v1.9.19 及之前存于 settings 键，DPAPI 加密）
                try
                {
                    var settingsRepo = new SettingsRepository();
                    var oldKey = settingsRepo.GetValue(SettingsKeys.DeepSeekApiKey, "");
                    if (!string.IsNullOrEmpty(oldKey))
                        providers[0].EncryptedApiKey = oldKey; // 已是 DPAPI 密文，直接沿用
                }
                catch { }

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
