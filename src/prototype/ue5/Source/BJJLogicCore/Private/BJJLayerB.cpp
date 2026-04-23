#include "BJJLayerB.h"

#include "BJJButtonBit.h"

namespace
{
struct FBestZone
{
    EBJJGripZone Zone;
    float Dot;
};

FBestZone PickBestZone(float Nx, float Ny)
{
    FBestZone Best{ BJJGripZones::Directions[0].Zone, -TNumericLimits<float>::Max() };
    for (const FBJJGripZoneDirection& Entry : BJJGripZones::Directions)
    {
        const float D = Nx * Entry.X + Ny * Entry.Y;
        if (D > Best.Dot)
        {
            Best.Dot = D;
            Best.Zone = Entry.Zone;
        }
    }
    return Best;
}
}

FBJJHipIntent FBJJLayerB::ComputeHipIntent(
    const FBJJInputFrame& Frame,
    const FBJJLayerBConfig& Config)
{
    const FVector2D LS = Frame.LeftStick;

    FBJJHipIntent Hip;
    // atan2(x, y) — pure-up (x=0, y=+1) yields 0, matching layerB.ts.
    Hip.HipAngleTarget = FMath::Atan2(LS.X, LS.Y) * Config.KAngleScale;
    Hip.HipPush = LS.Y;
    Hip.HipLateral = LS.X;
    return Hip;
}

FBJJLayerB::FGripStep FBJJLayerB::ComputeGripIntent(
    const FBJJInputFrame& Frame,
    EBJJGripZone LastZone,
    const FBJJLayerBConfig& Config)
{
    const float RsMag = FMath::Sqrt(Frame.RightStick.X * Frame.RightStick.X
                                  + Frame.RightStick.Y * Frame.RightStick.Y);
    const bool bAnyTriggerDown = Frame.LTrigger > 0.0f || Frame.RTrigger > 0.0f;

    EBJJGripZone NextZone = LastZone;

    if (RsMag >= Config.RsMagnitudeThreshold)
    {
        const float Nx = Frame.RightStick.X / RsMag;
        const float Ny = Frame.RightStick.Y / RsMag;
        const FBestZone Best = PickBestZone(Nx, Ny);
        if (LastZone == EBJJGripZone::None || Best.Zone == LastZone)
        {
            NextZone = Best.Zone;
        }
        else if (Best.Dot >= Config.ZoneSelectCosThreshold)
        {
            NextZone = Best.Zone;
        }
        // else: hysteresis hold on LastZone.
    }
    else if (!bAnyTriggerDown)
    {
        NextZone = EBJJGripZone::None;
    }

    // §B.2.2 — each trigger independently gates its hand's target.
    const EBJJGripZone LTarget = Frame.LTrigger > 0.0f ? NextZone : EBJJGripZone::None;
    const EBJJGripZone RTarget = Frame.RTrigger > 0.0f ? NextZone : EBJJGripZone::None;

    FBJJGripIntent Grip;
    if (LTarget == EBJJGripZone::None && RTarget == EBJJGripZone::None)
    {
        // ZERO_GRIP equivalent — strengths stay 0.
    }
    else
    {
        Grip.LHandTarget = LTarget;
        Grip.LGripStrength = Frame.LTrigger;
        Grip.RHandTarget = RTarget;
        Grip.RGripStrength = Frame.RTrigger;
    }

    return FGripStep{ Grip, NextZone };
}

TArray<FBJJDiscreteIntent> FBJJLayerB::ComputeDiscreteIntents(const FBJJInputFrame& Frame)
{
    TArray<FBJJDiscreteIntent> Out;

    if ((Frame.ButtonEdges & BJJButtonMask(EBJJButtonBit::LBumper)) != 0)
    {
        Out.Add({ EBJJDiscreteIntentKind::FootHookToggle, EBJJSide::L });
    }
    if ((Frame.ButtonEdges & BJJButtonMask(EBJJButtonBit::RBumper)) != 0)
    {
        Out.Add({ EBJJDiscreteIntentKind::FootHookToggle, EBJJSide::R });
    }
    if ((Frame.Buttons & BJJButtonMask(EBJJButtonBit::BtnBase)) != 0)
    {
        Out.Add({ EBJJDiscreteIntentKind::BaseHold, EBJJSide::None });
    }
    if ((Frame.ButtonEdges & BJJButtonMask(EBJJButtonBit::BtnRelease)) != 0)
    {
        Out.Add({ EBJJDiscreteIntentKind::GripReleaseAll, EBJJSide::None });
    }
    if ((Frame.ButtonEdges & BJJButtonMask(EBJJButtonBit::BtnBreath)) != 0)
    {
        Out.Add({ EBJJDiscreteIntentKind::BreathStart, EBJJSide::None });
    }
    if ((Frame.ButtonEdges & BJJButtonMask(EBJJButtonBit::BtnPause)) != 0)
    {
        Out.Add({ EBJJDiscreteIntentKind::Pause, EBJJSide::None });
    }

    return Out;
}

FBJJLayerBResult FBJJLayerB::Transform(
    const FBJJInputFrame& Frame,
    const FBJJLayerBState& Prev,
    const FBJJLayerBConfig& Config)
{
    FBJJLayerBResult Result;
    Result.Intent.Hip = ComputeHipIntent(Frame, Config);

    const FGripStep Step = ComputeGripIntent(Frame, Prev.LastZone, Config);
    Result.Intent.Grip = Step.Grip;
    Result.NextState.LastZone = Step.NextZone;

    Result.Intent.Discrete = ComputeDiscreteIntents(Frame);
    return Result;
}

FBJJLayerBResult FBJJLayerB::Transform(
    const FBJJInputFrame& Frame,
    const FBJJLayerBState& Prev)
{
    return Transform(Frame, Prev, FBJJLayerBConfig{});
}
