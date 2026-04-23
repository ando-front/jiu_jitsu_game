#pragma once

#include "CoreMinimal.h"
#include "BJJGameState.h"
#include "BJJInputFrame.h"
#include "BJJStepSimulation.generated.h"

USTRUCT(BlueprintType)
struct FBJJStepResult
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "BJJ|Step")
    FBJJGameState NextState;
};

class FBJJStepSimulation final
{
public:
    // RealDtSeconds is sampled from platform time, GameDtScale allows judgment-window slow-down.
    static FBJJStepResult Step(
        const FBJJGameState& PrevState,
        const FBJJInputFrame& InputFrame,
        float RealDtSeconds,
        float GameDtScale = 1.0f);
};
