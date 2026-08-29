using System;

namespace ME.Services
{
    /// <summary>云同步状态球的状态</summary>
    public enum SyncBallState
    {
        /// <summary>未绑定 GitHub（灰色）</summary>
        NotConfigured,
        /// <summary>已绑定但本次启动还没同步过（灰色）</summary>
        Idle,
        /// <summary>同步进行中（呼吸绿）</summary>
        Running,
        /// <summary>最近一次同步成功（绿色）</summary>
        Success,
        /// <summary>最近一次同步失败（红色）</summary>
        Failed
    }

    /// <summary>
    /// 云同步状态中枢：左下角状态球与轻提示从这里取状态。
    /// GitHubSyncService 的同步/上传/下载入口统一在此登记结果，设置页与启动自动同步同样生效。
    /// </summary>
    public static class SyncStatusService
    {
        public static SyncBallState State { get; private set; } = SyncBallState.Idle;
        public static string Message { get; private set; } = "";

        /// <summary>本次结果是否要弹左下角轻提示（状态球触发的同步为 true，设置页内同步不弹、避免与页面内结果重复）</summary>
        public static bool ToastPending { get; private set; }

        public static event Action StateChanged;

        public static void SetRunning(string msg = "正在同步…")
        {
            State = SyncBallState.Running;
            Message = msg;
            ToastPending = false;
            Raise();
        }

        /// <summary>登记一次同步结果：✗ 或含「错误：/失败」判为失败（红），其余判为成功（绿）</summary>
        public static void Report(string result, bool toast)
        {
            Message = result ?? "";
            ToastPending = toast;
            State = Classify(Message);
            Raise();
        }

        /// <summary>登录状态变化后刷新：未登录显示灰球，登录后回到未同步态</summary>
        public static void RefreshLoginState()
        {
            if (State == SyncBallState.Running) return;
            var loggedIn = !string.IsNullOrWhiteSpace(GitHubSyncService.Load().EncryptedToken);
            if (!loggedIn && State != SyncBallState.NotConfigured)
            {
                State = SyncBallState.NotConfigured;
                Message = "";
                Raise();
            }
            else if (loggedIn && State == SyncBallState.NotConfigured)
            {
                State = SyncBallState.Idle;
                Raise();
            }
        }

        /// <summary>轻提示只弹一次：展示后由窗口调用清除标记</summary>
        public static void ConsumeToast() => ToastPending = false;

        private static SyncBallState Classify(string r)
        {
            if (string.IsNullOrWhiteSpace(r)) return SyncBallState.Idle;
            if (r.StartsWith("✗") || r.Contains("错误：") || r.Contains("失败")) return SyncBallState.Failed;
            return SyncBallState.Success;
        }

        private static void Raise()
        {
            var h = StateChanged;
            try { h?.Invoke(); } catch { }
        }
    }
}
