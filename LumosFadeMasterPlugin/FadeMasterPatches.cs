using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LumosLIB.Kernel;
using LumosLIB.Kernel.Log;
using LumosLIB.Tools;
using LumosProtobuf;
using org.dmxc.lumos.Kernel.Scene;
using org.dmxc.lumos.Kernel.Scene.Fanning;
using org.dmxc.lumos.Kernel.SceneList;
using org.dmxc.lumos.Kernel.SceneList.ValuesStack;

namespace LumosFadeMasterPlugin
{
    /// <summary>
    /// Runtime patches into the Lumos kernel:
    ///  1. SceneList.Parameters / getParameter / setParameter / testParameter
    ///     -> adds the "Fade Master" fields to the cuelist settings dialog.
    ///  2. OutputValuesStack.ApplyTimings
    ///     -> rewrites the fade time of every value a cuelist fades in.
    /// </summary>
    public static class FadeMasterPatches
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(typeof(FadeMasterPatches));

        public const string HarmonyId = "de.tom.lumos.fademaster";

        public const string PARA_EXPRESSION = "FadeMasterExpression";
        public const string PARA_MODE = "FadeMasterMode";
        public const string PARA_FADEOUT = "FadeMasterIncludeFadeOut";
        public const string PARA_CURRENT = "FadeMasterCurrentValue";

        private const string CATEGORY = "Fade Master";

        // Labels shown in the dropdown, index == EFadeMasterMode
        private static readonly string[] ModeLabels =
        {
            "Off",
            "Scale (Cue Fade x Master)",
            "Override (Master = Seconds)"
        };

        private static Harmony _harmony;

