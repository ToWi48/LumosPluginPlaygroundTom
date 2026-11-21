using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumos.GUI.Executor;
using Lumos.GUI.Facade.Executor;
using Lumos.GUI.Facade.SceneList;
using Lumos.GUI.MIDI;
using LumosLIB.Kernel.Log;
using LumosPluginShared;
using org.dmxc.lumos.Kernel.Executor;

namespace LumosApcExecutorLink
{
    /// <summary>
    /// Links the APC mini pads to the executors:
    ///   row    = executor page (sorted by page number, page 1 = bottom row by default)
    ///   column = executor position inside the page (1..8 on APC #1, 9..16 on APC #2, ...)
    /// Pad press/release -> executor button 1 down/up.
    /// LEDs: empty executor = Off, member assigned = Assigned, cuelist running (or button 1 active for other members) = Active.
    /// Everything is recalculated continuously, so any executor assignment shows up on the pads automatically.
    /// </summary>
    internal sealed class ApcExecutorLinker : IDisposable
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(nameof(ApcExecutorLinker));

        private const byte NoteOff = 0x80;
        private const byte NoteOn = 0x90;

        private enum ELed : byte
        {
            Unknown,
            Off,
            Assigned,
            Active
        }

        private readonly ApcLinkConfig _cfg;

        private readonly object _lock = new object();

        // device index -> MIDI input / output currently used
        private readonly Dictionary<int, MidiInput> _inputs = new Dictionary<int, MidiInput>();
        private readonly Dictionary<int, MidiOutput> _outputs = new Dictionary<int, MidiOutput>();
        private readonly Dictionary<MidiInput, EventHandler<MidiEventArgs>> _handlers = new Dictionary<MidiInput, EventHandler<MidiEventArgs>>();

        // current pad -> executor mapping, key = (device, note)
        private volatile Dictionary<(int dev, int note), IExecutorFacade> _padMap = new Dictionary<(int, int), IExecutorFacade>();

        // pads held down -> executor that received the press (release goes to the same executor)
        private readonly ConcurrentDictionary<(int dev, int note), IExecutorFacade> _pressed = new ConcurrentDictionary<(int, int), IExecutorFacade>();

        // last LED message (status | velocity << 8) sent per pad = the pads this plugin currently owns
        private readonly Dictionary<(int dev, int note), int> _sent = new Dictionary<(int, int), int>();

        // pads that have a rule in a DMXControl MIDI rule set -> the UI rule wins, the plugin leaves them alone
        private volatile HashSet<(int dev, int note)> _reserved = new HashSet<(int, int)>();

        private Timer _timer;
        private int _ticking;
        private DateTime _lastFullResend = DateTime.MinValue;
        private bool _disposed;

        public ApcExecutorLinker(ApcLinkConfig cfg)
        {
            _cfg = cfg;
        }

        public void Start()
        {
            _timer = new Timer(_ => Tick(), null, 500, Math.Max(20, _cfg.RefreshMs));
            Log.Info(_cfg.AutoDetect
                    ? "APC Link started (auto detect '{0}', {1} device slot(s))"
                    : "APC Link started (fixed names, {1} device slot(s))",
                _cfg.DeviceNamePattern, _cfg.SlotCount);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            lock (_lock)
            {
                // release held buttons, switch LEDs off, detach
                foreach (var kv in _pressed)
                {
                    SafeButton(kv.Value, false);
                }
                _pressed.Clear();
                // switch off only the pads this plugin lit, everything else belongs to the UI rules
                foreach (var key in _sent.Keys.ToList())
                {
                    SendLed(key.dev, key.note, Look(_cfg.Off));
                }
                _sent.Clear();
                foreach (var kv in _handlers)
                {
                    kv.Key.MessageReceived -= kv.Value;
                }
                _handlers.Clear();
                _inputs.Clear();
                _outputs.Clear();
            }
        }

        // ------------------------------------------------------------------ periodic update

