// Lumos, Version=3.3.0.0, Culture=neutral, PublicKeyToken=null
// org.dmxc.lumos.Kernel.PropertyValue.Effect.CircleEffect
using System;
using System.Collections.Generic;
using Lumos.GUI.UICustomization;
using LumosLIB.Kernel.Log;
using LumosLIB.Kernel.Scene.Fanning;
using org.dmxc.lumos.Kernel.PropertyType;
using org.dmxc.lumos.Kernel.PropertyValue.Attachable;
using org.dmxc.lumos.Kernel.PropertyValue.Effect;
using org.dmxc.lumos.Kernel.Scene.Fanning;

public class CircleEffect : Abstract2DFrequentFunctionEffect
{
	private static readonly ILumosLog log = LumosLogger.getInstance(typeof(CircleEffect));

	public override string Name => "TomCircle";

	protected override ILumosLog Log => log;

	[UpdateParameter(true, false)]
	public object Frequency
	{
		get
		{
			return FrequencyX;
		}
		set
		{
			FrequencyX = value;
			FrequencyY = value;
		}
	}

	[UpdateParameter(true, false)]
	public object Phase
	{
		get
		{
			return PhaseX;
		}
		set
		{
			PhaseX = value;
			PhaseY = value;
		}
	}

	protected override IEnumerable<AttachableParameter> ParametersInternal
	{
		get
		{
			AttachableParameter frequencyParameter = new AttachableParameter("Frequency", null, typeof(NumericFannedValue))
			{
				LowerBound = 0.0,
				UpperBound = 15.0,
				ValueType = EUiValueType.Speed
			};
			Position lower = new Position(-90.0, -90.0);
			Position upper = new Position(90.0, 90.0);
			if (base.UsingPropertyContext != null && base.UsingPropertyContext.ValueType == typeof(Position) && base.UsingPropertyContext.HasFilterBounds)
			{
				lower = (Position)base.UsingPropertyContext.LowerBound;
				upper = (Position)base.UsingPropertyContext.UpperBound;
			}
			return new List<AttachableParameter>
			{
				new AttachableParameter("Amplitude X", null, typeof(NumericFannedValue))
				{
					LowerBound = lower.Pan,
					UpperBound = upper.Pan
				},
				new AttachableParameter("Amplitude Y", null, typeof(NumericFannedValue))
				{
					LowerBound = lower.Tilt,
					UpperBound = upper.Tilt
				},
				frequencyParameter,
				new AttachableParameter("Phase", "°", typeof(NumericFannedValue))
				{
					LowerBound = 0.0,
					UpperBound = 1080.0,
					ValueType = EUiValueType.Degree
				}
			};
		}
	}

	public override double PeriodicTime
	{
		get
		{
			try
			{
				return 1000.0 / FannedValueManager.ToDouble(Frequency);
			}
			catch
			{
				return 0.0;
			}
		}
	}

	public CircleEffect()
		: this(100.0, 100.0, 0.1, 0.0)
	{
	}

	protected CircleEffect(object amplitudeX, object amplitudeY, object frequency, object phase)
		: base(Guid.NewGuid().ToString())
	{
		base.AmplitudeX = amplitudeX;
		base.AmplitudeY = amplitudeY;
		Frequency = frequency;
		Phase = phase;
	}

	public sealed override double[,] GetPictureData(int dataCount)
	{
		double[,] ret = new double[2, dataCount];
		for (int i = 0; i < dataCount; i++)
		{
			double[] xy = GetXY(i, dataCount);
			ret[0, i] = xy[0];
			ret[1, i] = xy[1];
		}
		return ret;
	}

	protected override double[] getEffectVector2D(long timeInMs)
	{
		double frequency = FannedValueManager.ToDouble(Frequency);
		double phase = FannedValueManager.ToDouble(Phase);
		double offset = 1000.0 * phase / 360.0;
		double time = msDelta.Update(timeInMs, frequency);
		return GetXY((time + offset) % 1000.0, 1000.0);
	}

	public double[] GetXY(double time, double periodTime)
	{
		double x = 0.0;
		double y = 0.0;
		try
		{
			x = (float)Math.Cos(2.0 * time * Math.PI / periodTime);
			y = (float)Math.Sin(2.0 * time * Math.PI / periodTime);
		}
		catch (Exception e)
		{
			log.Error("", e);
		}
		return new double[2] { y, x };
	}

	protected override bool setParameterInternal(string name, object value)
	{
		if (FannedValueManager.getInstance().canConvertToValueInstance(value))
		{
			IFannedValue v = FannedValueManager.getInstance().convertToValueInstance(value);
			switch (name)
			{
			case "Amplitude X":
				base.AmplitudeX = v;
				return true;
			case "Amplitude Y":
				base.AmplitudeY = v;
				return true;
			case "Frequency":
				Frequency = v;
				return true;
			case "Phase":
				Phase = v;
				return true;
			}
		}
		return base.setParameterInternal(name, value);
	}

	protected override object getParameterInternal(string name)
	{
		object toConvert = null;
		EUiValueType type = EUiValueType.Unknown;
		switch (name)
		{
		case "Amplitude X":
			toConvert = base.AmplitudeX;
			break;
		case "Amplitude Y":
			toConvert = base.AmplitudeY;
			break;
		case "Frequency":
			toConvert = Frequency;
			type = EUiValueType.Speed;
			break;
		case "Phase":
			toConvert = Phase;
			type = EUiValueType.Degree;
			break;
		}
		return FannedValueManager.getInstance().convertToContainerInstance(toConvert, type);
	}

	protected override void SetInitialValuesFromUsingContext()
	{
		base.SetInitialValuesFromUsingContext();
		if (base.UsingPropertyContext.ValueType == typeof(Position) && base.UsingPropertyContext.HasFilterBounds)
		{
			Position ub = (Position)base.UsingPropertyContext.UpperBound;
			Position lb = (Position)base.UsingPropertyContext.LowerBound;
			setUsingTypeParameter("Amplitude X", (ub.Pan - lb.Pan) / 2.0);
			setUsingTypeParameter("Amplitude Y", (ub.Tilt - lb.Tilt) / 2.0);
		}
	}

	protected override AbstractEffect cloneAbstractEffect()
	{
		return new CircleEffect(base.AmplitudeX, base.AmplitudeY, Frequency, Phase);
	}
}
