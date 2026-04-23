#pragma once

#include "CoreMinimal.h"
#include "BJJButtonBit.generated.h"

// Button bitmask layout. Mirrors ButtonBit in
// src/prototype/web/src/input/types.ts — DO NOT reorder; scenario
// fixtures replay raw masks and rely on these indices.
UENUM(BlueprintType, meta = (Bitflags))
enum class EBJJButtonBit : uint8
{
    None        = 0       UMETA(Hidden),
    LBumper     = 1 << 0,
    RBumper     = 1 << 1,
    BtnBase     = 1 << 2,
    BtnRelease  = 1 << 3,
    BtnBreath   = 1 << 4,
    BtnReserved = 1 << 5,
    BtnPause    = 1 << 6,
};

ENUM_CLASS_FLAGS(EBJJButtonBit);

UENUM(BlueprintType)
enum class EBJJDeviceKind : uint8
{
    Xbox     UMETA(DisplayName = "Xbox"),
    DualSense UMETA(DisplayName = "DualSense"),
    Keyboard UMETA(DisplayName = "Keyboard"),
};

// Helper — treat an EBJJButtonBit value as its raw uint8 mask.
FORCEINLINE uint8 BJJButtonMask(EBJJButtonBit Bit)
{
    return static_cast<uint8>(Bit);
}
