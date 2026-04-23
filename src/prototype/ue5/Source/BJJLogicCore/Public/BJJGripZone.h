#pragma once

#include "CoreMinimal.h"
#include "BJJGripZone.generated.h"

// Layer B grip target zones. See docs/design/input_system_v1.md §B.2.1.
// EBJJGripZone::None replaces the TypeScript "GripZone | null" sentinel.
UENUM(BlueprintType)
enum class EBJJGripZone : uint8
{
    None         UMETA(DisplayName = "None"),
    SleeveL      UMETA(DisplayName = "Sleeve L"),
    SleeveR      UMETA(DisplayName = "Sleeve R"),
    CollarL      UMETA(DisplayName = "Collar L"),
    CollarR      UMETA(DisplayName = "Collar R"),
    WristL       UMETA(DisplayName = "Wrist L"),
    WristR       UMETA(DisplayName = "Wrist R"),
    Belt         UMETA(DisplayName = "Belt"),
    PostureBreak UMETA(DisplayName = "Posture Break"),
};

// Unit-vector direction for each zone in RS space (attacker viewpoint),
// matching GRIP_ZONE_DIRECTIONS in src/prototype/web/src/input/intent.ts.
// Stored as plain floats so the array is constexpr; converted to FVector2D
// at the comparison site.
struct FBJJGripZoneDirection
{
    EBJJGripZone Zone;
    float X;
    float Y;
};

namespace BJJGripZones
{
    // sqrt(1/2) for the diagonal zones. Matches Math.SQRT1_2 in Stage 1.
    inline constexpr float kInvSqrt2 = 0.70710678118654752440f;

    inline constexpr FBJJGripZoneDirection Directions[] = {
        { EBJJGripZone::SleeveL,      -kInvSqrt2, -kInvSqrt2 },
        { EBJJGripZone::SleeveR,       kInvSqrt2, -kInvSqrt2 },
        { EBJJGripZone::CollarL,      -kInvSqrt2,  kInvSqrt2 },
        { EBJJGripZone::CollarR,       kInvSqrt2,  kInvSqrt2 },
        { EBJJGripZone::WristL,       -1.0f,       0.0f      },
        { EBJJGripZone::WristR,        1.0f,       0.0f      },
        { EBJJGripZone::Belt,          0.0f,      -1.0f      },
        { EBJJGripZone::PostureBreak,  0.0f,       1.0f      },
    };
}
