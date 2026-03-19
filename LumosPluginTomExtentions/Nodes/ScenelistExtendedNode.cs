using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using LumosLIB.Kernel;
using LumosLIB.Kernel.Log;
using LumosLIB.Tools;
using System;
using System.Collections.Generic;
using Lumos.GUI.UICustomization;
using LumosLIB.Kernel.Log;
using LumosLIB.Kernel.Scene.Fanning;
using org.dmxc.lumos.Kernel.PropertyType;
using org.dmxc.lumos.Kernel.PropertyValue.Attachable;
using org.dmxc.lumos.Kernel.Scene.Fanning;
using LumosLIB.Tools.I18n;
using org.dmxc.lumos.Kernel.Input.v2.InputClass;
using org.dmxc.lumos.Kernel.Project;
using org.dmxc.lumos.Kernel.SceneList;
using org.dmxc.lumos.Kernel.Scene.Fanning;
using org.dmxc.lumos.Kernel.Scene.Fanning.Parameter;

namespace org.dmxc.lumos.Kernel.Input.v2.Worker;

public class ScenelistExtendedNode : AbstractNode, IWrapperGraphNode, IGraphNode, IDisposable, IParameterable<GenericParameter>, INotifyPropertyChanged, ISimpleProjectDirtyItem
{
	private static readonly ILumosLog log = LumosLogger.getInstance(typeof(ScenelistExtendedNode));

	public static readonly string NAME = DummyT._("Cuelist Extended");

	public static readonly string SCENELIST_TYPE = "__Scenelist_Extended";

	private IGraphNodeOutputPort outputID;

	private IGraphNodeOutputPort outputName;

	private IGraphNodeOutputPort outputNumber;

	private IGraphNodeOutputPort outputState;

	private IGraphNodeOutputPort outputCurrentCueID;

	private IGraphNodeOutputPort outputNextCueID;

	private IGraphNodeOutputPort outputCueCount;

	private IGraphNodeOutputPort outputIsReleasing;

	private IGraphNodeOutputPort outputFadeFactor;

	private IGraphNodeOutputPort outputIntensity;

	// private IGraphNodeOutputPort outputReleaseTime;

	private IGraphNodeOutputPort outputSpeedFactor;

	private IGraphNodeOutputPort outputPlayMode;

	private IGraphNodeInputPort inputScenelist;

	private IGraphNodeInputPort inputGo;

	private IGraphNodeInputPort inputGoBack;

	private IGraphNodeInputPort inputGoNext;

	private IGraphNodeInputPort inputGoTo;

	private IGraphNodeInputPort inputPlay;

	private IGraphNodeInputPort inputPause;

	private IGraphNodeInputPort inputStop;

	private IGraphNodeInputPort inputLoad;

	private IGraphNodeInputPort inputStomp;

	private IGraphNodeInputPort inputReset;

	private IGraphNodeInputPort inputFadeFactor;

	private IGraphNodeInputPort inputIntensity;

	private IGraphNodeInputPort inputReleaseTime;

	private IGraphNodeInputPort inputSpeedFactor;

	private IGraphNodeInputPort inputPlayMode;

	private bool go;

	private bool goBack;

	private int? goNext;

	private int? goTo;

	private bool play;

	private bool pause;

	private bool stop;

	private int? load;

	private bool stomp;

	private bool reset;

	private ISceneList _scenelist;

	public bool? Go
	{
		get
		{
			return go;
		}
		set
		{
			if (!go && value == true)
			{
				Scenelist?.go();
			}
			go = value == true;
		}
	}

	public bool? GoBack
	{
		get
		{
			return goBack;
		}
		set
		{
			if (!goBack && value == true)
			{
				Scenelist?.goBack();
			}
			goBack = value == true;
		}
	}

	public int? GoNext
	{
		get
		{
			return goNext;
		}
		set
		{
			if (goNext != value && value.HasValue)
			{
				Scenelist?.goNext(value.Value);
			}
			goNext = value;
		}
	}

	public int? GoTo
	{
		get
		{
			return goTo;
		}
		set
		{
			if (goTo != value && value.HasValue)
			{
				Scenelist?.goTo(value.Value);
			}
			goTo = value;
		}
	}

