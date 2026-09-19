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
            /// <summary>退出软件前自动把本机数据上传一次，防止「改完忘传，另一台设备下载到旧数据」</summary>
            public bool AutoPushOnExit { get; set; } = false;
            // 每个文件上次同步后的云端 sha，用于检测「云端比本地新」，避免覆盖其它设备的更新
            public Dictionary<string, string> FileShas { get; set; } = new Dictionary<string, string>();
            // 旧版单后端基线（迁移来源，读取时自动搬到 ProviderShas）
            // 多云端基线：云端名 → 文件名 → 版本标识（GitHub/Gitee 的 blob sha 与 WebDAV 的内容哈希互不相同）
            public Dictionary<string, Dictionary<string, string>> ProviderShas { get; set; } = new Dictionary<string, Dictionary<string, string>>();
            // 各云端使用的分支（GitHub=main，Gitee=master，WebDAV 不用）
            public Dictionary<string, string> ProviderBranches { get; set; } = new Dictionary<string, string>();
            // 每个文件上次同步后的本地内容哈希，用于检测「本地比云端新」
            public Dictionary<string, string> FileHashes { get; set; } = new Dictionary<string, string>();
            /// <summary>待处理的同步冲突（格式：云端键|文件名）。上传时"双方都改过且无法自动合并"的文件会记在这里，等用户决定用本机还是云端</summary>
            public List<string> PendingConflicts { get; set; } = new List<string>();
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

        /// <summary>当前同步方式缺少凭据时返回提示文案，否则 null（多云端同步时用 ProviderConfigured 判断）</summary>
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

        /// <summary>三种云端的固定键名顺序（展示/遍历用）</summary>
        public static readonly string[] ProviderKeys = { "github", "gitee", "webdav" };

        public static string ProviderLabel(string key) =>
            key == "gitee" ? "Gitee" : key == "webdav" ? "WebDAV" : "GitHub";

        /// <summary>该云端是否已配置凭据</summary>
        public static bool ProviderConfigured(SyncConfig c, string key)
        {
            switch (key)
            {
                case "gitee":
                    return !string.IsNullOrWhiteSpace(c.EncryptedGiteeToken);
                case "webdav":
                    return !string.IsNullOrWhiteSpace(c.WebDavUser) && !string.IsNullOrWhiteSpace(c.EncryptedWebDavPass);
                default:
                    return !string.IsNullOrWhiteSpace(c.EncryptedToken);
            }
        }

        /// <summary>当前已配置凭据的全部云端</summary>
        private static List<string> ActiveProviders(SyncConfig c) =>
            ProviderKeys.Where(k => ProviderConfigured(c, k)).ToList();

        private static ICloudStore StoreForKey(SyncConfig c, string key) =>
            key == "gitee" ? new GiteeStore() : key == "webdav" ? new WebDavStore() : new GitHubStore();

        /// <summary>按云端取基线表（云端 sha），不存在时建空表</summary>
        private static Dictionary<string, string> Baselines(SyncConfig c, string key)
        {
            if (!c.ProviderShas.TryGetValue(key, out var map))
            {
                map = new Dictionary<string, string>();
                c.ProviderShas[key] = map;
            }
            return map;
        }

        private static string ProviderBranch(SyncConfig c, string key)
        {
            if (c.ProviderBranches.TryGetValue(key, out var b) && !string.IsNullOrWhiteSpace(b)) return b;
            return key == "gitee" ? "master" : "main";
        }

        /// <summary>追加型数据文件：双端冲突时按条目合并（Uid 去重），而不是跳过</summary>
        private static readonly HashSet<string> AppendOnlyFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "time_records", "focus_sessions", "task_completions", "health_records", "water_containers"
        };

        /// <summary>单云端一次同步的结果</summary>
        private class ProviderResult
        {
            public string Key;
            public bool Configured;
            public bool Ok;
            public string Error;
            public int Up, Down, Same, Conflict, Merged;
            /// <summary>本地有未上传的修改，下载时主动跳过（避免未上传数据被云端覆盖）</summary>
            public int KeptLocal;

            public string Line
            {
                get
                {
                    if (!Configured) return $"{ProviderLabel(Key)} 未配置，跳过";
                    if (Up == 0 && Down == 0 && Merged == 0 && Conflict == 0 && KeptLocal == 0 && !string.IsNullOrEmpty(Error))
                        return $"{ProviderLabel(Key)} ✗（{Error}）";
                    var s = $"{ProviderLabel(Key)} ✓ 上传 {Up} · 下载 {Down}";
                    if (Merged > 0) s += $" · 合并 {Merged} 条";
                    if (KeptLocal > 0) s += $" · 保留本地未上传 {KeptLocal}";
                    if (Conflict > 0) s += $" · 冲突跳过 {Conflict}";
                    if (!string.IsNullOrEmpty(Error)) s += $" · 未成功：{Error}";
                    if (Up == 0 && Down == 0 && Merged == 0 && Conflict == 0 && KeptLocal == 0) return $"{ProviderLabel(Key)} ✓ 无变化";
                    return s;
                }
            }
        }

        /// <summary>
        /// 「最新版本」判定（不依赖任何文件时间）：以内容哈希 + 上次同步基线双向比较。
        /// 只有云端内容确实与本地不同时才动手；本地自上次同步后改过而云端没变 → 保留本地（等待上传），
        /// 两边都变过 → 追加型文件按条目合并，其余文件跳过并提示冲突，绝不覆盖任何一端的数据。
        /// </summary>
        private static bool LocalChangedSinceSync(SyncConfig c, string name, string localPath)
        {
            if (!File.Exists(localPath)) return false;
            if (!c.FileHashes.TryGetValue(name, out var known) || known == null) return false;
            return known != HashFile(localPath);
        }

        private static string Str(JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in el.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                    if (p.Value.ValueKind == JsonValueKind.Number || p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False)
                        return p.Value.ToString();
                    return null;
                }
            return null;
        }

        private static JsonElement SetProperty(JsonElement el, string prop, JsonElement value)
        {
            var dict = new Dictionary<string, JsonElement>();
            foreach (var p in el.EnumerateObject())
                if (!string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase)) dict[p.Name] = p.Value;
            dict[prop] = value;
            return JsonSerializer.SerializeToElement(dict);
        }

        /// <summary>无 Uid 时的兜底去重键：打卡按任务+日期、健康按类型+日期、容器按名称、其余按原始内容完全相同</summary>
        private static string MergeKey(JsonElement el, string file)
        {
            switch (file)
            {
                case "task_completions":
                {
                    var t = Str(el, "TaskId"); var d = Str(el, "Date");
                    return t != null && d != null ? $"t|{t}|{d}" : el.GetRawText();
                }
                case "health_records":
                {
                    var t = Str(el, "Type"); var d = Str(el, "Date");
                    return t != null && d != null ? $"h|{t}|{d}" : el.GetRawText();
                }
                case "water_containers":
                {
                    var n = Str(el, "Name");
                    return n != null ? "w|" + n : el.GetRawText();
                }
                default:
                    return el.GetRawText();
            }
        }

        /// <summary>
        /// 合并两个追加型 JSON 数组（按 Uid 去重，无 Uid 走兜底键；旧条目自动补 Uid）。
        /// 返回合并后的文本；added 为新增条目数；uidMigrated 表示本地旧条目被补了 Uid。
        /// </summary>
        private static string MergeJsonArray(string localJson, string remoteJson, string file, out int added, out bool uidMigrated)
        {
            added = 0; uidMigrated = false;
            try
            {
                var local = JsonSerializer.Deserialize<List<JsonElement>>(localJson) ?? new List<JsonElement>();
                var remote = JsonSerializer.Deserialize<List<JsonElement>>(remoteJson) ?? new List<JsonElement>();
                if (local.Count == 0 && remote.Count == 0) return localJson;
                if (local.Count == 0) { added = remote.Count; return remoteJson; }
                if (remote.Count == 0)
                {
                    // 仅给本地旧条目补 Uid，保持文件稳定
                    if (BackfillUids(local)) return SerializeArray(local);
                    return localJson;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var legacySeen = new HashSet<string>(StringComparer.Ordinal);
                int maxId = 0;
                foreach (var el in local)
                {
                    var uid = Str(el, "Uid");
                    if (!string.IsNullOrEmpty(uid)) seen.Add("u|" + uid);
                    else legacySeen.Add(MergeKey(el, file));
                    if (el.TryGetProperty("Id", out var idEl) && idEl.TryGetInt32(out var id) && id > maxId) maxId = id;
                }
                if (BackfillUids(local)) uidMigrated = true;
                // 补完 Uid 后重建 seen，同时保留旧条目的兜底键，避免过渡期重复导入
                seen.Clear(); maxId = 0;
                foreach (var el in local)
                {
                    var uid = Str(el, "Uid");
                    if (!string.IsNullOrEmpty(uid)) seen.Add("u|" + uid);
                    legacySeen.Add(MergeKey(el, file));
                    if (el.TryGetProperty("Id", out var idEl) && idEl.TryGetInt32(out var id) && id > maxId) maxId = id;
                }

                var result = new List<JsonElement>(local);
                foreach (var rel in remote)
                {
                    var uid = Str(rel, "Uid");
                    string key = !string.IsNullOrEmpty(uid) ? "u|" + uid : MergeKey(rel, file);
                    if (seen.Contains(key) || (string.IsNullOrEmpty(uid) && legacySeen.Contains(key))) continue;
                    var clone = rel.Clone();
                    if (string.IsNullOrEmpty(Str(clone, "Uid")))
                        clone = SetProperty(clone, "Uid", JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N")));
                    // Id 仅本地展示用：与本地冲突时重新分配，避免重复
                    if (clone.TryGetProperty("Id", out var cidEl) && cidEl.TryGetInt32(out var cid))
                    {
                        if (cid <= maxId)
                        {
                            clone = SetProperty(clone, "Id", JsonSerializer.SerializeToElement(maxId + 1));
                            maxId++;
                        }
                        else
                        {
                            maxId = cid;
                        }
                    }
                    seen.Add("u|" + Str(clone, "Uid"));
                    result.Add(clone);
                    added++;
                }
                if (added > 0 || uidMigrated) return SerializeArray(result);
                return localJson;
            }
            catch { return localJson; }
        }

        private static bool BackfillUids(List<JsonElement> list)
        {
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrEmpty(Str(list[i], "Uid")))
                {
                    list[i] = SetProperty(list[i], "Uid", JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N")));
                    changed = true;
                }
            }
            return changed;
        }

        private static string SerializeArray(List<JsonElement> list) =>
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });

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
                    c.Provider = string.IsNullOrWhiteSpace(c.Provider) ? "github" : c.Provider;
                    c.FileShas ??= new Dictionary<string, string>();
                    c.FileHashes ??= new Dictionary<string, string>();
                    c.ProviderShas ??= new Dictionary<string, Dictionary<string, string>>();
                    c.ProviderBranches ??= new Dictionary<string, string>();
                    // 数据仓库由 ME-OKR 更名为 ME-Data：旧配置自动迁移，避免两端同步中断
                    if (c.Repo == "ME-OKR" || c.Repo.EndsWith("/ME-OKR"))
                    {
                        c.Repo = c.Repo.Contains('/') ? c.Repo.Substring(0, c.Repo.IndexOf('/') + 1) + "ME-Data" : "ME-Data";
                        Save(c);
                    }
                    // 旧版单云端基线迁移到多云端结构（按原 Provider 归属）
                    if (c.ProviderShas.Count == 0 && c.FileShas.Count > 0)
                    {
                        var oldKey = string.IsNullOrWhiteSpace(c.Provider) ? "github" : c.Provider;
                        if (oldKey != "gitee" && oldKey != "webdav") oldKey = "github";
                        c.ProviderShas[oldKey] = new Dictionary<string, string>(c.FileShas);
                        if (!string.IsNullOrWhiteSpace(c.Branch))
                            c.ProviderBranches[oldKey] = c.Branch;
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

        /// <summary>启动时自动同步：已登录且开启「启动软件时自动同步」才执行（后台运行，不阻塞启动）</summary>
        public static async Task AutoSyncOnStartupAsync()
        {
            try
            {
                var c = Load();
                if (!c.AutoSyncOnStartup || ActiveProviders(c).Count == 0) return;
                LastAutoSyncResult = await SyncAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastAutoSyncResult = "自动同步失败：" + ex.Message;
            }
        }

        /// <summary>上传入口（带状态球登记）：推送所有已配置云端，结果同步反映到左下角状态球</summary>
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
            var active = ActiveProviders(c);
            if (active.Count == 0) return "✗ 请先在「设置 → 数据与备份」配置至少一个云同步账号";
            if (!Directory.Exists(DataDir) || Directory.GetFiles(DataDir, "*.json").Length == 0)
                return "✗ 没有可上传的数据";

            var results = new List<ProviderResult>();
            foreach (var key in active)
            {
                var res = new ProviderResult { Key = key, Configured = true };
                try
                {
                    c.Branch = ProviderBranch(c, key);
                    var store = StoreForKey(c, key);
                    await store.EnsureReadyAsync(c).ConfigureAwait(false);
                    c.ProviderBranches[key] = string.IsNullOrWhiteSpace(c.Branch) ? ProviderBranch(c, key) : c.Branch;
                    var baselines = Baselines(c, key);
                    var newShas = new Dictionary<string, string>(baselines);
                    var newHashes = new Dictionary<string, string>(c.FileHashes);
                    foreach (var f in Directory.GetFiles(DataDir, "*.json"))
                    {
                        try
                        {
                            var name = Path.GetFileName(f);
                            var rev = await store.RevOfAsync(c, name).ConfigureAwait(false);
                            var baseName = Path.GetFileNameWithoutExtension(name);

                            // 云端被其它设备更新过而本地没有先下载：
                            // 追加型文件自动合并后上传，其余跳过避免覆盖。
                            // 没有基线（首次上传到该云端）时也不盲写：先比对云端与本地的内容哈希，
                            // 两者不同说明云端可能有别的设备留下的、本机没下载过的数据 → 同样按上述规则处理。
                            bool hasBaseline = baselines.TryGetValue(name, out var known);
                            bool remoteNewer = hasBaseline && rev != null && known != rev;
                            if (!hasBaseline && rev != null)
                            {
                                // 本机没有该云端的基线（首次上传到这个云端）：读回云端内容比对哈希，
                                // 内容不同说明云端有别的设备留下、本机没下载过的数据 → 不能盲写覆盖
                                var probe = await store.ReadAsync(c, name).ConfigureAwait(false);
                                if (probe != null && HashText(probe) != HashFile(f)) remoteNewer = true;
                            }
                            else if (remoteNewer)
                            {
                                // 版本标识记账不准（云端返回的 sha 缺失/格式差异）也会造成基线不匹配，
                                // 用内容哈希复核一次：内容其实一致就正常上传并刷新基线，避免永远卡在「云端较新」
                                var probe = await store.ReadAsync(c, name).ConfigureAwait(false);
                                if (probe != null && HashText(probe) == HashFile(f)) remoteNewer = false;
                            }
                            if (remoteNewer)
                            {
                                if (AppendOnlyFiles.Contains(baseName))
                                {
                                    var localText = File.ReadAllText(f);
                                    var remoteText = await store.ReadAsync(c, name).ConfigureAwait(false);
                                    if (remoteText == null) continue;
                                    var merged = MergeJsonArray(localText, remoteText, baseName, out int add, out bool migrated);
                                    if (merged != localText)
                                    {
                                        File.WriteAllText(f, merged);
                                        JsonStore.InvalidateCache(baseName);
                                    }
                                    var newRev = await store.WriteAsync(c, name, merged, rev).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(newRev)) newShas[name] = newRev;
                                    newHashes[name] = HashFile(f);
                                    res.Up++; res.Merged += add;
                                    continue;
                                }
                                res.Conflict++;
                                RecordConflict(c, key, name);
                                continue;
                            }

                            var nrev = await store.WriteAsync(c, name, File.ReadAllText(f), rev).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(nrev)) newShas[name] = nrev;
                            newHashes[name] = HashFile(f);
                            res.Up++;
                            c.PendingConflicts.Remove(key + "|" + name);
                        }
                        catch (Exception ex) { res.Error = FirstError(res.Error, ex.Message); }
                    }
                    baselines.Clear();
                    foreach (var kv in newShas) baselines[kv.Key] = kv.Value;
                    c.FileHashes = newHashes;
                    res.Ok = string.IsNullOrEmpty(res.Error) || res.Up > 0 || res.Down > 0;
                    if (res.Ok) c.LastPushAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch (Exception ex) { res.Error = ex.Message; }
                results.Add(res);
            }
            if (results.Any(r => r.Up > 0)) Save(c);
            EventAggregator.Instance.Publish("SyncStatusChanged");
            return BuildSummary("上传", results);
        }

        /// <summary>下载入口（带状态球登记）：从所有已配置云端拉取（追加型文件自动合并），结果同步反映到状态球</summary>
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
            var active = ActiveProviders(c);
            if (active.Count == 0) return "✗ 请先在「设置 → 数据与备份」配置至少一个云同步账号";

            // 下载前备份本地数据（所有云端共用一次备份）
            if (Directory.Exists(DataDir))
            {
                var backup = DataDir + $"_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                Directory.CreateDirectory(backup);
                foreach (var f in Directory.GetFiles(DataDir, "*.json"))
                    File.Copy(f, Path.Combine(backup, Path.GetFileName(f)), true);
            }
            Directory.CreateDirectory(DataDir);

            var results = new List<ProviderResult>();
            foreach (var key in active)
            {
                var res = new ProviderResult { Key = key, Configured = true };
                try
                {
                    c.Branch = ProviderBranch(c, key);
                    var store = StoreForKey(c, key);
                    await store.EnsureReadyAsync(c).ConfigureAwait(false);
                    c.ProviderBranches[key] = string.IsNullOrWhiteSpace(c.Branch) ? ProviderBranch(c, key) : c.Branch;
                    Dictionary<string, string> remote;
                    try { remote = await store.ListAsync(c).ConfigureAwait(false); }
                    catch (Exception ex) when (ex.Message.Contains("404")) { remote = new Dictionary<string, string>(); }
                    if (remote.Count == 0) { res.Ok = true; results.Add(res); continue; }

                    var baselines = Baselines(c, key);
                    var newShas = new Dictionary<string, string>(baselines);
                    var newHashes = new Dictionary<string, string>(c.FileHashes);
                    foreach (var kv in remote)
                    {
                        var name = kv.Key;
                        if (string.IsNullOrEmpty(name) || !name.EndsWith(".json")) continue;
                        try
                        {
                            var localPath = Path.Combine(DataDir, name);
                            var baseName = Path.GetFileNameWithoutExtension(name);
                            var text = await store.ReadAsync(c, name).ConfigureAwait(false);
                            if (text == null) throw new Exception("文件内容为空");

                            bool localExists = File.Exists(localPath);

                            // 追加型文件：始终按条目合并，任何一端都不会丢数据
                            if (AppendOnlyFiles.Contains(baseName) && localExists)
                            {
                                var localText = File.ReadAllText(localPath);
                                var merged = MergeJsonArray(localText, text, baseName, out int add, out _);
                                if (merged != localText)
                                {
                                    File.WriteAllText(localPath, merged);
                                    JsonStore.InvalidateCache(baseName);
                                    res.Merged += add;
                                }
                                res.Down++;
                                newShas[name] = string.IsNullOrEmpty(kv.Value) ? HashText(text) : kv.Value;
                                newHashes[name] = HashFile(localPath);
                                continue;
                            }

                            // 其余文件：以「内容哈希」判定最新版本，不使用文件时间。
                            // 云端内容与本地一致 → 无需动作；
                            // 本地自上次同步后改过但云端没变 → 保留本地（等下次上传），绝不覆盖未上传的数据。
                            if (localExists)
                            {
                                var localHash = HashFile(localPath);
                                if (HashText(text) == localHash)
                                {
                                    res.Same++;
                                    newShas[name] = string.IsNullOrEmpty(kv.Value) ? HashText(text) : kv.Value;
                                    newHashes[name] = localHash;
                                    continue;
                                }
                                if (LocalChangedSinceSync(c, name, localPath))
                                {
                                    res.KeptLocal++;
                                    continue;
                                }
                            }

                            File.WriteAllText(localPath, text);
                            JsonStore.InvalidateCache(baseName);
                            res.Down++;
                            newShas[name] = string.IsNullOrEmpty(kv.Value) ? HashText(text) : kv.Value;
                            newHashes[name] = HashFile(localPath);
                        }
                        catch (Exception ex) { res.Error = FirstError(res.Error, ex.Message); }
                    }
                    baselines.Clear();
                    foreach (var kvv in newShas) baselines[kvv.Key] = kvv.Value;
                    c.FileHashes = newHashes;
                    res.Ok = string.IsNullOrEmpty(res.Error) || res.Up > 0 || res.Down > 0;
                    if (res.Ok && res.Down > 0) c.LastPullAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch (Exception ex) { res.Error = ex.Message; }
                results.Add(res);
            }
            if (results.Any(r => r.Down > 0)) Save(c);
            EventAggregator.Instance.Publish("SyncStatusChanged");
            return BuildSummary("下载", results);
        }

        /// <summary>登记一条待用户处理的同步冲突（去重）</summary>
        private static void RecordConflict(SyncConfig c, string key, string name)
        {
            var entry = key + "|" + name;
            if (!c.PendingConflicts.Contains(entry)) c.PendingConflicts.Add(entry);
        }

        /// <summary>当前待处理的冲突条目（只读副本）</summary>
        public static List<string> PendingConflicts()
        {
            var c = Load();
            return c.PendingConflicts.ToList();
        }

        /// <summary>处理同步冲突：逐个由用户决定「用云端覆盖本机」或「用本机覆盖云端」。
        /// preferCloud 有三种取值：true = 用云端覆盖本机；false = 用本机覆盖云端；null = 两边都留
        /// （云端版本另存为「文件名.from-cloud.json」继续同步，本机版本保留并在下次上传）。
        /// file/provider 传 null 表示处理全部；处理完的条目从待处理清单移除并刷新基线，避免下一次同步又报冲突。</summary>
        public static async Task<string> ResolveConflictsAsync(bool? preferCloud, string file = null, string provider = null)
        {
            var c = Load();
            var items = c.PendingConflicts.Where(e =>
                (provider == null || e.Substring(0, e.IndexOf('|')) == provider) &&
                (file == null || e.Substring(e.IndexOf('|') + 1) == file)).ToList();
            if (items.Count == 0) return "没有待处理的冲突";

            int ok = 0;
            var errors = new List<string>();
            foreach (var item in items)
            {
                var sep = item.IndexOf('|');
                if (sep <= 0) { c.PendingConflicts.Remove(item); continue; }
                var key = item.Substring(0, sep);
                var name = item.Substring(sep + 1);
                if (!ProviderConfigured(c, key)) { errors.Add($"{name}（{ProviderLabel(key)}）：该云端未配置"); continue; }
                try
                {
                    c.Branch = ProviderBranch(c, key);
                    var store = StoreForKey(c, key);
                    await store.EnsureReadyAsync(c).ConfigureAwait(false);
                    var localPath = Path.Combine(DataDir, name);
                    var baselines = Baselines(c, key);

                    if (preferCloud == null)
                    {
                        // 两边都留：云端内容另存为 *.from-cloud.json（继续同步），本机原文件不动，
                        // 下次上传时本机版本会作为「本地较新」正常推上去
                        var rev0 = await store.RevOfAsync(c, name).ConfigureAwait(false);
                        var text0 = await store.ReadAsync(c, name).ConfigureAwait(false);
                        if (text0 != null)
                        {
                            var copyName = Path.GetFileNameWithoutExtension(name) + ".from-cloud.json";
                            File.WriteAllText(Path.Combine(DataDir, copyName), text0);
                            JsonStore.InvalidateCache(Path.GetFileNameWithoutExtension(copyName));
                            var bl0 = Baselines(c, key);
                            bl0[copyName] = rev0 ?? HashText(text0);
                            c.FileHashes[copyName] = HashText(text0);
                        }
                        c.PendingConflicts.Remove(item);
                        ok++;
                        continue;
                    }

                    if (preferCloud.Value)
                    {
                        var rev = await store.RevOfAsync(c, name).ConfigureAwait(false);
                        if (rev == null) throw new Exception("云端已没有这个文件");
                        var text = await store.ReadAsync(c, name).ConfigureAwait(false);
                        if (text == null) throw new Exception("云端文件内容为空");
                        Directory.CreateDirectory(DataDir);
                        File.WriteAllText(localPath, text);
                        JsonStore.InvalidateCache(Path.GetFileNameWithoutExtension(name));
                        baselines[name] = rev;
                        c.FileHashes[name] = HashFile(localPath);
                    }
                    else
                    {
                        if (!File.Exists(localPath)) throw new Exception("本机已没有这个文件");
                        var content = File.ReadAllText(localPath);
                        var nrev = await store.WriteAsync(c, name, content, await store.RevOfAsync(c, name).ConfigureAwait(false)).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(nrev)) baselines[name] = nrev;
                        c.FileHashes[name] = HashFile(localPath);
                    }
                    c.PendingConflicts.Remove(item);
                    ok++;
                }
                catch (Exception ex) { errors.Add($"{name}（{ProviderLabel(key)}）：{ex.Message}"); }
            }
            if (ok > 0) Save(c);
            EventAggregator.Instance.Publish("SyncStatusChanged");
            var head = $"已处理 {ok}/{items.Count} 个冲突（{(preferCloud == null ? "两边都留" : preferCloud.Value ? "采用云端" : "采用本机")}）";
            return errors.Count > 0 ? head + "；失败：" + string.Join("；", errors) : head;
        }

        /// <summary>某个云端的展示目标（账号/仓库名 或 WebDAV 完整路径），诊断结果用</summary>
        private static string DescribeTarget(SyncConfig c, string key)
        {
            var repo = string.IsNullOrWhiteSpace(c.Repo) ? "ME-Data" : c.Repo;
            var name = repo.Contains('/') ? repo.Substring(repo.IndexOf('/') + 1) : repo;
            if (key == "webdav")
            {
                var baseUrl = string.IsNullOrWhiteSpace(c.WebDavUrl) ? "https://dav.jianguoyun.com/dav/" : c.WebDavUrl.Trim();
                return baseUrl.TrimEnd('/') + "/" + name;
            }
            var account = key == "gitee" ? c.GiteeAccountName : c.AccountName;
            return $"{account}/{name}";
        }

        /// <summary>
        /// 连接诊断：逐个云端走「连接 → 列目录 → 读第一个文件」，给出每步状态与耗时。
        /// 上传/下载失败时先跑一遍这个，能把「令牌失效 / 仓库不存在 / 限流 / 网络」区分开。
        /// </summary>
        public static async Task<string> DiagnoseAsync()
        {
            var c = Load();
            var lines = new List<string> { "诊断结果：" };
            foreach (var key in ProviderKeys)
            {
                if (!ProviderConfigured(c, key)) { lines.Add($"• {ProviderLabel(key)}：未配置，跳过"); continue; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    c.Branch = ProviderBranch(c, key);
                    var store = StoreForKey(c, key);
                    await store.EnsureReadyAsync(c).ConfigureAwait(false);
                    var t1 = sw.ElapsedMilliseconds;
                    var list = await store.ListAsync(c).ConfigureAwait(false);
                    var t2 = sw.ElapsedMilliseconds;
                    if (list.Count == 0)
                    {
                        lines.Add($"✓ {ProviderLabel(key)}：{DescribeTarget(c, key)}｜连接 {t1}ms｜列目录 {t2 - t1}ms｜云端还没有数据目录");
                    }
                    else
                    {
                        var firstName = list.Keys.First();
                        var text = await store.ReadAsync(c, firstName).ConfigureAwait(false);
                        var t3 = sw.ElapsedMilliseconds;
                        lines.Add($"✓ {ProviderLabel(key)}：{DescribeTarget(c, key)}｜连接 {t1}ms｜列目录 {t2 - t1}ms（{list.Count} 个文件）｜读 {firstName} {t3 - t2}ms");
                    }
                }
                catch (Exception ex)
                {
                    lines.Add($"✗ {ProviderLabel(key)}：{ex.Message}（耗时 {sw.ElapsedMilliseconds}ms）");
                }
            }
            if (c.PendingConflicts.Count > 0)
                lines.Add($"⚠ 有 {c.PendingConflicts.Count} 个冲突等待处理（点「处理冲突」选择用本机还是云端）");
            return string.Join("\n", lines);
        }

        /// <summary>退出前自动上传：最多等 12 秒，超时放弃（避免关不掉窗口）。成功返回 null，失败返回原因。</summary>
        public static string TryPushBeforeExit()
        {
            var c = Load();
            if (!c.AutoPushOnExit) return null;
            if (ActiveProviders(c).Count == 0) return null;
            try
            {
                var task = PushCoreAsync();
                if (!task.Wait(TimeSpan.FromSeconds(12))) return null; // 超时静默放弃
                return null;
            }
            catch { return null; }
        }

        /// <summary>同步决策日志：把最近一次上传/下载的逐文件判定落到本地文件，便于排查「为什么没同步上」</summary>
        public static string WriteSyncLog()
        {
            try
            {
                var dir = Path.Combine(DataDir, "..", "Logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"sync-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                var sb = new StringBuilder();
                sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"本机版本基线条目：{Load().FileHashes.Count}");
                sb.AppendLine($"待处理冲突：{string.Join("、", Load().PendingConflicts)}");
                sb.AppendLine($"最近一次结果：{LastAutoSyncResult}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { return "写入失败：" + ex.Message; }
        }

        private static string FirstError(string current, string next) =>
            string.IsNullOrEmpty(current) ? next : current;

        /// <summary>组装多云端明细反馈：✓ 开头表示至少一个云端成功；全部失败以 ✗ 开头</summary>
        private static string BuildSummary(string action, List<ProviderResult> results)
        {
            int okCount = results.Count(r => r.Ok && r.Configured);
            string head = okCount == 0
                ? $"✗ {action}失败：所有云端均未成功"
                : okCount == results.Count
                    ? $"✓ {action}完成"
                    : $"✓ {action}完成（部分云端未成功）";
            var lines = new List<string> { head };
            // 先明确列出「哪些平台成功 / 哪些平台没成功」，再给每个平台的明细
            var okNames = results.Where(r => r.Configured && r.Ok).Select(r => ProviderLabel(r.Key)).ToList();
            var badNames = results.Where(r => r.Configured && !r.Ok).Select(r => ProviderLabel(r.Key)).ToList();
            if (okNames.Count > 0) lines.Add($"已{action}：{string.Join("、", okNames)}");
            if (badNames.Count > 0) lines.Add($"未成功：{string.Join("、", badNames)}");
            foreach (var r in results) lines.Add(r.Line);
            // 失败的详细原因集中展示一次
            var fails = results.Where(r => r.Configured && !r.Ok && !string.IsNullOrEmpty(r.Error)).ToList();
            if (fails.Count > 0)
                lines.Add("失败原因：" + string.Join("；", fails.Select(f => $"{ProviderLabel(f.Key)}（{f.Error}）")));
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 智能同步：对所有已配置云端逐文件比较本地与云端，谁新用谁——
        /// 云端较新→下载到本地；本地较新→上传到云端；两边都改过→追加型文件自动合并、其余跳过并提示。
        /// </summary>
        private static async Task<List<ProviderResult>> SyncAllProvidersAsync(SyncConfig c)
        {
            var active = ActiveProviders(c);
            var results = new List<ProviderResult>();
            foreach (var key in active)
            {
                var res = new ProviderResult { Key = key, Configured = true };
                try
                {
                    c.Branch = ProviderBranch(c, key);
                    var store = StoreForKey(c, key);
                    await store.EnsureReadyAsync(c).ConfigureAwait(false);
                    c.ProviderBranches[key] = string.IsNullOrWhiteSpace(c.Branch) ? ProviderBranch(c, key) : c.Branch;
                    res = await SyncProviderAsync(c, key, store).ConfigureAwait(false);
                }
                catch (Exception ex) { res.Error = ex.Message; }
                results.Add(res);
            }
            return results;
        }

        private static async Task<ProviderResult> SyncProviderAsync(SyncConfig c, string key, ICloudStore store)
        {
            var res = new ProviderResult { Key = key, Configured = true };

            // 云端文件清单 name -> 版本标识
            var remote = new Dictionary<string, string>();
            try { remote = await store.ListAsync(c).ConfigureAwait(false); }
            catch (Exception ex) when (ex.Message.Contains("404")) { /* 云端还没有 data 目录，当作空 */ }

            var localNames = Directory.Exists(DataDir)
                ? Directory.GetFiles(DataDir, "*.json").Select(Path.GetFileName).ToList()
                : new List<string>();

            var baselines = Baselines(c, key);
            var newShas = new Dictionary<string, string>(baselines);
            var newHashes = new Dictionary<string, string>(c.FileHashes);

            foreach (var name in remote.Keys.Union(localNames).Distinct().ToList())
            {
                try
                {
                    var localPath = Path.Combine(DataDir, name);
                    bool localExists = File.Exists(localPath);
                    string localHash = localExists ? HashFile(localPath) : null;
                    bool remoteExists = remote.TryGetValue(name, out var rsha);
                    baselines.TryGetValue(name, out var knownSha);
                    c.FileHashes.TryGetValue(name, out var knownHash);
                    var baseName = Path.GetFileNameWithoutExtension(name);

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
                            res.Same++;
                            newShas[name] = rsha;
                            newHashes[name] = localHash;
                            continue;
                        }
                        wantDownload = remoteChanged && !localChanged;
                        wantUpload = localChanged && !remoteChanged;
                        if (!wantDownload && !wantUpload)
                        {
                            // 两边都改过：追加型文件按条目合并，其余跳过
                            if (AppendOnlyFiles.Contains(baseName))
                            {
                                var localText = File.ReadAllText(localPath);
                                var remoteText = await store.ReadAsync(c, name).ConfigureAwait(false);
                                if (remoteText == null) throw new Exception("云端文件内容为空");
                                var merged = MergeJsonArray(localText, remoteText, baseName, out int add, out _);
                                if (merged != localText)
                                {
                                    File.WriteAllText(localPath, merged);
                                    JsonStore.InvalidateCache(baseName);
                                }
                                var newRev = await store.WriteAsync(c, name, merged, rsha).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(newRev)) newShas[name] = newRev;
                                newHashes[name] = HashFile(localPath);
                                res.Up++; res.Merged += add;
                            }
                            else
                            {
                                res.Conflict++;
                            }
                            continue;
                        }
                    }

                    if (wantUpload)
                    {
                        var content = File.ReadAllText(localPath);
                        var newRev = await store.WriteAsync(c, name, content, remoteExists ? rsha : null).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(newRev)) newShas[name] = newRev;
                        newHashes[name] = localHash;
                        res.Up++;
                    }
                    else
                    {
                        var text = await store.ReadAsync(c, name).ConfigureAwait(false);
                        if (text == null) throw new Exception("文件内容为空");
                        if (AppendOnlyFiles.Contains(baseName) && localExists)
                        {
                            var localText = File.ReadAllText(localPath);
                            var merged = MergeJsonArray(localText, text, baseName, out int add, out _);
                            if (merged != localText)
                            {
                                File.WriteAllText(localPath, merged);
                                JsonStore.InvalidateCache(baseName);
                            }
                            res.Merged += add;
                        }
                        else
                        {
                            File.WriteAllText(localPath, text);
                            JsonStore.InvalidateCache(baseName);
                        }
                        newShas[name] = string.IsNullOrEmpty(rsha) ? HashText(text) : rsha;
                        newHashes[name] = HashFile(localPath);
                        res.Down++;
                    }
                }
                catch (Exception ex) { res.Error = FirstError(res.Error, $"{name}：{ex.Message}"); }
            }

            baselines.Clear();
            foreach (var kv in newShas) baselines[kv.Key] = kv.Value;
            c.FileHashes = newHashes;
            res.Ok = string.IsNullOrEmpty(res.Error);
            return res;
        }

        /// <summary>智能同步入口（带状态球登记）：toast=true 时完成后弹左下角轻提示（状态球/触发式同步用）。</summary>
        public static async Task<string> SyncAsync(bool toast = false)
        {
            SyncStatusService.SetRunning();
            string r;
            try
            {
                var c = Load();
                var active = ActiveProviders(c);
                if (active.Count == 0)
                {
                    r = "✗ 请先在「设置 → 数据与备份」配置至少一个云同步账号";
                }
                else
                {
                    Directory.CreateDirectory(DataDir);
                    foreach (var key in active)
                    {
                        if (string.IsNullOrWhiteSpace(c.Repo)) { c.Repo = "ME-Data"; Save(c); }
                    }
                    var results = await SyncAllProvidersAsync(c).ConfigureAwait(false);
                    if (results.Any(rr => rr.Up > 0 || rr.Down > 0))
                    {
                        c.LastSyncAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        Save(c);
                    }
                    EventAggregator.Instance.Publish("SyncStatusChanged");
                    r = BuildSummary("同步", results);
                }
            }
            catch (Exception ex) { r = "✗ 同步失败：" + ex.Message; }
            SyncStatusService.Report(r, toast);
            return r;
        }
    }
}
