// Copyright (c) BJJ Simulator project. See LICENSE.
//
// Ported 1:1 from src/prototype/web/src/state/hand_fsm.ts.
// See docs/design/state_machines_v1.md §2.1 for the transition table and
// docs/design/stage2_port_plan_v1.md §2 for C++ conventions.

#pragma once

#include "CoreMinimal.h"
#include "Core/BJJCoreTypes.h"
#include "BJJHandFSM.generated.h"

// -----------------------------------------------------------------------------
// Timing (§2.1.2 of state_machines_v1.md)
// -----------------------------------------------------------------------------

namespace BJJ
{
	struct FHandTiming
	{
		int32 ReachMinMs     = 200;  // §C.1.2 REACHING 200–350ms
		int32 ReachMaxMs     = 350;
		int32 RetractMs      = 150;  // §C.1.2 RETRACT 150ms
		int32 ShortMemoryMs  = 400;  // §C.2 parry short-term memory
	};

	inline constexpr FHandTiming DEFAULT_HAND_TIMING;
}

// -----------------------------------------------------------------------------
// State (USTRUCT because it sits inside FBJJGameState → public state →
// §2.2 rule: no TVariant here)
// -----------------------------------------------------------------------------

USTRUCT(BlueprintType)
struct BJJSIMULATOR_API FBJJHandFSM
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly) EBJJHandSide Side = EBJJHandSide::Left;
	UPROPERTY(BlueprintReadOnly) EBJJHandState State = EBJJHandState::Idle;
	UPROPERTY(BlueprintReadOnly) EBJJGripZone Target = EBJJGripZone::None;
	UPROPERTY(BlueprintReadOnly) int64 StateEnteredMs = 0;
	UPROPERTY(BlueprintReadOnly) int32 ReachDurationMs = 0;
	UPROPERTY(BlueprintReadOnly) EBJJGripZone LastParriedZone = EBJJGripZone::None;
	// Sentinel: BJJ::SENTINEL_TIME_MS until a parry occurs.
	UPROPERTY(BlueprintReadOnly) int64 LastParriedAtMs = MIN_int64;
};

// -----------------------------------------------------------------------------
// Tick I/O
// -----------------------------------------------------------------------------

USTRUCT()
struct BJJSIMULATOR_API FBJJHandTickInput
{
	GENERATED_BODY()

	int64 NowMs = 0;
	float TriggerValue = 0.f;           // [0,1]
	EBJJGripZone TargetZone = EBJJGripZone::None;
	bool bForceReleaseAll = false;
	bool bOpponentDefendsThisZone = false;
	bool bOpponentCutSucceeded = false;
	bool bTargetOutOfReach = false;
};

UENUM()
enum class EBJJGripBrokenReason : uint8
{
	TriggerReleased,
	ForceRelease,
	OpponentCut,
	OutOfReach,
};

UENUM()
enum class EBJJHandEventKind : uint8
{
	ReachStarted,
	Contact,
	Gripped,
	Parried,
	GripBroken,
};

// "Fat struct" event — one USTRUCT carries every event variant, with
// Kind deciding which payload fields matter. §2.2 of the port plan.
USTRUCT(BlueprintType)
struct BJJSIMULATOR_API FBJJHandTickEvent
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly) EBJJHandEventKind Kind = EBJJHandEventKind::ReachStarted;
	UPROPERTY(BlueprintReadOnly) EBJJHandSide Side = EBJJHandSide::Left;
	UPROPERTY(BlueprintReadOnly) EBJJGripZone Zone = EBJJGripZone::None;
	// Only meaningful for Kind == GripBroken.
	UPROPERTY(BlueprintReadOnly) EBJJGripBrokenReason GripBrokenReason = EBJJGripBrokenReason::TriggerReleased;
};

// -----------------------------------------------------------------------------
// Pure transition function — namespace form per port-plan §2.3.
// Declaration only; definition in Private/State/BJJHandFSM.cpp.
// -----------------------------------------------------------------------------

namespace BJJ::HandFSM
{
	/** Initialise a hand in IDLE at `NowMs`. */
	FBJJHandFSM Initial(EBJJHandSide Side, int64 NowMs);

	/**
	 * One FSM tick. Appends any produced events to OutEvents and returns
	 * the next state via OutNext. OutEvents is never cleared — the caller
	 * can accumulate events across multiple tick sites per frame.
	 */
	void Tick(
		const FBJJHandFSM& Prev,
		const FBJJHandTickInput& Input,
		FBJJHandFSM& OutNext,
		TArray<FBJJHandTickEvent>& OutEvents,
		const BJJ::FHandTiming& Timing = BJJ::DEFAULT_HAND_TIMING);
}