        private void Tick()
        {
            if (_disposed || Interlocked.Exchange(ref _ticking, 1) == 1)
            {
                return;
            }
            try
            {
                lock (_lock)
                {
                    BindDevices();
                    _reserved = BuildReserved();
                    _padMap = BuildPadMap();
                    UpdateLeds();
                }
            }
            catch (Exception e)
            {
                Log.Debug("APC Link tick failed: {0}", e.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        /// <summary>(Re-)binds MIDI devices; the MIDI window can recreate the device objects at any time.</summary>
        private void BindDevices()
        {
            DeviceInformation info = MIDIManager.getInstance().DeviceInfo;
            if (info?.InputDevices == null || info.OutputDevices == null)
            {
                return;
            }
            List<(MidiInput inp, MidiOutput outp)> slots = ResolveDevices(info);
            for (int dev = 0; dev < _cfg.SlotCount; dev++)
            {
                MidiInput inp = dev < slots.Count ? slots[dev].inp : null;
                _inputs.TryGetValue(dev, out MidiInput oldIn);
                if (!ReferenceEquals(inp, oldIn))
                {
                    if (oldIn != null && _handlers.TryGetValue(oldIn, out var oldHandler))
                    {
                        oldIn.MessageReceived -= oldHandler;
                        _handlers.Remove(oldIn);
                    }
                    _inputs.Remove(dev);
                    if (inp != null)
                    {
                        int devIndex = dev;
                        EventHandler<MidiEventArgs> h = (s, e) => OnMidi(devIndex, e.m);
                        inp.MessageReceived += h;
                        _handlers[inp] = h;
                        _inputs[dev] = inp;
                        Log.Info("APC Link: input {0} -> executors {1}-{2}", inp.DeviceName, dev * _cfg.Columns + 1, (dev + 1) * _cfg.Columns);
                    }
                }
                MidiOutput outp = dev < slots.Count ? slots[dev].outp : null;
                _outputs.TryGetValue(dev, out MidiOutput oldOut);
                if (!ReferenceEquals(outp, oldOut))
                {
                    _outputs.Remove(dev);
                    if (outp != null)
                    {
                        _outputs[dev] = outp;
                        ForgetSent(dev); // new device object -> send everything again
                        Log.Info("APC Link: output {0} -> executors {1}-{2}", outp.DeviceName, dev * _cfg.Columns + 1, (dev + 1) * _cfg.Columns);
                    }
                }
            }
        }

        // "2 - APC mini mk2" -> number 2, base "APC mini mk2"
        private static readonly System.Text.RegularExpressions.Regex WindowsPrefix =
            new System.Text.RegularExpressions.Regex(@"^\s*(\d+)\s*-\s*(.*)$");

        /// <summary>Device slots from left to right (index = slot).</summary>
        private List<(MidiInput inp, MidiOutput outp)> ResolveDevices(DeviceInformation info)
        {
            List<MidiInput> inputs = info.InputDevices.ToList();
            List<MidiOutput> outputs = info.OutputDevices.ToList();
            if (!_cfg.AutoDetect)
            {
                return _cfg.Devices
                    .Select(d => (FindByName(inputs, d.InputName), FindByName(outputs, d.OutputName)))
                    .ToList();
            }
            // Same port name for input and output, e.g. "3 - APC mini mk2". "MIDIIN2 (...)" / "MIDIOUT2 (...)" are the
            // controller's second port and are skipped.
            var names = inputs.Select(i => i.DeviceName).Concat(outputs.Select(o => o.DeviceName))
                .Where(IsControllerPort)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(PortNumber)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_cfg.ReverseDeviceOrder)
            {
                names.Reverse();
            }
            return names
                .Take(_cfg.SlotCount)
                .Select(n => (FindByName(inputs, n), FindByName(outputs, n)))
                .ToList();
        }

        private bool IsControllerPort(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(_cfg.DeviceNamePattern))
            {
                return false;
            }
            string baseName = WindowsPrefix.Match(name) is var m && m.Success ? m.Groups[2].Value : name;
            return baseName.Trim().StartsWith(_cfg.DeviceNamePattern.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static int PortNumber(string name)
        {
            var m = WindowsPrefix.Match(name ?? string.Empty);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 1;
        }

        private static T FindByName<T>(IEnumerable<T> devices, string name) where T : MidiDev
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            List<T> list = devices.ToList();
            return list.FirstOrDefault(d => string.Equals(d.DeviceName?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private Dictionary<(int dev, int note), IExecutorFacade> BuildPadMap()
        {
            var map = new Dictionary<(int, int), IExecutorFacade>();
            List<IExecutorPageFacade> pages = ExecutorManager.getInstance().ExecutorPages
                .OrderBy(p => p.PageNumber)
                .Skip(Math.Max(0, _cfg.PageOffset))
                .Take(_cfg.Rows)
                .ToList();
            int maxExecutors = _cfg.Columns * _cfg.SlotCount;
            for (int pageIdx = 0; pageIdx < pages.Count; pageIdx++)
            {
                // APC note layout: note 0 = bottom left, row-major upwards
                int row = _cfg.FirstPageAtBottom ? pageIdx : _cfg.Rows - 1 - pageIdx;
                // cache only - see ExecutorCache why GUIExecutors must not be used here
                List<IExecutorFacade> executors = ExecutorCache.GetExecutors(pages[pageIdx]).Take(maxExecutors).ToList();
                for (int pos = 0; pos < executors.Count; pos++)
                {
                    if (executors[pos] == null)
                    {
                        continue; // not loaded by the GUI yet
                    }
                    // column over the whole field (all devices side by side, device 1 = leftmost)
                    int globalCol = _cfg.ExecutorsRightToLeft ? maxExecutors - 1 - pos : pos;
                    int dev = globalCol / _cfg.Columns;
                    int note = row * _cfg.Columns + globalCol % _cfg.Columns;
                    map[(dev, note)] = executors[pos];
                }
            }
            return map;
        }

        /// <summary>
        /// Pads that are configured in a DMXControl MIDI rule set (learned via the UI) for one of our devices.
        /// Only the 8x8 grid is relevant; side buttons and faders are never touched by this plugin anyway.
        /// </summary>
        private HashSet<(int dev, int note)> BuildReserved()
        {
            var reserved = new HashSet<(int, int)>();
            try
            {
                MidiInformation info = MIDIManager.getInstance().MidiInfo;
                if (info?.RuleSets == null)
                {
                    return reserved;
                }
                foreach (RuleSet rs in info.RuleSets.ToList())
                {
                    int dev = _inputs.FirstOrDefault(kv => ReferenceEquals(kv.Value, rs.InputDevice)).Key;
                    bool match = rs.InputDevice != null && _inputs.TryGetValue(dev, out MidiInput i) && ReferenceEquals(i, rs.InputDevice);
                    if (!match)
                    {
                        int outDev = _outputs.FirstOrDefault(kv => ReferenceEquals(kv.Value, rs.OutputDevice)).Key;
                        match = rs.OutputDevice != null && _outputs.TryGetValue(outDev, out MidiOutput o) && ReferenceEquals(o, rs.OutputDevice);
                        dev = outDev;
                    }
                    if (!match)
                    {
                        continue;
                    }
                    foreach (DeviceRule rule in rs.Rules.ToList())
                    {
                        foreach (MidiMessage m in RuleMessages(rule))
                        {
                            bool isNote = m.message == NoteOn || m.message == NoteOff;
                            if (isNote && m.data1 < _cfg.Rows * _cfg.Columns)
                            {
                                reserved.Add((dev, m.data1));
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // rule sets are edited on the UI thread; just try again next tick
                Log.Debug("APC Link: reading rule sets failed: {0}", e.Message);
                return _reserved;
            }
            return reserved;
        }

        private static IEnumerable<MidiMessage> RuleMessages(DeviceRule rule)
        {
            switch (rule)
            {
                case ButtonRule b:
                    return new[] { b.EnableMessage, b.DisableMessage, b.EnabledBacktrack, b.DisabledBacktrack };
                case EncoderRule e:
                    return new[] { e.CWMessage, e.CCWMessage };
                case SliderRule s:
                    return new[] { s.SliderMessage };
                default:
                    return Enumerable.Empty<MidiMessage>();
            }
        }

        private void UpdateLeds()
        {
            bool full = (DateTime.UtcNow - _lastFullResend).TotalMilliseconds >= _cfg.FullResendMs;
            if (full)
            {
                _lastFullResend = DateTime.UtcNow;
            }
            int offLook = Look(_cfg.Off);
            HashSet<(int dev, int note)> reserved = _reserved;
            foreach (int dev in _outputs.Keys)
            {
                for (int note = 0; note < _cfg.Rows * _cfg.Columns; note++)
                {
                    var key = (dev, note);
                    _padMap.TryGetValue(key, out IExecutorFacade ex);
                    bool owned = _sent.TryGetValue(key, out int have);
                    if (reserved.Contains(key))
                    {
                        _sent.Remove(key); // UI rule owns this pad (incl. its LED)
                        continue;
                    }
                    if (ex == null || string.IsNullOrEmpty(ex.ExecutorMemberID))
                    {
                        // empty executor: clear our LED once, then leave the pad to the UI
                        if (owned)
                        {
                            if (have != offLook)
                            {
                                SendLed(dev, note, offLook);
                            }
                            _sent.Remove(key);
                        }
                        continue;
                    }
                    int want = GetLook(ex);
                    if (full || !owned || have != want)
                    {
                        if (SendLed(dev, note, want))
                        {
                            _sent[key] = want;
                        }
                    }
                }
            }
        }

        private static int Look(LedStyle style) => Look(style.StatusByte, style.Data2);

        private static int Look(byte status, int velocity) => status | ((velocity & 0x7F) << 8);

        /// <summary>LED message for an executor; the idle look takes the executor colour.</summary>
        private int GetLook(IExecutorFacade ex)
        {
            switch (GetLed(ex))
            {
                case ELed.Active:
                    return Look(_cfg.Active);
                case ELed.Assigned:
                    return Look(_cfg.Assigned.StatusByte, IdleVelocity(ex));
                default:
                    return Look(_cfg.Off);
            }
        }

        /// <summary>Executor colour mapped to the APC palette, fallback: configured Assigned colour.</summary>
        private int IdleVelocity(IExecutorFacade ex)
        {
            if (_cfg.UseExecutorColor)
            {
                try
                {
                    System.Windows.Media.Color c = ex.Color;
                    if (c.A > 0 && (c.R | c.G | c.B) != 0)
                    {
                        return ApcPalette.Nearest(c.R, c.G, c.B);
                    }
                }
                catch (Exception e)
                {
                    Log.Debug("APC Link: no colour for {0}: {1}", ex?.Name, e.Message);
                }
            }
            return _cfg.Assigned.Data2;
        }

        private ELed GetLed(IExecutorFacade ex)
        {
            if (ex == null)
            {
                return ELed.Off;
            }
            string wrapperId = ex.ExecutorMemberID;
            if (string.IsNullOrEmpty(wrapperId))
            {
                return ELed.Off;
            }
            IGUIExecutorMember member = ExecutorCache.GetMember(ex);
            if (member is ISceneListFacade cuelist)
            {
                return cuelist.State.ToString() != "Stopped" ? ELed.Active : ELed.Assigned;
            }
            if (member == null)
            {
                return ELed.Assigned; // not cached by the GUI yet
            }
            // macro, master, timecode, cuelist group: show button 1 state
            return ex.Button1Active ? ELed.Active : ELed.Assigned;
        }

        private void ForgetSent(int dev)
        {
            foreach (var key in _sent.Keys.Where(k => k.dev == dev).ToList())
            {
                _sent.Remove(key);
            }
        }

        /// <param name="look">status | velocity &lt;&lt; 8, see <see cref="Look(byte, int)"/></param>
        private bool SendLed(int dev, int note, int look)
        {
            if (!_outputs.TryGetValue(dev, out MidiOutput outp) || outp?.OutputDevice == null)
            {
                return false;
            }
            int raw = (look & 0xFF) | ((note & 0x7F) << 8) | (((look >> 8) & 0x7F) << 16);
            try
            {
                outp.OutputDevice.Send(raw);
                return true;
            }
            catch (Exception e)
            {
                Log.Debug("APC Link: send to {0} failed: {1}", outp.DeviceName, e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ pad input

        private void OnMidi(int dev, MidiMessage m)
        {
            if (_disposed)
            {
                return;
            }
            // DMXControl splits status into channel (1-based) + message (status with channel nibble removed)
            byte type = m.message;
            int note = m.data1;
            if (note >= _cfg.Rows * _cfg.Columns)
            {
                return; // not a grid pad (track / scene buttons, fader)
            }
            var key = (dev, note);
            bool press = type == NoteOn && m.data2 > 0;
            bool release = type == NoteOff || (type == NoteOn && m.data2 == 0);
            if (press)
            {
                if (_reserved.Contains(key))
                {
                    return; // pad is assigned via a MIDI rule in the UI
                }
                if (_padMap.TryGetValue(key, out IExecutorFacade ex) && ex != null && !string.IsNullOrEmpty(ex.ExecutorMemberID))
                {
                    _pressed[key] = ex;
                    SafeButton(ex, true);
                }
            }
            else if (release)
            {
                if (_pressed.TryRemove(key, out IExecutorFacade ex))
                {
                    SafeButton(ex, false);
                }
            }
        }

        private static void SafeButton(IExecutorFacade ex, bool active)
        {
            try
            {
                ex.button1(active);
            }
            catch (Exception e)
            {
                Log.Debug("APC Link: button1 on {0} failed: {1}", ex?.Name, e.Message);
            }
        }
    }
}
