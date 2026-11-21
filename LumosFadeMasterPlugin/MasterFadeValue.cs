using System;
using System.Linq;
using LumosLIB.Tools;
using org.dmxc.lumos.Kernel.Scene.Fanning;

namespace LumosFadeMasterPlugin
{
    /// <summary>
    /// Wraps a cue fade value that references masters, e.g. "{ParameterMaster 1}" or "500 > {ParameterMaster 2:/10}".
    ///
    /// The kernel reads fade values numerically (Convert.ToInt64 on getFannedValues / ToObjectArray),
    /// so towards the kernel this value always delivers milliseconds, evaluated at call time.
    /// The GUI and the project file get <see cref="RawValue"/> (with the master strings) via patches
    /// in FannedValueManager.
    ///
    /// Unit: a master value is interpreted as SECONDS (ParameterMaster value, SpeedMaster period),
    /// plain numbers in the fanning are milliseconds as usual.
    ///
    /// Intentionally no public parameterless constructor: otherwise the kernel's assembly scan would try
    /// to register this type as a fanning operator.
    /// </summary>
    [Serializable]
    public sealed class MasterFadeValue : IFannedValue
    {
        private readonly string _op;

        private readonly object[] _rawParts;

        [NonSerialized]
        private long _lastGoodSingleMs;

        public MasterFadeValue(IFannedValue raw)
        {
            _op = raw.Operators?.FirstOrDefault() ?? string.Empty;
            _rawParts = raw.ToObjectArray().ToArray();
        }

        /// <summary>Fade value with the master expressions (for GUI + saving).</summary>
        public IFannedValue RawValue => FannedValueManager.getInstance().getValueInstance(_op, (object[])_rawParts.Clone());

        /// <summary>True for the common case: the fade field contains exactly one master expression.</summary>
        public bool IsSingleExpression => _rawParts.Length == 1 && IsParameter(_rawParts[0]);

        public static bool ContainsParameter(IFannedValue value)
        {
            try
            {
                return value != null && value.ToObjectArray().Any(IsParameter);
            }
            catch
            {
                return false;
            }
        }

        public static IFannedValue WrapIfNeeded(IFannedValue value)
        {
            if (value == null || value is MasterFadeValue || !ContainsParameter(value))
            {
                return value;
            }
            return new MasterFadeValue(value);
        }

        private static bool IsParameter(object o) => o is string s && LumosTools.IsParameter(s);

        /// <summary>Current value of a single-expression fade in ms (live).</summary>
        public bool TryEvaluateSingleMs(out long ms)
        {
            ms = _lastGoodSingleMs;
            if (!IsSingleExpression)
            {
                return false;
            }
            if (FadeMasterExpression.TryEvaluate((string)_rawParts[0], out double seconds) && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
            {
                ms = ToMs(seconds);
                _lastGoodSingleMs = ms;
            }
            return true;
        }

        private static long ToMs(double seconds) => (long)Math.Round(Math.Clamp(seconds, 0.0, 86_400.0) * 1000.0);

        private IFannedValue Evaluated()
        {
            object[] parts = new object[_rawParts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                object p = _rawParts[i];
                if (IsParameter(p))
                {
                    parts[i] = FadeMasterExpression.TryEvaluate((string)p, out double s) ? (double)ToMs(s) : 0.0;
                }
                else
                {
                    parts[i] = FannedValueManager.TryToDouble(p, out double d) ? d : 0.0;
                }
            }
            return FannedValueManager.getInstance().getValueInstance(_op, parts)
                   ?? new StaticFannedValue(parts[0]);
        }

        private static object[] ToLongs(object[] values)
        {
            return values.Select(v => (object)(FannedValueManager.TryToDouble(v, out double d) ? (long)Math.Round(d) : 0L)).ToArray();
        }

        // ---------------------------------------------------------------- IFannedValue (kernel view: ms)

        public string[] Operators => new[] { _op };

        public int ValuesCount => Evaluated().ValuesCount;

        public object[] getFannedValues(int count) => ToLongs(Evaluated().getFannedValues(count));

        public object[] ToObjectArray() => ToLongs(Evaluated().ToObjectArray());

        public int CompareTo(object obj)
        {
            long mine = Convert.ToInt64(getFannedValues(1)[0]);
            if (obj is IFannedValue other)
            {
                return mine.CompareTo(FannedValueManager.ToInt(other.getFannedValues(1)[0]));
            }
            return 1;
        }

        public override bool Equals(object obj)
        {
            return obj is MasterFadeValue o && o._op == _op && o._rawParts.SequenceEqual(_rawParts);
        }

        public override int GetHashCode()
        {
            return _rawParts.Aggregate(_op.GetHashCode(), (h, p) => h * 31 + (p?.GetHashCode() ?? 0));
        }

        public override string ToString()
        {
            return string.Join(" " + _op + " ", _rawParts.Select(p => p?.ToString()));
        }
    }
}
