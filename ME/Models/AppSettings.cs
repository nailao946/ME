namespace ME.Models
{
    public class AppSettings
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public static class SettingsKeys
    {
        public const string Theme = "Theme";
        public const string Language = "Language";
        public const string CornerRadius = "CornerRadius";
        public const string FocusSoundEnabled = "FocusSoundEnabled";
        public const string SoundEnabled = "SoundEnabled";
        public const string AutoStart = "AutoStart";
        public const string MinimizeToTray = "MinimizeToTray";
        public const string LastBackupDate = "LastBackupDate";
        public const string DefaultView = "DefaultView";
        public const string WindowBorderColor = "WindowBorderColor";
        public const string TraySoundEnabled = "TraySoundEnabled";
        public const string TrayBalloonEnabled = "TrayBalloonEnabled";
        public const string FloatingWindowEnabled = "FloatingWindowEnabled";
        /// <summary>全局快捷键 Ctrl+Alt+M 显隐主窗口（默认关）</summary>
        public const string GlobalHotkeyEnabled = "GlobalHotkeyEnabled";
        public const string WeekStartDay = "WeekStartDay";
        public const string StatsIncludedTags = "StatsIncludedTags";
        public const string PomodoroAutoStart = "PomodoroAutoStart";
        public const string HealthWaterGoal = "HealthWaterGoal";
        public const string HealthHeight = "HealthHeight";
        public const string HealthGender = "HealthGender";
        public const string HealthWaterMigrated = "HealthWaterMigrated";
        public const string UricTarget = "UricTarget";
        public const string DeepSeekApiKey = "DeepSeekApiKey";
        public const string AiSystemPrompt = "AiSystemPrompt";
    }

    public enum AppTheme
    {
        Light,
        Dark
    }
}
