using System;

namespace ME.Models
{
    /// <summary>
    /// 记账记录（支出 / 收入）。文件名 expenses.json，存于本地 JsonData 目录。
    /// </summary>
    public class ExpenseRecord
    {
        public int Id { get; set; }
        public string Uid { get; set; } = "";
        public string Date { get; set; } = "";        // yyyy-MM-dd
        public double Amount { get; set; }
        public bool IsIncome { get; set; }
        public string Category { get; set; } = "";
        public string Note { get; set; }
        public string CreatedAt { get; set; } = "";   // yyyy-MM-dd HH:mm:ss
    }
}
