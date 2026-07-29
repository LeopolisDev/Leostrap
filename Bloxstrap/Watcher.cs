using System.Runtime.InteropServices;

using Leostrap.AppData;
using Leostrap.Integrations;
using Leostrap.Models;

namespace Leostrap
{
    public class Watcher : IDisposable
    {
        private const int VK_SPACE = 0x20;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        private readonly InterProcessLock _lock = new("Watcher");

        private readonly WatcherData? _watcherData;

        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly DiscordRichPresence? RichPresence;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";

            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists");
                return;
            }

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

            if (String.IsNullOrEmpty(watcherDataArg))
            {
#if DEBUG
                string path = new RobloxPlayerData().ExecutablePath;
                if (!File.Exists(path))
                    throw new ApplicationException("Roblox player is not been installed");

                using var gameClientProcess = Process.Start(path);

                _watcherData = new()
                {
                    ProcessId = gameClientProcess.Id,
                    ProcessName = new RobloxPlayerData().ProcessName
                };
#else
                throw new Exception("Watcher data not specified");
#endif
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            if (String.IsNullOrWhiteSpace(_watcherData.ProcessName))
            {
                try
                {
                    _watcherData.ProcessName = Process.GetProcessById(_watcherData.ProcessId).ProcessName;
                }
                catch
                {
                    _watcherData.ProcessName = new RobloxPlayerData().ProcessName;
                }
            }

            if (App.Settings.Prop.EnableActivityTracking)
            {
                ActivityWatcher = new(_watcherData.LogFile);

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        CloseProcess(_watcherData.ProcessId, true);
                    };
                }

                if (App.Settings.Prop.UseDiscordRichPresence)
                    RichPresence = new(ActivityWatcher);
            }

            if (_watcherData.ShowNotifyIcon)
                _notifyIcon = new(this);
        }

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

        private bool IsRobloxForeground()
        {
            if (_watcherData is null)
                return false;

            nint hWnd = GetForegroundWindow();

            if (hWnd == 0)
                return false;

            GetWindowThreadProcessId(hWnd, out uint processId);
            return processId == _watcherData.ProcessId;
        }

        private async Task TriggerJumpyAsync()
        {
            const string LOG_IDENT = "Watcher::TriggerJumpyAsync";
            const int offset = 100;
            const int durationMs = 100;

            if (_watcherData is null)
                return;

            nint hWnd = GetForegroundWindow();

            if (hWnd == 0 || !GetWindowRect(hWnd, out RECT rect))
                return;

            int x = rect.Left;
            int y = rect.Top;

            if (GetCursorPos(out POINT cursor) && !SetCursorPos(cursor.X, cursor.Y - offset))
                App.Logger.WriteLine(LOG_IDENT, "Failed to move cursor");

            if (!SetWindowPos(hWnd, 0, x, y - offset, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOSENDCHANGING))
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to move Roblox window up");
                return;
            }

            await Task.Delay(durationMs);

            if (Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId) && GetForegroundWindow() == hWnd)
            {
                if (!SetWindowPos(hWnd, 0, x, y, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOSENDCHANGING))
                    App.Logger.WriteLine(LOG_IDENT, "Failed to move Roblox window back down");
            }
        }

        private async Task RunJumpyAsync()
        {
            const string LOG_IDENT = "Watcher::RunJumpyAsync";

            App.Logger.WriteLine(LOG_IDENT, "Starting Jumpy helper");

            bool wasSpaceDown = false;

            while (_watcherData is not null && Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId))
            {
                bool isSpaceDown = (GetAsyncKeyState(VK_SPACE) & 0x8000) != 0;

                if (isSpaceDown && !wasSpaceDown && IsRobloxForeground())
                    await TriggerJumpyAsync();

                wasSpaceDown = isSpaceDown;

                await Task.Delay(16);
            }
        }

        private static bool KillProcessesByName(string processName)
        {
            bool killedAny = false;

            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    killedAny = true;
                    process.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("Watcher::KillProcessesByName", $"Failed to close process {process.Id} ({processName})");
                    App.Logger.WriteException("Watcher::KillProcessesByName", ex);
                }
            }

            return killedAny;
        }

        private static void KillRobloxCrashHandler()
        {
            const string LOG_IDENT = "Watcher::KillRobloxCrashHandler";

            foreach (Process process in Process.GetProcessesByName("RobloxCrashHandler"))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close process {process.Id}");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }

        private async Task KeepRobloxClosedAsync()
        {
            const string LOG_IDENT = "Watcher::KeepRobloxClosedAsync";
            const int timeoutSeconds = 10;

            if (!App.Settings.Prop.CloseRobloxCompletely || _watcherData is null)
                return;

            App.Logger.WriteLine(LOG_IDENT, "Watching for Roblox respawns after close");

            int quietTicks = 0;
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                bool killedAny = KillProcessesByName(_watcherData.ProcessName);

                if (App.Settings.Prop.CloseCrashHandler)
                    killedAny |= KillProcessesByName("RobloxCrashHandler");

                if (killedAny)
                    quietTicks = 0;
                else
                    quietTicks += 1;

                if (quietTicks >= 3)
                    break;

                await Task.Delay(1000);
            }
        }

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            Task? jumpyTask = App.Settings.Prop.EnableJumpy ? RunJumpyAsync() : null;

            ActivityWatcher?.Start();

            while (Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId))
            {
                if (App.Settings.Prop.CloseCrashHandler)
                    KillRobloxCrashHandler();

                await Task.Delay(1000);
            }

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.Settings.Prop.CloseCrashHandler)
                KillRobloxCrashHandler();

            await KeepRobloxClosedAsync();

            if (jumpyTask is not null)
                await jumpyTask;

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            _notifyIcon?.Dispose();
            RichPresence?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
