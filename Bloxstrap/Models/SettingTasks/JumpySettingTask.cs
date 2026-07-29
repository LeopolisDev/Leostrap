using Leostrap.Models.SettingTasks.Base;

namespace Leostrap.Models.SettingTasks
{
    public class JumpySettingTask : BoolBaseTask
    {
        public JumpySettingTask() : base("Jumpy")
        {
            OriginalState = App.Settings.Prop.EnableJumpy;
        }

        public override bool NewState
        {
            get => base.NewState;
            set
            {
                App.Settings.Prop.EnableJumpy = value;
                base.NewState = value;
            }
        }

        public override void Execute()
        {
            OriginalState = NewState;
        }
    }
}
