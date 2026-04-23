#include "BJJStepSimulation.h"

#include "BJJIntent.h"

namespace
{
FBJJIntent TransformIntent(const FBJJInputFrame& InputFrame)
{
    FBJJIntent Intent;
    Intent.GripDirection = InputFrame.LeftStick;

    constexpr int32 PassBit = 1 << 0;
    constexpr int32 CutBit = 1 << 1;
    constexpr int32 CounterBit = 1 << 2;

    Intent.bAttemptPass = (InputFrame.ButtonsMask & PassBit) != 0;
    Intent.bAttemptCut = (InputFrame.ButtonsMask & CutBit) != 0;
    Intent.bAttemptCounter = (InputFrame.ButtonsMask & CounterBit) != 0;

    return Intent;
}
}

FBJJStepResult FBJJStepSimulation::Step(
    const FBJJGameState& PrevState,
    const FBJJInputFrame& InputFrame,
    float RealDtSeconds,
    float GameDtScale)
{
    const float SafeRealDt = FMath::Max(0.0f, RealDtSeconds);
    const float SafeScale = FMath::Max(0.0f, GameDtScale);
    const float GameDt = SafeRealDt * SafeScale;

    const FBJJIntent Intent = TransformIntent(InputFrame);

    FBJJGameState NextState = PrevState;
    NextState.GameTimeSeconds += GameDt;

    const float StaminaDrain = Intent.bAttemptPass || Intent.bAttemptCut ? 0.15f * GameDt : 0.03f * GameDt;
    NextState.AttackerStamina = FMath::Clamp(PrevState.AttackerStamina - StaminaDrain, 0.0f, 1.0f);

    const FVector2D TargetPosture = Intent.GripDirection.GetClampedToMaxSize(1.0f);
    const float BlendAlpha = FMath::Clamp(6.0f * GameDt, 0.0f, 1.0f);
    NextState.PostureBreak = FMath::Lerp(PrevState.PostureBreak, TargetPosture, BlendAlpha);

    FBJJStepResult Result;
    Result.NextState = NextState;
    return Result;
}
