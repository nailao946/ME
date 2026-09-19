using System;
using System.Globalization;
using System.Threading;
using ME.Core;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    /// <summary>Desktop language preference, system fallback, and change notification.</summary>
    public static class LanguageService
    {
        public const string System = "system";
        public const string Chinese = "zh";
        public const string English = "en";
        public static event EventHandler LanguageChanged;
        private static string _language = System;

        public static string Language => _language;
        public static void Initialize()
        {
            _language = new SettingsRepository().GetValue(SettingsKeys.Language, System);
            ApplyCulture(false);
        }
        public static void SetLanguage(string language)
        {
            if (language != System && language != Chinese && language != English) language = System;
            _language = language;
            new SettingsRepository().SetValue(SettingsKeys.Language, language);
            ApplyCulture(true);
        }
        public static string EffectiveLanguage => _language == System
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? Chinese : English)
            : _language;
        public static string Text(string chinese, string english) => EffectiveLanguage == English ? english : chinese;

        private static void ApplyCulture(bool notify)
        {
            var culture = EffectiveLanguage == English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            if (notify)
            {
                LanguageChanged?.Invoke(null, EventArgs.Empty);
                EventAggregator.Instance.Publish("LanguageChanged");
            }
        }
    }
}
