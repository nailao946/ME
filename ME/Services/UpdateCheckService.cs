using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ME.Services
{
    /// <summary>
    /// 版本检测：对比本机版本与 GitHub Releases 上发布的最新版本（设置-关于页用）。
    /// 仓库公开，匿名访问即可；从每个 Release 的 tag、标题、资产文件名里提取版本号取最大值
    /// （发布资产常直接带版本，如 ME-PE-v2.4.32.apk），预发布 Release 也参与比较。
    /// 复用云同步配置里的代理设置，GitHub 直连不畅时走用户配置的代理。
    /// </summary>
    public static class UpdateCheckService
    {
        public class UpdateResult
        {
            public bool HasUpdate;
            public string CurrentVersion;
            public string LatestVersion;   // 仓库没发布过版本时为 null
            public string ReleaseUrl;      // 最新版本所在发布页，前往下载
            public string Error;           // 检查失败/未发布过版本时的提示
        }

        private const string ReleasesApi = "https://api.github.com/repos/nailao946/ME/releases?per_page=20";
        private const string ReleasesPage = "https://github.com/nailao946/ME/releases";
        private static readonly Regex VerRegex = new Regex(@"v?(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.Compiled);

        private static Version ParseVer(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = VerRegex.Match(s);
            if (!m.Success) return null;
            try
            {
                return new Version(
                    int.Parse(m.Groups[1].Value),
                    int.Parse(m.Groups[2].Value),
                    m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
            }
            catch { return null; }
        }

        public static async Task<UpdateResult> CheckAsync()
        {
            var asm = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var cur = new Version(Math.Max(asm.Major, 0), Math.Max(asm.Minor, 0), Math.Max(asm.Build, 0));
            var r = new UpdateResult { CurrentVersion = $"{cur.Major}.{cur.Minor}.{cur.Build}" };
            try
            {
                var cfg = GitHubSyncService.Load();
                using var client = CreateClient(cfg);
                var text = await client.GetStringAsync(ReleasesApi).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(text);
                Version latest = null;
                string url = ReleasesPage;
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    var relUrl = rel.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                    // 版本号可能写在 tag、发布标题或资产文件名里，逐个提取
                    var candidates = new List<string>();
                    if (rel.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String) candidates.Add(t.GetString());
                    if (rel.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) candidates.Add(n.GetString());
                    if (rel.TryGetProperty("assets", out var assets))
                        foreach (var a in assets.EnumerateArray())
                            if (a.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String) candidates.Add(an.GetString());
                    foreach (var s in candidates)
                    {
                        var v = ParseVer(s);
                        if (v != null && (latest == null || v > latest))
                        {
                            latest = v;
                            if (!string.IsNullOrEmpty(relUrl)) url = relUrl;
                        }
                    }
                }
                if (latest == null)
                {
                    r.Error = "仓库还没有发布过版本（发布 Release 后才能检测更新）";
                    return r;
                }
                r.LatestVersion = $"{latest.Major}.{latest.Minor}.{latest.Build}";
                r.ReleaseUrl = url;
                r.HasUpdate = latest > cur;
                return r;
            }
            catch (Exception ex)
            {
                var msg = ex.Message ?? "";
                r.Error = msg.Length > 160 ? msg.Substring(0, 160) + "…" : msg;
                return r;
            }
        }

        private static HttpClient CreateClient(GitHubSyncService.SyncConfig c)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(c.Proxy))
            {
                try { handler.Proxy = new System.Net.WebProxy(c.Proxy.Trim()); handler.UseProxy = true; } catch { }
            }
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ME-PC");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }
    }
}
