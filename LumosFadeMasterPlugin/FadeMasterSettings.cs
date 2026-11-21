using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LumosLIB.Kernel.Log;
using LumosLIB.Kernel.Scene.Fanning.Parameter;
using LumosProtobuf;
using LumosProtobuf.Resource;
using org.dmxc.lumos.Kernel.Project;
using org.dmxc.lumos.Kernel.Resource;
using org.dmxc.lumos.Kernel.Scene.Fanning;
using org.dmxc.lumos.Kernel.Scene.Fanning.Parameter;
using org.dmxc.lumos.Kernel.SceneList;
using org.dmxc.lumos.Kernel.SceneList.ValuesStack;

namespace LumosFadeMasterPlugin
{
    public enum EFadeMasterMode
    {
        /// <summary>Plugin does nothing for this cuelist.</summary>
        Off,
        /// <summary>Cue fade time is multiplied by the master value.</summary>
        Scale,
        /// <summary>Master value replaces the cue fade time (value in seconds).</summary>
        Override
    }

    /// <summary>Fade master configuration of a single cuelist.</summary>
    public sealed class FadeMasterSetting
    {
        // Re-evaluate the master expression at most every few ms (a GO touches many items).
        private const long CacheTicks = 20 * TimeSpan.TicksPerMillisecond;

        private long _cacheTimestamp = long.MinValue;
        private double _cacheValue;
        private bool _cacheValid;

        public string CuelistId { get; }

        public string Expression { get; set; } = string.Empty;

        public EFadeMasterMode Mode { get; set; } = EFadeMasterMode.Off;

        public bool IncludeFadeOut { get; set; }

        public FadeMasterSetting(string cuelistId)
        {
            CuelistId = cuelistId;
        }

        public bool IsActive => Mode != EFadeMasterMode.Off && !string.IsNullOrWhiteSpace(Expression);

        public void InvalidateCache() => _cacheTimestamp = long.MinValue;

