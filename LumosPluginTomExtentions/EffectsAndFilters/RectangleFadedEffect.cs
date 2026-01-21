// Lumos, Version=3.3.0.0, Culture=neutral, PublicKeyToken=null
// org.dmxc.lumos.Kernel.PropertyValue.Effect.RectangleEffect
using System;
using System.Collections.Generic;
using System.Linq;
using Lumos.GUI.UICustomization;
using LumosLIB.Kernel.Log;
using LumosLIB.Kernel.Scene.Fanning;
using LumosLIB.Tools;
using org.dmxc.lumos.Kernel.PropertyValue.Attachable;
using org.dmxc.lumos.Kernel.PropertyValue.Effect;
using org.dmxc.lumos.Kernel.Scene.Fanning;

public class RectangleEffect : AbstractFrequentFunctionEffect
{
	private static readonly ILumosLog log = LumosLogger.getInstance(typeof(RectangleEffect));

	[UpdateParameter(true, false)]
	public object PulsePause { get; set; }

	[UpdateParameter(true, false)]
	public object FadeTime { get; set; }

	protected override IEnumerable<AttachableParameter> ParametersInternal => base.ParametersInternal.Concat(new[]
	{
		new AttachableParameter("Pulse Pause", null, typeof(NumericFannedValue))
		{
			LowerBound = 0.1,
			UpperBound = 10.0,
			ValueType = EUiValueType.Time
		},
		new AttachableParameter("Fade Time", null, typeof(NumericFannedValue))
		{
			LowerBound = 0.0,
			UpperBound = 10.0,
			ValueType = EUiValueType.Time
		}
	});


	public override string Name => "Rectangle Fade";

	protected override ILumosLog Log => log;

	private RectangleEffect(object amplitude, object frequency, object phase, object pulsePause, object fadeTime)
		: base(amplitude, frequency, phase)
	{
		PulsePause = pulsePause;
		FadeTime = fadeTime;
	}

	public RectangleEffect()
		: this(100.0, 0.1, 0.0, 1.0, 0.0)
	{
	}

	protected override object getParameterInternal(string name)
	{
		if (name.EqualsIgnoreCase("Pulse Pause"))
		{
			return FannedValueManager.getInstance().convertToContainerInstance(PulsePause, EUiValueType.Time);
		}

		if (name.EqualsIgnoreCase("Fade Time"))
		{
			return FannedValueManager.getInstance().convertToContainerInstance(FadeTime, EUiValueType.Time);
		}
		return base.getParameterInternal(name);
	}

	protected override bool setParameterInternal(string name, object value)
	{
		if (name.EqualsIgnoreCase("Pulse Pause"))
		{
			PulsePause = FannedValueManager.getInstance().convertToValueInstance(value);
			return true;
		}
		if (name.EqualsIgnoreCase("Fade Time"))
		{
			FadeTime = FannedValueManager.getInstance().convertToValueInstance(value);
			return true;
		}
		return base.setParameterInternal(name, value);
	}

	protected override double getNormedEffectValue(double timeInMsRelative)
	{
		// double pause = FannedValueManager.ToDouble(PulsePause);
		// double fade = FannedValueManager.ToDouble(FadeTime);

		// if (pause > 0.0)
		// {
		// 	pause /= pause + 1.0;
		// 	if ((timeInMsRelative / 1000.0 + FannedValueManager.ToDouble(base.Phase) / 360.0) % 1.0 < pause)
		// 	{
		// 		return 0.0;
		// 	}
		// }
		// return 1.0;

		// double pause = Math.Max(0.0, FannedValueManager.ToDouble(PulsePause));
		// double fade = Math.Max(0.0, FannedValueManager.ToDouble(FadeTime));
		// double onTime = 1.0;
		// double cycle = pause + onTime;
		// double phaseFrac = FannedValueManager.ToDouble(base.Phase) / 360.0;
		// double timeSec = timeInMsRelative / 1000.0;

		// // Position innerhalb des Zyklus in Sekunden
		// double cyclePos = (timeSec / cycle + phaseFrac) % 1.0;
		// if (cyclePos < 0.0) cyclePos += 1.0;
		// double tInCycle = cyclePos * cycle;

		// // In Pause (off)
		// if (tInCycle < pause)
		// 	return 0.0;

		// // In On-Time
		// double tOn = tInCycle - pause; // Bereich [0, onTime)

		// if (fade <= 0.0)
		// 	return 1.0;

		// // Hilfsfunktion clamp (0..1)
		// double clampVal(double v) => Math.Max(0.0, Math.Min(1.0, v));

		// // Falls die beiden Fades sich überlappen -> dreieckiger Verlauf (Peak in der Mitte)
		// if (fade * 2.0 >= onTime)
		// {
		// 	double half = onTime / 2.0;
		// 	if (tOn <= half)
		// 		return clampVal(tOn / half);
		// 	else
		// 		return clampVal((onTime - tOn) / half);
		// }

		// // Normalfall: Fade-in (erste `fade`), konstant 1.0, Fade-out (letzte `fade`)
		// if (tOn < fade)
		// 	return clampVal(tOn / fade);

		// if (tOn > onTime - fade)
		// 	return clampVal((onTime - tOn) / fade);

		// return 1.0;

		double pauseRaw = FannedValueManager.ToDouble(PulsePause);
		double fadeRaw = FannedValueManager.ToDouble(FadeTime);

		// 1. Gesamtdauer berechnen (Basis ist 1.0 für "On" + Pause)
		double totalDuration = pauseRaw + 1.0;

		// 2. Normalisierte Werte (0.0 bis 1.0 Skala)
		double normalizedPause = pauseRaw / totalDuration;
		double normalizedFade = fadeRaw / totalDuration;

		// 3. Aktuelle Position im Zyklus (0.0 bis 1.0)
		double progress = (timeInMsRelative / 1000.0 + FannedValueManager.ToDouble(base.Phase) / 360.0) % 1.0;

		// Phase 1: Pause (Licht aus)
		if (progress < normalizedPause)
		{
			return 0.0;
		}

		// Phase 2: On-Zeit (Hier finden die Fades statt)
		double activeProgress = progress - normalizedPause; // Zeit seit Ende der Pause
		double activeWindow = 1.0 - normalizedPause;       // Dauer der On-Phase

		// Sicherheitscheck: Wenn fade = 0 oder extrem klein, sofort 1.0 (wie im Original)
		if (normalizedFade <= 0.0001)
		{
			return 1.0;
		}

		// Begrenzung: Fade darf maximal die Hälfte der On-Zeit einnehmen (Dreiecksform)
		double actualFade = Math.Min(normalizedFade, activeWindow / 2.0);

		// Fade-In
		if (activeProgress < actualFade)
		{
			return activeProgress / actualFade;
		}
		// Fade-Out
		else if (activeProgress > (activeWindow - actualFade))
		{
			return (activeWindow - activeProgress) / actualFade;
		}

		// Steady On (Voll an zwischen den Fades)
		return 1.0;

	}

	protected override AbstractFrequentFunctionEffect cloneAbstractFrequentFunctionEffect()
	{
		return new RectangleEffect(base.Amplitude, base.Frequency, base.Phase, PulsePause, FadeTime);
	}
}
