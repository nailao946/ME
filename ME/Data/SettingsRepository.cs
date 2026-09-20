using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    public class SettingsRepository
    {
        private const string FileName = "settings";

        // 进程内缓存（2 秒 TTL，写穿）。之前每次 GetValue 都整读一遍 settings.json，
        // 而 ThemeService 一次 ApplyTheme 要读 7 次 + 写 3 次，滑杆拖动时直接卡爆。
        private static readonly object _lock = new object();
        private static List<AppSettings> _cache;
        private static DateTime _cacheTime = DateTime.MinValue;

        public string GetValue(string key, string defaultValue = "")
        {
            var settings = Snapshot();
            return settings.FirstOrDefault(s => s.Key == key)?.Value ?? defaultValue;
        }

        public void SetValue(string key, string value)
        {
            lock (_lock)
            {
                var settings = JsonStore.Load<AppSettings>(FileName);
                var existing = settings.FirstOrDefault(s => s.Key == key);
                if (existing != null)
                {
                    existing.Value = value;
                }
                else
                {
                    settings.Add(new AppSettings { Key = key, Value = value });
                }
                JsonStore.Save(FileName, settings);
                _cache = settings;           // 写穿：自己的写立刻对自己可见
                _cacheTime = DateTime.UtcNow;
            }
        }

        private static List<AppSettings> Snapshot()
        {
            lock (_lock)
            {
                if (_cache != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromSeconds(2))
                    return _cache;
                _cache = JsonStore.Load<AppSettings>(FileName);
                _cacheTime = DateTime.UtcNow;
                return _cache;
            }
        }
    }
}