	public bool? Play
	{
		get
		{
			return play;
		}
		set
		{
			if (!play && value == true)
			{
				Scenelist?.play();
			}
			play = value == true;
		}
	}

	public bool? Pause
	{
		get
		{
			return pause;
		}
		set
		{
			if (!pause && value == true)
			{
				Scenelist?.pause();
			}
			pause = value == true;
		}
	}

	public bool? Stop
	{
		get
		{
			return stop;
		}
		set
		{
			if (!stop && value == true)
			{
				Task.Run(() => Scenelist?.stop());
			}
			stop = value == true;
		}
	}

	public int? Load
	{
		get
		{
			return load;
		}
		set
		{
			if (load != value && value.HasValue)
			{
				Scenelist?.load(value.Value);
			}
			load = value;
		}
	}

	public bool? Stomp
	{
		get
		{
			return stomp;
		}
		set
		{
			Scenelist?.stomp(value == true);
			stomp = value == true;
		}
	}

	public bool? Reset
	{
		get
		{
			return reset;
		}
		set
		{
			if (!reset && value == true)
			{
				Scenelist?.reset();
			}
			reset = value == true;
		}
	}

	public ISceneList Scenelist
	{
		get
		{
			return _scenelist;
		}
		private set
		{
			if (_scenelist != value)
			{
				if (_scenelist != null)
				{
					_scenelist.PropertyChanged -= Scenelist_PropertyChanged;
				}
				_scenelist = value;
				if (_scenelist != null)
				{
					_scenelist.PropertyChanged += Scenelist_PropertyChanged;
				}
				outputID.Value = _scenelist?.ID;
				setOutputName();
				setOutputNumber();
				setOutputState();
				setOutputCurrentCueIDIndex();
				setOutputNextCueIDIndex();
				setOutputCueCount();
				setOutputIsReleasing();
				setOutputFadeFactor();
				setOutputIntensity();
				// setOutputReleaseTime();
				setOutputSpeedFactor();
				setOutputPlayMode();
			}
		}
	}

	public string MemberInputName => inputScenelist.Name;

	public string MemberInputValue => inputScenelist.Value?.ToString() ?? null;

	private bool setOutputName()
	{
		if (object.Equals(outputName.Value, Scenelist?.Name))
		{
			return false;
		}
		outputName.Value = Scenelist?.Name;
		return true;
	}

	private bool setOutputNumber()
	{
		if (object.Equals(outputNumber.Value, Scenelist?.Number))
		{
			return false;
		}
		outputNumber.Value = Scenelist?.Number;
		return true;
	}

	private bool setOutputState()
	{
		if (object.Equals(outputState.Value, Scenelist?.State))
		{
			return false;
		}
		outputState.Value = Scenelist?.State;
		return true;
	}

	private bool setOutputCurrentCueIDIndex()
	{
		if (object.Equals(outputCurrentCueID.Value, Scenelist?.Index))
		{
			return false;
		}
		if (Scenelist != null)
		{
			int index = Scenelist.Index;
			outputCurrentCueID.Value = Scenelist.Scenes?.ElementAtOrDefault(index)?.ID;
		}
		else
		{
			outputCurrentCueID.Value = null;
		}
		return true;
	}

	private bool setOutputNextCueIDIndex()
	{
		if (object.Equals(outputNextCueID.Value, Scenelist?.NextIndex))
		{
			return false;
		}
		if (Scenelist != null)
		{
			int index = Scenelist.NextIndex;
			outputNextCueID.Value = Scenelist.Scenes?.ElementAtOrDefault(index)?.ID;
		}
		else
		{
			outputNextCueID.Value = null;
		}
		return true;
	}

	private bool setOutputCueCount()
	{
		if (object.Equals(outputCueCount.Value, Scenelist?.Scenes?.Count))
		{
			return false;
		}
		outputCueCount.Value = Scenelist?.Scenes?.Count;
		return true;
	}

	private bool setOutputIsReleasing()
	{
		if (object.Equals(outputIsReleasing.Value, Scenelist?.IsReleasing))
		{
			return false;
		}
		outputIsReleasing.Value = Scenelist?.IsReleasing;
		return true;
	}

