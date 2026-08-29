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

    /// <summary>
    /// 模块仪表盘组件（PC 端专属配置，存于 custom_dashboards.json，安卓端会忽略此文件）。
    /// Type：stat=数值统计 chart=趋势折线 pie=分布占比 streak=连续打卡
    /// </summary>
    public class CustomDashboardWidget
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Type { get; set; } = "stat";
        /// <summary>关联的字段 Key（streak 可为空表示任意记录）</summary>
        public string FieldKey { get; set; } = "";
        /// <summary>stat 聚合方式：sum avg max min latest count</summary>
        public string Agg { get; set; } = "sum";
        /// <summary>统计范围：today week month all（chart 用 Days）</summary>
        public string Range { get; set; } = "all";
        /// <summary>chart 趋势天数：7 / 30</summary>
        public int Days { get; set; } = 30;
    }

    public class CustomDashboard
    {
        public string ModuleId { get; set; } = "";
        public List<CustomDashboardWidget> Widgets { get; set; } = new();
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

    public static class CustomDashboardRepository
    {
        public static List<CustomDashboardWidget> GetFor(int moduleId)
        {
            var all = JsonStore.Load<CustomDashboard>("custom_dashboards");
            return all.FirstOrDefault(d => d.ModuleId == moduleId.ToString())?.Widgets ?? new List<CustomDashboardWidget>();
        }

        public static void SaveFor(int moduleId, List<CustomDashboardWidget> widgets)
        {
            var all = JsonStore.Load<CustomDashboard>("custom_dashboards");
            var i = all.FindIndex(d => d.ModuleId == moduleId.ToString());
            if (i >= 0) all[i].Widgets = widgets;
            else all.Add(new CustomDashboard { ModuleId = moduleId.ToString(), Widgets = widgets });
            JsonStore.Save("custom_dashboards", all);
        }
    }
}
