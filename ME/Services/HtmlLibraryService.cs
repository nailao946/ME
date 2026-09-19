using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    /// <summary>
    /// 模块资料库服务：让 AI 根据模块的字段定义与记录生成「个人资料库风格」的 HTML 页面 + 配套 CSV，
    /// 也可把页面导出为本地文件。HTML 存在 html_library.json，两端互通。
    /// </summary>
    public static class HtmlLibraryService
    {
        /// <summary>AI 生成资料页（返回保存后的页面）</summary>
        public static async Task<HtmlLibraryPage> GenerateWithAiAsync(CustomModule module, string userHint, AiProvider provider)
        {
            if (module == null) throw new InvalidOperationException("请先选择模块");
            var prompt = BuildPrompt(module, userHint);
            var reply = await LlmService.ChatAsync(provider, SystemPrompt, prompt, 0.4);
            var (html, csv) = ParseReply(reply);
            if (string.IsNullOrWhiteSpace(html))
                throw new InvalidOperationException("AI 没有返回可用的 HTML，请重试或换一个模型。");

            var page = new HtmlLibraryPage
            {
                ModuleId = module.Id,
                Title = FirstLine(userHint, module.Name + " · 资料页"),
                Html = html,
                Csv = csv ?? BuildDefaultCsv(module),
                Source = "ai",
            };
            return HtmlLibraryRepository.Add(page);
        }

        /// <summary>导入本地 HTML（可带同名 .csv）</summary>
        public static HtmlLibraryPage ImportFile(int moduleId, string htmlPath)
        {
            if (!System.IO.File.Exists(htmlPath)) throw new InvalidOperationException("找不到该 HTML 文件");
            var page = new HtmlLibraryPage
            {
                ModuleId = moduleId,
                Title = System.IO.Path.GetFileNameWithoutExtension(htmlPath),
                Html = System.IO.File.ReadAllText(htmlPath),
                Source = "manual",
            };
            var csvPath = System.IO.Path.ChangeExtension(htmlPath, ".csv");
            if (System.IO.File.Exists(csvPath)) page.Csv = System.IO.File.ReadAllText(csvPath);
            return HtmlLibraryRepository.Add(page);
        }

        /// <summary>把模块记录导出为 CSV（供手动建页时使用）</summary>
        public static string BuildDefaultCsv(CustomModule m)
        {
            var sb = new StringBuilder();
            var cols = new List<string> { "date", "time" };
            cols.AddRange(m.Fields.Select(f => f.Key));
            cols.Add("note");
            sb.AppendLine(string.Join(",", cols.Select(Escape)));
            foreach (var r in m.Records.OrderBy(r => r.Date).ThenBy(r => r.Time))
            {
                var row = new List<string> { Escape(r.Date), Escape(r.Time) };
                foreach (var f in m.Fields)
                    row.Add(Escape(r.Values.TryGetValue(f.Key, out var v) ? v : ""));
                row.Add(Escape(r.Note ?? ""));
                sb.AppendLine(string.Join(",", row));
            }
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            s = s ?? "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private const string SystemPrompt =
            "你是 ME 个人管理系统的资料页生成器。用户会给出一个模块的字段定义与记录数据。" +
            "请输出一个可直接在浏览器打开的单文件 HTML 页面：内嵌全部 CSS 与 JS（不引用外部资源），" +
            "风格类似个人资料库 / 知识库（清晰的标题层级、卡片式分区、表格、可搜索、数据可视化可用原生 SVG/Canvas 实现）。" +
            "务必：1) 用 <html-lib-csv>...</html-lib-csv> 标签包裹一段与页面数据一致的 CSV；" +
            "2) 页面内嵌同样的数据（可用 JS 数组或内联 CSV），保证离线打开也能看到全部内容；" +
            "3) 不要输出任何解释文字。";

        private static string BuildPrompt(CustomModule m, string userHint)
        {
            var sb = new StringBuilder();
            sb.AppendLine("模块名称：" + m.Name);
            sb.AppendLine("字段定义：");
            foreach (var f in m.Fields)
                sb.AppendLine($"- {f.Label}（类型 {f.Type}{(string.IsNullOrEmpty(f.Unit) ? "" : "，单位 " + f.Unit)}）");
            sb.AppendLine();
            sb.AppendLine("CSV 数据（date,time,各字段,note）：");
            sb.AppendLine(BuildDefaultCsv(m));
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(userHint)
                ? "请基于以上数据生成一个个人资料库风格的 HTML 页面。"
                : "用户额外要求：" + userHint.Trim());
            return sb.ToString();
        }

        /// <summary>从回复里解析 ```html 代码块 与 <html-lib-csv> 包裹的 CSV</summary>
        private static (string Html, string Csv) ParseReply(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return (null, null);
            string html = ExtractFenced(reply, "html");
            if (string.IsNullOrEmpty(html)) html = ExtractFenced(reply, null);
            string csv = ExtractTagged(reply, "html-lib-csv");
            if (string.IsNullOrEmpty(csv)) csv = ExtractFenced(reply, "csv");
            return (StripFence(html), StripFence(csv));
        }

        private static string ExtractFenced(string text, string lang)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                bool open = line.StartsWith("```");
                if (!open) continue;
                if (lang != null && !line.Substring(3).Trim().Equals(lang, StringComparison.OrdinalIgnoreCase)) continue;
                var sb = new StringBuilder();
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].Trim().StartsWith("```"))
                        return sb.ToString();
                    sb.AppendLine(lines[j]);
                }
            }
            return null;
        }

        private static string ExtractTagged(string text, string tag)
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";
            int a = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (a < 0) return null;
            int b = text.IndexOf(close, a, StringComparison.OrdinalIgnoreCase);
            if (b < 0) return null;
            return text.Substring(a + open.Length, b - a - open.Length).Trim();
        }

        private static string StripFence(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            var t = s.Trim();
            if (t.StartsWith("```")) t = t.Substring(3);
            if (t.EndsWith("```")) t = t.Substring(0, t.Length - 3);
            return t.TrimStart('\r', '\n').TrimEnd();
        }

        private static string FirstLine(string s, string fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            var line = s.Trim().Split('\n')[0].Trim();
            if (line.Length > 40) line = line.Substring(0, 40);
            return string.IsNullOrWhiteSpace(line) ? fallback : line;
        }
    }
}
