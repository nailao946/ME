using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    /// <summary>
    /// 主题引擎：两类主题风格（普通 / 毛玻璃）× 三种深浅模式（浅色 / 深色 / 跟随系统）。
    /// 毛玻璃下可再配「窗口透明度 + 背景（渐变配色 / 背景图片 / 纯透明）」。
    /// 约束：BackgroundBrush / CardBrush 等必须始终是 SolidColorBrush（代码里有强转），
    /// 渐变 / 图片 / 透明底只允许放在 WindowBackgroundBrush 上，仅窗口外壳使用。
    /// </summary>
    public static class ThemeService
    {
        public static event Action<string> ThemeChanged;

        public static class Keys
        {
            public const string Style = "ThemeStyle";          // Normal | Glass
            public const string Tone = "ThemeTone";            // Light | Dark | System
            public const string GlassMode = "GlassMode";       // Gradient | Image | Transparent
            public const string GlassOpacity = "GlassOpacity"; // 0-100
            public const string GlassImagePath = "GlassImagePath";
            public const string GlassGradient = "GlassGradient"; // 配色索引
        }

        /// <summary>毛玻璃渐变配色（浅色 / 深色各一套，三段渐变）</summary>
        public static readonly (string Name, string[] Light, string[] Dark)[] GlassGradients =
        {
            ("晨雾蓝",   new[] { "#EEF2FF", "#E3ECFB", "#F5E9FB" }, new[] { "#171A2B", "#1D2136", "#241D33" }),
            ("暮色紫",   new[] { "#F3EDFF", "#E9DFFB", "#FDEBF3" }, new[] { "#1D1826", "#241C33", "#2A1E2E" }),
            ("森林绿",   new[] { "#EAF7EF", "#DDF1E6", "#F2FBEE" }, new[] { "#131D18", "#18261F", "#1C2A22" }),
            ("蜜桃橙",   new[] { "#FFF1E8", "#FFE6D8", "#FFF7EC" }, new[] { "#221A16", "#2A201A", "#2E2620" }),
            ("海洋青",   new[] { "#E6F7F7", "#D8F0F2", "#EEFBF9" }, new[] { "#121C1E", "#172427", "#1B2B2E" }),
            ("玫瑰粉",   new[] { "#FFF0F4", "#FFE3EC", "#FFF6F9" }, new[] { "#23161B", "#2B1B22", "#321F28" }),
            ("石墨灰",   new[] { "#F1F1F4", "#E9E9EE", "#F8F8FA" }, new[] { "#161618", "#1C1C1F", "#222226" }),
            ("琥珀金",   new[] { "#FFF9EA", "#FFF2D9", "#FFFDF4" }, new[] { "#211C12", "#282316", "#2E2819" }),
        };

        public static string Style
        {
            get
            {
                var s = new SettingsRepository();
                var style = s.GetValue(Keys.Style, "");
                if (string.IsNullOrEmpty(style))
                {
                    // 兼容旧版：Theme=Glass 视为毛玻璃，其余视为普通
                    var legacy = s.GetValue(SettingsKeys.Theme, "Light");
                    style = legacy == "Glass" ? "Glass" : "Normal";
                }
                return style;
            }
        }

        public static string Tone
        {
            get
            {
                var s = new SettingsRepository();
                var tone = s.GetValue(Keys.Tone, "");
                if (string.IsNullOrEmpty(tone))
                {
                    var legacy = s.GetValue(SettingsKeys.Theme, "Light");
                    tone = legacy switch { "Dark" => "Dark", "System" => "System", _ => "Light" };
                }
                return tone;
            }
        }

        public static bool IsGlass => Style == "Glass";

        public static string GlassMode => new SettingsRepository().GetValue(Keys.GlassMode, "Gradient");
        public static int GlassOpacity
        {
            get
            {
                var raw = new SettingsRepository().GetValue(Keys.GlassOpacity, "88");
                return Math.Max(5, Math.Min(100, int.TryParse(raw, out var v) ? v : 88));
            }
        }
        public static string GlassImagePath => new SettingsRepository().GetValue(Keys.GlassImagePath, "");
        public static int GlassGradientIndex
        {
            get
            {
                var raw = new SettingsRepository().GetValue(Keys.GlassGradient, "0");
                var v = int.TryParse(raw, out var i) ? i : 0;
                return Math.Max(0, Math.Min(GlassGradients.Length - 1, v));
            }
        }

        public static void Initialize() => ApplyTheme();

        // 上一次实际应用的参数签名：一样就整体跳过（连续点击 / 滑杆防抖后的冗余调用不再重刷）
        private static string _lastSig;

        private static string Signature(bool isGlass, bool isDark)
        {
            return isGlass
                ? $"G|{isDark}|{GlassMode}|{GlassOpacity}|{GlassGradientIndex}|{GlassImagePath}"
                : $"N|{isDark}";
        }

        /// <summary>按当前设置（ThemeStyle + ThemeTone + 毛玻璃子项）应用主题</summary>
        public static void ApplyTheme()
        {
            bool isGlass = IsGlass;
            string tone = Tone;
            bool isDark = tone == "Dark" || (tone == "System" && IsSystemDark());
            var sig = Signature(isGlass, isDark);
            if (sig == _lastSig) return;
            _lastSig = sig;
            ApplyCore(isGlass, isDark);
            PersistLegacyKey(isGlass, isDark);
        }

        /// <summary>兼容旧调用（传入旧版 Theme 值），内部映射到新的 风格 + 深浅 两维</summary>
        public static void ApplyTheme(string theme)
        {
            bool isGlass = theme == "Glass";
            bool isDark = theme == "Dark" || ((theme == "System" || isGlass) && IsSystemDark());
            var sig = Signature(isGlass, isDark);
            if (sig == _lastSig) return;
            _lastSig = sig;
            ApplyCore(isGlass, isDark);
            PersistLegacyKey(isGlass, isDark);
        }

        /// <summary>按「风格 × 深浅」直接切换（供设置页连续调参用，不写设置键）</summary>
        public static void ApplyPreview(bool isGlass, bool isDark) => ApplyCore(isGlass, isDark);

        private static void PersistLegacyKey(bool isGlass, bool isDark)
        {
            try
            {
                var s = new SettingsRepository();
                s.SetValue(Keys.Style, isGlass ? "Glass" : "Normal");
                // 注意：必须回写原始 Tone（可能是 System）。
                // 之前这里写成解析后的 Light/Dark，会把「跟随系统」当场吃掉——
                // 用户点跟随系统 → 落盘变成 Light → 看起来"点了没反应"。
                s.SetValue(Keys.Tone, Tone);
                // 保留旧键，便于旧逻辑 / 旧版本回读
                s.SetValue(SettingsKeys.Theme, isGlass ? "Glass" : (isDark ? "Dark" : "Light"));
            }
            catch { }
            ThemeChanged?.Invoke(isGlass ? "Glass" : (IsDarkMode() ? "Dark" : "Light"));
        }

        private static void ApplyCore(bool isGlass, bool isDark)
        {
            var app = Application.Current;
            if (app == null) return;

            // WPF-UI (lepoco) Fluent 主题：让默认控件/文字颜色跟随深浅色（修复夜间模式黑字）
            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                    isDark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
            }
            catch { /* 主题管理器不可用时退回自绘换色 */ }

            var dicts = app.Resources.MergedDictionaries;
            foreach (var dict in dicts)
            {
                if (dict.Source == null || !dict.Source.OriginalString.Contains("Styles.xaml")) continue;
                ApplyPalette(dict, isDark);
                // 普通主题：卡片背景与 CardBrush 一致；毛玻璃会覆盖成渐变（玻璃拟态）
                dict["CardBackgroundBrush"] = dict["CardBrush"];
                if (isGlass) ApplyGlass(dict, isDark);
                else
                {
                    dict["WindowBackgroundBrush"] = new SolidColorBrush(isDark
                        ? ColorFromString("#1C1C1E") : ColorFromString("#F2F2F7"));
                }
                break;
            }
        }

        /// <summary>基础色板（普通 / 毛玻璃共用的文字、强调色）</summary>
        private static void ApplyPalette(ResourceDictionary dict, bool isDark)
        {
            if (isDark)
            {
                dict["BackgroundColor"] = ColorFromString("#1C1C1E");
                dict["CardColor"] = ColorFromString("#2C2C2E");
                dict["TextColor"] = ColorFromString("#F2F2F7");
                dict["SecondaryTextColor"] = ColorFromString("#AEAEB2");
                dict["BorderColor"] = ColorFromString("#3A3A3C");
                dict["NavHoverColor"] = ColorFromString("#3A3A3C");
                dict["NavSelectedColor"] = ColorFromString("#0A84FF");
                dict["NavSelectedHoverColor"] = ColorFromString("#0070E0");

                dict["BackgroundBrush"] = new SolidColorBrush(ColorFromString("#1C1C1E"));
                dict["CardBrush"] = new SolidColorBrush(ColorFromString("#2C2C2E"));
                dict["WindowBackgroundBrush"] = new SolidColorBrush(ColorFromString("#1C1C1E"));
                dict["TextBrush"] = new SolidColorBrush(ColorFromString("#F2F2F7"));
                dict["SecondaryTextBrush"] = new SolidColorBrush(ColorFromString("#AEAEB2"));
                dict["BorderBrush"] = new SolidColorBrush(ColorFromString("#3A3A3C"));
                dict["NavHoverBrush"] = new SolidColorBrush(ColorFromString("#3A3A3C"));
                dict["NavSelectedBrush"] = new SolidColorBrush(ColorFromString("#0A84FF"));
                dict["NavSelectedHoverBrush"] = new SolidColorBrush(ColorFromString("#0070E0"));

                dict["PrimaryBrush"] = new SolidColorBrush(ColorFromString("#0A84FF"));
                dict["PrimaryColor"] = ColorFromString("#0A84FF");

                dict["AccentRed"] = ColorFromString("#FF453A");
                dict["AccentGreen"] = ColorFromString("#30D158");
                dict["AccentBlue"] = ColorFromString("#0A84FF");
                dict["AccentPink"] = ColorFromString("#FF375F");
                dict["AccentGray"] = ColorFromString("#8E8E93");
                dict["AccentYellow"] = ColorFromString("#FFD60A");
                dict["AccentPurple"] = ColorFromString("#BF5AF2");
                dict["AccentTeal"] = ColorFromString("#64D2FF");

                dict["GoalRedBrush"] = new SolidColorBrush(ColorFromString("#FF453A"));
                dict["GoalGreenBrush"] = new SolidColorBrush(ColorFromString("#30D158"));
                dict["GoalBlueBrush"] = new SolidColorBrush(ColorFromString("#0A84FF"));
                dict["GoalPinkBrush"] = new SolidColorBrush(ColorFromString("#FF375F"));
                dict["GoalGrayBrush"] = new SolidColorBrush(ColorFromString("#636366"));
                dict["GoalYellowBrush"] = new SolidColorBrush(ColorFromString("#FFD60A"));

                dict["AccentRedBrush"] = new SolidColorBrush(ColorFromString("#FF453A"));
                dict["AccentGreenBrush"] = new SolidColorBrush(ColorFromString("#30D158"));
                dict["AccentBlueBrush"] = new SolidColorBrush(ColorFromString("#0A84FF"));
                dict["AccentPinkBrush"] = new SolidColorBrush(ColorFromString("#FF375F"));
                dict["AccentGrayBrush"] = new SolidColorBrush(ColorFromString("#636366"));
                dict["AccentYellowBrush"] = new SolidColorBrush(ColorFromString("#FFD60A"));
                dict["AccentPurpleBrush"] = new SolidColorBrush(ColorFromString("#BF5AF2"));
                dict["AccentTealBrush"] = new SolidColorBrush(ColorFromString("#64D2FF"));
            }
            else
            {
                dict["BackgroundColor"] = ColorFromString("#F2F2F7");
                dict["CardColor"] = ColorFromString("#FFFFFF");
                dict["TextColor"] = ColorFromString("#1C1C1E");
                dict["SecondaryTextColor"] = ColorFromString("#8E8E93");
                dict["BorderColor"] = ColorFromString("#E5E5EA");
                dict["NavHoverColor"] = ColorFromString("#F0F0F5");
                dict["NavSelectedColor"] = ColorFromString("#007AFF");
                dict["NavSelectedHoverColor"] = ColorFromString("#0066DD");

                dict["BackgroundBrush"] = new SolidColorBrush(ColorFromString("#F2F2F7"));
                dict["CardBrush"] = new SolidColorBrush(ColorFromString("#FFFFFF"));
                dict["WindowBackgroundBrush"] = new SolidColorBrush(ColorFromString("#F2F2F7"));
                dict["TextBrush"] = new SolidColorBrush(ColorFromString("#1C1C1E"));
                dict["SecondaryTextBrush"] = new SolidColorBrush(ColorFromString("#8E8E93"));
                dict["BorderBrush"] = new SolidColorBrush(ColorFromString("#E5E5EA"));
                dict["NavHoverBrush"] = new SolidColorBrush(ColorFromString("#F0F0F5"));
                dict["NavSelectedBrush"] = new SolidColorBrush(ColorFromString("#007AFF"));
                dict["NavSelectedHoverBrush"] = new SolidColorBrush(ColorFromString("#0066DD"));

                dict["PrimaryBrush"] = new SolidColorBrush(ColorFromString("#007AFF"));
                dict["PrimaryColor"] = ColorFromString("#007AFF");

                dict["AccentRed"] = ColorFromString("#FF3B30");
                dict["AccentGreen"] = ColorFromString("#34C759");
                dict["AccentBlue"] = ColorFromString("#007AFF");
                dict["AccentPink"] = ColorFromString("#FF2D55");
                dict["AccentGray"] = ColorFromString("#8E8E93");
                dict["AccentYellow"] = ColorFromString("#FFCC00");
                dict["AccentPurple"] = ColorFromString("#AF52DE");
                dict["AccentTeal"] = ColorFromString("#5AC8FA");

                dict["GoalRedBrush"] = new SolidColorBrush(ColorFromString("#FF3B30"));
                dict["GoalGreenBrush"] = new SolidColorBrush(ColorFromString("#34C759"));
                dict["GoalBlueBrush"] = new SolidColorBrush(ColorFromString("#007AFF"));
                dict["GoalPinkBrush"] = new SolidColorBrush(ColorFromString("#FF2D55"));
                dict["GoalGrayBrush"] = new SolidColorBrush(ColorFromString("#8E8E93"));
                dict["GoalYellowBrush"] = new SolidColorBrush(ColorFromString("#FFCC00"));

                dict["AccentRedBrush"] = new SolidColorBrush(ColorFromString("#FF3B30"));
                dict["AccentGreenBrush"] = new SolidColorBrush(ColorFromString("#34C759"));
                dict["AccentBlueBrush"] = new SolidColorBrush(ColorFromString("#007AFF"));
                dict["AccentPinkBrush"] = new SolidColorBrush(ColorFromString("#FF2D55"));
                dict["AccentGrayBrush"] = new SolidColorBrush(ColorFromString("#8E8E93"));
                dict["AccentYellowBrush"] = new SolidColorBrush(ColorFromString("#FFCC00"));
                dict["AccentPurpleBrush"] = new SolidColorBrush(ColorFromString("#AF52DE"));
                dict["AccentTealBrush"] = new SolidColorBrush(ColorFromString("#5AC8FA"));
            }
        }

        /// <summary>
        /// 毛玻璃外壳：渐变配色 / 背景图片 / 纯透明，叠加可调窗口透明度。
        /// 卡片改为半透明，浮在窗口底之上形成磨砂质感。
        /// </summary>
        private static void ApplyGlass(ResourceDictionary dict, bool isDark)
        {
            double op = GlassOpacity / 100.0;
            Brush shell;

            switch (GlassMode)
            {
                case "Image":
                    shell = BuildImageShell(GlassImagePath, op, isDark);
                    break;
                case "Transparent":
                    shell = new SolidColorBrush(isDark
                        ? Color.FromArgb((byte)(op * 255), 0x14, 0x14, 0x16)
                        : Color.FromArgb((byte)(op * 255), 0xF2, 0xF2, 0xF7));
                    break;
                default:
                    shell = BuildGradientShell(GlassGradientIndex, op, isDark);
                    break;
            }

            dict["WindowBackgroundBrush"] = shell;
            dict["WindowBackgroundColor"] = isDark ? ColorFromString("#1C1C1E") : ColorFromString("#F2F2F7");

            // 毛玻璃卡片：上亮下沉的纵向渐变（玻璃拟态的关键——纯半透明没有"体积感"）
            dict["CardBackgroundBrush"] = GlassCardBrush(isDark);

            // 毛玻璃下的卡片基色（保持 SolidColorBrush，供 ThemeService.Solid / 旧强转兜底）
            dict["CardBrush"] = new SolidColorBrush(isDark
                ? Color.FromArgb(0xA6, 0x22, 0x2A, 0x3D)
                : Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));
            dict["CardColor"] = isDark ? ColorFromString("#222A3D") : ColorFromString("#FFFFFF");

            // 描边与导航悬停也带一点透明，贴合磨砂质感
            dict["BorderBrush"] = new SolidColorBrush(isDark
                ? Color.FromArgb(0x8C, 0x6C, 0x75, 0x95)
                : Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
            dict["NavHoverBrush"] = new SolidColorBrush(isDark
                ? Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        }

        /// <summary>玻璃拟态卡片画笔：上缘更亮、下缘更透，模拟受光面</summary>
        private static Brush GlassCardBrush(bool isDark)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    isDark
                        ? new GradientStop(Color.FromArgb(0xD0, 0x2E, 0x36, 0x4C), 0)
                        : new GradientStop(Color.FromArgb(0xDB, 0xFF, 0xFF, 0xFF), 0),
                    isDark
                        ? new GradientStop(Color.FromArgb(0x92, 0x20, 0x28, 0x3B), 1)
                        : new GradientStop(Color.FromArgb(0x8F, 0xFF, 0xFF, 0xFF), 1),
                }
            };
            brush.Freeze();
            return brush;
        }

        private static Brush BuildGradientShell(int index, double opacity, bool isDark)
        {
            var palette = GlassGradients[Math.Max(0, Math.Min(GlassGradients.Length - 1, index))];
            var colors = isDark ? palette.Dark : palette.Light;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(ColorFromString(colors[0]), 0),
                    new GradientStop(ColorFromString(colors[1]), 0.45),
                    new GradientStop(ColorFromString(colors[2]), 1),
                },
                Opacity = opacity,
            };
            brush.Freeze();
            return brush;
        }

        private static Brush BuildImageShell(string path, double opacity, bool isDark)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return BuildGradientShell(GlassGradientIndex, opacity, isDark);
            try
            {
                var img = new ImageBrush(new ImageSourceConverter()
                    .ConvertFromString(path) as ImageSource)
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = opacity,
                };
                img.Freeze();
                return img;
            }
            catch
            {
                return BuildGradientShell(GlassGradientIndex, opacity, isDark);
            }
        }

        public static string GetCurrentTheme()
        {
            var settings = new SettingsRepository();
            return settings.GetValue(SettingsKeys.Theme, "Light");
        }

        public static bool IsDarkMode()
        {
            var tone = Tone;
            return tone == "Dark" || (tone == "System" && IsSystemDark());
        }

        private static bool IsSystemDark()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    return value is int v && v == 0;
                }
            }
            catch { }
            return false;
        }

        private static Color ColorFromString(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        /// <summary>
        /// 取主题画笔并强制返回 SolidColorBrush。
        /// 代码里大量 (SolidColorBrush)FindResource(...) 强转，一旦主题把某个键换成渐变/其它画笔
        /// 就会 InvalidCastException（毛玻璃首版就是这样崩的）。这里统一兜底：非纯色画笔取首段颜色。
        /// </summary>
        public static SolidColorBrush Solid(string key)
        {
            Brush b = null;
            var app = Application.Current;
            if (app != null) b = app.TryFindResource(key) as Brush;
            if (b is SolidColorBrush s) return s;
            var c = b is GradientBrush g && g.GradientStops.Count > 0 ? g.GradientStops[0].Color : Colors.Transparent;
            var solid = new SolidColorBrush(c);
            solid.Freeze();
            return solid;
        }
    }
}
