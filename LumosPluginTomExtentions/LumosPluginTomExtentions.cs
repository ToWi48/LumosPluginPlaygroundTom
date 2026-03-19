using LumosLIB.Kernel.Log;
using org.dmxc.lumos.Kernel.Input.v2;
using org.dmxc.lumos.Kernel.MainSwitch;
using org.dmxc.lumos.Kernel.Plugin;
using org.dmxc.lumos.Kernel.Resource;
using LumosPluginTomExtentions.Handler;
using org.dmxc.lumos.Kernel.HAL.Handler;
using org.dmxc.lumos.Kernel.HAL;
using System;
using org.dmxc.lumos.Kernel.DeviceProperties;

namespace LumosPluginTomExtentions
{
    public class LumosPluginTomExtentions : KernelPluginBase
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(typeof(LumosPluginTomExtentions));

        //PLEASE CHANGE GUID AND NAME TO PREVENT CONFLICTS
        public LumosPluginTomExtentions() : base("{915DF045-5B8C-409C-A96C-8F73D5C69C1C}", "Toms Demo Plugin")
        {
        }

        protected override void initializePlugin()
        {
            Log.Info("Initialize " + nameof(LumosPluginTomExtentions));
            // MainSwitchManager.getInstance().RegisterMainSwitch(MainSwitchTemplate.getInstance());
            HandlerFactory.getInstance().registerHandlerNode<ConnectingPropertiesDMXHandlerNode>("tom");
        }

        protected override void startupPlugin()
        {
            Log.Info("Startup " + nameof(LumosPluginTomExtentions));

            // var im = InputManager.getInstance();
            // try
            // {
            //     im.RegisterSource(new SourceTemplate("915DF04"));

            //     im.RegisterSink(new SinkTemplate("8F73D5C69C1C"));
            // }
            // catch (Exception ex)
            // {
            //     Log.Error(string.Empty, ex);
            // }
        }

        protected override void shutdownPlugin()
        {
            Log.Info("Shutdown " + nameof(LumosPluginTomExtentions));
        }

        public override void loadProject(LumosIOContext context)
        {
            base.loadProject(context);
            Log.Info("LoadProject " + nameof(LumosPluginTomExtentions));

            // IPropertyHandlerNode goboWheelInstance = HandlerFactory.getInstance().getHandlerInstance("gobowheel");

            // Log.Info(Convert.ToString(goboWheelInstance.getCurrentValue(GobowheelProperty.)));

            // HandlerFactory.getInstance().getHandlerInstance("dimmer").
            // ref int alias = ref original;
        }

        public override void saveProject(LumosIOContext context)
        {
            base.saveProject(context);
            Log.Info("SaveProject " + nameof(LumosPluginTomExtentions));
        }

        public override void closeProject(LumosIOContext context)
        {
            base.closeProject(context);
            Log.Info("CloseProject " + nameof(LumosPluginTomExtentions));
        }
    }
}