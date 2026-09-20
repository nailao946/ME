using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ME.Models
{
    public enum TaskType
    {
        OneTime,
        Periodic,
        Recurring,
        Quantitative
    }

    public enum RecurringPattern
    {
        Daily,
        Weekday,
        Weekend,
        Weekly,
        Monthly,
        Interval,
        Custom
    }

    public enum QuantitativeMode
    {
        Accumulate,
        Update
    }

    public class TaskItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private int _id;
        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }

        private string _title;
        public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

        private string _description;
        public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }

        private TaskType _type;
        public TaskType Type { get => _type; set { _type = value; OnPropertyChanged(); } }

        private int? _goalId;
        public int? GoalId { get => _goalId; set { _goalId = value; OnPropertyChanged(); } }

        private int? _parentTaskId;
        public int? ParentTaskId { get => _parentTaskId; set { _parentTaskId = value; OnPropertyChanged(); } }

        private DateTime? _startDate;
        public DateTime? StartDate { get => _startDate; set { _startDate = value; OnPropertyChanged(); } }

        private DateTime? _endDate;
        public DateTime? EndDate { get => _endDate; set { _endDate = value; OnPropertyChanged(); } }

        private bool _isCompleted;
        public bool IsCompleted { get => _isCompleted; set { _isCompleted = value; OnPropertyChanged(); } }

        private DateTime? _completedAt;
        public DateTime? CompletedAt { get => _completedAt; set { _completedAt = value; OnPropertyChanged(); } }

        private bool _isDeleted;
        public bool IsDeleted { get => _isDeleted; set { _isDeleted = value; OnPropertyChanged(); } }

        private DateTime _deletedAt;
        public DateTime DeletedAt { get => _deletedAt; set { _deletedAt = value; OnPropertyChanged(); } }

        private DateTime _createdAt;
        public DateTime CreatedAt { get => _createdAt; set { _createdAt = value; OnPropertyChanged(); } }

        private DateTime _updatedAt;
        public DateTime UpdatedAt { get => _updatedAt; set { _updatedAt = value; OnPropertyChanged(); } }

        private int _priority;
        public int Priority { get => _priority; set { _priority = value; OnPropertyChanged(); } }

        private RecurringPattern? _recurringPattern;
        public RecurringPattern? RecurringPattern { get => _recurringPattern; set { _recurringPattern = value; OnPropertyChanged(); } }

        private int? _recurringInterval;
        public int? RecurringInterval { get => _recurringInterval; set { _recurringInterval = value; OnPropertyChanged(); } }

        private string _recurringDaysOfWeek;
        public string RecurringDaysOfWeek { get => _recurringDaysOfWeek; set { _recurringDaysOfWeek = value; OnPropertyChanged(); } }

        private int? _recurringDayOfMonth;
        public int? RecurringDayOfMonth { get => _recurringDayOfMonth; set { _recurringDayOfMonth = value; OnPropertyChanged(); } }

        private bool _isLastDayOfMonth;
        public bool IsLastDayOfMonth { get => _isLastDayOfMonth; set { _isLastDayOfMonth = value; OnPropertyChanged(); } }

        private int? _recurringTimesPerDay;
        public int? RecurringTimesPerDay { get => _recurringTimesPerDay; set { _recurringTimesPerDay = value; OnPropertyChanged(); } }

        private int? _recurringTimesPerWeek;
        public int? RecurringTimesPerWeek { get => _recurringTimesPerWeek; set { _recurringTimesPerWeek = value; OnPropertyChanged(); } }

        private int? _recurringCurrentCount;
        public int? RecurringCurrentCount { get => _recurringCurrentCount; set { _recurringCurrentCount = value; OnPropertyChanged(); } }

        private int? _recurringTargetCount;
        public int? RecurringTargetCount { get => _recurringTargetCount; set { _recurringTargetCount = value; OnPropertyChanged(); } }

        private bool _isRecurringCompleted;
        public bool IsRecurringCompleted { get => _isRecurringCompleted; set { _isRecurringCompleted = value; OnPropertyChanged(); } }

        private DateTime? _lastCompletedDate;
        public DateTime? LastCompletedDate { get => _lastCompletedDate; set { _lastCompletedDate = value; OnPropertyChanged(); } }

        private QuantitativeMode? _quantitativeMode;
        public QuantitativeMode? QuantitativeMode { get => _quantitativeMode; set { _quantitativeMode = value; OnPropertyChanged(); } }

        private double? _quantitativeStart;
        public double? QuantitativeStart { get => _quantitativeStart; set { _quantitativeStart = value; OnPropertyChanged(); } }

        private double? _quantitativeTarget;
        public double? QuantitativeTarget { get => _quantitativeTarget; set { _quantitativeTarget = value; OnPropertyChanged(); } }

        private double? _quantitativeCurrent;
        public double? QuantitativeCurrent { get => _quantitativeCurrent; set { _quantitativeCurrent = value; OnPropertyChanged(); } }

        private string _quantitativeUnit;
        public string QuantitativeUnit { get => _quantitativeUnit; set { _quantitativeUnit = value; OnPropertyChanged(); } }

        private double? _quantitativeDailyMin;
        public double? QuantitativeDailyMin { get => _quantitativeDailyMin; set { _quantitativeDailyMin = value; OnPropertyChanged(); } }

        // 量化每日目标基线快照：当日完成 = 当前值 - 基线值 >= 每日目标（跨日时以首次访问的当前值滚动落基线）
        private DateTime? _quantSnapDate;
        public DateTime? QuantSnapDate { get => _quantSnapDate; set { _quantSnapDate = value; OnPropertyChanged(); } }

        private double? _quantSnapValue;
        public double? QuantSnapValue { get => _quantSnapValue; set { _quantSnapValue = value; OnPropertyChanged(); } }

        private bool _countTowardsParent;
        public bool CountTowardsParent { get => _countTowardsParent; set { _countTowardsParent = value; OnPropertyChanged(); } }

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set { _sortOrder = value; OnPropertyChanged(); } }

        private int? _timeTagId;
        public int? TimeTagId { get => _timeTagId; set { _timeTagId = value; OnPropertyChanged(); } }

        // 跨设备唯一标识（与安卓端对齐：任务依赖按 Uid 引用；旧数据在保存/同步时自动补）
        private string _uid;
        public string Uid { get => _uid; set { _uid = value; OnPropertyChanged(); } }

        // 前置任务依赖：存前置任务的 Uid；存在未完成前置时本任务锁定无法打卡
        private List<string> _blockedBy = new List<string>();
        public List<string> BlockedBy { get => _blockedBy; set { _blockedBy = value ?? new List<string>(); OnPropertyChanged(); } }

        // 量化进度日志（补记用）：某日进度 = 该日 Delta 合计；无日志时退回旧基线逻辑（与安卓端同口径）
        private List<QuantEntry> _quantLog = new List<QuantEntry>();
        public List<QuantEntry> QuantLog { get => _quantLog; set { _quantLog = value ?? new List<QuantEntry>(); OnPropertyChanged(); } }
    }

    /// <summary>量化某日增量（补记历史日期用；Date=yyyy-MM-dd）</summary>
    public class QuantEntry
    {
        public string Date { get; set; } = "";
        public double Delta { get; set; }
    }
}
