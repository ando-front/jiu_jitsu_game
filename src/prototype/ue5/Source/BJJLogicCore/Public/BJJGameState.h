#pragma once

#include "CoreMinimal.h"
#include "BJJLayerB.h"
#include "BJJGameState.generated.h"

// Aggregate simulation state. Stage 1 equivalents live in
// src/prototype/web/src/state/*. This struct will grow as FSMs are ported.
USTRUCT(BlueprintType)
struct FBJJGameState
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    float AttackerStamina = 1.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    float DefenderStamina = 1.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    FVector2D PostureBreak = FVector2D::ZeroVector;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    float GameTimeSeconds = 0.0f;

    // Per-player Layer B state (hysteresis memory). Threaded through
    // Step() so Layer B remains a pure transform.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|State")
    FBJJLayerBState AttackerLayerB;
};
