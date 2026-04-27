// UE Automation tests for Layer B.
// Mirrors src/prototype/web/tests/unit/layerB.test.ts one-for-one so the two
// implementations stay in lockstep. Run in-editor with
//   Window -> Test Automation -> "BJJ.Input.LayerB".
// Or on the commandline:
//   UnrealEditor-Cmd.exe <Project>.uproject -ExecCmds="Automation RunTests BJJ.Input.LayerB; Quit" -unattended

#include "CoreMinimal.h"
#include "Misc/AutomationTest.h"

#include "BJJInputFrame.h"
#include "BJJIntent.h"
#include "BJJLayerB.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
constexpr float kEpsilon = 1.0e-5f;

FBJJInputFrame MakeFrame()
{
    FBJJInputFrame Frame;
    return Frame;
}

FBJJInputFrame MakeFrame(FVector2D LS, FVector2D RS, float LT = 0.0f, float RT = 0.0f)
{
    FBJJInputFrame Frame;
    Frame.LeftStick = LS;
    Frame.RightStick = RS;
    Frame.LTrigger = LT;
    Frame.RTrigger = RT;
    return Frame;
}
}

// -------- §B.1 hip intent --------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBHipCentredTest,
    "BJJ.Input.LayerB.Hip.CentredIsZero",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBHipCentredTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJHipIntent Hip = FBJJLayerB::ComputeHipIntent(MakeFrame(), FBJJLayerBConfig{});
    TestEqual(TEXT("HipAngleTarget"), Hip.HipAngleTarget, 0.0f);
    TestEqual(TEXT("HipPush"), Hip.HipPush, 0.0f);
    TestEqual(TEXT("HipLateral"), Hip.HipLateral, 0.0f);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBHipPureUpTest,
    "BJJ.Input.LayerB.Hip.PureUpYieldsZeroAngle",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBHipPureUpTest::RunTest(const FString& /*Parameters*/)
{
    FBJJInputFrame Frame = MakeFrame();
    Frame.LeftStick = FVector2D(0.0f, 1.0f);
    const FBJJHipIntent Hip = FBJJLayerB::ComputeHipIntent(Frame, FBJJLayerBConfig{});
    TestTrue(TEXT("HipAngleTarget ~= 0"), FMath::IsNearlyZero(Hip.HipAngleTarget, kEpsilon));
    TestEqual(TEXT("HipPush"), Hip.HipPush, 1.0f);
    TestEqual(TEXT("HipLateral"), Hip.HipLateral, 0.0f);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBHipPureRightTest,
    "BJJ.Input.LayerB.Hip.PureRightYieldsScaledAngle",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBHipPureRightTest::RunTest(const FString& /*Parameters*/)
{
    FBJJInputFrame Frame = MakeFrame();
    Frame.LeftStick = FVector2D(1.0f, 0.0f);
    const FBJJHipIntent Hip = FBJJLayerB::ComputeHipIntent(Frame, FBJJLayerBConfig{});
    const float Expected = (PI / 2.0f) * BJJ_K_ANGLE_SCALE;
    TestTrue(TEXT("HipAngleTarget"), FMath::IsNearlyEqual(Hip.HipAngleTarget, Expected, kEpsilon));
    TestEqual(TEXT("HipLateral"), Hip.HipLateral, 1.0f);
    return true;
}

// -------- §B.2.1 grip zone selection --------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripNoTargetTest,
    "BJJ.Input.LayerB.Grip.CentredNoTriggersHasNoTargets",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripNoTargetTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        MakeFrame(), EBJJGripZone::None, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone"), Step.NextZone, EBJJGripZone::None);
    TestEqual(TEXT("LHandTarget"), Step.Grip.LHandTarget, EBJJGripZone::None);
    TestEqual(TEXT("RHandTarget"), Step.Grip.RHandTarget, EBJJGripZone::None);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripUpLeftCollarTest,
    "BJJ.Input.LayerB.Grip.UpLeftSelectsCollarL",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripUpLeftCollarTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(FVector2D::ZeroVector, FVector2D(-0.7f, 0.7f));
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::None, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone"), Step.NextZone, EBJJGripZone::CollarL);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripBeltTest,
    "BJJ.Input.LayerB.Grip.PureDownSelectsBelt",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripBeltTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(FVector2D::ZeroVector, FVector2D(0.0f, -1.0f));
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::None, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone"), Step.NextZone, EBJJGripZone::Belt);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripPostureBreakTest,
    "BJJ.Input.LayerB.Grip.PureUpSelectsPostureBreak",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripPostureBreakTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(FVector2D::ZeroVector, FVector2D(0.0f, 1.0f));
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::None, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone"), Step.NextZone, EBJJGripZone::PostureBreak);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripBothTriggersShareZoneTest,
    "BJJ.Input.LayerB.Grip.BothTriggersShareZone",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripBothTriggersShareZoneTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(
        FVector2D::ZeroVector, FVector2D(-1.0f, 0.0f), /*LT*/ 0.8f, /*RT*/ 0.5f);
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::None, FBJJLayerBConfig{});
    TestEqual(TEXT("LHandTarget"), Step.Grip.LHandTarget, EBJJGripZone::WristL);
    TestEqual(TEXT("RHandTarget"), Step.Grip.RHandTarget, EBJJGripZone::WristL);
    TestTrue(TEXT("LGripStrength"), FMath::IsNearlyEqual(Step.Grip.LGripStrength, 0.8f, kEpsilon));
    TestTrue(TEXT("RGripStrength"), FMath::IsNearlyEqual(Step.Grip.RGripStrength, 0.5f, kEpsilon));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripOnlyLTriggerTest,
    "BJJ.Input.LayerB.Grip.OnlyLTriggerGivesLeftTarget",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripOnlyLTriggerTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(
        FVector2D::ZeroVector, FVector2D(1.0f, 0.0f), /*LT*/ 1.0f, /*RT*/ 0.0f);
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::None, FBJJLayerBConfig{});
    TestEqual(TEXT("LHandTarget"), Step.Grip.LHandTarget, EBJJGripZone::WristR);
    TestEqual(TEXT("RHandTarget"), Step.Grip.RHandTarget, EBJJGripZone::None);
    return true;
}

