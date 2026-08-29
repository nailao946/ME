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
            public string AccountName { get; set; } = ""; // 授权后显示的 GitHub 用户名
            // 每个文件上次同步后的云端 sha，用于检测「云端比本地新」，避免覆盖其它设备的更新
            public Dictionary<string, string> FileShas { get; set; } = new Dictionary<string, string>();
        }

        /// <summary>设备码授权会话（GitHub Device Flow，用户只需在网页登录后输入代码点允许）</summary>
        public class DeviceFlowSession
        {
            public string DeviceCode { get; set; }
            public string UserCode { get; set; }
            public string VerificationUri { get; set; }
            public int Interval { get; set; } = 5;
            public DateTime ExpiresAt { get; set; }
        }

        private const string OAuthClientId = "Ov23liBQpCTtMnMWyzsa";

        /// <summary>开始设备码授权：请求 device code，返回会话（应随即打开浏览器）</summary>
        public static async Task<DeviceFlowSession> LoginStartAsync()
        {
            var c = Load();
            using var client = CreateClient(c);
            var payload = new Dictionary<string, string> { ["client_id"] = OAuthClientId, ["scope"] = "repo" };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = new FormUrlEncodedContent(payload);
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {(int)resp.StatusCode}：{Truncate(text, 200)}");
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            return new DeviceFlowSession
            {
                DeviceCode = r.GetProperty("device_code").GetString(),
                UserCode = r.GetProperty("user_code").GetString(),
                VerificationUri = r.GetProperty("verification_uri").GetString(),
                Interval = r.TryGetProperty("interval", out var it) ? it.GetInt32() : 5,
                ExpiresAt = DateTime.Now.AddSeconds(r.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 900)
            };
        }

        /// <summary>轮询一次授权结果。返回值：null=仍在等待用户授权；其他=token 或错误信息（以 ! 开头表示错误）</summary>
        public static async Task<string> LoginPollAsync(DeviceFlowSession s)
        {
            var c = Load();
            using var client = CreateClient(c);
            var payload = new Dictionary<string, string>
            {
                ["client_id"] = OAuthClientId,
                ["device_code"] = s.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = new FormUrlEncodedContent(payload);
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (r.TryGetProperty("error", out var err))
            {
                var code = err.GetString();
                if (code == "authorization_pending") return null;
                if (code == "slow_down") { s.Interval += 5; return null; }
                if (code == "expired_token") return "!授权码已过期，请重新开始";
                return "!授权失败：" + code;
            }
            var token = r.GetProperty("access_token").GetString();
            // 拉取用户名用于显示，失败不影响登录
            c.EncryptedToken = SecureStore.Encrypt(token);
            string account = "";
            try { account = await FetchLoginAsync(c).ConfigureAwait(false); } catch { }
            c.EncryptedToken = SecureStore.Encrypt(token);
            c.AccountName = account;
            Save(c);
            return token;
        }

        /// <summary>打开浏览器进入授权页</summary>
        public static void OpenLoginPage(DeviceFlowSession s)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(s.VerificationUri) { UseShellExecute = true }); } catch { }
        }

        /// <summary>退出登录：清除 token 与账号</summary>
        public static void Logout()
        {
            var c = Load();
            c.EncryptedToken = "";
            c.AccountName = "";
            Save(c);
            _resolvedRepo = null;
        }

        /// <summary>带鉴权头拉取当前登录的 GitHub 用户名（顺带缓存到配置）</summary>
        private static async Task<string> FetchLoginAsync(SyncConfig c)
        {
            using var client = CreateClient(c);
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SecureStore.Decrypt(c.EncryptedToken));
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"获取账号失败：HTTP {(int)resp.StatusCode} {Truncate(text, 160)}");
            using var doc = JsonDocument.Parse(text);
            var login = doc.RootElement.GetProperty("login").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(login)) throw new Exception("无法获取 GitHub 用户名");
            c.AccountName = login;
            Save(c);
            return login;
        }

        /// <summary>已保存 Token 时补拉账号名（用于启动后恢复登录状态显示）</summary>
        public static Task RefreshAccountAsync()
        {
            var c = Load();
            if (string.IsNullOrWhiteSpace(c.EncryptedToken)) return Task.CompletedTask;
            return FetchLoginAsync(c);
        }

        /// <summary>
        /// 确保同步仓库存在：默认 ME-OKR（私有），用户只填仓库名时自动挂到当前账号下（已存在则直接使用）。
        /// 登录后和上传/下载前调用，用户无需手填 owner/ 前缀。
        /// </summary>
        public static async Task<string> EnsureDefaultRepoAsync()
        {
            var c = Load();
            if (string.IsNullOrWhiteSpace(c.EncryptedToken)) throw new Exception("尚未登录 GitHub 账号");
            if (string.IsNullOrWhiteSpace(c.Repo)) c.Repo = "ME-OKR";
            var name = c.Repo.Contains('/') ? c.Repo.Substring(c.Repo.IndexOf('/') + 1) : c.Repo;
            if (string.IsNullOrWhiteSpace(name)) name = "ME-OKR";

            var login = string.IsNullOrWhiteSpace(c.AccountName) ? await FetchLoginAsync(c).ConfigureAwait(false) : c.AccountName;

            // 创建私有仓库（HTTP 422 = 已存在，直接使用）
            try
            {
                var payload = new Dictionary<string, object> { ["name"] = name, ["private"] = true, ["auto_init"] = false };
                using var client = CreateClient(c);
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/user/repos")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SecureStore.Decrypt(c.EncryptedToken));
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 422)
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new Exception($"创建仓库失败：HTTP {(int)resp.StatusCode} {Truncate(text, 160)}");
                }
            }
            catch (Exception ex) when (ex.Message.Contains("422")) { /* 已存在 */ }

            if (string.IsNullOrWhiteSpace(c.Branch)) c.Branch = "main";
            Save(c);
            _resolvedRepo = $"{login}/{name}";
            return _resolvedRepo;
        }

        /// <summary>把用户填的仓库名解析成 owner/name：只填 ME-OKR 时自动补当前账号前缀</summary>
        private static async Task<string> ResolveRepoAsync(SyncConfig c)
        {
            if (string.IsNullOrWhiteSpace(c.Repo)) c.Repo = "ME-OKR";
            if (c.Repo.Contains('/')) return c.Repo;
            if (_resolvedRepo != null && _resolvedRepo.EndsWith("/" + c.Repo)) return _resolvedRepo;
            var login = string.IsNullOrWhiteSpace(c.AccountName) ? await FetchLoginAsync(c).ConfigureAwait(false) : c.AccountName;
            _resolvedRepo = $"{login}/{c.Repo}";
            return _resolvedRepo;
        }
        private static string _resolvedRepo;

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

        /// <summary>上传 JsonData 全部 JSON 文件（逐文件 commit，已存在则带 sha 更新）。
        /// 防覆盖：若某文件云端 sha 与上次同步记录不一致（其它设备改过），跳过该文件并提示先下载。</summary>
        public static async Task<string> PushAsync()
        {
            var c = Load();
            if (string.IsNullOrWhiteSpace(c.EncryptedToken))
                return "✗ 请先登录 GitHub 账号或填写 Token";
            if (string.IsNullOrWhiteSpace(c.Repo))
            {
                try { await EnsureDefaultRepoAsync().ConfigureAwait(false); c = Load(); }
                catch (Exception ex) { return "✗ " + ex.Message; }
            }
            if (!Directory.Exists(DataDir)) return "✗ 没有可上传的数据";
            var files = Directory.GetFiles(DataDir, "*.json");
            if (files.Length == 0) return "✗ 没有可上传的数据";
            string repo;
            try { repo = await ResolveRepoAsync(c).ConfigureAwait(false); }
            catch (Exception ex) { return "✗ " + ex.Message; }
            int ok = 0; int skipped = 0; string lastErr = null;
            var newShas = new Dictionary<string, string>(c.FileShas);
            foreach (var f in files)
            {
                try
                {
                    var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(File.ReadAllText(f)));
                    string sha = null;
                    try
                    {
                        var existing = await SendAsync(c, HttpMethod.Get, Api(repo, $"data/{Path.GetFileName(f)}?ref={c.Branch}")).ConfigureAwait(false);
                        sha = existing.GetProperty("sha").GetString();
                    }
                    catch { /* 不存在则新建 */ }

                    // 云端被其它设备更新过而本地没有先下载 → 跳过，避免覆盖
                    if (c.FileShas.TryGetValue(Path.GetFileName(f), out var known) && sha != null && known != sha)
                    {
                        skipped++;
                        continue;
                    }

                    var payload = new Dictionary<string, object>
                    {
                        ["message"] = $"ME 数据同步（PC）· {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        ["content"] = content,
                        ["branch"] = string.IsNullOrWhiteSpace(c.Branch) ? "main" : c.Branch
                    };
                    if (sha != null) payload["sha"] = sha;
                    var putResp = await SendAsync(c, HttpMethod.Put, Api(repo, $"data/{Path.GetFileName(f)}"), payload).ConfigureAwait(false);
                    try { newShas[Path.GetFileName(f)] = putResp.GetProperty("content").GetProperty("sha").GetString(); } catch { }
                    ok++;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            if (ok > 0)
            {
                c.FileShas = newShas;
                c.LastPushAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save(c);
            }
            var baseMsg = ok == files.Length
                ? $"✓ 已上传 {ok} 个文件"
                : $"已上传 {ok}/{files.Length} 个" + (lastErr != null ? "，错误：" + lastErr : "");
            if (skipped > 0)
                baseMsg += $"；云端有 {skipped} 个文件比本地新，已跳过（请先「下载数据」再上传）";
            return baseMsg;
        }

        /// <summary>从仓库 data/ 目录下载并覆盖本地（先备份本地 JsonData）</summary>
        public static async Task<string> PullAsync()
        {
            var c = Load();
            if (string.IsNullOrWhiteSpace(c.EncryptedToken))
                return "✗ 请先登录 GitHub 账号或填写 Token";
            string repo;
            try { repo = await ResolveRepoAsync(c).ConfigureAwait(false); }
            catch (Exception ex) { return "✗ " + ex.Message; }
            JsonElement listing;
            try
            {
                listing = await SendAsync(c, HttpMethod.Get, Api(repo, $"data?ref={c.Branch}")).ConfigureAwait(false);
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
            var newShas = new Dictionary<string, string>(c.FileShas);
            foreach (var item in listing.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                if (string.IsNullOrEmpty(name) || !name.EndsWith(".json")) continue;
                total++;
                try
                {
                    var detail = await SendAsync(c, HttpMethod.Get, Api(repo, $"data/{name}?ref={c.Branch}")).ConfigureAwait(false);
                    var b64 = detail.GetProperty("content").GetString() ?? "";
                    var text = Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace("\n", "")));
                    File.WriteAllText(Path.Combine(DataDir, name), text);
                    JsonStore.InvalidateCache(Path.GetFileNameWithoutExtension(name));
                    try
                    {
                        var fsha = detail.GetProperty("sha").GetString();
                        if (fsha != null) newShas[name] = fsha;
                    }
                    catch { }
                    n++;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            if (n > 0)
            {
                c.FileShas = newShas;
                c.LastPullAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save(c);
            }
            if (n == total && n > 0) return $"✓ 已下载 {n} 个文件（原数据已备份）";
            return $"已下载 {n}/{total} 个" + (lastErr != null ? "，错误：" + lastErr : "");
        }
    }
}
