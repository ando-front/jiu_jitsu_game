// Copyright (c) BJJ Simulator project. See LICENSE.

using UnrealBuildTool;

public class BJJSimulator : ModuleRules
{
	public BJJSimulator(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = ModuleRules.PCHUsageMode.UseExplicitOrSharedPCHs;

		// UBT auto-discovers Public/ and Private/ — no explicit
		// PublicIncludePaths / PrivateIncludePaths needed. The includes
		// in source files use the folder-prefixed form, e.g.
		//   #include "State/BJJHandFSM.h"
		//   #include "Core/BJJCoreTypes.h"

		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core",
			"CoreUObject",
			"Engine",
			"InputCore",
			"EnhancedInput",
		});

		PrivateDependencyModuleNames.AddRange(new string[]
		{
			// Add as needed for Private/*.cpp (e.g., "UMG" when HUD lands).
		});

		// Automation test macros live in Core's Misc/AutomationTest.h, so no
		// extra dependency is required. Scenarios + debug tooling are guarded
		// with !UE_BUILD_SHIPPING inside source files.
	}
}
