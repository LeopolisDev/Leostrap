using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Collections.ObjectModel;

using Microsoft.Win32;

using Wpf.Ui.Mvvm.Contracts;

using Leostrap.UI.Elements.Dialogs;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;

namespace Leostrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagEditorPage.xaml
    /// </summary>
    public partial class FastFlagEditorPage
    {
        // believe me when i say there is absolutely zero point to using mvvm for this
        // using a datagrid is a codebehind thing only and thats it theres literally no way around it

        private readonly ObservableCollection<FastFlag> _fastFlagList = new();
        private readonly List<string> _validPrefixes = new()
        {
            "FFlag", "DFFlag", "SFFlag", "FInt", "DFInt", "FString", "DFString", "FLog", "DFLog"
        };

        // values must match the entire string to avoid cases where half the string
        // matches but the filter would still be invalid
        private readonly Regex _boolFilterPattern = new("^(?:true|false)(;[\\d]{1,})+$", RegexOptions.IgnoreCase);
        private readonly Regex _intFilterPattern = new("^([\\d]{1,})?(;[\\d]{1,})+$", RegexOptions.IgnoreCase);
        private readonly Regex _stringFilterPattern = new("^[^;]*(;[\\d]{1,})+$", RegexOptions.IgnoreCase);

        private const string RobloxCookiesFileName = @"Roblox\LocalStorage\RobloxCookies.dat";

        private bool _showPresets = false;
        private string _searchFilter = "";
        private bool _apiResetInProgress = false;

        public FastFlagEditorPage()
        {
            InitializeComponent();
        }

        private static void CloseRobloxProcesses()
        {
            const string LOG_IDENT = "FastFlagEditorPage::CloseRobloxProcesses";

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

        private static void ClearRobloxCookies()
        {
            const string LOG_IDENT = "FastFlagEditorPage::ClearRobloxCookies";

            string cookiePath = Path.Combine(Paths.LocalAppData, RobloxCookiesFileName);

            if (!File.Exists(cookiePath))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Cookie file not found: {cookiePath}");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Deleting cookie file: {cookiePath}");
            File.Delete(cookiePath);
        }

        private static bool TrySplitCommandLine(string commandLine, out string fileName, out string arguments)
        {
            fileName = "";
            arguments = "";

            commandLine = commandLine.Trim();
            if (String.IsNullOrWhiteSpace(commandLine))
                return false;

            if (commandLine.StartsWith('\"'))
            {
                int endQuote = commandLine.IndexOf('\"', 1);
                if (endQuote == -1)
                    return false;

                fileName = commandLine[1..endQuote];
                arguments = commandLine[(endQuote + 1)..].Trim();
                return !String.IsNullOrWhiteSpace(fileName);
            }

            int firstSpace = commandLine.IndexOf(' ');
            if (firstSpace == -1)
            {
                fileName = commandLine;
                return true;
            }

            fileName = commandLine[..firstSpace];
            arguments = commandLine[(firstSpace + 1)..].Trim();
            return !String.IsNullOrWhiteSpace(fileName);
        }

        private static bool TryRunRegistryUninstall(RegistryKey rootKey, string uninstallKeyPath)
        {
            const string LOG_IDENT = "FastFlagEditorPage::TryRunRegistryUninstall";

            using var uninstallKey = rootKey.OpenSubKey(uninstallKeyPath);

            if (uninstallKey is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Uninstall key not found: {rootKey.Name}\\{uninstallKeyPath}");
                return false;
            }

            string? commandLine =
                uninstallKey.GetValue("QuietUninstallString") as string
                ?? uninstallKey.GetValue("UninstallString") as string
                ?? uninstallKey.GetValue("") as string;

            if (String.IsNullOrWhiteSpace(commandLine))
            {
                App.Logger.WriteLine(LOG_IDENT, $"No uninstall command found in: {uninstallKeyPath}");
                return false;
            }

            if (!TrySplitCommandLine(commandLine, out string fileName, out string arguments))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to parse uninstall command: {commandLine}");
                return false;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Starting uninstall command: {fileName} {arguments}".Trim());

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(fileName) ?? Paths.UserProfile
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit();
            return true;
        }

        private static bool TryUninstallRobloxFromKnownKeys()
        {
            var candidateKeys = new[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Roblox Player",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Roblox Player",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio-admin",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio-admin"
            };

            foreach (var rootKey in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var keyPath in candidateKeys)
                {
                    if (TryRunRegistryUninstall(rootKey, keyPath))
                        return true;
                }
            }

            return false;
        }

        private static bool TryUninstallRobloxFromDisplayName()
        {
            const string LOG_IDENT = "FastFlagEditorPage::TryUninstallRobloxFromDisplayName";

            var uninstallRoots = new[]
            {
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.CurrentUser, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var (rootKey, rootPath) in uninstallRoots)
            {
                using var uninstallRoot = rootKey.OpenSubKey(rootPath);

                if (uninstallRoot is null)
                    continue;

                foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                {
                    using var subKey = uninstallRoot.OpenSubKey(subKeyName);

                    if (subKey is null)
                        continue;

                    string? displayName = subKey.GetValue("DisplayName") as string;
                    string? publisher = subKey.GetValue("Publisher") as string;

                    bool looksLikeRoblox =
                        subKeyName.Contains("roblox", StringComparison.OrdinalIgnoreCase)
                        || (!String.IsNullOrEmpty(displayName) && displayName.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                        || (!String.IsNullOrEmpty(publisher) && publisher.Contains("Roblox", StringComparison.OrdinalIgnoreCase));

                    if (!looksLikeRoblox)
                        continue;

                    string keyPath = $"{rootPath}\\{subKeyName}";

                    if (TryRunRegistryUninstall(rootKey, keyPath))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Used fallback uninstall key: {rootKey.Name}\\{keyPath}");
                        return true;
                    }
                }
            }

            return false;
        }

        private static void UninstallRoblox()
        {
            const string LOG_IDENT = "FastFlagEditorPage::UninstallRoblox";

            bool uninstalled = TryUninstallRobloxFromKnownKeys() || TryUninstallRobloxFromDisplayName();

            if (!uninstalled)
                throw new InvalidOperationException("Could not find a Roblox uninstall entry.");

            App.Logger.WriteLine(LOG_IDENT, "Roblox uninstall completed");
        }

        private static string? FindByeBanAsyncExecutable()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "ByeBanAsync.exe"),
                Path.Combine(AppContext.BaseDirectory, "ByeBanAsync", "ByeBanAsync.exe"),
                Path.Combine(repoRoot, "ByeBanAsync", "target", "release", "ByeBanAsync.exe"),
                Path.Combine(repoRoot, "ByeBanAsync", "target", "debug", "ByeBanAsync.exe")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static async Task RunByeBanAsync()
        {
            const string LOG_IDENT = "FastFlagEditorPage::RunByeBanAsync";

            string? exePath = FindByeBanAsyncExecutable();

            if (exePath is null)
                throw new FileNotFoundException("Could not find ByeBanAsync.exe. Build the ByeBanAsync project first.");

            App.Logger.WriteLine(LOG_IDENT, $"Launching ByeBanAsync from {exePath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);

            if (process is null)
                throw new InvalidOperationException("Failed to start ByeBanAsync.");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ByeBanAsync exited with code {process.ExitCode}.");

            App.Logger.WriteLine(LOG_IDENT, "ByeBanAsync finished successfully");
        }

        private void ReloadList()
        {
            var selectedEntry = DataGrid.SelectedItem as FastFlag;

            _fastFlagList.Clear();

            var presetFlags = FastFlagManager.PresetFlags.Values;

            foreach (var pair in App.FastFlags.Prop.OrderBy(x => x.Key))
            {
                if (!_showPresets && presetFlags.Contains(pair.Key))
                    continue;

                if (!pair.Key.ToLower().Contains(_searchFilter.ToLower()))
                    continue;

                var entry = new FastFlag
                {
                    // Enabled = true,
                    Name = pair.Key,
                    Value = pair.Value.ToString()!
                };

                /* if (entry.Name.StartsWith("Disable"))
                {
                    entry.Enabled = false;
                    entry.Name = entry.Name[7..];
                } */

                _fastFlagList.Add(entry);
            }

            if (DataGrid.ItemsSource is null)
                DataGrid.ItemsSource = _fastFlagList;

            if (selectedEntry is null)
                return;

            var newSelectedEntry = _fastFlagList.Where(x => x.Name == selectedEntry.Name).FirstOrDefault();

            if (newSelectedEntry is null)
                return;
            
            DataGrid.SelectedItem = newSelectedEntry;
            DataGrid.ScrollIntoView(newSelectedEntry);
        }

        private void ClearSearch(bool refresh = true)
        {
            SearchTextBox.Text = "";
            _searchFilter = "";

            if (refresh)
                ReloadList();
        }

        private void ShowAddDialog()
        {
            var dialog = new AddFastFlagDialog();
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            if (dialog.Tabs.SelectedIndex == 0)
                AddSingle(dialog.FlagNameTextBox.Text.Trim(), dialog.FlagValueTextBox.Text);
            else if (dialog.Tabs.SelectedIndex == 1)
                ImportJSON(dialog.JsonTextBox.Text);
        }

        private void AddSingle(string name, string value)
        {
            FastFlag? entry;

            if (App.FastFlags.GetValue(name) is null)
            {
                if (!ValidateFlagEntry(name, value))
                {
                    ShowAddDialog();
                    return;
                }

                entry = new FastFlag
                {
                    // Enabled = true,
                    Name = name,
                    Value = value
                };

                if (!name.Contains(_searchFilter))
                    ClearSearch();

                _fastFlagList.Add(entry);

                App.FastFlags.SetValue(entry.Name, entry.Value);
            }
            else
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);

                bool refresh = false;

                if (!_showPresets && FastFlagManager.PresetFlags.Values.Contains(name))
                {
                    TogglePresetsButton.IsChecked = true;
                    _showPresets = true;
                    refresh = true;
                }

                if (!name.Contains(_searchFilter))
                {
                    ClearSearch(false);
                    refresh = true;
                }

                if (refresh)
                    ReloadList();

                entry = _fastFlagList.Where(x => x.Name == name).FirstOrDefault();
            }

            DataGrid.SelectedItem = entry;
            DataGrid.ScrollIntoView(entry);
        }

        private void ImportJSON(string json)
        {
            Dictionary<string, object>? list = null;

            json = json.Trim();

            // autocorrect where possible
            if (!json.StartsWith('{'))
                json = '{' + json;

            if (!json.EndsWith('}'))
            {
                int lastIndex = json.LastIndexOf('}');

                if (lastIndex == -1)
                    json += '}';
                else
                    json = json.Substring(0, lastIndex+1);
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                list = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);

                if (list is null)
                    throw new Exception("JSON deserialization returned null");
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(                    
                    String.Format(Strings.Menu_FastFlagEditor_InvalidJSON, ex.Message),
                    MessageBoxImage.Error
                );

                ShowAddDialog();

                return;
            }

            if (list.Count > 16)
            {
                var result = Frontend.ShowMessageBox(
                    Strings.Menu_FastFlagEditor_LargeConfig, 
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNo
                );

                if (result != MessageBoxResult.Yes)
                    return;
            }

            var conflictingFlags = App.FastFlags.Prop.Where(x => list.ContainsKey(x.Key)).Select(x => x.Key);
            bool overwriteConflicting = false;

            if (conflictingFlags.Any())
            {
                int count = conflictingFlags.Count();

                string message = String.Format(
                    Strings.Menu_FastFlagEditor_ConflictingImport,
                    count,
                    String.Join(", ", conflictingFlags.Take(25))
                );

                if (count > 25)
                    message += "...";

                var result = Frontend.ShowMessageBox(message, MessageBoxImage.Question, MessageBoxButton.YesNo);

                overwriteConflicting = result == MessageBoxResult.Yes;
            }

            foreach (var pair in list)
            {
                if (App.FastFlags.Prop.ContainsKey(pair.Key) && !overwriteConflicting)
                    continue;

                if (pair.Value is null)
                    continue;

                var val = pair.Value.ToString();

                if (val is null)
                    continue;

                if (!ValidateFlagEntry(pair.Key, val))
                    continue;

                App.FastFlags.SetValue(pair.Key, pair.Value);
            }

            ClearSearch();
        }

        private bool ValidateFlagEntry(string name, string value)
        {
            string lowerValue = value.ToLowerInvariant();
            string errorMessage = "";

            if (!_validPrefixes.Any(name.StartsWith))
                errorMessage = Strings.Menu_FastFlagEditor_InvalidPrefix;
            else if (!name.All(x => char.IsLetterOrDigit(x) || x == '_'))
                errorMessage = Strings.Menu_FastFlagEditor_InvalidCharacter;
            
            if (name.EndsWith("_PlaceFilter") || name.EndsWith("_DataCenterFilter"))
                errorMessage = !ValidateFilter(name, value) ? Strings.Menu_FastFlagEditor_InvalidPlaceFilter : ""; 
            else if ((name.StartsWith("FInt") || name.StartsWith("DFInt")) && !Int32.TryParse(value, out _))
                errorMessage = Strings.Menu_FastFlagEditor_InvalidNumberValue;
            else if ((name.StartsWith("FFlag") || name.StartsWith("DFFlag")) && lowerValue != "true" && lowerValue != "false")
                errorMessage = Strings.Menu_FastFlagEditor_InvalidBoolValue;
            
            if (!String.IsNullOrEmpty(errorMessage))
            { 
                Frontend.ShowMessageBox(String.Format(errorMessage, name), MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private bool ValidateFilter(string name, string value)
        {
            if(name.StartsWith("FFlag") || name.StartsWith("DFFlag"))
                return _boolFilterPattern.IsMatch(value);
            if (name.StartsWith("FInt") || name.StartsWith("DFInt"))
                return _intFilterPattern.IsMatch(value);
            if (name.StartsWith("FString") || name.StartsWith("DFString") || name.StartsWith("FLog") || name.StartsWith("DFLog"))
                return _stringFilterPattern.IsMatch(value);
            
            return false;
        }

        // refresh list on page load to synchronize with preset page
        private void Page_Loaded(object sender, RoutedEventArgs e) => ReloadList();

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.DataContext is not FastFlag entry)
                return;

            if (e.EditingElement is not TextBox textbox)
                return;

            switch (e.Column.Header)
            {
                case "Name":
                    string oldName = entry.Name;
                    string newName = textbox.Text;

                    if (newName == oldName)
                        return;

                    if (App.FastFlags.GetValue(newName) is not null)
                    {
                        Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);
                        e.Cancel = true;
                        textbox.Text = oldName;
                        return;
                    }

                    App.FastFlags.SetValue(oldName, null);
                    App.FastFlags.SetValue(newName, entry.Value);

                    if (!newName.Contains(_searchFilter))
                        ClearSearch();

                    entry.Name = newName;

                    break;

                case "Value":
                    string oldValue = entry.Value;
                    string newValue = textbox.Text;

                    if (!ValidateFlagEntry(entry.Name, newValue))
                    {
                        e.Cancel = true;
                        textbox.Text = oldValue;
                        return;
                    }

                    App.FastFlags.SetValue(entry.Name, newValue);

                    break;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
                window.Navigate(typeof(FastFlagsPage));
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => ShowAddDialog();

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var tempList = new List<FastFlag>();

            foreach (FastFlag entry in DataGrid.SelectedItems)
                tempList.Add(entry);

            foreach (FastFlag entry in tempList)
            {
                _fastFlagList.Remove(entry);
                App.FastFlags.SetValue(entry.Name, null);
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
                return;

            _showPresets = button.IsChecked ?? false;
            ReloadList();
        }

        private void ExportJSONButton_Click(object sender, RoutedEventArgs e)
        {
            string json = JsonSerializer.Serialize(App.FastFlags.Prop, new JsonSerializerOptions { WriteIndented = true });
            Clipboard.SetDataObject(json);
            Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_JsonCopiedToClipboard, MessageBoxImage.Information);
        }

        private async void ApiResetButton_Click(object sender, RoutedEventArgs e)
        {
            const string LOG_IDENT = "FastFlagEditorPage::ApiResetButton_Click";

            if (_apiResetInProgress)
                return;

            var result = Frontend.ShowMessageBox(
                "This will close all Roblox instances, clear your Roblox cookies, and uninstall Roblox. Continue?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo,
                MessageBoxResult.No
            );

            if (result != MessageBoxResult.Yes)
                return;

            _apiResetInProgress = true;
            ApiResetButton.IsEnabled = false;

            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Beginning API reset flow");

                await Task.Run(() =>
                {
                    CloseRobloxProcesses();
                    ClearRobloxCookies();
                    UninstallRoblox();
                });

                await RunByeBanAsync();

                App.State.Prop.ForceReinstall = true;
                App.State.Save();

                Frontend.ShowMessageBox(
                    "ByeBanAsync has finished. Click OK and Roblox will be installed again.",
                    MessageBoxImage.Information
                );

                LaunchHandler.LaunchRoblox(LaunchMode.Player);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "API reset failed");
                App.Logger.WriteException(LOG_IDENT, ex);

                Frontend.ShowMessageBox(
                    $"Api Reset failed: {ex.Message}",
                    MessageBoxImage.Error
                );
            }
            finally
            {
                _apiResetInProgress = false;
                ApiResetButton.IsEnabled = true;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textbox)
                return;

            _searchFilter = textbox.Text;
            ReloadList();
        }
    }
}
