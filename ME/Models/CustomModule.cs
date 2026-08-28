using System;
using System.Collections.Generic;
using System.Linq;
using ME.Data;

namespace ME.Models
{
    /// <summary>
    /// 自定义模块（可扩展记录块）—— 与安卓端 custom_modules.json 格式完全一致。
    /// 字段类型：number=数值 text=文本 time=时间 bool=是否 select=单选
    /// </summary>
    public class CustomModuleField
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "number";
        public string Unit { get; set; }
        public string Options { get; set; }
    }

    public class CustomModuleRecord
    {
        public int Id { get; set; }
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
        public Dictionary<string, string> Values { get; set; } = new();
        public string Note { get; set; }
    }

    public class CustomModule
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>图标索引（两端内置同一组图标，与安卓 ModuleIconList 顺序一致）</summary>
        public int Icon { get; set; }
        public string ColorHex { get; set; } = "#4F6EF7";
        public List<CustomModuleField> Fields { get; set; } = new();
        public List<CustomModuleRecord> Records { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public bool IsDeleted { get; set; }
    }
}

namespace ME.Data
{
    using ME.Models;

    public static class CustomModuleRepository
    {
        public static List<CustomModule> GetAll() =>
            JsonStore.Load<CustomModule>("custom_modules").Where(m => !m.IsDeleted).OrderBy(m => m.Id).ToList();

        public static void SaveAll(List<CustomModule> modules) => JsonStore.Save("custom_modules", modules);

        public static CustomModule Add(CustomModule m)
        {
            var all = JsonStore.Load<CustomModule>("custom_modules");
            m.Id = all.Count == 0 ? 1 : all.Max(x => x.Id) + 1;
            m.CreatedAt = DateTime.Now;
            all.Add(m);
            JsonStore.Save("custom_modules", all);
            return m;
        }

        public static void Update(CustomModule m)
        {
            var all = JsonStore.Load<CustomModule>("custom_modules");
            var i = all.FindIndex(x => x.Id == m.Id);
            if (i >= 0) { all[i] = m; JsonStore.Save("custom_modules", all); }
        }

        public static void Delete(int id)
        {
            var all = JsonStore.Load<CustomModule>("custom_modules");
            all.RemoveAll(x => x.Id == id);
            JsonStore.Save("custom_modules", all);
        }

        public static void AddRecord(int moduleId, CustomModuleRecord rec)
        {
            var all = JsonStore.Load<CustomModule>("custom_modules");
            var m = all.FirstOrDefault(x => x.Id == moduleId);
            if (m == null) return;
            rec.Id = m.Records.Count == 0 ? 1 : m.Records.Max(r => r.Id) + 1;
            m.Records.Add(rec);
            JsonStore.Save("custom_modules", all);
        }

        public static void DeleteRecord(int moduleId, int recordId)
        {
            var all = JsonStore.Load<CustomModule>("custom_modules");
            var m = all.FirstOrDefault(x => x.Id == moduleId);
            if (m == null) return;
            m.Records.RemoveAll(r => r.Id == recordId);
            JsonStore.Save("custom_modules", all);
        }
    }
}
