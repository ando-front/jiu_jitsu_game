#pragma once

#include "CoreMinimal.h"
#include "BJJButtonBit.h"
#include "BJJInputFrame.generated.h"

// Layer A output — one struct per frame handed to Layer B.
// Matches InputFrame in src/prototype/web/src/input/types.ts and
// docs/design/input_system_v1.md §A.4.
USTRUCT(BlueprintType)
struct FBJJInputFrame
{
    GENERATED_BODY()

    // Sample time, milliseconds since app start (performance.now equivalent).
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input")
    double TimestampMs = 0.0;

    // Post-deadzone / post-curve stick values in [-1, 1]^2.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input")
    FVector2D LeftStick = FVector2D::ZeroVector;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input")
    FVector2D RightStick = FVector2D::ZeroVector;

    // Trigger pressures in [0, 1].
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input")
    float LTrigger = 0.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input")
    float RTrigger = 0.0f;

    // Packed button state — held bits. See EBJJButtonBit.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input", meta = (Bitmask, BitmaskEnum = "EBJJButtonBit"))
    uint8 Buttons = 0;

    // Bits set only on the frame the button went down (rising edge).
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input", meta = (Bitmask, BitmaskEnum = "EBJJButtonBit"))
    uint8 ButtonEdges = 0;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Input")
    EBJJDeviceKind DeviceKind = EBJJDeviceKind::Keyboard;
};

FORCEINLINE bool BJJButtonIsDown(const FBJJInputFrame& Frame, EBJJButtonBit Bit)
{
    return (Frame.Buttons & BJJButtonMask(Bit)) != 0;
}

FORCEINLINE bool BJJButtonWasPressed(const FBJJInputFrame& Frame, EBJJButtonBit Bit)
{
    return (Frame.ButtonEdges & BJJButtonMask(Bit)) != 0;
}
