using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Lumos.GUI.Plugin;
using Lumos.GUI.UICustomization;
using LumosLIB.Kernel.Log;
using LumosLIB.Kernel.Scene.Fanning;
using GuiFannedValueManager = Lumos.GUI.Facade.Scene.Fanning.FannedValueManager;

namespace LumosFadeMasterGuiPlugin
{
    /// <summary>
    /// GUI part of "Cuelist Fade Master".
    ///
    /// The cuelist editor's Fade / Fade Down cells parse every part of the input with the timecode parser,
    /// so "{ParameterMaster 1}" is rejected before it ever reaches the kernel. This plugin lets master
    /// expressions through for time fields; numeric parts are still parsed as time ("2s", "500ms", ...).
    /// The kernel plugin "Cuelist Fade Master" must be active too, it evaluates the expressions.
    /// </summary>
    public class LumosFadeMasterGuiPlugin : GuiPluginBase
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(nameof(LumosFadeMasterGuiPlugin));

        private const string HarmonyId = "de.tom.lumos.fademaster.gui";

        private static Harmony _harmony;

        private static MethodInfo _getFannedParts;

        private static MethodInfo _convertParts;

        private static object _timeConverter;

        private static MethodInfo _tryParseTime;

        public LumosFadeMasterGuiPlugin() : base("{9D2C7E15-4B3A-4F8E-A6D1-2E5B8C0F7A64}", "Cuelist Fade Master (GUI)")
        {
        }

        protected override void initializePlugin()
        {
            Log.Info("Initialize " + nameof(LumosFadeMasterGuiPlugin));
        }

        protected override void startupPlugin()
        {
            Log.Info("Startup " + nameof(LumosFadeMasterGuiPlugin));
            try
            {
                Type t = typeof(GuiFannedValueManager);
                _getFannedParts = AccessTools.Method(t, "getFannedParts");
                _convertParts = AccessTools.Method(t, "tryConvertFannedValue", new[]
                {
                    typeof(object[]), typeof(EUiValueType), typeof(Type), typeof(IEnumerable<Type>), typeof(string),
                    typeof(IFannedValueContainer).MakeByRefType()
                });
                _timeConverter = AccessTools.Field(t, "TC")?.GetValue(null);
                _tryParseTime = _timeConverter == null ? null : AccessTools.Method(_timeConverter.GetType(), "TryParseString");
                MethodInfo target = AccessTools.Method(t, "tryConvertFannedValue", new[]
                {
                    typeof(string), typeof(EUiValueType), typeof(Type), typeof(IEnumerable<Type>), typeof(IEnumerable<string>),
                    typeof(IFannedValueContainer).MakeByRefType()
                });
                if (_getFannedParts == null || _convertParts == null || _tryParseTime == null || target == null)
                {
                    throw new MissingMethodException("FadeMaster GUI: patch target not found (incompatible DMXControl version?)");
                }
                _harmony = _harmony ?? new Harmony(HarmonyId);
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(LumosFadeMasterGuiPlugin), nameof(TryConvert_Prefix)));
                Log.Info("FadeMaster GUI: time fields accept master expressions now");
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster GUI: master expressions in time fields not available", e);
            }

            // Freeze Change button (independent of the time field patch)
            try
            {
                _harmony = _harmony ?? new Harmony(HarmonyId);
                FreezeChangeButton.PatchSortOrder(_harmony);
                FreezeChangeButton.Instance.SetVisible(true);
                Log.Info("FadeMaster GUI: 'Freeze Change' button added below 'Freeze'");
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster GUI: unable to add 'Freeze Change' button", e);
            }
        }

        protected override void shutdownPlugin()
        {
            Log.Info("Shutdown " + nameof(LumosFadeMasterGuiPlugin));
            try
            {
                FreezeChangeButton.Instance.SetVisible(false);
            }
            catch (Exception e)
            {
                Log.Debug("FadeMaster GUI: hiding button failed: {0}", e.Message);
            }
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        // Original (private): bool tryConvertFannedValue(string input, EUiValueType uitype, Type expectedType,
        //   IEnumerable<Type> possibleContainers, IEnumerable<string> validOperators, out IFannedValueContainer container)
        private static bool TryConvert_Prefix(GuiFannedValueManager __instance, string input, EUiValueType uitype, Type expectedType,
            IEnumerable<Type> possibleContainers, IEnumerable<string> validOperators, ref IFannedValueContainer container, ref bool __result)
        {
            if (uitype != EUiValueType.FadeTime || string.IsNullOrWhiteSpace(input) || input.IndexOf('{') < 0)
            {
                return true; // untouched Lumos behaviour
            }
            try
            {
                object[] splitArgs = { input, validOperators, null };
                var rawParts = (string[])_getFannedParts.Invoke(__instance, splitArgs);
                string op = (string)splitArgs[2];
                var parts = new object[rawParts.Length];
                for (int i = 0; i < rawParts.Length; i++)
                {
                    string p = rawParts[i].Trim();
                    if (p.StartsWith("{") && p.EndsWith("}"))
                    {
                        parts[i] = p; // master expression, validated by the kernel
                        continue;
                    }
                    object[] parseArgs = BuildParseArgs(p);
                    if (!(bool)_tryParseTime.Invoke(_timeConverter, parseArgs))
                    {
                        container = null;
                        __result = false;
                        return false;
                    }
                    parts[i] = parseArgs[1].ToString();
                }
                object[] convertArgs = { parts, uitype, expectedType, possibleContainers, op, null };
                __result = (bool)_convertParts.Invoke(__instance, convertArgs);
                container = (IFannedValueContainer)convertArgs[5];
                return false;
            }
            catch (Exception e)
            {
                Log.Debug("FadeMaster GUI: unable to parse '{0}': {1}", input, e.Message);
                return true;
            }
        }

        // TryParseString(string input, out long time, ETimeScale numericInput = MILISECOND, ETimeScale outputValue = MILISECOND)
        private static object[] BuildParseArgs(string input)
        {
            ParameterInfo[] ps = _tryParseTime.GetParameters();
            var args = new object[ps.Length];
            args[0] = input;
            for (int i = 2; i < ps.Length; i++)
            {
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : Type.Missing;
            }
            return args;
        }
    }
}
