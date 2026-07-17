using System.Windows;
using System.Windows.Input;
using Leostrap.UI.Elements.About;
using CommunityToolkit.Mvvm.Input;

namespace Leostrap.UI.ViewModels.Settings
{
    public class MainWindowViewModel : NotifyPropertyChangedViewModel
    {
        public ICommand OpenAboutCommand => new RelayCommand(OpenAbout);
        
        public ICommand CloseAllRobloxInstancesCommand => new RelayCommand(CloseAllRobloxInstances);

        public ICommand SaveAndPlayCommand => new RelayCommand(SaveAndPlay);

        public ICommand SaveSettingsCommand => new RelayCommand(SaveSettings);
        
        public ICommand CloseWindowCommand => new RelayCommand(CloseWindow);

        public EventHandler? RequestSaveNoticeEvent;
        
        public EventHandler? RequestCloseWindowEvent;

        public bool TestModeEnabled
        {
            get => App.LaunchSettings.TestModeFlag.Active;
            set
            {
                if (value)
                {
                    var result = Frontend.ShowMessageBox(Strings.Menu_TestMode_Prompt, MessageBoxImage.Information, MessageBoxButton.YesNo);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                App.LaunchSettings.TestModeFlag.Active = value;
            }
        }

        private void OpenAbout() => new MainWindow().ShowDialog();

        private static void CloseAllRobloxInstances()
        {
            const string LOG_IDENT = "MainWindowViewModel::CloseAllRobloxInstances";

            var processNames = new[]
            {
                App.RobloxPlayerAppName,
                App.RobloxStudioAppName,
                "RobloxCrashHandler"
            };

            foreach (var process in processNames.SelectMany(Process.GetProcessesByName).DistinctBy(x => x.Id))
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    App.Logger.WriteLine(LOG_IDENT, $"Closing {process.ProcessName} ({process.Id})");
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close {process.ProcessName} ({process.Id})");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                finally
                {
                    process.Close();
                }
            }
        }

        private void CloseWindow() => RequestCloseWindowEvent?.Invoke(this, EventArgs.Empty);

        private void SaveAndPlay()
        {
            SaveSettings();
            LaunchHandler.LaunchRoblox(LaunchMode.Player);
        }

        private void SaveSettings()
        {
            const string LOG_IDENT = "MainWindowViewModel::SaveSettings";

            App.Settings.Save();
            App.State.Save();
            App.FastFlags.Save();

            foreach (var pair in App.PendingSettingTasks)
            {
                var task = pair.Value;

                if (task.Changed)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Executing pending task '{task}'");
                    task.Execute();
                }
            }

            App.PendingSettingTasks.Clear();

            RequestSaveNoticeEvent?.Invoke(this, EventArgs.Empty);
        }
    }
}
