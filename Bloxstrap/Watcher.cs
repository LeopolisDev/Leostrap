using Leostrap.AppData;
using Leostrap.Integrations;
using Leostrap.Models;

namespace Leostrap
{
    public class Watcher : IDisposable
    {
        private readonly InterProcessLock _lock = new("Watcher");

        private readonly WatcherData? _watcherData;
        
        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly DiscordRichPresence? RichPresence;

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

            _notifyIcon = new(this);
        }

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

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
