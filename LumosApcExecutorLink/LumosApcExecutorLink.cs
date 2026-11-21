using System;
using System.IO;
using Lumos.GUI.Plugin;
using LumosLIB.Kernel.Log;

namespace LumosApcExecutorLink
{
    /// <summary>
    /// GUI plugin: links AKAI APC mini (mk2) pad grids to the executor pages.
    /// Executor x.y (page x, position y) = pad in row x, column y. Pad -> executor button 1, LED = executor state.
    /// </summary>
    public class LumosApcExecutorLink : GuiPluginBase
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(nameof(LumosApcExecutorLink));

        private ApcExecutorLinker _linker;

        public LumosApcExecutorLink() : base("{6E1A4C9B-2F37-4D85-B0A2-91C3E7D5F468}", "APC Executor Link")
        {
        }

        protected override void initializePlugin()
        {
            Log.Info("Initialize " + nameof(LumosApcExecutorLink));
        }

        protected override void startupPlugin()
        {
            Log.Info("Startup " + nameof(LumosApcExecutorLink));
            try
            {
                string path = ResolveConfigPath();
                ApcLinkConfig cfg = ApcLinkConfig.LoadOrCreate(path, (msg, e) => Log.Warn(msg, e));
                Log.Info("APC Link: config {0}", path);
                _linker = new ApcExecutorLinker(cfg);
                _linker.Start();
            }
            catch (Exception e)
            {
                Log.Error("APC Link: startup failed", e);
            }
        }

        protected override void shutdownPlugin()
        {
            Log.Info("Shutdown " + nameof(LumosApcExecutorLink));
            _linker?.Dispose();
            _linker = null;
        }

        /// <summary>
        /// Config next to the DLL if present (or writable), otherwise %APPDATA%\DMXControl3\ApcExecutorLink.config.xml
        /// (Program Files is usually not writable).
        /// </summary>
        private static string ResolveConfigPath()
        {
            string local = ApcLinkConfig.DefaultPath;
            if (File.Exists(local) || IsWritable(Path.GetDirectoryName(local)))
            {
                return local;
            }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMXControl3");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, ApcLinkConfig.FileName);
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".apclink_probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
