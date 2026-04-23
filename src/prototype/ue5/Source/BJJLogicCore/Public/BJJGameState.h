#pragma once

#include "CoreMinimal.h"
#include "BJJGameState.generated.h"

USTRUCT(BlueprintType)
struct FBJJGameState
{
    GENERATED_BODY()

    // Prototype-level values. Replace with full FSM aggregate state during port.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    float AttackerStamina = 1.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    float DefenderStamina = 1.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    FVector2D PostureBreak = FVector2D::ZeroVector;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    float GameTimeSeconds = 0.0f;
};