        public static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }
            _harmony = new Harmony(HarmonyId);
            var self = typeof(FadeMasterPatches);

            Patch(AccessTools.PropertyGetter(typeof(SceneList), nameof(SceneList.Parameters)),
                postfix: new HarmonyMethod(self, nameof(Parameters_Postfix)));
            Patch(AccessTools.Method(typeof(SceneList), nameof(SceneList.getParameter), new[] { typeof(GenericParameter) }),
                prefix: new HarmonyMethod(self, nameof(GetParameter_Prefix)));
            Patch(AccessTools.Method(typeof(SceneList), nameof(SceneList.setParameter), new[] { typeof(GenericParameter), typeof(object) }),
                prefix: new HarmonyMethod(self, nameof(SetParameter_Prefix)));
            Patch(AccessTools.Method(typeof(SceneList), nameof(SceneList.testParameter), new[] { typeof(GenericParameter), typeof(object) }),
                prefix: new HarmonyMethod(self, nameof(TestParameter_Prefix)));
            // --- cue fade fields (cuelist editor): allow master expressions
            Patch(AccessTools.PropertySetter(typeof(AbstractEntryContainer), nameof(AbstractEntryContainer.Fade)),
                prefix: new HarmonyMethod(self, nameof(FadeSetter_Prefix)));
            Patch(AccessTools.PropertySetter(typeof(AbstractEntryContainer), nameof(AbstractEntryContainer.FadeDown)),
                prefix: new HarmonyMethod(self, nameof(FadeSetter_Prefix)));
            // GUI gets the raw expression instead of the evaluated ms
            Patch(AccessTools.Method(typeof(FannedValueManager), nameof(FannedValueManager.convertToContainerInstance)),
                prefix: new HarmonyMethod(self, nameof(ConvertToContainer_Prefix)));
            // project file stores the raw expression
            Patch(AccessTools.Method(typeof(FannedValueManager), "serializeIFannedValue"),
                prefix: new HarmonyMethod(self, nameof(Serialize_Prefix)));

            // --- runtime: bind on GO, follow the masters live on every tick
            Patch(AccessTools.Method(typeof(OutputValuesStack), "ApplyTimings"),
                postfix: new HarmonyMethod(self, nameof(ApplyTimings_Postfix)));
            Patch(AccessTools.Method(typeof(OutputValueItem), nameof(OutputValueItem.CalculateFadeDelay)),
                prefix: new HarmonyMethod(self, nameof(CalculateFadeDelay_Prefix)));

            // --- Freeze Change: hold back cuelist starts / stops
            FreezeChange.ApplyPatches(_harmony);
        }

        public static void Remove()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            ClearBindings();
        }

        private static void Patch(System.Reflection.MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (original == null)
            {
                throw new MissingMethodException("FadeMaster: patch target not found (incompatible DMXControl version?)");
            }
            _harmony.Patch(original, prefix: prefix, postfix: postfix);
            Log.Debug("FadeMaster: patched {0}.{1}", original.DeclaringType?.Name, original.Name);
        }

        private static bool IsOwnParameter(GenericParameter p)
        {
            string n = p?.Name;
            return n == PARA_EXPRESSION || n == PARA_MODE || n == PARA_FADEOUT || n == PARA_CURRENT;
        }

        // ------------------------------------------------------------------ settings dialog

        private static void Parameters_Postfix(ref List<GenericParameter> __result)
        {
            try
            {
                if (__result == null || __result.Any(IsOwnParameter))
                {
                    return;
                }
                ParameterCategory cat = ParameterCategoryTools.FromName(CATEGORY);
                __result.Add(new GenericParameter(PARA_EXPRESSION, "SceneList", typeof(string), EGenericParameterOptions.MULTISELECTABLE, null, cat)
                {
                    DisplayName = "Fade In Master"
                });
                __result.Add(new GenericParameter(PARA_MODE, "SceneList", typeof(string), EGenericParameterOptions.MULTISELECTABLE, ModeLabels.Cast<object>().ToArray(), cat)
                {
                    DisplayName = "Fade In Master Mode"
                });
                __result.Add(new GenericParameter(PARA_FADEOUT, "SceneList", typeof(bool), EGenericParameterOptions.MULTISELECTABLE, null, cat)
                {
                    DisplayName = "Fade Master also for Fade Out"
                });
                __result.Add(new GenericParameter(PARA_CURRENT, "SceneList", typeof(string), EGenericParameterOptions.READONLY, null, cat)
                {
                    DisplayName = "Fade Master current value"
                });
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster: Parameters_Postfix", e);
            }
        }

        private static bool GetParameter_Prefix(SceneList __instance, GenericParameter parameter, ref object __result)
        {
            if (!IsOwnParameter(parameter))
            {
                return true;
            }
            FadeMasterSetting s = FadeMasterStore.Get(__instance.ID);
            switch (parameter.Name)
            {
                case PARA_EXPRESSION:
                    __result = s?.Expression ?? string.Empty;
                    break;
                case PARA_MODE:
                    __result = ModeLabels[(int)(s?.Mode ?? EFadeMasterMode.Off)];
                    break;
                case PARA_FADEOUT:
                    __result = s?.IncludeFadeOut ?? false;
                    break;
                case PARA_CURRENT:
                    __result = s?.DescribeCurrentValue() ?? "-";
                    break;
            }
            return false;
        }

        private static bool TestParameter_Prefix(GenericParameter parameter, object value, ref bool __result)
        {
            if (!IsOwnParameter(parameter))
            {
                return true;
            }
            __result = parameter.Name switch
            {
                PARA_EXPRESSION => value is string str && FadeMasterExpression.IsValid(str, out _),
                PARA_MODE => value is string m && Array.IndexOf(ModeLabels, m) >= 0,
                PARA_FADEOUT => value is bool,
                _ => false
            };
            return false;
        }

        private static bool SetParameter_Prefix(SceneList __instance, GenericParameter parameter, object value, ref bool __result)
        {
            if (!IsOwnParameter(parameter))
            {
                return true;
            }
            __result = false;
            FadeMasterSetting s = FadeMasterStore.GetOrCreate(__instance);
            switch (parameter.Name)
            {
                case PARA_EXPRESSION:
                {
                    string expr = FadeMasterExpression.Normalize(value as string);
                    if (!FadeMasterExpression.IsValid(expr, out string error))
                    {
                        // The kernel sends ArgumentException messages back to the GUI.
                        throw new ArgumentException(error);
                    }
                    if (expr != s.Expression)
                    {
                        s.Expression = expr;
                        // Convenience: entering a master switches the mode on.
                        if (expr.Length > 0 && s.Mode == EFadeMasterMode.Off)
                        {
                            s.Mode = EFadeMasterMode.Scale;
                        }
                        __result = true;
                    }
                    break;
                }
                case PARA_MODE:
                {
                    int idx = Array.IndexOf(ModeLabels, value as string);
                    if (idx >= 0 && (EFadeMasterMode)idx != s.Mode)
                    {
                        s.Mode = (EFadeMasterMode)idx;
                        __result = true;
                    }
                    break;
                }
                case PARA_FADEOUT:
                    if (value is bool b && b != s.IncludeFadeOut)
                    {
                        s.IncludeFadeOut = b;
                        __result = true;
                    }
                    break;
            }
            if (__result)
            {
                s.InvalidateCache();
                FadeMasterStore.MarkDirty(__instance, parameter.Name);
                Log.Info("FadeMaster: cuelist '{0}' -> Mode={1}, Master={2}, FadeOut={3}", __instance.Name, s.Mode, s.Expression, s.IncludeFadeOut);
            }
            return false;
        }

        // ------------------------------------------------------------------ runtime

        // ------------------------------------------------------------------ cue fade fields

        // AbstractEntryContainer.set_Fade / set_FadeDown: wrap values containing {Master ...}
        private static void FadeSetter_Prefix(ref IFannedValue value)
        {
            try
            {
                value = MasterFadeValue.WrapIfNeeded(value);
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster: FadeSetter_Prefix", e);
            }
        }

        // FannedValueManager.convertToContainerInstance(object value, ...) -> what the GUI displays
        private static void ConvertToContainer_Prefix(ref object value)
        {
            if (value is MasterFadeValue m)
            {
                value = m.RawValue;
            }
        }

        // FannedValueManager.serializeIFannedValue(IFannedValue value) -> what is saved in the project
        private static void Serialize_Prefix(ref IFannedValue value)
        {
            if (value is MasterFadeValue m)
            {
                value = m.RawValue;
            }
        }

        // ------------------------------------------------------------------ runtime

        /// <summary>Live fade source of one running OutputValueItem.</summary>
        private sealed class FadeBinding
        {
            public long StaticBase;          // fade time Lumos computed (cue / goto / timings)
            public MasterFadeValue CueMaster; // cue fade field with a single master expression, or null
            public FadeMasterSetting Cuelist; // cuelist "Fade In Master" setting, or null
            public bool FadeIn;

            public long Compute()
            {
                long baseFade = StaticBase;
                if (CueMaster != null && CueMaster.TryEvaluateSingleMs(out long cueMs))
                {
                    baseFade = cueMs;
                }
                if (Cuelist != null && (FadeIn || Cuelist.IncludeFadeOut) && Cuelist.TryComputeFadeTime(baseFade, out long f))
                {
                    return f;
                }
                return baseFade;
            }
        }

        private static ConditionalWeakTable<OutputValueItem, FadeBinding> _bindings = new();

        public static void ClearBindings() => _bindings = new ConditionalWeakTable<OutputValueItem, FadeBinding>();

        // Original: private void ApplyTimings(OutputValueItem item, long timeOffset, bool fadeIn,
        //   FadeDelayTimings timings, PropertyFadeDelayTimings propertyTimings, int sourceIndex,
        //   int destIndex, EGotoOptions options, bool? valueNew)
        // Called whenever a value starts fading (GO, GoTo, GoBack, adjusted running fades).
        private static void ApplyTimings_Postfix(OutputValuesStack __instance, OutputValueItem item, bool fadeIn, int sourceIndex)
        {
            if (item == null)
            {
                return;
            }
            try
            {
                _bindings.Remove(item);
                // sourceIndex -1 == autoprepare (move-in-black): keep Lumos' own timing.
                if (sourceIndex < 0)
                {
                    return;
                }
                SceneList owner = FadeMasterStore.GetOwner(__instance);
                if (owner == null)
                {
                    return;
                }
                FadeMasterSetting setting = FadeMasterStore.Get(owner.ID);
                if (setting != null && !setting.IsActive)
                {
                    setting = null;
                }
                MasterFadeValue cueMaster = fadeIn ? FindCueMaster(owner, item) : null;
                if (setting == null && cueMaster == null)
                {
                    return;
                }
                var binding = new FadeBinding
                {
                    StaticBase = item.FadeTime,
                    CueMaster = cueMaster,
                    Cuelist = setting,
                    FadeIn = fadeIn
                };
                _bindings.AddOrUpdate(item, binding);
                item.FadeTime = binding.Compute();
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster: ApplyTimings_Postfix", e);
            }
        }

        /// <summary>
        /// Returns the cue's fade value if (a) it is a single master expression and (b) the item really uses the
        /// cue fade (not an entry fade override and not a GoTo/GoBack/TakeFades time).
        /// </summary>
        private static MasterFadeValue FindCueMaster(SceneList owner, OutputValueItem item)
        {
            IOutputValue v = item.Value;
            if (v == null || v.FadeTime2.HasValue)
            {
                return null; // entry has its own fade time
            }
            IScene scene = owner[item.Index];
            if (scene == null)
            {
                return null;
            }
            IFannedValue fv;
            if (item.FadeTime == v.FadeTime)
            {
                fv = scene.Fade;
            }
            else if (item.FadeTime == v.FadeDownTime)
            {
                fv = scene.FadeDown;
            }
            else
            {
                return null; // time was overridden by cuelist timings
            }
            return fv is MasterFadeValue m && m.IsSingleExpression ? m : null;
        }

        // Original: public bool CalculateFadeDelay(long time, out double delayPercent, out double fadePercent, out double totalPercent)
        // Called every processing tick for every running item -> follow the master live.
        // When the fade time changes mid-fade, the item's start offset is shifted so the current fade
        // percentage stays the same: the fade just continues faster/slower, no jump.
        private static void CalculateFadeDelay_Prefix(OutputValueItem __instance, long time)
        {
            if (!_bindings.TryGetValue(__instance, out FadeBinding b))
            {
                return;
            }
            try
            {
                long oldFade = __instance.FadeTime;
                long newFade = b.Compute();
                if (newFade == oldFade)
                {
                    return;
                }
                double inFade = time - __instance.Offset - __instance.DelayTime; // ms into the fade phase
                if (inFade <= 0)
                {
                    __instance.FadeTime = newFade; // still waiting / in delay
                    return;
                }
                if (oldFade <= 0 || inFade >= oldFade)
                {
                    _bindings.Remove(__instance); // fade finished, nothing to follow anymore
                    return;
                }
                double percent = inFade / oldFade;
                double newOffsetMs = time - __instance.DelayTime - percent * newFade;
                __instance.OffsetIn100Ns = (long)Math.Round(newOffsetMs * 10000.0);
                __instance.FadeTime = newFade;
            }
            catch (Exception e)
            {
                _bindings.Remove(__instance);
                Log.Error("FadeMaster: CalculateFadeDelay_Prefix", e);
            }
        }
    }
}