	private bool setOutputFadeFactor()
	{
		if (object.Equals(outputFadeFactor.Value, Scenelist?.FadeFactor))
		{
			return false;
		}
		outputFadeFactor.Value = Scenelist?.FadeFactor;
		return true;
	}

	private bool setOutputIntensity()
	{
		if (object.Equals(outputIntensity.Value, Scenelist?.Intensity))
		{
			return false;
		}
		outputIntensity.Value = Scenelist?.Intensity;
		return true;
	}

	// private bool setOutputReleaseTime()
	// {
	// 	if (object.Equals(outputReleaseTime.Value, Scenelist?.ReleaseTime))
	// 	{
	// 		return false;
	// 	}
	// 	outputReleaseTime.Value = Scenelist?.ReleaseTime;
	// 	return true;
	// }

	private bool setOutputSpeedFactor()
	{
		if (object.Equals(outputSpeedFactor.Value, Scenelist?.SpeedFactor))
		{
			return false;
		}
		outputSpeedFactor.Value = Scenelist?.SpeedFactor;
		return true;
	}

	private bool setOutputPlayMode()
	{
		if (object.Equals(outputPlayMode.Value, Scenelist?.PlayMode))
		{
			return false;
		}
		outputPlayMode.Value = Scenelist?.PlayMode;
		return true;
	}

	private void setInputFadeFactorValue()
	{
		if (Scenelist != null)
		{
			double? fadeFactor = LumosTools.TryConvertToDouble(inputFadeFactor.Value);
			if (fadeFactor.HasValue)
			{
				Scenelist.FadeFactor = fadeFactor.Value;
			}
		}
	}

	private void setInputIntensityValue()
	{
		if (Scenelist != null)
		{
			double? intensity = LumosTools.TryConvertToDouble(inputIntensity.Value);
			if (intensity.HasValue)
			{
				Scenelist.Intensity = intensity.Value;
			}
		}
	}

	private void setInputReleaseTimeValue()
	{
		if (Scenelist != null)
		{
			// log.Info(Convert.ToString(inputReleaseTime.Value));

			IFannedValue? ReleaseTime = FannedValueManager.getInstance().convertToValueInstance(LumosTools.TryConvertToDouble(inputReleaseTime.Value));
			NumericFannedValue? NumericReleaseTime = NumericFannedValue.FromDouble((double)LumosTools.TryConvertToDouble(inputReleaseTime.Value));

			// log.Info(Convert.ToString(ReleaseTime.getFannedValues(1)[0]));

			if (ReleaseTime != null)
			{
				// log.Info("ReleaseTime is fanned value");

				Scenelist.ReleaseTime = ReleaseTime;
				Scenelist.GUIReleaseTime = NumericReleaseTime;
			}
		}
	}

	private void setInputSpeedFactorValue()
	{
		if (Scenelist != null)
		{
			double? speedFactor = LumosTools.TryConvertToDouble(inputSpeedFactor.Value);
			if (speedFactor.HasValue)
			{
				Scenelist.SpeedFactor = speedFactor.Value;
			}
		}
	}

	private void setInputPlayMode()
	{
		if (Scenelist == null)
		{
			return;
		}
		if (inputPlayMode.Value is ESceneListPlayMode)
		{
			Scenelist.PlayMode = (ESceneListPlayMode)inputPlayMode.Value;
			return;
		}
		int? val = LumosTools.TryConvertToInt32(inputPlayMode.Value);
		if (val.HasValue)
		{
			Scenelist.PlayMode = (ESceneListPlayMode)Enum.ToObject(typeof(ESceneListPlayMode), val.Value);
		}
	}

	public ScenelistExtendedNode(GraphNodeID id)
		: base(id, SCENELIST_TYPE, KnownCategories.SCENELISTS)
	{
		Name = NAME;
	}

	protected override void OnAddedToGraph()
	{
		base.OnAddedToGraph();
		setInputScenelist();
		AssignEvents(register: true);
	}

	protected override void OnRemovedFromGraph()
	{
		base.OnRemovedFromGraph();
		AssignEvents(register: false);
	}