        public bool TryGetMasterValue(out double value)
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - _cacheTimestamp < CacheTicks)
            {
                value = _cacheValue;
                return _cacheValid;
            }
            _cacheValid = FadeMasterExpression.TryEvaluate(Expression, out _cacheValue);
            _cacheTimestamp = now;
            value = _cacheValue;
            return _cacheValid;
        }

        /// <summary>Computes the new fade time in ms. Returns false if nothing should change.</summary>
        public bool TryComputeFadeTime(long cueFadeTime, out long newFadeTime)
        {
            newFadeTime = cueFadeTime;
            if (!IsActive || !TryGetMasterValue(out double v) || double.IsNaN(v) || double.IsInfinity(v))
            {
                return false;
            }
            v = Math.Max(0.0, v);
            double result = Mode switch
            {
                EFadeMasterMode.Scale => cueFadeTime * v,
                EFadeMasterMode.Override => v * 1000.0,
                _ => cueFadeTime
            };
            // Cap at 24h to stay far away from overflow.
            newFadeTime = (long)Math.Round(Math.Min(result, 86_400_000.0));
            return true;
        }

        public string DescribeCurrentValue()
        {
            if (string.IsNullOrWhiteSpace(Expression))
            {
                return "-";
            }
            if (!FadeMasterExpression.TryEvaluate(Expression, out double v))
            {
                return "Master nicht gefunden";
            }
            return Mode switch
            {
                EFadeMasterMode.Scale => string.Format(CultureInfo.InvariantCulture, "x {0:0.###} (Cue-Fade wird multipliziert)", v),
                EFadeMasterMode.Override => string.Format(CultureInfo.InvariantCulture, "{0:0.###} s (ersetzt Cue-Fade)", Math.Max(0, v)),
                _ => string.Format(CultureInfo.InvariantCulture, "{0:0.###} (inaktiv)", v)
            };
        }
    }

    /// <summary>Parses / evaluates expressions in the Device Control syntax: {ParameterMaster 2:*1000}.</summary>
    public static class FadeMasterExpression
    {
        /// <summary>Accepts "ParameterMaster 2", "{ParameterMaster 2}", "{ParameterMaster 2:/100}".</summary>
        public static string Normalize(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return string.Empty;
            }
            string inner = expression.Trim().Trim('{', '}').Trim();
            return inner.Length == 0 ? string.Empty : "{" + inner + "}";
        }

        public static bool IsValid(string expression, out string error)
        {
            error = null;
            string normalized = Normalize(expression);
            if (normalized.Length == 0)
            {
                return true; // empty = disabled
            }
            if (FindParameter(normalized) == null)
            {
                error = $"Unbekannter Master: '{normalized}'. Beispiel: {{ParameterMaster 1}} oder {{ParameterMaster 1:/100}}";
                return false;
            }
            if (!TryEvaluate(normalized, out _))
            {
                error = $"Ausdruck '{normalized}' liefert keinen Zahlenwert.";
                return false;
            }
            return true;
        }

        public static bool TryEvaluate(string expression, out double value)
        {
            value = 0.0;
            string normalized = Normalize(expression);
            if (normalized.Length == 0)
            {
                return false;
            }
            // Never let the lookup create new masters (ParameterManager creates on demand by default!).
            if (FindParameter(normalized) == null)
            {
                return false;
            }
            try
            {
                // "s": SpeedMasters deliver their period in seconds, ParameterMasters their plain value.
                object raw = FannedValueManager.getValueFromParameter(normalized, 1, "s");
                return FannedValueManager.TryToDouble(raw, out value, "s");
            }
            catch
            {
                return false;
            }
        }

        private static IParameter FindParameter(string normalized)
        {
            string name = normalized.Trim('{', '}');
            return ParameterManager.getInstance().GetParameterInstance(name, create: false);
        }
    }

    /// <summary>Holds all cuelist settings and maps a cuelist's OutputValuesStack to its setting.</summary>
    public static class FadeMasterStore
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(typeof(FadeMasterStore));

        private const string ResourceName = "FadeMasterPlugin.xml";

        private static readonly ConcurrentDictionary<string, FadeMasterSetting> ByCuelistId = new();

        private sealed class OwnerRef
        {
            public SceneList SceneList;
        }

        // OutputValuesStack -> owning cuelist (SceneList == null: stack is not a cuelist, e.g. programmer)
        private static ConditionalWeakTable<object, OwnerRef> _stackOwners = new();

        private static readonly AccessTools.FieldRef<SceneList, IOutputValuesStack> StackRef =
            AccessTools.FieldRefAccess<SceneList, IOutputValuesStack>("_stack");

        public static FadeMasterSetting Get(string cuelistId)
        {
            return cuelistId != null && ByCuelistId.TryGetValue(cuelistId, out var s) ? s : null;
        }

        public static FadeMasterSetting GetOrCreate(SceneList sceneList)
        {
            return ByCuelistId.GetOrAdd(sceneList.ID, id => new FadeMasterSetting(id));
        }

        /// <summary>Finds the cuelist that owns an OutputValuesStack (cached).</summary>
        public static SceneList GetOwner(object stack)
        {
            if (stack == null)
            {
                return null;
            }
            if (_stackOwners.TryGetValue(stack, out OwnerRef r))
            {
                return r.SceneList;
            }
            try
            {
                foreach (SceneList sl in SceneListManager.getInstance().SceneLists.OfType<SceneList>())
                {
                    IOutputValuesStack s = StackRef(sl);
                    if (s != null)
                    {
                        _stackOwners.AddOrUpdate(s, new OwnerRef { SceneList = sl });
                    }
                }
            }
            catch (Exception e)
            {
                Log.Debug("FadeMaster: stack owner scan failed: " + e.Message);
            }
            if (_stackOwners.TryGetValue(stack, out r))
            {
                return r.SceneList;
            }
            _stackOwners.AddOrUpdate(stack, new OwnerRef()); // negative cache
            return null;
        }

        public static void Clear()
        {
            ByCuelistId.Clear();
            _stackOwners = new ConditionalWeakTable<object, OwnerRef>();
        }

        public static void Load()
        {
            Clear();
            try
            {
                var meta = new LumosResourceMetadata(ResourceName, EResourceContentType.ManagedTree);
                LumosResource r = ResourceManager.getInstance().TryLoadResource(EResourceType.Project, meta, EIOFlags.LOCAL_ONLY);
                ManagedTreeItem root = r?.ManagedData;
                if (root == null)
                {
                    return;
                }
                foreach (ManagedTreeItem item in root.GetChildren("Cuelist"))
                {
                    if (!item.hasValue<string>("ID"))
                    {
                        continue;
                    }
                    string id = item.getValue<string>("ID");
                    if (SceneListManager.getInstance().GetSceneListByID(id) is not SceneList sl)
                    {
                        continue;
                    }
                    FadeMasterSetting s = GetOrCreate(sl);
                    s.Expression = item.getValueOrDefault("Expression", string.Empty);
                    s.IncludeFadeOut = item.getValueOrDefault("IncludeFadeOut", false);
                    s.Mode = Enum.TryParse(item.getValueOrDefault("Mode", "Off"), out EFadeMasterMode m) ? m : EFadeMasterMode.Off;
                }
                Log.Info($"FadeMaster: {ByCuelistId.Count} cuelist setting(s) loaded");
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster: unable to load settings", e);
            }
        }

        public static void Save()
        {
            try
            {
                var root = new ManagedTreeItem("FadeMasterPlugin");
                foreach (FadeMasterSetting s in ByCuelistId.Values)
                {
                    if (string.IsNullOrWhiteSpace(s.Expression) && s.Mode == EFadeMasterMode.Off)
                    {
                        continue;
                    }
                    if (SceneListManager.getInstance().GetSceneListByID(s.CuelistId) == null)
                    {
                        continue; // cuelist was deleted
                    }
                    ManagedTreeItem item = root.AddChild("Cuelist");
                    item.setValue("ID", s.CuelistId);
                    item.setValue("Expression", s.Expression ?? string.Empty);
                    item.setValue("Mode", s.Mode.ToString());
                    item.setValue("IncludeFadeOut", s.IncludeFadeOut);
                }
                ResourceManager.getInstance().SaveResource(EResourceType.Project, new LumosResource(ResourceName, root));
            }
            catch (Exception e)
            {
                Log.Error("FadeMaster: unable to save settings", e);
            }
        }

        private static readonly System.Reflection.MethodInfo OnPropertyChangedMethod =
            AccessTools.Method(typeof(SceneList), "OnPropertyChanged", new[] { typeof(PropertyChangedEventArgs), typeof(bool) });

        /// <summary>Marks the cuelist (and therefore the project) as changed.</summary>
        public static void MarkDirty(SceneList sceneList, string propertyName)
        {
            try
            {
                OnPropertyChangedMethod?.Invoke(sceneList, new object[] { new PropertyChangedEventArgs(propertyName), true });
            }
            catch (Exception e)
            {
                Log.Debug("FadeMaster: unable to mark cuelist dirty: " + e.Message);
            }
        }
    }
}
