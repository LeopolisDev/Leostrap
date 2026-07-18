using Leostrap.Enums;

namespace Leostrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {
        public RobloxProcessPriority[] RobloxProcessPriorities => Enum.GetValues<RobloxProcessPriority>();

        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set => App.Settings.Prop.ConfirmLaunches = value;
        }

        public bool AllowMultiInstanceLaunching
        {
            get => App.Settings.Prop.AllowMultiInstanceLaunching;
            set => App.Settings.Prop.AllowMultiInstanceLaunching = value;
        }

        public RobloxProcessPriority SelectedRobloxProcessPriority
        {
            get => App.Settings.Prop.RobloxProcessPriority;
            set => App.Settings.Prop.RobloxProcessPriority = value;
        }

        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set => App.Settings.Prop.BackgroundUpdatesEnabled = value;
        }

        public bool IsRobloxInstallationMissing => !App.IsPlayerInstalled && !App.IsStudioInstalled;

        public bool ForceRobloxReinstallation
        {
            get => App.State.Prop.ForceReinstall || IsRobloxInstallationMissing;
            set => App.State.Prop.ForceReinstall = value;
        }
    }
}
