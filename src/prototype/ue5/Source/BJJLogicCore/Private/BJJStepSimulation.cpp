#include "BJJStepSimulation.h"

#include "BJJLayerB.h"

FBJJStepResult FBJJStepSimulation::Step(
    const FBJJGameState& PrevState,
    const FBJJInputFrame& InputFrame,
    float RealDtSeconds,
    float GameDtScale)
{
    const float SafeRealDt = FMath::Max(0.0f, RealDtSeconds);
    const float SafeScale = FMath::Max(0.0f, GameDtScale);
    const float GameDt = SafeRealDt * SafeScale;

    const FBJJLayerBResult LayerB = FBJJLayerB::Transform(InputFrame, PrevState.AttackerLayerB);

    FBJJGameState NextState = PrevState;
    NextState.AttackerLayerB = LayerB.NextState;
    NextState.GameTimeSeconds += GameDt;

    // Placeholder stamina model — replaced when the stamina FSM is ported.
    const bool bActiveAttempt = LayerB.Intent.Grip.LGripStrength > 0.0f
        || LayerB.Intent.Grip.RGripStrength > 0.0f;
    const float StaminaDrain = (bActiveAttempt ? 0.15f : 0.03f) * GameDt;
    NextState.AttackerStamina = FMath::Clamp(PrevState.AttackerStamina - StaminaDrain, 0.0f, 1.0f);

    // Placeholder posture drift — replaced when posture_break is ported.
    const FVector2D TargetPosture =
        FVector2D(LayerB.Intent.Hip.HipLateral, LayerB.Intent.Hip.HipPush).GetClampedToMaxSize(1.0f);
    const float BlendAlpha = FMath::Clamp(6.0f * GameDt, 0.0f, 1.0f);
    NextState.PostureBreak = FMath::Lerp(PrevState.PostureBreak, TargetPosture, BlendAlpha);

    FBJJStepResult Result;
    Result.NextState = NextState;
    Result.Intent = LayerB.Intent;
    return Result;
}
