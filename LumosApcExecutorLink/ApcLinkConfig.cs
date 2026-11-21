using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace LumosApcExecutorLink
{
    /// <summary>One LED look, in the notation of the DMXControl MIDI rule editor ("Channel:Message D1 D2").</summary>
    public class LedStyle
    {
        /// <summary>"Channel" field of the rule editor (e.g. 150).</summary>
        public int Channel { get; set; }

        /// <summary>"Message" field of the rule editor (e.g. 0).</summary>
        public int Message { get; set; }

        /// <summary>"Data2" = velocity = colour index on the APC mini mk2.</summary>
        public int Data2 { get; set; }

        /// <summary>Status byte actually sent (DMXControl sends channel + message - 1).</summary>
        public byte StatusByte => (byte)(Channel + Message - 1);

        public LedStyle()
        {
        }

        public LedStyle(int channel, int message, int data2)
        {
            Channel = channel;
            Message = message;
            Data2 = data2;
        }
    }

    /// <summary>One APC mini = 8 executor columns.</summary>
    public class ApcDevice
    {
        /// <summary>Name of the MIDI input as shown in the DMXControl MIDI window.</summary>
        public string InputName { get; set; }

        /// <summary>Name of the MIDI output as shown in the DMXControl MIDI window.</summary>
        public string OutputName { get; set; }
    }

    /// <summary>Configuration, stored as XML next to the plugin DLL (created with defaults on first start).</summary>
    public class ApcLinkConfig
    {
        public const string FileName = "ApcExecutorLink.config.xml";

        /// <summary>
        /// true: find the controllers by <see cref="DeviceNamePattern"/>, ignoring the "2 - " / "3 - " prefix Windows adds
        /// when a device is re-plugged. <see cref="Devices"/> is ignored then.
        /// </summary>
        public bool AutoDetect { get; set; } = true;

        /// <summary>Part of the MIDI port name that identifies the controller.</summary>
        public string DeviceNamePattern { get; set; } = "APC mini mk2";

        /// <summary>Number of controllers side by side (columns = MaxDevices * Columns). Executors beyond stay unmapped.</summary>
        public int MaxDevices { get; set; } = 2;

        /// <summary>Auto detect sorts by the Windows number (no number = 1). true = swap the order (physically reversed).</summary>
        public bool ReverseDeviceOrder { get; set; }

        /// <summary>Only used with AutoDetect = false: devices from left to right by exact port name.</summary>
        public List<ApcDevice> Devices { get; set; } = new List<ApcDevice>();

        /// <summary>Pads per row on one device (APC mini: 8).</summary>
        public int Columns { get; set; } = 8;

        /// <summary>Rows per device (APC mini: 8) = number of executor pages shown.</summary>
        public int Rows { get; set; } = 8;

        /// <summary>true: page 1 = bottom row (APC note 0 = bottom left), false: page 1 = top row.</summary>
        public bool FirstPageAtBottom { get; set; } = false;

        /// <summary>true: executor 1 = rightmost column of the whole field (last device), counting to the left.</summary>
        public bool ExecutorsRightToLeft { get; set; } = false;

        /// <summary>Executor pages are sorted by page number; this skips the first N pages (e.g. 8 = show pages 9-16).</summary>
        public int PageOffset { get; set; }

        /// <summary>Pad of an empty executor.</summary>
        public LedStyle Off { get; set; } = new LedStyle(1, 144, 0);

        /// <summary>Idle pad colour = executor colour (mapped to the APC palette). Assigned.Data2 is the fallback.</summary>
        public bool UseExecutorColor { get; set; } = true;

        /// <summary>"Grundeinstellung": executor has a member, cuelist not running (rule: DisabledBacktrack 150:0 D2:9).</summary>
        public LedStyle Assigned { get; set; } = new LedStyle(150, 0, 9);

        /// <summary>"Aktiveinstellung": cuelist running / button active (rule: EnabledBacktrack 158:0 D2:3).</summary>
        public LedStyle Active { get; set; } = new LedStyle(158, 0, 3);

        /// <summary>LED refresh interval.</summary>
        public int RefreshMs { get; set; } = 100;

        /// <summary>All LEDs are re-sent periodically (device replugged, other rules overwrote LEDs).</summary>
        public int FullResendMs { get; set; } = 5000;

        public static ApcLinkConfig CreateDefault()
        {
            return new ApcLinkConfig();
        }

        /// <summary>Number of device slots side by side.</summary>
        public int SlotCount => AutoDetect ? Math.Max(1, MaxDevices) : Math.Max(1, Devices.Count);

        public static string DefaultPath =>
            Path.Combine(Path.GetDirectoryName(typeof(ApcLinkConfig).Assembly.Location) ?? ".", FileName);

        public static ApcLinkConfig LoadOrCreate(string path, Action<string, Exception> logError)
        {
            var serializer = new XmlSerializer(typeof(ApcLinkConfig));
            try
            {
                if (File.Exists(path))
                {
                    using (var s = File.OpenRead(path))
                    {
                        var cfg = (ApcLinkConfig)serializer.Deserialize(s);
                        if (cfg.AutoDetect || cfg.Devices.Count > 0)
                        {
                            return cfg;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logError("APC Link: unable to read " + path + ", using defaults", e);
            }
            ApcLinkConfig def = CreateDefault();
            try
            {
                using (var s = File.Create(path))
                {
                    serializer.Serialize(s, def);
                }
            }
            catch (Exception e)
            {
                logError("APC Link: unable to write default config " + path, e);
            }
            return def;
        }
    }
}
