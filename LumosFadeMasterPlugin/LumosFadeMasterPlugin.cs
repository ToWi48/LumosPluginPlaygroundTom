using System;
using LumosLIB.Kernel.Log;
using org.dmxc.lumos.Kernel.MainSwitch;
using org.dmxc.lumos.Kernel.Plugin;
using org.dmxc.lumos.Kernel.Resource;

namespace LumosFadeMasterPlugin
{
    /// <summary>
    /// Kernel plugin: lets a ParameterMaster (or any master usable in Device Control,
    /// e.g. {ParameterMaster 2:/100}) control the fade-in time of a whole cuelist.
    /// Configured per cuelist in the cuelist settings, category "Fade Master".
    /// </summary>
    public class LumosFadeMasterPlugin : KernelPluginBase
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(typeof(LumosFadeMasterPlugin));

        public LumosFadeMasterPlugin() : base("{3B6E2F4A-8C1D-4E7B-9A52-6F0D1C8E7A31}", "Cuelist Fade Master")
        {
        }

        protected override void initializePlugin()
        {
            Log.Info("Initialize " + nameof(LumosFadeMasterPlugin));
        }

        protected override void startupPlugin()
        {
            Log.Info("Startup " + nameof(LumosFadeMasterPlugin));
            try
            {
                FadeMasterPatches.Apply();
                FreezeChange.PluginActive = true;
                RegisterFreezeChangeSwitch();
                // Plugin may be enabled while a project is already open.
                FadeMasterStore.Load();
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster: startup failed, plugin disabled", e);
                FreezeChange.PluginActive = false;
                FadeMasterPatches.Remove();
            }
        }

        protected override void shutdownPlugin()
        {
            Log.Info("Shutdown " + nameof(LumosFadeMasterPlugin));
            // release Freeze Change first so nothing stays held back
            FreezeChange.SetActive(false);
            FreezeChange.PluginActive = false;
            FadeMasterPatches.Remove();
            FadeMasterStore.Clear();
        }

        private static bool _switchRegistered;

        /// <summary>Main switches cannot be unregistered, so this happens once per kernel run.</summary>
        private static void RegisterFreezeChangeSwitch()
        {
            if (_switchRegistered)
            {
                return;
            }
            try
            {
                MainSwitchManager.getInstance().RegisterMainSwitch(CuelistChangesSwitch.Instance);
                _switchRegistered = true;
                Log.Info("FadeMaster: main switch '{0}' registered (Freeze Change)", CuelistChangesSwitch.Instance.Name);
            }
            catch (Exception e)
            {
                Log.Warn("FadeMaster: unable to register Freeze Change switch: " + e.Message);
            }
        }

        public override void loadProject(LumosIOContext context)
        {
            base.loadProject(context);
            FadeMasterStore.Load();
        }

        public override void saveProject(LumosIOContext context)
        {
            base.saveProject(context);
            FadeMasterStore.Save();
        }

        public override void closeProject(LumosIOContext context)
        {
            base.closeProject(context);
            FadeMasterStore.Clear();
            FreezeChange.ClearPending();
        }
    }
}
