using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    /// <summary>
    /// 用药提醒服务：启动后定时检查，到时间点且当天该次未提醒过时，
    /// 通过系统托盘气泡通知"该用药了"。仅在用户为用药开启 Remind 时生效。
    /// </summary>
    public class MedicationReminderService
    {
        private readonly DispatcherTimer _timer;
        private readonly MedicationRepository _repo = new MedicationRepository();
        // 去重 key：medId|yyyy-MM-dd|HH:mm
        private readonly HashSet<string> _fired = new HashSet<string>();

        public MedicationReminderService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _timer.Tick += (s, e) => Check();
            _timer.Start();
        }

        private void Check()
        {
            try
            {
                var now = DateTime.Now;
                var today = now.ToString("yyyy-MM-dd");
                var hhmm = now.ToString("HH:mm");

                foreach (var m in _repo.GetAll())
                {
                    if (!m.Remind || !m.IsActive || string.IsNullOrEmpty(m.Times)) continue;
                    if (!ShouldRemindToday(m, now)) continue;

                    var times = m.Times.Split(',').Select(t => t.Trim()).ToList();
                    if (!times.Contains(hhmm)) continue;

                    var key = $"{m.Id}|{today}|{hhmm}";
                    if (_fired.Add(key))
                    {
                        AppNotifier.Show("💊 用药提醒", $"{m.Name}：到时间用药了（{hhmm}）");
                    }
                }

                // 清理一天前的已提醒记录，避免内存无限增长
                var cutoff = now.AddDays(-1).ToString("yyyy-MM-dd");
                _fired.RemoveWhere(k =>
                {
                    var parts = k.Split('|');
                    return parts.Length < 2 || string.CompareOrdinal(parts[1], cutoff) < 0;
                });
            }
            catch { }
        }

        /// <summary>按频率判断今天是否该吃（时间点命中交给调用方判断）</summary>
        private bool ShouldRemindToday(MedicationRecord m, DateTime now)
        {
            switch (m.Frequency)
            {
                case MedicationFrequency.Daily:
                    return true;
                case MedicationFrequency.WeeklyDays:
                    if (string.IsNullOrEmpty(m.WeeklyDays)) return true;
                    var today = ((int)now.DayOfWeek + 6) % 7 + 1; // 周一=1 … 周日=7
                    return m.WeeklyDays.Split(',')
                        .Select(s => int.TryParse(s, out var d) ? d : 0)
                        .Contains(today);
                case MedicationFrequency.EveryNDays:
                    if (!m.StartDate.HasValue) return true;
                    var days = (now.Date - m.StartDate.Value.Date).Days;
                    return days % Math.Max(1, m.FrequencyN) == 0;
                case MedicationFrequency.Interval:
                case MedicationFrequency.AsNeeded:
                default:
                    return true;
            }
        }
    }
}
