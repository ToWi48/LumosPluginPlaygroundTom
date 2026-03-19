using LumosLIB.Tools;

namespace org.dmxc.lumos.Kernel.PropertyValue.Filter.Chaser;

public class RepeatingThreeChaserPattern : AbstractChaserPattern
{
	private int index;

	public override string Name => "Repeating Three";

	protected override void ResetSteps()
	{
		index = 0;
	}

	protected override double?[] GetNextStep(int deviceCount, int length)
	{
		return GetNextStep(deviceCount, length, init: true);
	}

	protected double?[] GetNextStep(int deviceCount, int length, bool init)
	{
		double?[] ret = new double?[deviceCount];
		if (init)
		{
			ret.InitArray(0.0);
		}
		
		// Jede 2. Lampe erhält denselben Wert - alternierend mit index
		for (int i = 0; i < deviceCount; i++)
		{
			if ((i + index) % 3 == 0)
			{
				ret[i] = 1.0;
			}
		}
		
		index = (index + 1) % deviceCount;
		return ret;
	}
}
