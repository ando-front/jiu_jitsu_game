// Copyright (c) BJJ Simulator project. See LICENSE.

using UnrealBuildTool;
using System.Collections.Generic;

public class BJJSimulatorTarget : TargetRules
{
	public BJJSimulatorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V5;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_5;
		ExtraModuleNames.AddRange(new string[] { "BJJSimulator" });
	}
}