	private void Scenelist_PropertyChanged(object sender, PropertyChangedEventArgs args)
	{
		string propertyName = args.PropertyName;
		if (propertyName == null)
		{
			return;
		}
		switch (propertyName.Length)
		{
		case 6:
			switch (propertyName[0])
			{
			case 'N':
				if (propertyName == "Number" && setOutputNumber())
				{
					OnProcessRequested();
				}
				break;
			case 'S':
				if (propertyName == "Scenes" && (setOutputNextCueIDIndex() | setOutputCueCount() | setOutputCurrentCueIDIndex()))
				{
					OnProcessRequested();
				}
				break;
			}
			break;
		case 9:
			switch (propertyName[0])
			{
			case 'I':
				if (propertyName == "Intensity" && setOutputIntensity())
				{
					OnProcessRequested();
				}
				break;
			case 'N':
				if (propertyName == "NextIndex" && setOutputNextCueIDIndex())
				{
					OnProcessRequested();
				}
				break;
			}
			break;
		case 11:
			switch (propertyName[0])
			{
			case 'S':
				if (propertyName == "SpeedFactor" && setOutputSpeedFactor())
				{
					OnProcessRequested();
				}
				break;
			// case 'R':
			// 	if (propertyName == "ReleaseTime" && setOutputReleaseTime())
			// 	{
			// 		OnProcessRequested();
			// 	}
			// 	break;
			case 'I':
				if (propertyName == "IsReleasing" && setOutputIsReleasing())
				{
					OnProcessRequested();
				}
				break;
			}
			break;
		case 5:
			switch (propertyName[0])
			{
			case 'S':
				if (propertyName == "State" && setOutputState())
				{
					OnProcessRequested();
				}
				break;
			case 'I':
				if (propertyName == "Index" && setOutputCurrentCueIDIndex())
				{
					OnProcessRequested();
				}
				break;
			}
			break;
		case 4:
			if (propertyName == "Name" && setOutputName())
			{
				OnProcessRequested();
			}
			break;
		case 10:
			if (propertyName == "FadeFactor" && setOutputFadeFactor())
			{
				OnProcessRequested();
			}
			break;
		case 8:
			if (propertyName == "PlayMode" && setOutputPlayMode())
			{
				OnProcessRequested();
			}
			break;
		case 7:
			break;
		}
	}

	public override void InitAfterLoadOrCreate(bool load)
	{
		base.InitAfterLoadOrCreate(load);
		if (load)
		{
			setInputScenelist();
		}
	}

