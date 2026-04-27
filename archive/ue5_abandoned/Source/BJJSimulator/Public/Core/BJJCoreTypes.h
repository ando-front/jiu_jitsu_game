// Copyright (c) BJJ Simulator project. See LICENSE.
//
// Core enums shared across the input / state / AI layers.
// Ported from:
//   src/prototype/web/src/input/types.ts       (ButtonBit)
//   src/prototype/web/src/input/intent.ts      (GripZone)
//   src/prototype/web/src/state/hand_fsm.ts    (HandSide / HandState)
//   src/prototype/web/src/state/foot_fsm.ts    (FootSide / FootState)
//
// Port policy: enums with a sentinel "None" keep the sentinel at ordinal 0
// so default-initialised USTRUCTs land on it automatically.

#pragma once

#include "CoreMinimal.h"
#include "BJJCoreTypes.generated.h"

// -----------------------------------------------------------------------------
// Sides
// -----------------------------------------------------------------------------

UENUM(BlueprintType)
enum class EBJJHandSide : uint8
{
	Left  UMETA(DisplayName = "L"),
	Right UMETA(DisplayName = "R"),
};

UENUM(BlueprintType)
enum class EBJJFootSide : uint8
{
	Left  UMETA(DisplayName = "L"),
	Right UMETA(DisplayName = "R"),
};

// -----------------------------------------------------------------------------
// Grip zones (§B.2.1 of input_system_v1.md)
// TS None → C++ None at ordinal 0, so default-initialised state means
// "no grip target" without extra logic.
// -----------------------------------------------------------------------------

UENUM(BlueprintType)
enum class EBJJGripZone : uint8
{
	None        UMETA(DisplayName = "(none)"),
	SleeveL     UMETA(DisplayName = "SLEEVE_L"),
	SleeveR     UMETA(DisplayName = "SLEEVE_R"),
	CollarL     UMETA(DisplayName = "COLLAR_L"),
	CollarR     UMETA(DisplayName = "COLLAR_R"),
	WristL      UMETA(DisplayName = "WRIST_L"),
	WristR      UMETA(DisplayName = "WRIST_R"),
	Belt        UMETA(DisplayName = "BELT"),
	PostureBreak UMETA(DisplayName = "POSTURE_BREAK"),
};

// -----------------------------------------------------------------------------
// Hand / Foot FSM states (§2.1 / §2.2 of state_machines_v1.md)
// -----------------------------------------------------------------------------

UENUM(BlueprintType)
enum class EBJJHandState : uint8
{
	Idle     UMETA(DisplayName = "IDLE"),
	Reaching UMETA(DisplayName = "REACHING"),
	Contact  UMETA(DisplayName = "CONTACT"),
	Gripped  UMETA(DisplayName = "GRIPPED"),
	Parried  UMETA(DisplayName = "PARRIED"),
	Retract  UMETA(DisplayName = "RETRACT"),
};

UENUM(BlueprintType)
enum class EBJJFootState : uint8
{
	Locked   UMETA(DisplayName = "LOCKED"),
	Unlocked UMETA(DisplayName = "UNLOCKED"),
	Locking  UMETA(DisplayName = "LOCKING"),
};

// -----------------------------------------------------------------------------
// Input button bitflags (§A.2.1 of input_system_v1.md)
// Mirrors ButtonBit from src/prototype/web/src/input/types.ts.
// Stored in a single uint32 per frame; bitwise tests only.
// -----------------------------------------------------------------------------

UENUM(BlueprintType, meta = (Bitflags, UseEnumValuesAsMaskValuesInEditor = "true"))
enum class EBJJButtonBit : uint32
{
	None        = 0            UMETA(Hidden),
	LBumper     = 1 << 0,
	RBumper     = 1 << 1,
	BtnBase     = 1 << 2,       // A / ✕ / Space
	BtnRelease  = 1 << 3,       // B / ◯ / X-key
	BtnBreath   = 1 << 4,       // Y / △ / C-key
	BtnReserved = 1 << 5,       // X / □ / V-key
	BtnPause    = 1 << 6,       // Options / Esc
};

ENUM_CLASS_FLAGS(EBJJButtonBit);

// -----------------------------------------------------------------------------
// Sentinel value for uninitialised timestamps. Ports
// `Number.NEGATIVE_INFINITY` from Stage 1 TS.
// Always compare with `== BJJ_SENTINEL_TIME_MS` before using in arithmetic —
// otherwise an INT64_MIN-anchored subtraction overflows (see
// docs/design/stage2_port_plan_v1.md §2.5).
// -----------------------------------------------------------------------------

namespace BJJ
{
	inline constexpr int64 SENTINEL_TIME_MS = MIN_int64;
}
