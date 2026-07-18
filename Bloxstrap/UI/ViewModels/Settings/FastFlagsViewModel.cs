using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using Leostrap.Enums.FlagPresets;

namespace Leostrap.UI.ViewModels.Settings
{
    public class FastFlagsViewModel : NotifyPropertyChangedViewModel
    {
        private Dictionary<string, object>? _preResetFlags;

        public event EventHandler? RequestPageReloadEvent;
        
        public event EventHandler? OpenFlagEditorEvent;

        private void OpenFastFlagEditor() => OpenFlagEditorEvent?.Invoke(this, EventArgs.Empty);

        public ICommand OpenFastFlagEditorCommand => new RelayCommand(OpenFastFlagEditor);

        public bool UseFastFlagManager
        {
            get => App.Settings.Prop.UseFastFlagManager;
            set => App.Settings.Prop.UseFastFlagManager = value;
        }

        public bool CloseCrashHandler
        {
            get => App.Settings.Prop.CloseCrashHandler;
            set => App.Settings.Prop.CloseCrashHandler = value;
        }

        public IReadOnlyDictionary<MSAAMode, string?> MSAALevels => FastFlagManager.MSAAModes;

        public MSAAMode SelectedMSAALevel
        {
            get => MSAALevels.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.MSAA")).Key;
            set => App.FastFlags.SetPreset("Rendering.MSAA", MSAALevels[value]);
        }

        public bool FixDisplayScaling
        {
            get => App.FastFlags.GetPreset("Rendering.DisableScaling") == "True";
            set => App.FastFlags.SetPreset("Rendering.DisableScaling", value ? "True" : null);
        }

        public bool UncapFps
        {
            get => App.FastFlags.GetPreset("Rendering.UncapFps") == "9999";
            set => App.FastFlags.SetPreset("Rendering.UncapFps", value ? "9999" : null);
        }

        public bool GraySky
        {
            get => App.FastFlags.GetPreset("Rendering.GraySky") == "True";
            set => App.FastFlags.SetPreset("Rendering.GraySky", value ? "True" : null);
        }

        public IReadOnlyList<RenderingMode> RenderingModes => Enum.GetValues<RenderingMode>();

        public RenderingMode SelectedRenderingMode
        {
            get
            {
                if (App.FastFlags.GetValue("FFlagDebugGraphicsPreferVulkan") == "True")
                    return RenderingMode.Vulkan;

                if (App.FastFlags.GetValue("FFlagDebugGraphicsPreferOpenGL") == "True")
                    return RenderingMode.OpenGL;

                if (App.FastFlags.GetValue("FFlagDebugGraphicsPreferD3D11") == "True")
                    return RenderingMode.D3D11;

                return RenderingMode.Automatic;
            }
            set
            {
                App.FastFlags.SetValue("FFlagDebugGraphicsPreferD3D11", null);
                App.FastFlags.SetValue("FFlagDebugGraphicsPreferD3D11FL10", null);
                App.FastFlags.SetValue("FFlagDebugGraphicsPreferVulkan", null);
                App.FastFlags.SetValue("FFlagDebugGraphicsPreferOpenGL", null);
                App.FastFlags.SetValue("FFlagGraphicsEnableD3D10Compute", null);

                switch (value)
                {
                    case RenderingMode.D3D11:
                        App.FastFlags.SetValue("FFlagDebugGraphicsPreferD3D11", "True");
                        break;

                    case RenderingMode.Vulkan:
                        App.FastFlags.SetValue("FFlagDebugGraphicsPreferVulkan", "True");
                        break;

                    case RenderingMode.OpenGL:
                        App.FastFlags.SetValue("FFlagDebugGraphicsPreferOpenGL", "True");
                        break;
                }
            }
        }

        public bool DisableGrass
        {
            get =>
                App.FastFlags.GetValue("FIntFRMMinGrassDistance") == "0" &&
                App.FastFlags.GetValue("FIntFRMMaxGrassDistance") == "0" &&
                App.FastFlags.GetValue("FIntRenderGrassDetailStrands") == "0";
            set
            {
                App.FastFlags.SetValue("FIntFRMMinGrassDistance", value ? "0" : null);
                App.FastFlags.SetValue("FIntFRMMaxGrassDistance", value ? "0" : null);
                App.FastFlags.SetValue("FIntRenderGrassDetailStrands", value ? "0" : null);
            }
        }

        public bool PauseVoxelizer
        {
            get =>
                App.FastFlags.GetValue("DFFlagDebugPauseVoxelizer") == "True" &&
                App.FastFlags.GetValue("FIntRenderShadowIntensity") == "0";
            set
            {
                App.FastFlags.SetValue("DFFlagDebugPauseVoxelizer", value ? "True" : null);
                App.FastFlags.SetValue("FIntRenderShadowIntensity", value ? "0" : null);
            }
        }

        public IReadOnlyDictionary<TextureQuality, string?> TextureQualities => FastFlagManager.TextureQualityLevels;

        public TextureQuality SelectedTextureQuality
        {
            get => TextureQualities.Where(x => x.Value == App.FastFlags.GetPreset("Rendering.TextureQuality.Level")).FirstOrDefault().Key;
            set
            {
                if (value == TextureQuality.Default)
                {
                    App.FastFlags.SetPreset("Rendering.TextureQuality", null);
                }
                else
                {
                    App.FastFlags.SetPreset("Rendering.TextureQuality.OverrideEnabled", "True");
                    App.FastFlags.SetPreset("Rendering.TextureQuality.Level", TextureQualities[value]);
                }
            }
        }
        public bool ResetConfiguration
        {
            get => _preResetFlags is not null;

            set
            {
                if (value)
                {
                    _preResetFlags = new(App.FastFlags.Prop);
                    App.FastFlags.Prop.Clear();
                }
                else
                {
                    App.FastFlags.Prop = _preResetFlags!;
                    _preResetFlags = null;
                }

                RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
