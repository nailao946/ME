using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace ME.Services
{
    /// <summary>
    /// DWM 亚克力模糊（ACCENT_ENABLE_ACRYLICBLURBEHIND）：
    /// 毛玻璃主题下让窗口真正把桌面内容模糊掉，而不是只靠半透明叠色硬"演"。
    /// 失败（旧系统 / 驱动不支持）时静默退回纯半透明效果，不影响使用。
    /// </summary>
    public static class WindowAcrylic
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor; // AABBGGRR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        /// <summary>
        /// 开/关窗口背后的亚克力模糊。tint 叠在模糊之上，alpha 越大越"雾"。
        /// </summary>
        public static void Apply(Window window, bool on, System.Windows.Media.Color tint)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                var policy = new AccentPolicy
                {
                    AccentState = on ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_DISABLED,
                    GradientColor = on
                        ? ((uint)tint.A << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R
                        : 0u,
                };
                var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(policy));
                try
                {
                    Marshal.StructureToPtr(policy, ptr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = ptr,
                        SizeOfData = Marshal.SizeOf(policy),
                    };
                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch
            {
                // 不支持就保持普通半透明观感
            }
        }
    }
}
