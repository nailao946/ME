using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ME.Data
{
    public static class JsonStore
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly string DataPath;

        private static readonly ConcurrentDictionary<string, (object data, DateTime timestamp)> _cache = new();

        static JsonStore()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DataPath = Path.Combine(appData, "ME", "JsonData");
            if (!Directory.Exists(DataPath))
                Directory.CreateDirectory(DataPath);
        }

        private static string GetFilePath(string fileName) => Path.Combine(DataPath, fileName + ".json");

        public static List<T> Load<T>(string fileName) where T : new()
        {
            var path = GetFilePath(fileName);
            if (!File.Exists(path))
                return new List<T>();

            var json = File.ReadAllText(path);
            return string.IsNullOrEmpty(json) ? new List<T>() : JsonSerializer.Deserialize<List<T>>(json, _options) ?? new List<T>();
        }

        public static List<T> LoadWithCache<T>(string fileName, int cacheSeconds = 2) where T : new()
        {
            if (_cache.TryGetValue(fileName, out var entry))
            {
                if (DateTime.UtcNow - entry.timestamp < TimeSpan.FromSeconds(cacheSeconds))
                {
                    return (List<T>)entry.data;
                }
            }
            var data = Load<T>(fileName);
            _cache[fileName] = (data, DateTime.UtcNow);
            return data;
        }

        public static void InvalidateCache(string fileName)
        {
            _cache.TryRemove(fileName, out _);
        }

        public static void InvalidateAllCache()
        {
            _cache.Clear();
        }

        public static void Save<T>(string fileName, List<T> data)
        {
            var json = JsonSerializer.Serialize(data, _options);
            File.WriteAllText(GetFilePath(fileName), json);
            InvalidateCache(fileName);
        }

        public static void Backup(string backupPath)
        {
            if (Directory.Exists(DataPath))
            {
                if (!Directory.Exists(backupPath))
                    Directory.CreateDirectory(backupPath);
                foreach (var file in Directory.GetFiles(DataPath))
                {
                    File.Copy(file, Path.Combine(backupPath, Path.GetFileName(file)), true);
                }
            }
        }

        public static void Restore(string backupPath)
        {
            if (Directory.Exists(backupPath))
            {
                foreach (var file in Directory.GetFiles(backupPath))
                {
                    File.Copy(file, Path.Combine(DataPath, Path.GetFileName(file)), true);
                }
            }
        }
    }

    public static class DatabaseHelper
    {
        public static void Initialize()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dataPath = Path.Combine(appData, "ME", "JsonData");
            if (!Directory.Exists(dataPath))
                Directory.CreateDirectory(dataPath);

            var backupPath = Path.Combine(appData, "ME", "Backups");
            if (!Directory.Exists(backupPath))
                Directory.CreateDirectory(backupPath);
        }

        public static void BackupDatabase(string backupPath)
        {
            JsonStore.Backup(backupPath);
        }

        public static void RestoreDatabase(string backupPath)
        {
            JsonStore.Restore(backupPath);
        }
    }
}
