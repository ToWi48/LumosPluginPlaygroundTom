using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LumosLIB.Kernel.Log;
using LumosProtobuf.Cuelist;
using LumosToolsLIB.Tools;
using org.dmxc.lumos.Kernel.MainSwitch;
using org.dmxc.lumos.Kernel.Run;
using org.dmxc.lumos.Kernel.SceneList;

namespace LumosFadeMasterPlugin
{
    /// <summary>
    /// "Freeze Change": while active, cuelists are neither started nor stopped. Every start / stop request is
    /// remembered (only the resulting change per cuelist counts) and executed when Freeze Change is released.
    ///
    /// Held back:  play / go / goTo / goNext on a STOPPED cuelist (= start), stop() / stop(false) on a running one.
    /// Passed:     GO on a running cuelist (next cue), pause / resume, stop(true) (kernel internal, e.g. release
    ///             fade finished), everything while the kernel is not running.
    /// </summary>
    public static class FreezeChange
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(typeof(FreezeChange));

        private sealed class Pending
        {
            public SceneList Cuelist;
            public bool Start;
            public Func<bool> Replay;
            public long Seq;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Pending> PendingChanges = new Dictionary<string, Pending>();
        private static long _seq;
        private static bool _active;

        // > 0 while a SceneList entry method is executing on this thread -> nested calls are internal, never intercepted
        [ThreadStatic] private static int _depth;
        // true while the queued changes are being executed
        [ThreadStatic] private static bool _replaying;

        /// <summary>Plugin enabled (patches active). While false, the switch is inert.</summary>
        public static bool PluginActive { get; set; }

        public static bool Active
        {
            get { lock (Sync) return _active; }
        }

        public static event EventHandler ActiveChanged;

        public static int PendingCount
        {
            get { lock (Sync) return PendingChanges.Count; }
        }

        public static void SetActive(bool active)
        {
            List<Pending> toApply;
            lock (Sync)
            {
                if (!PluginActive)
                {
                    active = false;
                }
                if (_active == active)
                {
                    return;
                }
                _active = active;
                toApply = active ? null : PendingChanges.Values.OrderBy(p => p.Seq).ToList();
                if (!active)
                {
                    PendingChanges.Clear();
                }
            }
            Log.Info(active ? "Freeze Change ON - cuelist starts / stops are held back"
                            : "Freeze Change OFF - applying {0} held change(s)", toApply?.Count ?? 0);
            ActiveChanged?.InvokeFailSafe(null, EventArgs.Empty);
            if (toApply != null)
            {
                Apply(toApply);
            }
        }

        private static void Apply(List<Pending> changes)
        {
            _replaying = true;
            try
            {
                foreach (Pending p in changes)
                {
                    try
                    {
                        if (p.Cuelist.IsDisposed)
                        {
                            continue;
                        }
                        Log.Debug("Freeze Change: {0} cuelist '{1}'", p.Start ? "starting" : "stopping", p.Cuelist.Name);
                        p.Replay();
                    }
                    catch (Exception e)
                    {
                        Log.Error("Freeze Change: unable to apply change to cuelist " + p.Cuelist?.Name, e);
                    }
                }
            }
            finally
            {
                _replaying = false;
            }
        }

        /// <summary>Project closed: cuelists are gone, drop everything that is held back.</summary>
        public static void ClearPending()
        {
            lock (Sync)
            {
                PendingChanges.Clear();
            }
        }

        // ------------------------------------------------------------------ decisions (called from the patches)

