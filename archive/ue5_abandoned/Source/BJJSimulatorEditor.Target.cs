// Copyright (c) BJJ Simulator project. See LICENSE.

using UnrealBuildTool;
using System.Collections.Generic;

public class BJJSimulatorEditorTarget : TargetRules
{
	public BJJSimulatorEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V5;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_5;
		ExtraModuleNames.AddRange(new string[] { "BJJSimulator" });
	}
}
