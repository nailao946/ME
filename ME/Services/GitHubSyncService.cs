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
using ME.Core;

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
            public string Provider { get; set; } = "github"; // github | gitee | webdav
            public string EncryptedToken { get; set; } = "";
            public string EncryptedRefreshToken { get; set; } = ""; // GitHub App 开启「令牌过期」时用于自动续期
            public string TokenExpiresAt { get; set; } = "";        // 令牌到期本地时间；空 = 令牌不过期
            public string Repo { get; set; } = "";       // Git 供应商=仓库名（可 owner/name）；WebDAV=文件夹名
            public string Branch { get; set; } = "main"; // 仅 Git 供应商（GitHub 默认 main，Gitee 默认 master）
            public string Proxy { get; set; } = "";      // 可选，如 http://127.0.0.1:7897
            public string LastPushAt { get; set; } = "";
            public string LastPullAt { get; set; } = "";
            public string LastSyncAt { get; set; } = "";  // 最近一次自动同步时间
            public string AccountName { get; set; } = ""; // 授权后显示的 GitHub 用户名
            public string EncryptedGiteeToken { get; set; } = ""; // Gitee 私人令牌（DPAPI 加密）
            public string GiteeAccountName { get; set; } = "";    // Gitee 用户名（显示用）
            public string WebDavUrl { get; set; } = "";   // WebDAV 地址，空 = 坚果云 https://dav.jianguoyun.com/dav/
            public string WebDavUser { get; set; } = "";  // WebDAV 账号（坚果云为注册手机号/邮箱）
            public string EncryptedWebDavPass { get; set; } = ""; // WebDAV 密码/应用密码（DPAPI 加密）
            public bool AutoSyncOnStartup { get; set; } = true; // 启动软件时自动同步
            // 每个文件上次同步后的云端 sha，用于检测「云端比本地新」，避免覆盖其它设备的更新
            public Dictionary<string, string> FileShas { get; set; } = new Dictionary<string, string>();
            // 每个文件上次同步后的本地内容哈希，用于检测「本地比云端新」
            public Dictionary<string, string> FileHashes { get; set; } = new Dictionary<string, string>();
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
            // 先立即落盘 token——拉取用户名（api.github.com）可能很慢甚至超时，不能拖住登录完成
            c.EncryptedToken = SecureStore.Encrypt(token);
            StoreExpiry(c, r);
            Save(c);
            try
            {
                var account = await FetchLoginAsync(c).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(account)) { c.AccountName = account; Save(c); }
            }
            catch { }
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
            c.EncryptedRefreshToken = "";
            c.TokenExpiresAt = "";
            c.AccountName = "";
            Save(c);
            _resolvedRepo = null;
        }

        /// <summary>带鉴权头拉取当前登录的 GitHub 用户名（顺带缓存到配置）</summary>
        private static async Task<string> FetchLoginAsync(SyncConfig c)
        {
            await EnsureFreshTokenAsync(c).ConfigureAwait(false);
            using var client = CreateClient(c);
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = AuthHeader(c);
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new Exception("获取账号失败：" + DescribeApiError((int)resp.StatusCode, text));
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
        /// 确保同步目标存在（按所选同步方式：GitHub/Gitee 自动创建私有仓库，WebDAV 创建目录）。
        /// 登录后和上传/下载前调用，用户无需手填 owner/ 前缀。返回用于展示的目标名。
        /// </summary>
        public static async Task<string> EnsureDefaultRepoAsync()
        {
            var c = Load();
            await StoreFor(c).EnsureReadyAsync(c).ConfigureAwait(false);
            return DisplayTarget(c);
        }

        private static string DisplayTarget(SyncConfig c)
        {
            var repo = string.IsNullOrWhiteSpace(c.Repo) ? "ME-Data" : c.Repo;
            var name = repo.Contains('/') ? repo.Substring(repo.IndexOf('/') + 1) : repo;
            if (c.Provider == "webdav")
            {
                var baseUrl = string.IsNullOrWhiteSpace(c.WebDavUrl) ? "https://dav.jianguoyun.com/dav/" : c.WebDavUrl.Trim();
                return baseUrl.TrimEnd('/') + "/" + name;
            }
            var account = c.Provider == "gitee" ? c.GiteeAccountName : c.AccountName;
            return $"{account}/{name}";
        }

        /// <summary>当前同步方式缺少凭据时返回提示文案，否则 null</summary>
        private static string CredentialsMissing(SyncConfig c)
        {
            if (c.Provider == "gitee")
                return string.IsNullOrWhiteSpace(c.EncryptedGiteeToken) ? "请先填写 Gitee 私人令牌（gitee.com → 设置 → 私人令牌）" : null;
            if (c.Provider == "webdav")
                return string.IsNullOrWhiteSpace(c.WebDavUser) || string.IsNullOrWhiteSpace(c.EncryptedWebDavPass)
                    ? "请先填写 WebDAV 账号和密码" : null;
            return string.IsNullOrWhiteSpace(c.EncryptedToken) ? "请先登录 GitHub 账号或填写 Token" : null;
        }

        private static ICloudStore StoreFor(SyncConfig c) =>
            c.Provider == "gitee" ? new GiteeStore() :
            c.Provider == "webdav" ? new WebDavStore() : new GitHubStore();

        /// <summary>把用户填的仓库名解析成 owner/name：只填 ME-Data 时自动补当前账号前缀</summary>
        private static async Task<string> ResolveRepoAsync(SyncConfig c)
        {
            if (string.IsNullOrWhiteSpace(c.Repo)) c.Repo = "ME-Data";
            if (c.Repo.Contains('/')) return c.Repo;
            if (_resolvedRepo != null && _resolvedRepo.EndsWith("/" + c.Repo)) return _resolvedRepo;
            var login = string.IsNullOrWhiteSpace(c.AccountName) ? await FetchLoginAsync(c).ConfigureAwait(false) : c.AccountName;
            _resolvedRepo = $"{login}/{c.Repo}";
            return _resolvedRepo;
        }
        private static string _resolvedRepo;

        /// <summary>内容指纹（WebDAV 没有 sha 概念，用内容 SHA1 当版本标识判断「云端是否被改过」）</summary>
        private static string HashText(string text)
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>
        /// 云端存储的统一抽象：上传/下载/智能同步只认这份接口，GitHub / Gitee / WebDAV 各自实现。
        /// 版本标识：Git 供应商=文件 blob sha，WebDAV=内容 SHA1。
        /// </summary>
        private interface ICloudStore
        {
            string Label { get; }
            Task EnsureReadyAsync(SyncConfig c);
            /// <summary>列出 data 目录下所有 .json 文件：文件名 → 版本标识（目录不存在返回空表）</summary>
            Task<Dictionary<string, string>> ListAsync(SyncConfig c);
            /// <summary>读取文件内容；文件不存在返回 null</summary>
            Task<string> ReadAsync(SyncConfig c, string name);
            /// <summary>当前云端版本标识；文件不存在返回 null</summary>
            Task<string> RevOfAsync(SyncConfig c, string name);
            /// <summary>写入文件，返回新的云端版本标识</summary>
            Task<string> WriteAsync(SyncConfig c, string name, string content, string prevRev);
            string DescribeError(int status, string body);
        }

        private class GitHubStore : ICloudStore
        {
            public string Label => "GitHub";

            public async Task EnsureReadyAsync(SyncConfig c)
            {
                if (string.IsNullOrWhiteSpace(c.EncryptedToken)) throw new Exception("尚未登录 GitHub 账号");
                await EnsureFreshTokenAsync(c).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(c.Repo)) c.Repo = "ME-Data";
                var name = c.Repo.Contains('/') ? c.Repo.Substring(c.Repo.IndexOf('/') + 1) : c.Repo;
                if (string.IsNullOrWhiteSpace(name)) name = "ME-Data";

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
                    req.Headers.Authorization = AuthHeader(c);
                    using var resp = await client.SendAsync(req).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 422)
                    {
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new Exception("创建仓库失败：" + DescribeApiError((int)resp.StatusCode, text));
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("422")) { /* 已存在 */ }

                if (string.IsNullOrWhiteSpace(c.Branch)) c.Branch = "main";
                Save(c);
                _resolvedRepo = $"{login}/{name}";
            }

            public async Task<Dictionary<string, string>> ListAsync(SyncConfig c)
            {
                var repo = await ResolveRepoAsync(c).ConfigureAwait(false);
                var map = new Dictionary<string, string>();
                var listing = await SendAsync(c, HttpMethod.Get, Api(repo, $"data?ref={c.Branch}")).ConfigureAwait(false);
                if (listing.ValueKind == JsonValueKind.Array)
                    foreach (var item in listing.EnumerateArray())
                    {
                        var name = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                        var fsha = item.TryGetProperty("sha", out var sh) ? sh.GetString() : null;
                        if (!string.IsNullOrEmpty(name) && name.EndsWith(".json") && !string.IsNullOrEmpty(fsha))
                            map[name] = fsha;
                    }
                return map;
            }

            public async Task<string> ReadAsync(SyncConfig c, string name)
            {
                var repo = await ResolveRepoAsync(c).ConfigureAwait(false);
                var detail = await SendAsync(c, HttpMethod.Get, Api(repo, $"data/{Uri.EscapeDataString(name)}?ref={c.Branch}")).ConfigureAwait(false);
                var b64 = detail.GetProperty("content").GetString() ?? "";
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace("\n", "")));
            }

            public async Task<string> RevOfAsync(SyncConfig c, string name)
            {
                try
                {
                    var repo = await ResolveRepoAsync(c).ConfigureAwait(false);
                    var detail = await SendAsync(c, HttpMethod.Get, Api(repo, $"data/{Uri.EscapeDataString(name)}?ref={c.Branch}")).ConfigureAwait(false);
                    return detail.TryGetProperty("sha", out var sh) ? sh.GetString() : null;
                }
                catch { return null; } // 不存在则新建
            }

            public async Task<string> WriteAsync(SyncConfig c, string name, string content, string prevRev)
            {
                var repo = await ResolveRepoAsync(c).ConfigureAwait(false);
                var payload = new Dictionary<string, object>
                {
                    ["message"] = $"ME 数据同步（PC）· {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
                    ["branch"] = string.IsNullOrWhiteSpace(c.Branch) ? "main" : c.Branch
                };
                if (!string.IsNullOrEmpty(prevRev)) payload["sha"] = prevRev;
                var putResp = await SendAsync(c, HttpMethod.Put, Api(repo, $"data/{Uri.EscapeDataString(name)}"), payload).ConfigureAwait(false);
                try { return putResp.GetProperty("content").GetProperty("sha").GetString() ?? ""; } catch { return ""; }
            }

            public string DescribeError(int status, string body) => DescribeApiError(status, body);
        }

        private class GiteeStore : ICloudStore
        {
            public string Label => "Gitee";
            private const string Api = "https://gitee.com/api/v5";

            private static string Token(SyncConfig c)
            {
                var t = SecureStore.Decrypt(c.EncryptedGiteeToken);
                if (string.IsNullOrWhiteSpace(t))
                    throw new Exception("本机保存的 Gitee 令牌无法读取，请重新填写私人令牌");
                return t;
            }

            private string Url(SyncConfig c, string path) =>
                $"{Api}{path}{(path.Contains('?') ? '&' : '?')}access_token={Uri.EscapeDataString(Token(c))}";

            private async Task<JsonElement> SendAsync(SyncConfig c, HttpMethod method, string path, object payload = null)
            {
                using var client = CreateClient(c);   // 复用统一客户端（代理/超时/UserAgent）
                using var req = new HttpRequestMessage(method, Url(c, path));
                if (payload != null)
                    req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new Exception(DescribeError((int)resp.StatusCode, text));
                if (string.IsNullOrWhiteSpace(text)) return default;
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }

            private async Task<string> AccountAsync(SyncConfig c)
            {
                if (!string.IsNullOrWhiteSpace(c.GiteeAccountName)) return c.GiteeAccountName;
                var user = await SendAsync(c, HttpMethod.Get, "/user").ConfigureAwait(false);
                var login = user.TryGetProperty("login", out var lg) ? lg.GetString() : null;
                if (string.IsNullOrWhiteSpace(login))
                    throw new Exception("无法获取 Gitee 用户名，请检查令牌是否勾选了 user_info 与 projects 权限");
                c.GiteeAccountName = login;
                Save(c);
                return login;
            }

            private string RepoName(SyncConfig c) =>
                string.IsNullOrWhiteSpace(c.Repo) || c.Repo.Trim() == "" ? "ME-Data"
                : (c.Repo.Contains('/') ? c.Repo.Substring(c.Repo.IndexOf('/') + 1) : c.Repo);

            private async Task<string> FullRepoAsync(SyncConfig c) =>
                c.Repo != null && c.Repo.Contains('/') ? c.Repo : $"{await AccountAsync(c).ConfigureAwait(false)}/{RepoName(c)}";

            public async Task EnsureReadyAsync(SyncConfig c)
            {
                if (string.IsNullOrWhiteSpace(c.EncryptedGiteeToken))
                    throw new Exception("请先填写 Gitee 私人令牌（gitee.com → 设置 → 私人令牌，勾选 projects 与 user_info）");
                await AccountAsync(c).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(c.Repo)) c.Repo = "ME-Data";

                // 创建私有仓库（已存在则直接使用）；Gitee 空仓库不能写 contents，auto_init 先生成初始提交
                try
                {
                    await SendAsync(c, HttpMethod.Post, "/user/repos", new Dictionary<string, object>
                    {
                        ["name"] = RepoName(c), ["private"] = true, ["auto_init"] = true
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var m = ex.Message;
                    if (!(m.Contains("已存在") || m.Contains("同名") || m.Contains("exist") || m.Contains("already")))
                        throw new Exception("创建仓库失败：" + m);
                }

                if (string.IsNullOrWhiteSpace(c.Branch) || c.Branch.Trim() == "main") c.Branch = "master"; // Gitee 默认分支
                Save(c);
            }

            public async Task<Dictionary<string, string>> ListAsync(SyncConfig c)
            {
                var full = await FullRepoAsync(c).ConfigureAwait(false);
                var map = new Dictionary<string, string>();
                var listing = await SendAsync(c, HttpMethod.Get, $"/repos/{full}/contents/data?ref={c.Branch}").ConfigureAwait(false);
                if (listing.ValueKind == JsonValueKind.Array)
                    foreach (var item in listing.EnumerateArray())
                    {
                        var name = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                        var sha = item.TryGetProperty("sha", out var sh) ? sh.GetString() : null;
                        if (!string.IsNullOrEmpty(name) && name.EndsWith(".json") && !string.IsNullOrEmpty(sha))
                            map[name] = sha;
                    }
                return map;
            }

            public async Task<string> ReadAsync(SyncConfig c, string name)
            {
                var full = await FullRepoAsync(c).ConfigureAwait(false);
                var detail = await SendAsync(c, HttpMethod.Get, $"/repos/{full}/contents/data/{Uri.EscapeDataString(name)}?ref={c.Branch}").ConfigureAwait(false);
                var b64 = detail.GetProperty("content").GetString() ?? "";
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace("\n", "")));
            }

            public async Task<string> RevOfAsync(SyncConfig c, string name)
            {
                try
                {
                    var full = await FullRepoAsync(c).ConfigureAwait(false);
                    var detail = await SendAsync(c, HttpMethod.Get, $"/repos/{full}/contents/data/{Uri.EscapeDataString(name)}?ref={c.Branch}").ConfigureAwait(false);
                    return detail.TryGetProperty("sha", out var sh) ? sh.GetString() : null;
                }
                catch { return null; }
            }

            public async Task<string> WriteAsync(SyncConfig c, string name, string content, string prevRev)
            {
                var full = await FullRepoAsync(c).ConfigureAwait(false);
                var path = $"/repos/{full}/contents/data/{Uri.EscapeDataString(name)}";
                var payload = new Dictionary<string, object>
                {
                    ["message"] = $"ME 数据同步（PC）· {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
                    ["branch"] = string.IsNullOrWhiteSpace(c.Branch) ? "master" : c.Branch
                };
                JsonElement resp;
                if (!string.IsNullOrEmpty(prevRev))
                {
                    payload["sha"] = prevRev;
                    resp = await SendAsync(c, HttpMethod.Put, path, payload).ConfigureAwait(false);
                }
                else
                {
                    // Gitee 与 GitHub 不同：PUT 是纯「更新」接口，不带 sha 一律 400 sha is missing（即使文件不存在），
                    // 新建文件必须走 POST；若撞上已存在（本地版本记录缺失），取最新 sha 转更新
                    try { resp = await SendAsync(c, HttpMethod.Post, path, payload).ConfigureAwait(false); }
                    catch (Exception ex) when (ex.Message.Contains("存在") || ex.Message.Contains("exist", StringComparison.OrdinalIgnoreCase))
                    {
                        var fresh = await RevOfAsync(c, name).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(fresh)) throw;
                        payload["sha"] = fresh;
                        resp = await SendAsync(c, HttpMethod.Put, path, payload).ConfigureAwait(false);
                    }
                }
                try { return resp.GetProperty("content").GetProperty("sha").GetString() ?? ""; } catch { return ""; }
            }

            public string DescribeError(int status, string body) =>
                status == 401
                    ? "Gitee 令牌已失效（被撤销或已过期），请重新填写私人令牌"
                    : $"HTTP {status}：{Truncate(body, 240)}";
        }

        private class WebDavStore : ICloudStore
        {
            public string Label => "WebDAV";

            // 每次操作新建实例：ListAsync 预取的内容缓存在这里，Read/RevOf 直接命中，避免重复下载
            private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

            private string BaseUrl(SyncConfig c) =>
                (string.IsNullOrWhiteSpace(c.WebDavUrl) ? "https://dav.jianguoyun.com/dav/" : c.WebDavUrl.Trim()).TrimEnd('/') + "/";

            private string Folder(SyncConfig c)
            {
                var repo = string.IsNullOrWhiteSpace(c.Repo) ? "ME-Data" : c.Repo.Trim();
                var segs = repo.Split('/').Where(s => s.Length > 0).Select(Uri.EscapeDataString);
                return BaseUrl(c) + string.Join("/", segs) + "/";
            }

            private HttpClient Client(SyncConfig c)
            {
                var pass = SecureStore.Decrypt(c.EncryptedWebDavPass);
                if (string.IsNullOrWhiteSpace(c.WebDavUser) || string.IsNullOrWhiteSpace(pass))
                    throw new Exception("请先填写 WebDAV 账号和密码");
                var handler = new HttpClientHandler();
                if (!string.IsNullOrWhiteSpace(c.Proxy))
                {
                    try { handler.Proxy = new System.Net.WebProxy(c.Proxy.Trim()); handler.UseProxy = true; } catch { }
                }
                var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ME-PC");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.WebDavUser.Trim()}:{pass}")));
                return client;
            }

            public async Task EnsureReadyAsync(SyncConfig c)
            {
                if (string.IsNullOrWhiteSpace(c.Repo)) { c.Repo = "ME-Data"; Save(c); }
                await EnsureDirsAsync(c).ConfigureAwait(false);
            }

            /// <summary>
            /// 逐级创建同步目录。坚果云等 WebDAV 服务不会隐式建父目录：父级缺失时 MKCOL/PUT
            /// 一律 409（AncestorsNotFound）——实测坚果云返回 &lt;s:exception&gt;AncestorsNotFound&lt;/s:exception&gt;。
            /// 旧版把 MKCOL 的 409 当「可继续」，目录没建成照样上传 → 每个文件都 409 失败。
            /// </summary>
            private async Task EnsureDirsAsync(SyncConfig c)
            {
                if (!Uri.TryCreate(BaseUrl(c), UriKind.Absolute, out var baseUri) ||
                    (baseUri.Scheme != "http" && baseUri.Scheme != "https"))
                    throw new Exception("WebDAV 服务器地址无效，请检查（坚果云为 https://dav.jianguoyun.com/dav/）");
                var authority = baseUri.GetLeftPart(UriPartial.Authority);
                var repo = string.IsNullOrWhiteSpace(c.Repo) ? "ME-Data" : c.Repo.Trim();
                var segs = repo.Split('/').Where(s => s.Length > 0).Select(Uri.EscapeDataString);
                using var client = Client(c);
                var path = baseUri.AbsolutePath.TrimEnd('/');
                foreach (var seg in segs)
                {
                    path += "/" + seg;
                    var (code, body) = await SendDavAsync(client, "MKCOL", authority + path).ConfigureAwait(false);
                    if (code == 409)
                    {
                        // 坚果云最终一致：刚建好的上级目录偶发立刻查不到，稍等重试一次
                        await Task.Delay(800).ConfigureAwait(false);
                        (code, body) = await SendDavAsync(client, "MKCOL", authority + path).ConfigureAwait(false);
                    }
                    // 201 = 已创建；405/301/200 = 目录已存在，均可继续
                    if (code != 201 && code != 405 && code != 301 && code != 200)
                        throw new Exception("创建 WebDAV 目录失败：" + DescribeError(code, body));
                }
            }

            private static async Task<(int Code, string Body)> SendDavAsync(HttpClient client, string method, string url, string content = null)
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), url);
                if (content != null) req.Content = new StringContent(content, Encoding.UTF8, "application/json");
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            public async Task<Dictionary<string, string>> ListAsync(SyncConfig c)
            {
                var map = new Dictionary<string, string>();
                string body;
                using (var client = Client(c))
                {
                    using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), Folder(c));
                    req.Headers.Add("Depth", "1");
                    req.Content = new StringContent(
                        "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/></d:prop></d:propfind>",
                        Encoding.UTF8, "application/xml");
                    using var resp = await client.SendAsync(req).ConfigureAwait(false);
                    var code = (int)resp.StatusCode;
                    body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (code == 404) return map;   // 目录还没建
                    if (code != 207 && (code < 200 || code > 299))
                        throw new Exception(DescribeError(code, body));
                }

                // 解析 multistatus：每个 <response> 里的 <href>；用 LocalName 匹配以兼容任意命名空间前缀
                var doc = new System.Xml.Linq.XDocument();
                try { doc = System.Xml.Linq.XDocument.Parse(body); } catch { return map; }
                var folder = Folder(c);
                foreach (var respEl in doc.Descendants().Where(e => e.Name.LocalName == "response"))
                {
                    var href = respEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value;
                    if (string.IsNullOrEmpty(href)) continue;
                    var decoded = Uri.UnescapeDataString(href);
                    if (decoded.EndsWith("/")) continue; // 目录本身或子目录
                    var name = decoded.Split('/').Last();
                    if (!name.EndsWith(".json")) continue;
                    var content = await ReadRawAsync(c, name).ConfigureAwait(false);
                    if (content == null) continue;
                    _cache[name] = content;
                    map[name] = HashText(content);
                }
                return map;
            }

            private async Task<string> ReadRawAsync(SyncConfig c, string name)
            {
                using var client = Client(c);
                using var req = new HttpRequestMessage(HttpMethod.Get, Folder(c) + Uri.EscapeDataString(name));
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                var code = (int)resp.StatusCode;
                if (code == 404) return null;
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (code < 200 || code > 299)
                    throw new Exception(DescribeError(code, text));
                return text;
            }

            public async Task<string> ReadAsync(SyncConfig c, string name)
            {
                if (_cache.TryGetValue(name, out var cached)) return cached;
                return await ReadRawAsync(c, name).ConfigureAwait(false);
            }

            public async Task<string> RevOfAsync(SyncConfig c, string name)
            {
                var content = await ReadAsync(c, name).ConfigureAwait(false);
                return content == null ? null : HashText(content);
            }

            public async Task<string> WriteAsync(SyncConfig c, string name, string content, string prevRev)
            {
                var url = Folder(c) + Uri.EscapeDataString(name);
                using var client = Client(c);
                var (code, text) = await SendDavAsync(client, "PUT", url, content).ConfigureAwait(false);
                if (code == 409)
                {
                    // 目标目录在云端缺失（坚果云 AncestorsNotFound）或最终一致延迟：重建目录后重试一次
                    await EnsureDirsAsync(c).ConfigureAwait(false);
                    (code, text) = await SendDavAsync(client, "PUT", url, content).ConfigureAwait(false);
                }
                if (code < 200 || code > 299)
                    throw new Exception(DescribeError(code, text));
                return HashText(content);
            }

            public string DescribeError(int status, string body) =>
                status == 401 || status == 403
                    ? "WebDAV 账号或密码不正确（坚果云请用网页版「安全选项 → 添加应用密码」生成的密码，不能用登录密码）"
                    : status == 409
                    ? "HTTP 409：目标文件夹在云端无法就位（自动创建未生效或请求过于频繁——坚果云免费版每 30 分钟限约 600 个请求），请稍后重试，或在坚果云客户端手动建好目标文件夹"
                    : $"HTTP {status}：{Truncate(body, 240)}";
        }

        /// <summary>反馈提交目标仓库（项目 Issues，非用户的同步数据仓库）</summary>
        private const string FeedbackRepo = "nailao946/ME";

        /// <summary>
        /// 提交用户反馈到项目仓库 Issues。任何 GitHub 账号都能在公开仓库提 issue，无需仓库写权限；
        /// 标题由弹窗组装（含类型前缀），正文由弹窗组装类型段落后在此追加版本与平台信息。返回 issue 编号。
        /// </summary>
        public static async Task<int> SubmitFeedbackAsync(string title, string content)
        {
            var c = Load();
            if (string.IsNullOrWhiteSpace(c.EncryptedToken))
                throw new Exception("提交反馈需要 GitHub 授权（与云同步方式无关）：请在「设置 → 数据与备份」切换到 GitHub 并登录后再提交");
            var t = (title ?? "").Trim();
            if (t.Length == 0) throw new Exception("请填写反馈标题");
            var text = (content ?? "").Trim();
            if (text.Length == 0) throw new Exception("请先填写反馈内容");

            var body = text + $"\n\n---\n来自 ME 桌面版 v{AppVersionText} · Windows";
            var payload = new Dictionary<string, string> { ["title"] = Truncate(t, 80), ["body"] = body };

            await EnsureFreshTokenAsync(c).ConfigureAwait(false);
            using var client = CreateClient(c);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{FeedbackRepo}/issues")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = AuthHeader(c);
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new Exception("提交失败：" + DescribeApiError((int)resp.StatusCode, respText));
            using var doc = JsonDocument.Parse(respText);
            return doc.RootElement.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
        }

        private static string AppVersionText
        {
            get
            {
                try
                {
                    var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (v != null) return $"{v.Major}.{v.Minor}.{v.Build}";
                }
                catch { }
                return "?";
            }
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
                {
                    var c = JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(ConfigPath)) ?? new SyncConfig();
                    // 数据仓库由 ME-OKR 更名为 ME-Data：旧配置自动迁移，避免两端同步中断
                    if (c.Repo == "ME-OKR" || c.Repo.EndsWith("/ME-OKR"))
                    {
                        c.Repo = c.Repo.Contains('/') ? c.Repo.Substring(0, c.Repo.IndexOf('/') + 1) + "ME-Data" : "ME-Data";
                        Save(c);
                    }
                    return c;
                }
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
            await EnsureFreshTokenAsync(c).ConfigureAwait(false);
            using var client = CreateClient(c);
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = AuthHeader(c);
            if (payload != null)
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new Exception(DescribeApiError((int)resp.StatusCode, text));
            if (string.IsNullOrWhiteSpace(text)) return default;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);

        /// <summary>统一鉴权头：令牌解密失败/为空时直接给出可操作的提示，不发出空凭据</summary>
        private static AuthenticationHeaderValue AuthHeader(SyncConfig c)
        {
            var t = SecureStore.Decrypt(c.EncryptedToken);
            if (string.IsNullOrWhiteSpace(t))
                throw new Exception("本机保存的 GitHub 授权无法读取，请重新授权登录");
            return new AuthenticationHeaderValue("Bearer", t);
        }

        /// <summary>统一 API 错误文案：401 = 令牌已在 GitHub 侧失效（被撤销或过期），引导重新授权</summary>
        private static string DescribeApiError(int status, string body)
        {
            if (status == 401)
                return "GitHub 授权已失效（令牌被撤销或已过期），请在上方点「重新授权」重新登录一次即可恢复";
            return $"HTTP {status}：{Truncate(body, 240)}";
        }

        /// <summary>把授权接口返回的过期信息存进配置（应用未开启「令牌过期」时没有这两个字段，存空）</summary>
        private static void StoreExpiry(SyncConfig c, JsonElement r)
        {
            if (r.TryGetProperty("refresh_token", out var rt) && !string.IsNullOrWhiteSpace(rt.GetString()))
                c.EncryptedRefreshToken = SecureStore.Encrypt(rt.GetString());
            else
                c.EncryptedRefreshToken = "";
            if (r.TryGetProperty("expires_in", out var ex) && ex.TryGetInt32(out int secs) && secs > 0)
                c.TokenExpiresAt = DateTime.Now.AddSeconds(secs).ToString("yyyy-MM-dd HH:mm:ss");
            else
                c.TokenExpiresAt = "";
        }

        /// <summary>
        /// GitHub App 开启「令牌过期」后用户令牌 8 小时失效：到期前 10 分钟内自动用 refresh_token 换新，
        /// 用户无需反复重新授权。未存过期时间（应用关闭过期）时什么都不做；换新失败不打断，
        /// 让后续请求自然收到 401 并看到重新授权提示。
        /// </summary>
        private static async Task EnsureFreshTokenAsync(SyncConfig c)
        {
            if (string.IsNullOrWhiteSpace(c.TokenExpiresAt) || string.IsNullOrWhiteSpace(c.EncryptedRefreshToken))
                return;
            if (!DateTime.TryParse(c.TokenExpiresAt, out var exp)) return;
            if (exp - DateTime.Now > TimeSpan.FromMinutes(10)) return;
            var rt = SecureStore.Decrypt(c.EncryptedRefreshToken);
            if (string.IsNullOrWhiteSpace(rt)) return;
            try
            {
                using var client = CreateClient(c);
                var payload = new Dictionary<string, string>
                {
                    ["client_id"] = OAuthClientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = rt
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Content = new FormUrlEncodedContent(payload);
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(text);
                var r = doc.RootElement;
                if (!r.TryGetProperty("access_token", out var at)) return;
                c.EncryptedToken = SecureStore.Encrypt(at.GetString());
                if (r.TryGetProperty("refresh_token", out var nrt) && !string.IsNullOrWhiteSpace(nrt.GetString()))
                    c.EncryptedRefreshToken = SecureStore.Encrypt(nrt.GetString()); // GitHub 每次刷新都会轮换 refresh_token
                if (r.TryGetProperty("expires_in", out var nx) && nx.TryGetInt32(out int secs) && secs > 0)
                    c.TokenExpiresAt = DateTime.Now.AddSeconds(secs).ToString("yyyy-MM-dd HH:mm:ss");
                Save(c);
            }
            catch { }
        }

        private static string HashFile(string path)
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        /// <summary>最近一次启动自动同步的结果（设置页显示用）</summary>
        public static string LastAutoSyncResult { get; private set; } = "";

        /// <summary>
        /// 智能同步：逐文件比较本地与云端，谁新用谁——
        /// 云端较新→下载到本地；本地较新→上传到云端；两边都改过→跳过并提示；无变化→跳过。
        /// 启动自动同步与设置页都可调用。
        /// </summary>
        /// <summary>
        /// 智能同步入口（带状态球登记）：toast=true 时完成后弹左下角轻提示（状态球/触发式同步用）。
        /// 启动自动同步与设置页调用时 toast=false，不弹提示。
        /// </summary>
        public static async Task<string> SyncAsync(bool toast = false)
        {
            SyncStatusService.SetRunning();
            string r;
            try { r = await SyncCoreAsync().ConfigureAwait(false); }
            catch (Exception ex) { r = "✗ 同步失败：" + ex.Message; }
            SyncStatusService.Report(r, toast);
            return r;
        }

        private static async Task<string> SyncCoreAsync()
        {
            var c = Load();
            var missing = CredentialsMissing(c);
            if (missing != null) return "✗ " + missing;
            if (string.IsNullOrWhiteSpace(c.Repo))
            {
                try { await EnsureDefaultRepoAsync().ConfigureAwait(false); c = Load(); }
                catch (Exception ex) { return "✗ " + ex.Message; }
            }
            Directory.CreateDirectory(DataDir);
            var store = StoreFor(c);

            // 云端文件清单 name -> 版本标识
            var remote = new Dictionary<string, string>();
            try { remote = await store.ListAsync(c).ConfigureAwait(false); }
            catch (Exception ex) when (ex.Message.Contains("404")) { /* 云端还没有 data 目录，当作空 */ }

            var localNames = Directory.Exists(DataDir)
                ? Directory.GetFiles(DataDir, "*.json").Select(Path.GetFileName).ToList()
                : new List<string>();

            int up = 0, down = 0, conflict = 0, same = 0; string lastErr = null;
            var newShas = new Dictionary<string, string>(c.FileShas);
            var newHashes = new Dictionary<string, string>(c.FileHashes);

            foreach (var name in remote.Keys.Union(localNames).Distinct().ToList())
            {
                try
                {
                    var localPath = Path.Combine(DataDir, name);
                    bool localExists = File.Exists(localPath);
                    string localHash = localExists ? HashFile(localPath) : null;
                    bool remoteExists = remote.TryGetValue(name, out var rsha);
                    c.FileShas.TryGetValue(name, out var knownSha);
                    c.FileHashes.TryGetValue(name, out var knownHash);

                    bool wantUpload, wantDownload;
                    if (!remoteExists && localExists) { wantUpload = true; wantDownload = false; }
                    else if (remoteExists && !localExists) { wantDownload = true; wantUpload = false; }
                    else if (!remoteExists) continue;
                    else
                    {
                        bool remoteChanged = knownSha != null && knownSha != rsha;
                        bool localChanged = knownHash != null && knownHash != localHash;
                        if (!remoteChanged && !localChanged)
                        {
                            same++;
                            newShas[name] = rsha;
                            newHashes[name] = localHash;
                            continue;
                        }
                        wantDownload = remoteChanged && !localChanged;
                        wantUpload = localChanged && !remoteChanged;
                        if (!wantDownload && !wantUpload) { conflict++; continue; } // 两边都改过
                    }

                    if (wantUpload)
                    {
                        var content = File.ReadAllText(localPath);
                        var newRev = await store.WriteAsync(c, name, content, remoteExists ? rsha : null).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(newRev)) newShas[name] = newRev;
                        newHashes[name] = localHash;
                        up++;
                    }
                    else
                    {
                        var text = await store.ReadAsync(c, name).ConfigureAwait(false);
                        if (text == null) throw new Exception("文件内容为空");
                        File.WriteAllText(localPath, text);
                        JsonStore.InvalidateCache(Path.GetFileNameWithoutExtension(name));
                        newShas[name] = string.IsNullOrEmpty(rsha) ? HashText(text) : rsha;
                        newHashes[name] = HashFile(localPath);
                        down++;
                    }
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }

            c.FileShas = newShas;
            c.FileHashes = newHashes;
            if (up + down > 0)
            {
                c.LastSyncAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save(c);
            }
            var msg = $"✓ 同步完成：上传 {up} 个，下载 {down} 个，无变化 {same} 个";
            if (conflict > 0) msg += $"；{conflict} 个文件本地与云端都有修改已跳过（可分别用上传/下载处理）";
            if (lastErr != null) msg += $"；错误：{lastErr}";
            EventAggregator.Instance.Publish("SyncStatusChanged");
            return msg;
        }

        /// <summary>启动时自动同步：已登录且开启「启动软件时自动同步」才执行（后台运行，不阻塞启动）</summary>
        public static async Task AutoSyncOnStartupAsync()
        {
            try
            {
                var c = Load();
                if (!c.AutoSyncOnStartup || CredentialsMissing(c) != null) return;
                LastAutoSyncResult = await SyncAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastAutoSyncResult = "自动同步失败：" + ex.Message;
            }
        }

        /// <summary>上传入口（带状态球登记）：结果同步反映到左下角状态球</summary>
        public static async Task<string> PushAsync()
        {
            SyncStatusService.SetRunning();
            string r;
            try { r = await PushCoreAsync().ConfigureAwait(false); }
            catch (Exception ex) { r = "✗ 上传失败：" + ex.Message; }
            SyncStatusService.Report(r, false);
            return r;
        }

        private static async Task<string> PushCoreAsync()
        {
            var c = Load();
            var missing = CredentialsMissing(c);
            if (missing != null) return "✗ " + missing;
            if (string.IsNullOrWhiteSpace(c.Repo))
            {
                try { await EnsureDefaultRepoAsync().ConfigureAwait(false); c = Load(); }
                catch (Exception ex) { return "✗ " + ex.Message; }
            }
            if (!Directory.Exists(DataDir)) return "✗ 没有可上传的数据";
            var files = Directory.GetFiles(DataDir, "*.json");
            if (files.Length == 0) return "✗ 没有可上传的数据";
            var store = StoreFor(c);
            int ok = 0; int skipped = 0; string lastErr = null;
            var newShas = new Dictionary<string, string>(c.FileShas);
            var newHashes = new Dictionary<string, string>(c.FileHashes);
            foreach (var f in files)
            {
                try
                {
                    var name = Path.GetFileName(f);
                    var rev = await store.RevOfAsync(c, name).ConfigureAwait(false);

                    // 云端被其它设备更新过而本地没有先下载 → 跳过，避免覆盖
                    if (c.FileShas.TryGetValue(name, out var known) && rev != null && known != rev)
                    {
                        skipped++;
                        continue;
                    }

                    var newRev = await store.WriteAsync(c, name, File.ReadAllText(f), rev).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(newRev)) newShas[name] = newRev;
                    newHashes[name] = HashFile(f);
                    ok++;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            if (ok > 0)
            {
                c.FileShas = newShas;
                c.FileHashes = newHashes;
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

        /// <summary>下载入口（带状态球登记）：结果同步反映到左下角状态球</summary>
        public static async Task<string> PullAsync()
        {
            SyncStatusService.SetRunning();
            string r;
            try { r = await PullCoreAsync().ConfigureAwait(false); }
            catch (Exception ex) { r = "✗ 下载失败：" + ex.Message; }
            SyncStatusService.Report(r, false);
            return r;
        }

        private static async Task<string> PullCoreAsync()
        {
            var c = Load();
            var missing = CredentialsMissing(c);
            if (missing != null) return "✗ " + missing;
            var store = StoreFor(c);
            Dictionary<string, string> remote;
            try { remote = await store.ListAsync(c).ConfigureAwait(false); }
            catch (Exception ex) when (ex.Message.Contains("404"))
            {
                return "同步目录为空，没有可下载的数据";
            }
            if (remote.Count == 0)
                return "同步目录为空，没有可下载的数据";

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
            var newHashes = new Dictionary<string, string>(c.FileHashes);
            foreach (var kv in remote)
            {
                var name = kv.Key;
                if (string.IsNullOrEmpty(name) || !name.EndsWith(".json")) continue;
                total++;
                try
                {
                    var text = await store.ReadAsync(c, name).ConfigureAwait(false);
                    if (text == null) throw new Exception("文件内容为空");
                    var localPath = Path.Combine(DataDir, name);
                    File.WriteAllText(localPath, text);
                    JsonStore.InvalidateCache(Path.GetFileNameWithoutExtension(name));
                    newShas[name] = string.IsNullOrEmpty(kv.Value) ? HashText(text) : kv.Value;
                    newHashes[name] = HashFile(localPath);
                    n++;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            if (n > 0)
            {
                c.FileShas = newShas;
                c.FileHashes = newHashes;
                c.LastPullAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save(c);
            }
            if (n == total && n > 0) return $"✓ 已下载 {n} 个文件（原数据已备份）";
            return $"已下载 {n}/{total} 个" + (lastErr != null ? "，错误：" + lastErr : "");
        }
    }
}