        private static bool CanIntercept()
        {
            if (_depth > 0 || _replaying || !PluginActive)
            {
                return false;
            }
            lock (Sync)
            {
                if (!_active)
                {
                    return false;
                }
            }
            try
            {
                return KernelRunManager.getInstance().KernelStartupOrRunning;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Start-type call. Returns true if the call has been held back (original must not run).</summary>
        private static bool HoldStart(SceneList sl, Func<bool> replay)
        {
            lock (Sync)
            {
                if (PendingChanges.TryGetValue(sl.ID, out Pending p))
                {
                    if (!p.Start)
                    {
                        // running cuelist with a held stop: starting again cancels the stop
                        PendingChanges.Remove(sl.ID);
                        Log.Debug("Freeze Change: stop of '{0}' cancelled", sl.Name);
                    }
                    else
                    {
                        p.Replay = replay; // newest start request wins (go / goTo index)
                    }
                    return true;
                }
                if (sl.State == ESceneListState.Stopped)
                {
                    PendingChanges[sl.ID] = new Pending { Cuelist = sl, Start = true, Replay = replay, Seq = ++_seq };
                    Log.Debug("Freeze Change: start of '{0}' held", sl.Name);
                    return true;
                }
                return false; // already running (GO = next cue) or paused (resume): allowed
            }
        }

        /// <summary>Stop-type call. Returns true if the call has been held back (original must not run).</summary>
        private static bool HoldStop(SceneList sl, Func<bool> replay)
        {
            lock (Sync)
            {
                if (PendingChanges.TryGetValue(sl.ID, out Pending p))
                {
                    if (p.Start)
                    {
                        // stopped cuelist with a held start: stopping cancels the start
                        PendingChanges.Remove(sl.ID);
                        Log.Debug("Freeze Change: start of '{0}' cancelled", sl.Name);
                    }
                    else
                    {
                        p.Replay = replay;
                    }
                    return true;
                }
                if (sl.State != ESceneListState.Stopped)
                {
                    PendingChanges[sl.ID] = new Pending { Cuelist = sl, Start = false, Replay = replay, Seq = ++_seq };
                    Log.Debug("Freeze Change: stop of '{0}' held", sl.Name);
                    return true;
                }
                return false; // already stopped: nothing to hold
            }
        }

        // ------------------------------------------------------------------ Harmony patches on SceneList

        public static void ApplyPatches(Harmony harmony)
        {
            var self = typeof(FreezeChange);
            var leave = new HarmonyMethod(self, nameof(Leave_Finalizer));
            Patch(harmony, AccessTools.Method(typeof(SceneList), nameof(SceneList.play), Type.EmptyTypes), nameof(Play_Prefix), leave);
            Patch(harmony, AccessTools.Method(typeof(SceneList), nameof(SceneList.go), Type.EmptyTypes), nameof(Go_Prefix), leave);
            Patch(harmony, AccessTools.Method(typeof(SceneList), nameof(SceneList.goTo), new[] { typeof(int) }), nameof(GoTo_Prefix), leave);
            Patch(harmony, AccessTools.Method(typeof(SceneList), nameof(SceneList.goNext), new[] { typeof(int) }), nameof(GoNext_Prefix), leave);
            Patch(harmony, AccessTools.Method(typeof(SceneList), nameof(SceneList.stop), Type.EmptyTypes), nameof(Stop_Prefix), leave);
            Patch(harmony, AccessTools.Method(typeof(SceneList), nameof(SceneList.stop), new[] { typeof(bool) }), nameof(StopBool_Prefix), leave);
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodBase original, string prefix, HarmonyMethod finalizer)
        {
            if (original == null)
            {
                throw new MissingMethodException("FreezeChange: patch target not found for " + prefix);
            }
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(FreezeChange), prefix), finalizer: finalizer);
            Log.Debug("FadeMaster: patched SceneList.{0} (Freeze Change)", original.Name);
        }

        // __state = true -> the original runs and _depth was incremented (finalizer decrements)

        private static bool Enter(out bool __state)
        {
            _depth++;
            __state = true;
            return true;
        }

        private static bool Play_Prefix(SceneList __instance, ref bool __result, out bool __state)
        {
            __state = false;
            if (CanIntercept() && HoldStart(__instance, __instance.play))
            {
                __result = true;
                return false;
            }
            return Enter(out __state);
        }

        private static bool Go_Prefix(SceneList __instance, ref bool __result, out bool __state)
        {
            __state = false;
            if (CanIntercept() && HoldStart(__instance, __instance.go))
            {
                __result = true;
                return false;
            }
            return Enter(out __state);
        }

        private static bool GoTo_Prefix(SceneList __instance, int index, ref bool __result, out bool __state)
        {
            __state = false;
            SceneList sl = __instance;
            if (CanIntercept() && HoldStart(sl, () => sl.goTo(index)))
            {
                __result = true;
                return false;
            }
            return Enter(out __state);
        }

        private static bool GoNext_Prefix(SceneList __instance, int index, ref bool __result, out bool __state)
        {
            __state = false;
            SceneList sl = __instance;
            if (CanIntercept() && HoldStart(sl, () => sl.goNext(index)))
            {
                __result = true;
                return false;
            }
            return Enter(out __state);
        }

        private static bool Stop_Prefix(SceneList __instance, ref bool __result, out bool __state)
        {
            __state = false;
            if (CanIntercept() && HoldStop(__instance, __instance.stop))
            {
                __result = true;
                return false;
            }
            return Enter(out __state);
        }

        private static bool StopBool_Prefix(SceneList __instance, bool stopImmediate, ref bool __result, out bool __state)
        {
            __state = false;
            // stop(true) is used by the kernel itself (release fade finished, reset, ...) -> never held back
            SceneList sl = __instance;
            if (!stopImmediate && CanIntercept() && HoldStop(sl, () => sl.stop(false)))
            {
                __result = true;
                return false;
            }
            return Enter(out __state);
        }

        private static Exception Leave_Finalizer(Exception __exception, bool __state)
        {
            if (__state && _depth > 0)
            {
                _depth--;
            }
            return __exception;
        }
    }

    /// <summary>
    /// Main switch "Cuelist Changes" (inverted on purpose: DMXControl switches main switches ON at startup by default).
    /// ON = normal operation, OFF = Freeze Change active. The GUI button "Freeze Change" toggles this switch.
    /// </summary>
    public sealed class CuelistChangesSwitch : IMainSwitch
    {
        public const string SwitchId = "FadeMasterCuelistChanges";

        public static readonly CuelistChangesSwitch Instance = new CuelistChangesSwitch();

        private CuelistChangesSwitch()
        {
            FreezeChange.ActiveChanged += (s, e) => EnabledChanged?.InvokeFailSafe(this, EventArgs.Empty);
        }

        public string ID => SwitchId;

        public string Name => "Cuelist Changes";

        public bool Enabled
        {
            get => !FreezeChange.Active;
            set => FreezeChange.SetActive(!value);
        }

        public event EventHandler<EventArgs> EnabledChanged;
    }
}
