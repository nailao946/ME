using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ME.Data;

namespace ME.Services
{
    /// <summary>
    /// 云同步提供者接口（预留）。
    /// 未来接入云端/网盘/自建服务器时实现该接口并在 SyncService.Provider 注册，
    /// 安卓端可复用同一套 JSON 数据格式实现双向同步。
    /// </summary>
    public interface ISyncProvider
    {
        /// <summary>上传全量 JSON 数据，返回远端版本号</summary>
        Task<string> PushAsync(string json);

        /// <summary>拉取远端数据，返回 JSON；无数据时返回 null</summary>
        Task<string> PullAsync();

        Task<bool> TestConnectionAsync();
    }

    public static class SyncService
    {
        /// <summary>当前同步后端（默认未配置 = 仅本地）</summary>
        public static ISyncProvider Provider { get; set; }

        public static readonly Dictionary<string, string> DataFiles = new()
        {
            ["tasks"] = "tasks",
            ["goals"] = "goals",
            ["tags"] = "goal_tags",
            ["time_tags"] = "time_tags",
            ["time_records"] = "time_records",
            ["task_completions"] = "task_completions",
            ["focus_sessions"] = "focus_sessions",
            ["settings"] = "settings",
            ["visions"] = "visions",
            ["reviews"] = "reviews",
            ["health_records"] = "health_records",
            ["water_containers"] = "water_containers",
        };

        public static string ExportAllAsJson()
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ME", "JsonData");
            var merged = new Dictionary<string, object>();
            if (Directory.Exists(dataDir))
            {
                foreach (var kvp in DataFiles)
                {
                    var path = Path.Combine(dataDir, kvp.Key + ".json");
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        merged[kvp.Value] = doc.RootElement;
                    }
                }
            }
            return JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
        }

        public static DateTime? GetLastSyncTime()
        {
            var repo = new SettingsRepository();
            var val = repo.GetValue("LastSyncTime", "");
            if (DateTime.TryParse(val, out var dt)) return dt;
            return null;
        }

        public static void SetLastSyncTime(DateTime time)
        {
            var repo = new SettingsRepository();
            repo.SetValue("LastSyncTime", time.ToString("o"));
        }

        public static async Task SyncAsync()
        {
            if (Provider == null)
                return; // 未配置云端后端，仅本地

            var json = ExportAllAsJson();
            await Provider.PushAsync(json);
            SetLastSyncTime(DateTime.Now);
        }
    }
}
