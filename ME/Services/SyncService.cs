using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ME.Data;

namespace ME.Services
{
    public static class SyncService
    {
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

        public static Task SyncAsync()
        {
            return Task.CompletedTask;
        }
    }
}
