using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    /// <summary>
    /// 记账仓储（JSON 持仓，文件名 expenses）。Id 自增规则参考 CustomModuleRepository。
    /// </summary>
    public static class ExpenseRepository
    {
        private const string FileName = "expenses";

        /// <summary>全部记录，按日期倒序（同日按 Id 倒序）</summary>
        public static List<ExpenseRecord> GetAll() =>
            JsonStore.Load<ExpenseRecord>(FileName)
                .OrderByDescending(r => r.Date)
                .ThenByDescending(r => r.Id)
                .ToList();

        /// <summary>原始加载（不排序），供其它汇总逻辑按需处理</summary>
        public static List<ExpenseRecord> GetAllRaw() =>
            JsonStore.Load<ExpenseRecord>(FileName);

        /// <summary>取某月记录（yyyy-MM 前缀匹配）</summary>
        public static List<ExpenseRecord> GetByMonth(string yyyyMM) =>
            GetAll().Where(r => r.Date != null && r.Date.StartsWith(yyyyMM)).ToList();

        public static int Insert(ExpenseRecord rec)
        {
            var all = JsonStore.Load<ExpenseRecord>(FileName);
            rec.Id = all.Count == 0 ? 1 : all.Max(r => r.Id) + 1;
            if (string.IsNullOrEmpty(rec.Uid)) rec.Uid = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(rec.CreatedAt)) rec.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            all.Add(rec);
            JsonStore.Save(FileName, all);
            return rec.Id;
        }

        public static void Update(ExpenseRecord rec)
        {
            var all = JsonStore.Load<ExpenseRecord>(FileName);
            var i = all.FindIndex(r => r.Id == rec.Id);
            if (i >= 0)
            {
                if (string.IsNullOrEmpty(rec.Uid)) rec.Uid = Guid.NewGuid().ToString("N");
                all[i] = rec;
                JsonStore.Save(FileName, all);
            }
        }

        /// <summary>删除记录；若 Uid 为空则先自动补一个（保证同步去重字段完整）</summary>
        public static void Delete(int id)
        {
            var all = JsonStore.Load<ExpenseRecord>(FileName);
            var rec = all.FirstOrDefault(r => r.Id == id);
            if (rec == null) return;
            if (string.IsNullOrEmpty(rec.Uid)) rec.Uid = Guid.NewGuid().ToString("N");
            all.Remove(rec);
            JsonStore.Save(FileName, all);
        }
    }
}