	public override void AddDefaultPorts()
	{
		inputScenelist = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "Cuelist" || c.Name == "Scenelist") ?? AddInputPort(null, "Cuelist");
		inputScenelist.ShortName = DummyT._("Cuelist");
		inputScenelist.DefaultParameterType = typeof(NameNumberIDValue);
		inputScenelist.Value = inputScenelist.Value ?? string.Empty;
		inputScenelist.InputValueChanged += delegate
		{
			setInputScenelist();
		};
		inputGo = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "GO") ?? AddInputPort(null, "GO");
		inputGo.ShortName = DummyT._("GO");
		inputGo.DefaultParameterType = typeof(bool);
		inputGo.ShowAsParameter = false;
		inputGo.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			Go = AbstractNode.ConvertToBoolResult(value);
		};
		inputGoBack = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "GO BACK") ?? AddInputPort(null, "GO BACK");
		inputGoBack.ShortName = DummyT._("GO BACK");
		inputGoBack.DefaultParameterType = typeof(bool);
		inputGoBack.ShowAsParameter = false;
		inputGoBack.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			GoBack = AbstractNode.ConvertToBoolResult(value);
		};
		inputGoNext = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "GO NEXT") ?? AddInputPort(null, "GO NEXT");
		inputGoNext.ShortName = DummyT._("GO NEXT");
		inputGoNext.DefaultParameterType = typeof(int);
		inputGoNext.ShowAsParameter = false;
		inputGoNext.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			GoNext = AbstractNode.GetScene(Scenelist, value)?.SceneIndex;
		};
		inputGoTo = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "GO TO") ?? AddInputPort(null, "GO TO");
		inputGoTo.ShortName = DummyT._("GO TO");
		inputGoTo.DefaultParameterType = typeof(int);
		inputGoTo.ShowAsParameter = false;
		inputGoTo.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			GoTo = AbstractNode.GetScene(Scenelist, value)?.SceneIndex;
		};
		inputPlay = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "PLAY") ?? AddInputPort(null, "PLAY");
		inputPlay.ShortName = DummyT._("PLAY");
		inputPlay.DefaultParameterType = typeof(bool);
		inputPlay.ShowAsParameter = false;
		inputPlay.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			Play = AbstractNode.ConvertToBoolResult(value);
		};
		inputPause = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "PAUSE") ?? AddInputPort(null, "PAUSE");
		inputPause.ShortName = DummyT._("PAUSE");
		inputPause.DefaultParameterType = typeof(bool);
		inputPause.ShowAsParameter = false;
		inputPause.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			Pause = AbstractNode.ConvertToBoolResult(value);
		};
		inputStop = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "STOP") ?? AddInputPort(null, "STOP");
		inputStop.ShortName = DummyT._("STOP");
		inputStop.DefaultParameterType = typeof(bool);
		inputStop.ShowAsParameter = false;
		inputStop.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			Stop = AbstractNode.ConvertToBoolResult(value);
		};
		inputLoad = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "LOAD") ?? AddInputPort(null, "LOAD");
		inputLoad.ShortName = DummyT._("LOAD");
		inputLoad.DefaultParameterType = typeof(int);
		inputLoad.ShowAsParameter = false;
		inputLoad.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			Load = AbstractNode.GetScene(Scenelist, value)?.SceneIndex;
		};
		inputStomp = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "STOMP") ?? AddInputPort(null, "STOMP");
		inputStomp.ShortName = DummyT._("STOMP");
		inputStomp.DefaultParameterType = typeof(bool);
		inputStomp.ShowAsParameter = false;
		inputStomp.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			Stomp = AbstractNode.ConvertToBoolResult(value);
		};
		inputReset = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "RESET") ?? AddInputPort(null, "RESET");
		inputReset.ShortName = DummyT._("RESET");
		inputReset.DefaultParameterType = typeof(bool);
		inputReset.ShowAsParameter = false;
		inputReset.InputValueChanged += delegate(GraphNodePortID o, object value)
		{
			Reset = AbstractNode.ConvertToBoolResult(value);
		};
		inputFadeFactor = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "Fade Factor") ?? AddInputPort(null, "Fade Factor");
		inputFadeFactor.ShortName = DummyT._("Fade Factor");
		inputFadeFactor.DefaultParameterType = typeof(double?);
		inputFadeFactor.InputValueChanged += delegate
		{
			setInputFadeFactorValue();
		};
		inputIntensity = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "Intensity") ?? AddInputPort(null, "Intensity");
		inputIntensity.ShortName = DummyT._("Intensity");
		inputIntensity.DefaultParameterType = typeof(double?);
		inputIntensity.InputValueChanged += delegate
		{
			setInputIntensityValue();
		};
		inputReleaseTime = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "Release Time") ?? AddInputPort(null, "Release Time");
		inputReleaseTime.ShortName = DummyT._("Release Time");
		inputReleaseTime.DefaultParameterType = typeof(double?);
		inputReleaseTime.InputValueChanged += delegate
		{
			setInputReleaseTimeValue();
		};
		inputSpeedFactor = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "Speed Factor") ?? AddInputPort(null, "Speed Factor");
		inputSpeedFactor.ShortName = DummyT._("Speed Factor");
		inputSpeedFactor.DefaultParameterType = typeof(double?);
		inputSpeedFactor.InputValueChanged += delegate
		{
			setInputSpeedFactorValue();
		};
		inputPlayMode = base.Inputs.SingleOrDefault((IGraphNodeInputPort c) => c.Name == "Play Mode") ?? AddInputPort(null, "Play Mode");
		inputPlayMode.ShortName = DummyT._("Play Mode");
		inputPlayMode.DefaultParameterType = typeof(ESceneListPlayMode?);
		inputPlayMode.InputValueChanged += delegate
		{
			setInputPlayMode();
		};
		outputName = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Name") ?? AddOutputPort(null, "Name");
		outputName.ShortName = DummyT._("Name");
		outputID = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "ID") ?? AddOutputPort(null, "ID");
		outputID.ShortName = DummyT._("ID");
		outputNumber = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Number") ?? AddOutputPort(null, "Number");
		outputNumber.ShortName = DummyT._("Number");
		outputState = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "State") ?? AddOutputPort(null, "State");
		outputState.ShortName = DummyT._("State");
		outputCurrentCueID = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Current Cue ID") ?? AddOutputPort(null, "Current Cue ID");
		outputCurrentCueID.ShortName = DummyT._("Current Cue ID");
		outputNextCueID = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Next Cue ID") ?? AddOutputPort(null, "Next Cue ID");
		outputNextCueID.ShortName = DummyT._("Next Cue ID");
		outputCueCount = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Cue Count") ?? AddOutputPort(null, "Cue Count");
		outputCueCount.ShortName = DummyT._("Cue Count");
		outputIsReleasing = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Is Releasing") ?? AddOutputPort(null, "Is Releasing");
		outputIsReleasing.ShortName = DummyT._("Is Releasing");
		outputFadeFactor = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Fade Factor") ?? AddOutputPort(null, "Fade Factor");
		outputFadeFactor.ShortName = DummyT._("Fade Factor");
		outputIntensity = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Intensity") ?? AddOutputPort(null, "Intensity");
		outputIntensity.ShortName = DummyT._("Intensity");
		// outputReleaseTime = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Release Time") ?? AddOutputPort(null, "Release Time");
		// outputReleaseTime.ShortName = DummyT._("Release Time");
		outputSpeedFactor = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Speed Factor") ?? AddOutputPort(null, "Speed Factor");
		outputSpeedFactor.ShortName = DummyT._("Speed Factor");
		outputPlayMode = base.Outputs.SingleOrDefault((IGraphNodeOutputPort c) => c.Name == "Play Mode") ?? AddOutputPort(null, "Play Mode");
		outputPlayMode.ShortName = DummyT._("Play Mode");
	}

	private void setInputScenelist()
	{
		try
		{
			Scenelist = AbstractNode.GetSceneList(inputScenelist?.Value);
		}
		catch (Exception e)
		{
			log.Error(e);
		}
	}

	protected override void DisposeHook()
	{
		base.DisposeHook();
		AssignEvents(register: false);
		Scenelist = null;
	}

	private void AssignEvents(bool register)
	{
		SceneListManager sm = SceneListManager.getInstance();
		sm.SceneListAdded -= OnSceneListAdded;
		sm.SceneListRemoved -= OnSceneListRemoved;
		sm.SceneListNameChanged -= OnSceneListNameNumberChanged;
		sm.SceneListNumberChanged -= OnSceneListNameNumberChanged;
		if (register)
		{
			sm.SceneListAdded += OnSceneListAdded;
			sm.SceneListRemoved += OnSceneListRemoved;
			sm.SceneListNameChanged += OnSceneListNameNumberChanged;
			sm.SceneListNumberChanged += OnSceneListNameNumberChanged;
		}
	}

	private void OnSceneListNameNumberChanged(object sender, SceneListEventArgs e)
	{
		if (Scenelist == null || Scenelist == e.SceneList)
		{
			ISceneList scenelist = Scenelist;
			setInputScenelist();
			if (scenelist != Scenelist)
			{
				OnProcessRequested();
			}
		}
	}

	private void OnSceneListAdded(object sender, SceneListEventArgs e)
	{
		if (base.IsDisposed)
		{
			AssignEvents(register: false);
		}
		else if (Scenelist == null)
		{
			setInputScenelist();
			if (Scenelist != null)
			{
				OnProcessRequested();
			}
		}
	}

	private void OnSceneListRemoved(object sender, SceneListEventArgs e)
	{
		if (base.IsDisposed)
		{
			AssignEvents(register: false);
		}
		else if (Scenelist != null && Scenelist == e.SceneList)
		{
			Scenelist = null;
			OnProcessRequested();
		}
	}
}
