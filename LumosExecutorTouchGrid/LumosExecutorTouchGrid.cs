using System;
using System.Linq;
using System.Windows.Forms;
using Lumos.GUI;
using Lumos.GUI.Plugin;
using LumosLIB.Kernel.Log;

namespace LumosExecutorTouchGrid
{
    /// <summary>
    /// GUI plugin: adds the window "Executor Touch" (menu Windows). All executor pages as rows, all executors
    /// as touch fields; pressing a field triggers button 1 of the executor.
    /// </summary>
    public class LumosExecutorTouchGrid : GuiPluginBase
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(nameof(LumosExecutorTouchGrid));

        private ExecutorTouchGridWindow _window;

        public LumosExecutorTouchGrid() : base("{B4F81D2E-7C35-4A96-8E0B-3D5C9A1F6E27}", "Executor Touch Grid")
        {
        }

        protected override void initializePlugin()
        {
            Log.Info("Initialize " + nameof(LumosExecutorTouchGrid));
        }

        protected override void startupPlugin()
        {
            Log.Info("Startup " + nameof(LumosExecutorTouchGrid));
            OnUiThread(() =>
            {
                try
                {
                    WindowManager wm = WindowManager.getInstance();
                    // the window may already exist (restored from the saved window layout)
                    _window = wm.OpenGuiWindows.OfType<ExecutorTouchGridWindow>().FirstOrDefault() ?? new ExecutorTouchGridWindow();
                    try
                    {
                        wm.AddWindow(_window);
                    }
                    catch (Exception e)
                    {
                        Log.Debug("TouchGrid: window already registered: {0}", e.Message);
                    }
                    Log.Info("TouchGrid: window 'Executor Touch' registered (menu Windows)");
                }
                catch (Exception e)
                {
                    Log.Error("TouchGrid: unable to create window", e);
                }
            });
        }

        protected override void shutdownPlugin()
        {
            Log.Info("Shutdown " + nameof(LumosExecutorTouchGrid));
            ExecutorTouchGridWindow w = _window;
            _window = null;
            if (w == null)
            {
                return;
            }
            OnUiThread(() =>
            {
                try
                {
                    WindowManager.getInstance().RemoveWindow(w);
                    w.Close();
                    w.Dispose();
                }
                catch (Exception e)
                {
                    Log.Debug("TouchGrid: closing window failed: {0}", e.Message);
                }
            });
        }

        private static void OnUiThread(Action a)
        {
            Form main = WindowManager.getInstance().MainFormWindow;
            if (main != null && main.IsHandleCreated && main.InvokeRequired)
            {
                main.BeginInvoke(a);
            }
            else
            {
                a();
            }
        }
    }
}
