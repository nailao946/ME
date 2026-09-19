using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ME.Data
{
    /// <summary>
    /// 模块资料库页面：一段可脱离数据库独立打开的 HTML + 配套 CSV。
    /// 存于 html_library.json，随云同步 / 备份在 PC 与安卓互通。
    /// </summary>
    public class HtmlLibraryPage
    {
        public int Id { get; set; }
        /// <summary>绑定的自定义模块 Id；0 = 不绑定任何模块（全局资料）</summary>
        public int ModuleId { get; set; }
        public string Title { get; set; } = "";
        public string Html { get; set; } = "";
        public string Csv { get; set; } = "";
        /// <summary>manual = 手写 / 导入，ai = AI 生成</summary>
        public string Source { get; set; } = "manual";
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public bool IsDeleted { get; set; }

        public long SizeBytes => System.Text.Encoding.UTF8.GetByteCount(Html ?? "") + System.Text.Encoding.UTF8.GetByteCount(Csv ?? "");
    }

    /// <summary>html_library.json 读写（与其它数据文件同目录，自动参与云同步与备份）</summary>
    public static class HtmlLibraryRepository
    {
        private const string FileName = "html_library";

        public static List<HtmlLibraryPage> GetAll()
        {
            return JsonStore.Load<HtmlLibraryPage>(FileName).Where(p => !p.IsDeleted).ToList();
        }

        public static List<HtmlLibraryPage> GetFor(int moduleId)
        {
            return GetAll().Where(p => p.ModuleId == moduleId)
                .OrderByDescending(p => p.UpdatedAt).ToList();
        }

        public static HtmlLibraryPage Get(int id)
        {
            return JsonStore.Load<HtmlLibraryPage>(FileName).FirstOrDefault(p => p.Id == id && !p.IsDeleted);
        }

        public static HtmlLibraryPage Add(HtmlLibraryPage page)
        {
            var all = JsonStore.Load<HtmlLibraryPage>(FileName);
            page.Id = all.Count > 0 ? all.Max(p => p.Id) + 1 : 1;
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            page.CreatedAt = now;
            page.UpdatedAt = now;
            all.Add(page);
            JsonStore.Save(FileName, all);
            return page;
        }

        public static void Update(HtmlLibraryPage page)
        {
            var all = JsonStore.Load<HtmlLibraryPage>(FileName);
            var existing = all.FirstOrDefault(p => p.Id == page.Id);
            if (existing == null) { Add(page); return; }
            existing.Title = page.Title;
            existing.Html = page.Html;
            existing.Csv = page.Csv;
            existing.Source = page.Source;
            existing.ModuleId = page.ModuleId;
            existing.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            JsonStore.Save(FileName, all);
        }

        public static void Delete(int id)
        {
            var all = JsonStore.Load<HtmlLibraryPage>(FileName);
            var existing = all.FirstOrDefault(p => p.Id == id);
            if (existing == null) return;
            existing.IsDeleted = true;
            JsonStore.Save(FileName, all);
        }

        /// <summary>把页面导出成 .html + .csv 两个文件（返回实际写出的文件路径）</summary>
        public static (string HtmlPath, string CsvPath) Export(HtmlLibraryPage page, string folder)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string safe = MakeSafeFileName(page.Title);
            string htmlPath = Path.Combine(folder, $"{safe}.html");
            File.WriteAllText(htmlPath, page.Html ?? "");
            string csvPath = null;
            if (!string.IsNullOrWhiteSpace(page.Csv))
            {
                csvPath = Path.Combine(folder, $"{safe}.csv");
                File.WriteAllText(csvPath, page.Csv);
            }
            return (htmlPath, csvPath);
        }

        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var ch in (name ?? "资料页"))
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            var s = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(s) ? "资料页" : s;
        }
    }
}