// -------- §B.2.1 hysteresis --------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBHysteresisBoundaryHoldTest,
    "BJJ.Input.LayerB.Grip.BoundaryNudgeDoesNotFlip",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBHysteresisBoundaryHoldTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(FVector2D::ZeroVector, FVector2D(-0.92f, 0.38f));
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::WristL, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone (held)"), Step.NextZone, EBJJGripZone::WristL);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBHysteresisFirmFlipTest,
    "BJJ.Input.LayerB.Grip.FirmMovePastThresholdFlips",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBHysteresisFirmFlipTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(FVector2D::ZeroVector, FVector2D(-0.6f, 0.8f));
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::WristL, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone (flipped)"), Step.NextZone, EBJJGripZone::CollarL);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripReleaseClearsZoneTest,
    "BJJ.Input.LayerB.Grip.ReleaseClearsZoneWhenNoTrigger",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripReleaseClearsZoneTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(FVector2D::ZeroVector, FVector2D::ZeroVector);
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::WristL, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone"), Step.NextZone, EBJJGripZone::None);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBGripTriggerHoldsZoneTest,
    "BJJ.Input.LayerB.Grip.TriggerHoldsZoneAfterRelease",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBGripTriggerHoldsZoneTest::RunTest(const FString& /*Parameters*/)
{
    const FBJJInputFrame Frame = MakeFrame(
        FVector2D::ZeroVector, FVector2D::ZeroVector, /*LT*/ 1.0f, /*RT*/ 0.0f);
    const FBJJLayerB::FGripStep Step = FBJJLayerB::ComputeGripIntent(
        Frame, EBJJGripZone::CollarR, FBJJLayerBConfig{});
    TestEqual(TEXT("NextZone (held by trigger)"), Step.NextZone, EBJJGripZone::CollarR);
    return true;
}

