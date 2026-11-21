using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Forms;
using HarmonyLib;
using Lumos.GUI;
using Lumos.GUI.Actions;
using Lumos.GUI.GuiActions;
using Lumos.GUI.Net;
using Lumos.GUI.Net.gClient;
using Lumos.GUI.Windows;
using LumosLIB.Kernel.Log;
using LumosToolsLIB.Tools;

namespace LumosFadeMasterGuiPlugin
{
    /// <summary>
    /// "Freeze Change" button in the main tool strip on the left, directly below "Freeze", built exactly like the
    /// built-in Freeze button (TOOL_STRIP action, group "30_HighlightBlackout", checked state = active).
    ///
    /// The logic lives in the kernel plugin "Cuelist Fade Master": it registers the main switch
    /// "Cuelist Changes" (ON = normal, OFF = Freeze Change active). This button just toggles that switch.
    /// </summary>
    internal sealed class FreezeChangeButton : IActionProvider
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(nameof(FreezeChangeButton));

        public const string SwitchId = "FadeMasterCuelistChanges"; // must match CuelistChangesSwitch.SwitchId (kernel)
        public const string ActionId = "ActFreezeChange";
        private const string Group = "30_HighlightBlackout";     // same group as Highlight / Blackout / Freeze
        private const int Sort = 4;                               // Freeze = 3, "Stop all Cuelists" = 4 (tie -> see CompareTo patch)

        public static readonly FreezeChangeButton Instance = new FreezeChangeButton();

        private bool _visible;
        private bool _registered;
        private bool _eventsAssigned;

        // cached kernel state
        private bool? _switchEnabled; // null = switch unknown (kernel plugin inactive)
        private DateTime _fetched = DateTime.MinValue;

        public string ID => "LumosFadeMasterGuiPlugin.FreezeChange";

        public ReadOnlyCollection<CommandLineActionMetadata> CommandLineActions => new List<CommandLineActionMetadata>().AsReadOnly();

        public event EventHandler<EventArgs> ActionsChanged;

        /// <summary>Show / hide the button (plugin enabled / disabled). Action providers cannot be unregistered.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!_registered)
            {
                GuiActionManager.getInstance().registerActionProvider(this);
                _registered = true;
            }
            AssignEvents();
            _fetched = DateTime.MinValue;
            ActionsChanged?.InvokeFailSafe(this, EventArgs.Empty);
            GuiActionManager.getInstance().tryRefreshActionContainer(MainForm.MAIN_STRIP_CONTROL_CONTAINER_ID, ERefreshFlags.COMPLETE);
        }

        private void AssignEvents()
        {
            if (_eventsAssigned)
            {
                return;
            }
            MainSwitch_gClient client = GrpcManager.getInstance().LookupService<MainSwitch_gClient>();
            if (client != null)
            {
                client.MainSwitchChanged += _ =>
                {
                    // switched elsewhere (other GUI, input assignment, ...) -> refresh check state
                    _fetched = DateTime.MinValue;
                    RefreshChecked();
                };
                _eventsAssigned = true;
            }
        }

        public Task<ReadOnlyCollection<ActionItemMetadata>> getActionsAsync()
        {
            var list = new List<ActionItemMetadata>();
            if (_visible)
            {
                var m = new ActionItemMetadata(ActionId, ID, "Freeze Change", KnownIcons.FREEZE, EActionItemDisplayType.TOOL_STRIP, Sort, Group, null,
                    (ActionItemCallback)Click,
                    "Freeze Change: cuelists are neither started nor stopped.\nAll starts / stops are executed when Freeze Change is released.")
                {
                    ItemCheckedFunction = (o, s, t) => IsFrozenAsync(),
                    CheckEnabledFunction = (o, s, t) => IsAvailableAsync(),
                    AutoRefresh = ERefreshFlags.CHECKED
                };
                m.AddDefaultContainers(MainForm.MAIN_STRIP_CONTROL_CONTAINER_ID);
                list.Add(m);
            }
            return Task.FromResult(list.AsReadOnly());
        }

        public void ActionInvoked(object sender, ActionItemMetadata meta, EMouseButtons buttons, bool down)
        {
        }

        private async void Click(object sender, ActionItemMetadata meta, EMouseButtons buttons, bool down)
        {
            if (down || buttons != EMouseButtons.Left)
            {
                return;
            }
            try
            {
                MainSwitch_gClient client = GrpcManager.getInstance().LookupService<MainSwitch_gClient>();
                if (client == null)
                {
                    return;
                }
                bool frozen = await IsFrozenAsync().ConfigureAwait(true);
                // switch ON = normal operation -> toggling Freeze Change = set switch to the current frozen state
                if (await client.SetMainSwitch(SwitchId, frozen).ConfigureAwait(true))
                {
                    _switchEnabled = frozen;
                    _fetched = DateTime.UtcNow;
                }
                if (sender is ToolStripItem item)
                {
                    await GuiActionManager.getInstance().UpdateChecked(item).ConfigureAwait(true);
                }
            }
            catch (Exception e)
            {
                Log.Warn("Freeze Change: toggling failed: " + e.Message);
            }
        }

        private async Task<bool?> GetSwitchAsync()
        {
            if ((DateTime.UtcNow - _fetched).TotalMilliseconds < 1000)
            {
                return _switchEnabled;
            }
            MainSwitch_gClient client = GrpcManager.getInstance().LookupService<MainSwitch_gClient>();
            if (client == null)
            {
                return null;
            }
            AssignEvents();
            try
            {
                _switchEnabled = await client.GetMainSwitchAsync(SwitchId).ConfigureAwait(false);
            }
            catch
            {
                _switchEnabled = null;
            }
            _fetched = DateTime.UtcNow;
            return _switchEnabled;
        }

        private async Task<bool> IsFrozenAsync() => await GetSwitchAsync().ConfigureAwait(false) == false;

        /// <summary>Disabled (greyed) when the kernel plugin is not active.</summary>
        private async Task<bool> IsAvailableAsync() => (await GetSwitchAsync().ConfigureAwait(false)).HasValue;

        private void RefreshChecked()
        {
            try
            {
                GuiActionManager.getInstance().tryRefreshAction(MainForm.MAIN_STRIP_CONTROL_CONTAINER_ID, ID, ActionId, ERefreshFlags.CHECKED | ERefreshFlags.ENABLED);
            }
            catch
            {
                // GUI shutting down
            }
        }

        // ------------------------------------------------------------------ sort order

        /// <summary>
        /// "Stop all Cuelists" also has sort number 4 in the same group; List.Sort is not stable, so make the order
        /// explicit: Freeze (3) &lt; Freeze Change (4) &lt; Stop all Cuelists (4).
        /// </summary>
        public static void PatchSortOrder(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(ActionItemMetadata), nameof(ActionItemMetadata.CompareTo), new[] { typeof(ActionItemMetadata) });
            if (target == null)
            {
                Log.Warn("Freeze Change: ActionItemMetadata.CompareTo not found, button order may vary");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(FreezeChangeButton), nameof(CompareTo_Postfix)));
        }

        private static void CompareTo_Postfix(ActionItemMetadata __instance, ActionItemMetadata other, ref int __result)
        {
            if (__result != 0 || other == null)
            {
                return;
            }
            if (__instance.ActionID == ActionId && other.ActionID == "ActStopAllCuelists")
            {
                __result = -1;
            }
            else if (__instance.ActionID == "ActStopAllCuelists" && other.ActionID == ActionId)
            {
                __result = 1;
            }
        }
    }
}
