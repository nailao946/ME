using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ME.Models
{
    public enum GoalColor
    {
        Red, Green, Blue, Pink, Gray, Yellow
    }

    public enum GoalTimeFrame
    {
        ShortTerm, LongTerm, Inspiration
    }

    public enum DecomposeMode
    {
        OKR, Milestone, Category
    }

    public class Goal : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private int _id;
        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private string _description;
        public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }

        private GoalColor _color;
        public GoalColor Color { get => _color; set { _color = value; OnPropertyChanged(); } }

        private string _colorHex;
        /// <summary>自定义颜色（扩展字段，与安卓端互通；非空时优先于枚举 Color）</summary>
        public string ColorHex { get => _colorHex; set { _colorHex = value; OnPropertyChanged(); } }

        private GoalTimeFrame _timeFrame;
        public GoalTimeFrame TimeFrame { get => _timeFrame; set { _timeFrame = value; OnPropertyChanged(); } }

        private int? _parentId;
        public int? ParentId { get => _parentId; set { _parentId = value; OnPropertyChanged(); } }

        private DateTime? _startDate;
        public DateTime? StartDate { get => _startDate; set { _startDate = value; OnPropertyChanged(); } }

        private DateTime? _endDate;
        public DateTime? EndDate { get => _endDate; set { _endDate = value; OnPropertyChanged(); } }

        private double _progress;
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }

        private bool _isArchived;
        public bool IsArchived { get => _isArchived; set { _isArchived = value; OnPropertyChanged(); } }

        private bool _isLocked;
        public bool IsLocked { get => _isLocked; set { _isLocked = value; OnPropertyChanged(); } }

        private bool _isDeleted;
        public bool IsDeleted { get => _isDeleted; set { _isDeleted = value; OnPropertyChanged(); } }

        private DateTime? _deletedAt;
        public DateTime? DeletedAt { get => _deletedAt; set { _deletedAt = value; OnPropertyChanged(); } }

        private DateTime _createdAt;
        public DateTime CreatedAt { get => _createdAt; set { _createdAt = value; OnPropertyChanged(); } }

        private DateTime _updatedAt;
        public DateTime UpdatedAt { get => _updatedAt; set { _updatedAt = value; OnPropertyChanged(); } }

        private string _notes;
        public string Notes { get => _notes; set { _notes = value; OnPropertyChanged(); } }

        private int? _tagId;
        public int? TagId { get => _tagId; set { _tagId = value; OnPropertyChanged(); } }

        private string _tagColor;
        /// <summary>
        /// Populated from TagId lookup. Used for XAML binding.
        /// </summary>
        public string TagColor { get => _tagColor; set { _tagColor = value; OnPropertyChanged(); } }

        private string _tagName;
        /// <summary>
        /// Populated from TagId lookup. Used for XAML binding.
        /// </summary>
        public string TagName { get => _tagName; set { _tagName = value; OnPropertyChanged(); } }

        private System.Collections.Generic.List<TaskItem> _subtasks;
        /// <summary>
        /// Child tasks. Populated when loading. Not persisted directly.
        /// </summary>
        public System.Collections.Generic.List<TaskItem> Subtasks { get => _subtasks; set { _subtasks = value; OnPropertyChanged(); } }

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

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set { _sortOrder = value; OnPropertyChanged(); } }
    }
}