// -------- §B.3 discrete intents --------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBDiscreteFootHookLTest,
    "BJJ.Input.LayerB.Discrete.LBumperEmitsFootHookL",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBDiscreteFootHookLTest::RunTest(const FString& /*Parameters*/)
{
    FBJJInputFrame Frame;
    Frame.ButtonEdges = BJJButtonMask(EBJJButtonBit::LBumper);
    const TArray<FBJJDiscreteIntent> List = FBJJLayerB::ComputeDiscreteIntents(Frame);
    TestEqual(TEXT("Count"), List.Num(), 1);
    if (List.Num() == 1)
    {
        TestEqual(TEXT("Kind"), List[0].Kind, EBJJDiscreteIntentKind::FootHookToggle);
        TestEqual(TEXT("Side"), List[0].Side, EBJJSide::L);
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBDiscreteBaseHoldTest,
    "BJJ.Input.LayerB.Discrete.BaseHoldEveryFrameWhenHeld",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBDiscreteBaseHoldTest::RunTest(const FString& /*Parameters*/)
{
    FBJJInputFrame Frame;
    Frame.Buttons = BJJButtonMask(EBJJButtonBit::BtnBase);
    const TArray<FBJJDiscreteIntent> List = FBJJLayerB::ComputeDiscreteIntents(Frame);
    TestEqual(TEXT("Count"), List.Num(), 1);
    if (List.Num() == 1)
    {
        TestEqual(TEXT("Kind"), List[0].Kind, EBJJDiscreteIntentKind::BaseHold);
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBDiscreteReleaseTest,
    "BJJ.Input.LayerB.Discrete.ReleaseEmitsGripReleaseAll",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBDiscreteReleaseTest::RunTest(const FString& /*Parameters*/)
{
    FBJJInputFrame Frame;
    Frame.ButtonEdges = BJJButtonMask(EBJJButtonBit::BtnRelease);
    const TArray<FBJJDiscreteIntent> List = FBJJLayerB::ComputeDiscreteIntents(Frame);
    TestEqual(TEXT("Count"), List.Num(), 1);
    if (List.Num() == 1)
    {
        TestEqual(TEXT("Kind"), List[0].Kind, EBJJDiscreteIntentKind::GripReleaseAll);
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBDiscreteMultiEventTest,
    "BJJ.Input.LayerB.Discrete.MultipleSimultaneousEvents",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBDiscreteMultiEventTest::RunTest(const FString& /*Parameters*/)
{
    FBJJInputFrame Frame;
    Frame.Buttons = BJJButtonMask(EBJJButtonBit::BtnBase);
    Frame.ButtonEdges =
        BJJButtonMask(EBJJButtonBit::LBumper) |
        BJJButtonMask(EBJJButtonBit::BtnBreath) |
        BJJButtonMask(EBJJButtonBit::BtnRelease);

    const TArray<FBJJDiscreteIntent> List = FBJJLayerB::ComputeDiscreteIntents(Frame);

    const FBJJDiscreteIntent FootHook{ EBJJDiscreteIntentKind::FootHookToggle, EBJJSide::L };
    const FBJJDiscreteIntent Base{ EBJJDiscreteIntentKind::BaseHold, EBJJSide::None };
    const FBJJDiscreteIntent Release{ EBJJDiscreteIntentKind::GripReleaseAll, EBJJSide::None };
    const FBJJDiscreteIntent Breath{ EBJJDiscreteIntentKind::BreathStart, EBJJSide::None };

    TestEqual(TEXT("Count"), List.Num(), 4);
    TestTrue(TEXT("Contains FootHookL"), List.Contains(FootHook));
    TestTrue(TEXT("Contains BaseHold"), List.Contains(Base));
    TestTrue(TEXT("Contains GripReleaseAll"), List.Contains(Release));
    TestTrue(TEXT("Contains BreathStart"), List.Contains(Breath));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBDiscreteEmptyTest,
    "BJJ.Input.LayerB.Discrete.NoButtonsProducesEmptyList",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBDiscreteEmptyTest::RunTest(const FString& /*Parameters*/)
{
    const TArray<FBJJDiscreteIntent> List = FBJJLayerB::ComputeDiscreteIntents(MakeFrame());
    TestEqual(TEXT("Count"), List.Num(), 0);
    return true;
}

// -------- Integration: state threading across frames --------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FBJJLayerBIntegrationHysteresisAcrossFramesTest,
    "BJJ.Input.LayerB.Transform.HysteresisSurvivesAcrossFrames",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FBJJLayerBIntegrationHysteresisAcrossFramesTest::RunTest(const FString& /*Parameters*/)
{
    FBJJLayerBState State;

    const FBJJInputFrame F1 = MakeFrame(FVector2D::ZeroVector, FVector2D(-1.0f, 0.0f));
    const FBJJLayerBResult R1 = FBJJLayerB::Transform(F1, State);
    State = R1.NextState;
    TestEqual(TEXT("Frame1 LastZone"), State.LastZone, EBJJGripZone::WristL);

    const FBJJInputFrame F2 = MakeFrame(FVector2D::ZeroVector, FVector2D(-0.92f, 0.38f));
    const FBJJLayerBResult R2 = FBJJLayerB::Transform(F2, State);
    State = R2.NextState;
    TestEqual(TEXT("Frame2 LastZone (held)"), State.LastZone, EBJJGripZone::WristL);

    const FBJJInputFrame F3 = MakeFrame(FVector2D::ZeroVector, FVector2D(-0.6f, 0.8f));
    const FBJJLayerBResult R3 = FBJJLayerB::Transform(F3, State);
    TestEqual(TEXT("Frame3 LastZone (flipped)"), R3.NextState.LastZone, EBJJGripZone::CollarL);

    return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
