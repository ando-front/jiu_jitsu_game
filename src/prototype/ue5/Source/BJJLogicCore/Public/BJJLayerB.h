#pragma once

#include "CoreMinimal.h"
#include "BJJGripZone.h"
#include "BJJInputFrame.h"
#include "BJJIntent.h"
#include "BJJLayerB.generated.h"

// Layer B — InputFrame -> Intent transformation (pure).
// Ports src/prototype/web/src/input/layerB.ts.
// Contracts: docs/design/input_system_v1.md §B.1 (hip), §B.2 (grip), §B.3 (discrete).

// §B.1 — LS scaling. Full stick tilt maps to ±0.6 * π/2 ≈ ±0.94 rad so the
// pelvis does not rotate beyond the natural seated range in closed guard.
inline constexpr float BJJ_K_ANGLE_SCALE = 0.6f;

// §B.2.1 — hysteresis: switching zones requires the new stick direction to
// be within ±15° of the new zone centre (cos 30° ≈ 0.866).
inline constexpr float BJJ_ZONE_SELECT_COS_THRESHOLD = 0.86602540378443864676f;

// Below this stick magnitude the grip "target" is considered absent.
inline constexpr float BJJ_RS_MAGNITUDE_THRESHOLD = 0.2f;

USTRUCT(BlueprintType)
struct FBJJLayerBConfig
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|LayerB")
    float KAngleScale = BJJ_K_ANGLE_SCALE;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|LayerB")
    float ZoneSelectCosThreshold = BJJ_ZONE_SELECT_COS_THRESHOLD;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|LayerB")
    float RsMagnitudeThreshold = BJJ_RS_MAGNITUDE_THRESHOLD;
};

// Per-player Layer B state carried across frames. Kept external so the
// transform itself is a pure function.
USTRUCT(BlueprintType)
struct FBJJLayerBState
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|LayerB")
    EBJJGripZone LastZone = EBJJGripZone::None;
};

USTRUCT(BlueprintType)
struct FBJJLayerBResult
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|LayerB")
    FBJJIntent Intent;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|LayerB")
    FBJJLayerBState NextState;
};

class FBJJLayerB
{
public:
    static FBJJLayerBResult Transform(
        const FBJJInputFrame& Frame,
        const FBJJLayerBState& Prev,
        const FBJJLayerBConfig& Config);

    // Convenience overload using defaults.
    static FBJJLayerBResult Transform(
        const FBJJInputFrame& Frame,
        const FBJJLayerBState& Prev);

    // Exposed for targeted tests — mirror the per-piece functions in layerB.ts.
    static FBJJHipIntent ComputeHipIntent(
        const FBJJInputFrame& Frame,
        const FBJJLayerBConfig& Config);

    struct FGripStep
    {
        FBJJGripIntent Grip;
        EBJJGripZone NextZone;
    };

    static FGripStep ComputeGripIntent(
        const FBJJInputFrame& Frame,
        EBJJGripZone LastZone,
        const FBJJLayerBConfig& Config);

    static TArray<FBJJDiscreteIntent> ComputeDiscreteIntents(const FBJJInputFrame& Frame);
};
