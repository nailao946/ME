using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    /// <summary>
    /// 小米运动健康官方导出数据导入器。
    /// 支持：小米账号「隐私中心」导出的 zip（内含 *_MiFitness_hlth_center_*.csv），或直接选择 CSV 文件。
    /// 尽力解析睡眠（分钟）与体重（kg）导入健康记录；步数/心率因暂无对应分类，跳过。
    /// 说明：小米未开放实时 API，此为唯一合规的官方数据通道（需用户手动下载）。
    /// </summary>
    public static class XiaomiImportService
    {
        public class ImportResult
        {
            public int SleepImported;
            public int WeightImported;
            public int Overwritten;
            public int SkippedRows;
            public List<string> Messages = new List<string>();
        }

        public static ImportResult ImportFile(string path)
        {
            var result = new ImportResult();
            var files = new List<string>();
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries.Where(en => en.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                {
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
                    ParseCsv(reader, entry.Name, result);
                }
            }
            else
            {
                using var reader = new StreamReader(path, Encoding.UTF8, true);
                ParseCsv(reader, Path.GetFileName(path), result);
            }
            return result;
        }

        private static void ParseCsv(StreamReader reader, string fileName, ImportResult result)
        {
            // 跳过空文件
            var headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine)) return;

            var headers = SplitCsv(headerLine);
            if (headers.Length < 2)
            {
                result.Messages.Add($"{fileName}：表头无法识别，已跳过");
                return;
            }

            int dateIdx = -1, sleepIdx = -1, weightIdx = -1;
            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i].Trim().ToLowerInvariant();
                if (dateIdx < 0 && (h.Contains("date") || h.Contains("time") || h.Contains("日期") || h.Contains("时间")))
                    dateIdx = i;
                if (sleepIdx < 0 && (h.Contains("sleep") || h.Contains("睡眠")))
                    sleepIdx = i;
                if (weightIdx < 0 && (h.Contains("weight") || h.Contains("体重")))
                    weightIdx = i;
            }

            // 文件里没有可识别的睡眠/体重列
            if (sleepIdx < 0 && weightIdx < 0)
            {
                result.Messages.Add($"{fileName}：未找到睡眠或体重数据列，已跳过");
                return;
            }
            // 有数据列但缺日期列，无法导入
            if (dateIdx < 0)
            {
                result.Messages.Add($"{fileName}：未找到日期列（date/日期），已跳过");
                return;
            }

            var repo = new HealthRepository();
            int line = 1;
            string lineText;
            while ((lineText = reader.ReadLine()) != null)
            {
                line++;
                var cols = SplitCsv(lineText);
                if (cols == null)
                {
                    result.SkippedRows++; // 引号未闭合等异常行
                    continue;
                }
                if (cols.Length <= Math.Max(dateIdx, Math.Max(sleepIdx, weightIdx))) continue;

                DateTime? date = TryParseDate(cols[dateIdx]);
                if (date == null)
                {
                    result.SkippedRows++;
                    continue;
                }
                var dateStr = date.Value.ToString("yyyy-MM-dd");

                if (sleepIdx >= 0 && double.TryParse(cols[sleepIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out var sleepVal) && sleepVal > 0)
                {
                    if (repo.GetByTypeAndDate("sleep", dateStr) != null) result.Overwritten++;
                    repo.Upsert(new HealthRecord { Type = "sleep", Date = dateStr, Value = sleepVal });
                    result.SleepImported++;
                }
                if (weightIdx >= 0 && double.TryParse(cols[weightIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out var weightVal) && weightVal > 0)
                {
                    if (repo.GetByTypeAndDate("weight", dateStr) != null) result.Overwritten++;
                    repo.Upsert(new HealthRecord { Type = "weight", Date = dateStr, Value = weightVal });
                    result.WeightImported++;
                }
            }
            if (sleepIdx >= 0 || weightIdx >= 0)
                result.Messages.Add($"{fileName}：解析 {line - 1} 行");
        }

        private static DateTime? TryParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();

            // 8 位纯数字 yyyymmdd（必须在 Unix 秒之前判断，否则被误判为时间戳）
            if (t.Length == 8 && int.TryParse(t, out var ymd))
            {
                var y = ymd / 10000; var m = (ymd / 100) % 100; var d = ymd % 100;
                try { return new DateTime(y, m, d); } catch { return null; }
            }

            // 含分隔符的日期（yyyy-MM-dd / yyyy/MM/dd / yyyy.MM.dd）
            if (t.IndexOfAny(new[] { '-', '/', '.' }) >= 0 && DateTime.TryParse(t, out var dt))
                return dt.Date;

            // Unix 秒/毫秒时间戳（排除 8 位日期区间 19700101~20991231 之外的范围）
            if (long.TryParse(t, out var ts) && (ts < 10000000 || ts >= 100000000))
            {
                try
                {
                    if (ts > 100000000000L) ts /= 1000; // 毫秒→秒
                    return DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.Date;
                }
                catch { return null; }
            }
            return null;
        }

        /// <summary>简单 CSV 分行（支持引号包裹字段）；引号未闭合时返回 null 表示该行异常</summary>
        private static string[] SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            foreach (var ch in line)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }
            if (inQuotes) return null; // 引号未闭合，行异常
            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }
}
