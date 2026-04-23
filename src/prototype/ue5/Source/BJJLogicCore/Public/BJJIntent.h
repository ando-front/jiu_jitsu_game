#pragma once

#include "CoreMinimal.h"
#include "BJJGripZone.h"
#include "BJJIntent.generated.h"

// Layer B output types. Mirrors src/prototype/web/src/input/intent.ts.
// Contracts: docs/design/input_system_v1.md §B.

// §B.1 — hip intent (pelvis pose target).
USTRUCT(BlueprintType)
struct FBJJHipIntent
{
    GENERATED_BODY()

    // Radians. Yaw around the opponent-centric axis.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    float HipAngleTarget = 0.0f;

    // [-1, 1]. + = push away, - = pull in.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    float HipPush = 0.0f;

    // [-1, 1]. ± = side-cut direction.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    float HipLateral = 0.0f;
};

// §B.2 — per-hand grip target + pressure.
USTRUCT(BlueprintType)
struct FBJJGripIntent
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    EBJJGripZone LHandTarget = EBJJGripZone::None;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    float LGripStrength = 0.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    EBJJGripZone RHandTarget = EBJJGripZone::None;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    float RGripStrength = 0.0f;
};

// §B.3 — discrete/edge intents. A TS tagged union becomes an enum + side
// payload. Multiple can fire in one frame, so the carrier is TArray.
UENUM(BlueprintType)
enum class EBJJDiscreteIntentKind : uint8
{
    FootHookToggle UMETA(DisplayName = "Foot Hook Toggle"),
    BaseHold       UMETA(DisplayName = "Base Hold"),
    GripReleaseAll UMETA(DisplayName = "Grip Release All"),
    BreathStart    UMETA(DisplayName = "Breath Start"),
    Pause          UMETA(DisplayName = "Pause"),
};

UENUM(BlueprintType)
enum class EBJJSide : uint8
{
    None UMETA(Hidden),
    L    UMETA(DisplayName = "Left"),
    R    UMETA(DisplayName = "Right"),
};

USTRUCT(BlueprintType)
struct FBJJDiscreteIntent
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    EBJJDiscreteIntentKind Kind = EBJJDiscreteIntentKind::BaseHold;

    // Only meaningful for FootHookToggle; Side::None otherwise.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    EBJJSide Side = EBJJSide::None;

    bool operator==(const FBJJDiscreteIntent& Other) const
    {
        return Kind == Other.Kind && Side == Other.Side;
    }
};

USTRUCT(BlueprintType)
struct FBJJIntent
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    FBJJHipIntent Hip;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    FBJJGripIntent Grip;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Intent")
    TArray<FBJJDiscreteIntent> Discrete;
};
