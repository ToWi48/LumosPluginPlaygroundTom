using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using LumosLIB.Tools;
using LumosLIB.Kernel.Log;
using org.dmxc.lumos.Kernel.DeviceProperties;
using org.dmxc.lumos.Kernel.Exceptions;
using org.dmxc.lumos.Kernel.Project;
using org.dmxc.lumos.Kernel.PropertyType;
using org.dmxc.lumos.Kernel.HAL;
using org.dmxc.lumos.Kernel.HAL.Handler;

// namespace org.dmxc.lumos.Kernel.HAL.Handler;
namespace LumosPluginTomExtentions.Handler;

internal sealed class ConnectingPropertiesDMXHandlerNode : AbstractDMXHandlerNode
{
	public byte DMXValue { get; private set; }

	private static readonly ILumosLog log = LumosLogger.getInstance(typeof(RectangleFadedEffect));

	private static ILumosLog Log => log;

	protected override IEnumerable<string> IllegalAttributes => base.IllegalAttributes.Except(DMXRangeWithDiscreteValue.DiscreteValueAttributes);

	protected override List<PropertyDependencyBag> PropHandleProperties => new List<PropertyDependencyBag>
	{
		new PropertyDependencyBag(null, new List<string> { DMXOffset.ToString() })
	};

	protected override IDeviceProperty SingleHandleProperty => null;

	private ConnectingPropertiesDMXHandlerNode()
	{
	}

	protected override PropertyHandlerWorker getPropertyWorker(IDeviceProperty prop)
	{
		return null;
	}

	public override void parseXmlNode(XmlNode ddfnode)
	{
		Log.Info("Hey");

		base.parseXmlNode(ddfnode);
		if (DMXOffset == -1)
		{
			throw XmlParseExceptionFactory.MissingAttribute("dmxchannel", ddfnode);
		}
		string a = ddfnode.GetAttributeIgnoreCase(DMXRangeWithDiscreteValue.VAL_ATTRIBUTE);
		if (string.IsNullOrEmpty(a))
		{
			a = ddfnode.GetAttributeIgnoreCase(DMXRangeWithDiscreteValue.VALUE_ATTRIBUTE);
		}
		if (!string.IsNullOrEmpty(a))
		{
			try
			{
				DMXValue = Convert.ToByte(a);
				return;
			}
			catch (Exception)
			{
				throw XmlParseExceptionFactory.InvalidAttribute(DMXRangeWithDiscreteValue.VAL_ATTRIBUTE, a, ddfnode);
			}
		}
		throw XmlParseExceptionFactory.MissingAttribute(DMXRangeWithDiscreteValue.VAL_ATTRIBUTE, ddfnode);
	}

	protected override IPropertyType getPropTypeInstance(IDeviceProperty prop)
	{
		return null;
	}

	protected override void initializeHandler()
	{
		base.initializeHandler();
		setDMXValue(DMXOffset, (byte)((double)DMXValue + 5), null, DeviceManager.getInstance());
	}

	public override HALHandleContext getLastHandleContext(string dependency)
	{
		return base.getLastHandleContext(dependency) ?? new HALHandleContext(null, null, isDynamic: false, DeviceManager.getInstance(), null);
	}
}