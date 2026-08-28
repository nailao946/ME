using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ME.Data;

namespace ME.Services
{
    /// <summary>
    /// GitHub 免费云同步：把 %LocalAppData%\ME\JsonData 的 JSON 文件提交到用户自己的私有仓库 data/ 目录，
    /// 与安卓端（ME PE）共用同一套仓库布局与设置键。Token 用 DPAPI 加密保存；
    /// 配置文件放在 JsonData 目录之外，避免随数据一起被上传。
    /// </summary>
    public static class GitHubSyncService
    {
        public class SyncConfig
        {
            public string EncryptedToken { get; set; } = "";
            public string Repo { get; set; } = "";       // owner/name
            public string Branch { get; set; } = "main";
            public string Proxy { get; set; } = "";      // 可选，如 http://127.0.0.1:7897
            public string LastPushAt { get; set; } = "";
            public string LastPullAt { get; set; } = "";
        }

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ME", "sync_config.json");

        public static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ME", "JsonData");

        public static SyncConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(ConfigPath)) ?? new SyncConfig();
            }
            catch { }
            return new SyncConfig();
        }

        public static void Save(SyncConfig c)
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(c, JsonOpts));
        }

        private static HttpClient CreateClient(SyncConfig c)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(c.Proxy))
            {
                try { handler.Proxy = new System.Net.WebProxy(c.Proxy.Trim()); handler.UseProxy = true; } catch { }
            }
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ME-PC");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        private static string Api(string repo, string path) => $"https://api.github.com/repos/{repo}/contents/{path}";

        private static async Task<JsonElement> SendAsync(SyncConfig c, HttpMethod method, string url, object payload = null)
        {
            using var client = CreateClient(c);
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SecureStore.Decrypt(c.EncryptedToken));
            if (payload != null)
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)resp.StatusCode}：{Truncate(text, 240)}");
            if (string.IsNullOrWhiteSpace(text)) return default;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);

        /// <summary>上传 JsonData 全部 JSON 文件（逐文件 commit，已存在则带 sha 更新）</summary>
        public static async Task<string> PushAsync()
        {
            var c = Load();
            if (string.IsNullOrWhiteSpace(c.Repo) || string.IsNullOrWhiteSpace(c.EncryptedToken))
                return "✗ 请先填写仓库名和 Token";
            if (!Directory.Exists(DataDir)) return "✗ 没有可上传的数据";
            var files = Directory.GetFiles(DataDir, "*.json");
            if (files.Length == 0) return "✗ 没有可上传的数据";
            int ok = 0; string lastErr = null;
            foreach (var f in files)
            {
                try
                {
                    var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(File.ReadAllText(f)));
                    string sha = null;
                    try
                    {
                        var existing = await SendAsync(c, HttpMethod.Get, Api(c.Repo, $"data/{Path.GetFileName(f)}?ref={c.Branch}")).ConfigureAwait(false);
                        sha = existing.GetProperty("sha").GetString();
                    }
                    catch { /* 不存在则新建 */ }
                    var payload = new Dictionary<string, object>
                    {
                        ["message"] = $"ME 数据同步（PC）· {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        ["content"] = content,
                        ["branch"] = string.IsNullOrWhiteSpace(c.Branch) ? "main" : c.Branch
                    };
                    if (sha != null) payload["sha"] = sha;
                    await SendAsync(c, HttpMethod.Put, Api(c.Repo, $"data/{Path.GetFileName(f)}"), payload).ConfigureAwait(false);
                    ok++;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            if (ok == files.Length)
            {
                c.LastPushAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save(c);
                return $"✓ 已上传 {ok} 个文件";
            }
            return $"已上传 {ok}/{files.Length} 个" + (lastErr != null ? "，错误：" + lastErr : "");
        }

        /// <summary>从仓库 data/ 目录下载并覆盖本地（先备份本地 JsonData）</summary>
        public static async Task<string> PullAsync()
        {
            var c = Load();
            if (string.IsNullOrWhiteSpace(c.Repo) || string.IsNullOrWhiteSpace(c.EncryptedToken))
                return "✗ 请先填写仓库名和 Token";
            JsonElement listing;
            try
            {
                listing = await SendAsync(c, HttpMethod.Get, Api(c.Repo, $"data?ref={c.Branch}")).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex.Message.Contains("404"))
            {
                return "仓库 data 目录为空，没有可下载的数据";
            }
            if (listing.ValueKind != JsonValueKind.Array || listing.GetArrayLength() == 0)
                return "仓库 data 目录为空，没有可下载的数据";

            // 本地备份
            if (Directory.Exists(DataDir))
            {
                var backup = DataDir + $"_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                Directory.CreateDirectory(backup);
                foreach (var f in Directory.GetFiles(DataDir, "*.json"))
                    File.Copy(f, Path.Combine(backup, Path.GetFileName(f)), true);
            }
            Directory.CreateDirectory(DataDir);

            int n = 0; int total = 0; string lastErr = null;
            foreach (var item in listing.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                if (string.IsNullOrEmpty(name) || !name.EndsWith(".json")) continue;
                total++;
                try
                {
                    var detail = await SendAsync(c, HttpMethod.Get, Api(c.Repo, $"data/{name}?ref={c.Branch}")).ConfigureAwait(false);
                    var b64 = detail.GetProperty("content").GetString() ?? "";
                    var text = Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace("\n", "")));
                    File.WriteAllText(Path.Combine(DataDir, name), text);
                    JsonStore.InvalidateCache(Path.GetFileNameWithoutExtension(name));
                    n++;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            if (n > 0)
            {
                c.LastPullAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save(c);
            }
            if (n == total && n > 0) return $"✓ 已下载 {n} 个文件（原数据已备份）";
            return $"已下载 {n}/{total} 个" + (lastErr != null ? "，错误：" + lastErr : "");
        }
    }
}
