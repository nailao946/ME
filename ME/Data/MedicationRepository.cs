using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    /// <summary>
    /// 用药记录仓库（medications.json），沿用 JsonStore 模式。
    /// </summary>
    public class MedicationRepository
    {
        private const string FileName = "medications";

        public List<MedicationRecord> GetAll()
        {
            return JsonStore.Load<MedicationRecord>(FileName)
                .Where(m => !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt).ToList();
        }

        /// <summary>当前正在服用的药物（有效期内）</summary>
        public List<MedicationRecord> GetActive()
        {
            return GetAll().Where(m => m.IsActive).ToList();
        }

        public MedicationRecord GetById(int id)
        {
            return JsonStore.Load<MedicationRecord>(FileName).FirstOrDefault(m => m.Id == id);
        }

        public int Insert(MedicationRecord record)
        {
            var records = JsonStore.Load<MedicationRecord>(FileName);
            var maxId = records.Count > 0 ? records.Max(m => m.Id) : 0;
            record.Id = maxId + 1;
            record.CreatedAt = DateTime.Now;
            records.Add(record);
            JsonStore.Save(FileName, records);
            return record.Id;
        }

        public void Update(MedicationRecord record)
        {
            var records = JsonStore.Load<MedicationRecord>(FileName);
            var existing = records.FirstOrDefault(m => m.Id == record.Id);
            if (existing != null)
            {
                existing.Name = record.Name;
                existing.Type = record.Type;
                existing.SpecValue = record.SpecValue;
                existing.Unit = record.Unit;
                existing.Frequency = record.Frequency;
                existing.FrequencyN = record.FrequencyN;
                existing.WeeklyDays = record.WeeklyDays;
                existing.Times = record.Times;
                existing.StartDate = record.StartDate;
                existing.EndDate = record.EndDate;
                existing.Note = record.Note;
                JsonStore.Save(FileName, records);
            }
        }

        public void Delete(int id)
        {
            var records = JsonStore.Load<MedicationRecord>(FileName);
            var target = records.FirstOrDefault(m => m.Id == id);
            if (target != null)
            {
                target.IsDeleted = true;
                JsonStore.Save(FileName, records);
            }
        }

        // ===== 显示辅助 =====
        public static string MedicationTypeName(MedicationType t)
        {
            switch (t)
            {
                case MedicationType.Capsule: return "胶囊";
                case MedicationType.Tablet: return "药片";
                case MedicationType.Liquid: return "液体";
                case MedicationType.Topical: return "外用";
                case MedicationType.Inhaler: return "吸入";
                case MedicationType.Powder: return "粉末";
                case MedicationType.Injection: return "注射";
                case MedicationType.Drop: return "滴剂";
                case MedicationType.Patch: return "贴剂";
                default: return "其他";
            }
        }

        public static string MedicationUnitName(MedicationUnit u)
        {
            switch (u)
            {
                case MedicationUnit.Ml: return "毫升";
                case MedicationUnit.Mg: return "毫克";
                case MedicationUnit.G: return "克";
                case MedicationUnit.Mcg: return "微克";
                default: return "%";
            }
        }

        public static string MedicationUnitAbbr(MedicationUnit u)
        {
            switch (u)
            {
                case MedicationUnit.Ml: return "ml";
                case MedicationUnit.Mg: return "mg";
                case MedicationUnit.G: return "g";
                case MedicationUnit.Mcg: return "μg";
                default: return "%";
            }
        }

        public static string FrequencyName(MedicationFrequency f)
        {
            switch (f)
            {
                case MedicationFrequency.Daily: return "每天";
                case MedicationFrequency.EveryNDays: return "每隔 N 天";
                case MedicationFrequency.WeeklyDays: return "每周特定日期";
                case MedicationFrequency.Interval: return "循环定时";
                default: return "按需";
            }
        }
    }
}
