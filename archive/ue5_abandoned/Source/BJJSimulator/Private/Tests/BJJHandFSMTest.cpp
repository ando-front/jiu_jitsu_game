// Copyright (c) BJJ Simulator project. See LICENSE.
//
// Automation Test mirror of src/prototype/web/tests/unit/hand_fsm.test.ts.
// Each IMPLEMENT_SIMPLE_AUTOMATION_TEST here corresponds to one `it(...)`
// case from the Stage 1 Vitest suite so a regression on either side
// produces a named, greppable failure.
//
// Run from UE editor: Tools → Session Frontend → Automation → run
// `BJJSimulator.State.HandFSM.*`.

#if WITH_DEV_AUTOMATION_TESTS

#include "Misc/AutomationTest.h"
#include "State/BJJHandFSM.h"

namespace
{
	constexpr int32 REACH_MID =
		(BJJ::DEFAULT_HAND_TIMING.ReachMinMs + BJJ::DEFAULT_HAND_TIMING.ReachMaxMs) / 2; // 275

	FBJJHandTickInput BaseInput()
	{
		FBJJHandTickInput In;
		In.NowMs = 0;
		In.TriggerValue = 0.f;
		In.TargetZone = EBJJGripZone::None;
		In.bForceReleaseAll = false;
		In.bOpponentDefendsThisZone = false;
		In.bOpponentCutSucceeded = false;
		In.bTargetOutOfReach = false;
		return In;
	}

	// Convenience: run a sequence of inputs, returning the last state and the
	// concatenated event kinds (as string ids for easy equality checks).
	struct FRunResult
	{
		FBJJHandFSM Final;
		TArray<EBJJHandEventKind> EventKinds;
	};

	FRunResult RunSequence(FBJJHandFSM Start, const TArray<FBJJHandTickInput>& Steps)
	{
		FRunResult R;
		R.Final = Start;
		for (const FBJJHandTickInput& Step : Steps)
		{
			TArray<FBJJHandTickEvent> Events;
			FBJJHandFSM Next;
			BJJ::HandFSM::Tick(R.Final, Step, Next, Events);
			for (const FBJJHandTickEvent& Ev : Events)
			{
				R.EventKinds.Add(Ev.Kind);
			}
			R.Final = Next;
		}
		return R;
	}
}

// -----------------------------------------------------------------------------
// IDLE → REACHING
// -----------------------------------------------------------------------------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMIdleTriggerAloneNoop,
	"BJJSimulator.State.HandFSM.Idle.TriggerAloneDoesNothing",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMIdleTriggerAloneNoop::RunTest(const FString&)
{
	const FBJJHandFSM Hand = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	FBJJHandTickInput In = BaseInput();
	In.TriggerValue = 1.f;
	In.TargetZone = EBJJGripZone::None;

	FBJJHandFSM Next;
	TArray<FBJJHandTickEvent> Events;
	BJJ::HandFSM::Tick(Hand, In, Next, Events);

	TestEqual(TEXT("state stays IDLE"), (uint8)Next.State, (uint8)EBJJHandState::Idle);
	TestEqual(TEXT("no events"), Events.Num(), 0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMIdleTriggerPlusZoneStartsReaching,
	"BJJSimulator.State.HandFSM.Idle.TriggerPlusTargetStartsReach",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMIdleTriggerPlusZoneStartsReaching::RunTest(const FString&)
{
	const FBJJHandFSM Hand = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	FBJJHandTickInput In = BaseInput();
	In.TriggerValue = 1.f;
	In.TargetZone = EBJJGripZone::SleeveR;

	FBJJHandFSM Next;
	TArray<FBJJHandTickEvent> Events;
	BJJ::HandFSM::Tick(Hand, In, Next, Events);

	TestEqual(TEXT("transitions to REACHING"), (uint8)Next.State, (uint8)EBJJHandState::Reaching);
	TestEqual(TEXT("target is SLEEVE_R"), (uint8)Next.Target, (uint8)EBJJGripZone::SleeveR);
	TestEqual(TEXT("exactly one event"), Events.Num(), 1);
	TestEqual(TEXT("event is REACH_STARTED"), (uint8)Events[0].Kind, (uint8)EBJJHandEventKind::ReachStarted);
	return true;
}

// -----------------------------------------------------------------------------
// REACHING → CONTACT → GRIPPED
// -----------------------------------------------------------------------------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMFullGripSequence,
	"BJJSimulator.State.HandFSM.Reaching.GripsAfterFullDuration",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMFullGripSequence::RunTest(const FString&)
{
	const FBJJHandFSM Hand = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	auto Step = [](int64 T, EBJJGripZone Zone, float Trig)
	{
		FBJJHandTickInput In = BaseInput();
		In.NowMs = T; In.TriggerValue = Trig; In.TargetZone = Zone; return In;
	};
	TArray<FBJJHandTickInput> Steps = {
		Step(0,            EBJJGripZone::CollarL, 1.f),
		Step(REACH_MID,    EBJJGripZone::CollarL, 1.f), // CONTACT
		Step(REACH_MID+16, EBJJGripZone::CollarL, 1.f), // GRIPPED
	};
	FRunResult R = RunSequence(Hand, Steps);

	TestEqual(TEXT("final is GRIPPED"), (uint8)R.Final.State, (uint8)EBJJHandState::Gripped);
	TestEqual(TEXT("events count"), R.EventKinds.Num(), 3);
	TestEqual(TEXT("event[0] REACH_STARTED"), (uint8)R.EventKinds[0], (uint8)EBJJHandEventKind::ReachStarted);
	TestEqual(TEXT("event[1] CONTACT"), (uint8)R.EventKinds[1], (uint8)EBJJHandEventKind::Contact);
	TestEqual(TEXT("event[2] GRIPPED"), (uint8)R.EventKinds[2], (uint8)EBJJHandEventKind::Gripped);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMTriggerReleaseMidReachAbortsToIdle,
	"BJJSimulator.State.HandFSM.Reaching.TriggerReleaseMidReachIdles",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMTriggerReleaseMidReachAbortsToIdle::RunTest(const FString&)
{
	const FBJJHandFSM Hand = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	FBJJHandTickInput Start = BaseInput();
	Start.TriggerValue = 1.f; Start.TargetZone = EBJJGripZone::CollarL; Start.NowMs = 0;
	FBJJHandTickInput Release = BaseInput();
	Release.NowMs = 100; Release.TriggerValue = 0.f; Release.TargetZone = EBJJGripZone::None;

	FRunResult R = RunSequence(Hand, { Start, Release });
	TestEqual(TEXT("final IDLE"), (uint8)R.Final.State, (uint8)EBJJHandState::Idle);
	TestEqual(TEXT("only REACH_STARTED fired"), R.EventKinds.Num(), 1);
	return true;
}

// -----------------------------------------------------------------------------
// §2.1.3 CONTACT resolution
// -----------------------------------------------------------------------------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMDefendedZoneParries,
	"BJJSimulator.State.HandFSM.Contact.DefendedZoneParries",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMDefendedZoneParries::RunTest(const FString&)
{
	const FBJJHandFSM Hand = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	auto Step = [](int64 T, EBJJGripZone Zone, float Trig, bool bDef = false)
	{
		FBJJHandTickInput In = BaseInput();
		In.NowMs = T; In.TriggerValue = Trig; In.TargetZone = Zone; In.bOpponentDefendsThisZone = bDef;
		return In;
	};
	TArray<FBJJHandTickInput> Steps = {
		Step(0,            EBJJGripZone::SleeveR, 1.f),
		Step(REACH_MID,    EBJJGripZone::SleeveR, 1.f),                   // CONTACT
		Step(REACH_MID+16, EBJJGripZone::SleeveR, 1.f, /*bDef=*/true),   // PARRIED
	};
	FRunResult R = RunSequence(Hand, Steps);
	TestTrue(TEXT("PARRIED event observed"),
		R.EventKinds.Contains(EBJJHandEventKind::Parried));
	TestEqual(TEXT("LastParriedZone remembered"),
		(uint8)R.Final.LastParriedZone, (uint8)EBJJGripZone::SleeveR);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMShortMemoryReparries,
	"BJJSimulator.State.HandFSM.Contact.ShortMemoryReparriesSameZoneWithin400ms",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMShortMemoryReparries::RunTest(const FString&)
{
	// Hand-crafted REACHING state with a recent parry baked in.
	FBJJHandFSM Primed = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	Primed.State = EBJJHandState::Reaching;
	Primed.Target = EBJJGripZone::SleeveR;
	Primed.StateEnteredMs = 100;
	Primed.ReachDurationMs = 100;
	Primed.LastParriedZone = EBJJGripZone::SleeveR;
	Primed.LastParriedAtMs = 50; // 150 ms before the tick below

	// Reach completes → CONTACT.
	FBJJHandTickInput InContact = BaseInput();
	InContact.NowMs = 200; InContact.TriggerValue = 1.f; InContact.TargetZone = EBJJGripZone::SleeveR;
	FBJJHandFSM Contacted;
	TArray<FBJJHandTickEvent> E1;
	BJJ::HandFSM::Tick(Primed, InContact, Contacted, E1);
	TestEqual(TEXT("CONTACT reached"), (uint8)Contacted.State, (uint8)EBJJHandState::Contact);

	// CONTACT frame: 150 ms since last parry → still within 400 ms memory.
	FBJJHandFSM Resolved;
	TArray<FBJJHandTickEvent> E2;
	BJJ::HandFSM::Tick(Contacted, InContact, Resolved, E2);
	TestEqual(TEXT("re-parried by memory"), (uint8)Resolved.State, (uint8)EBJJHandState::Parried);
	bool bSawParried = false;
	for (const auto& Ev : E2) { if (Ev.Kind == EBJJHandEventKind::Parried) { bSawParried = true; break; } }
	TestTrue(TEXT("PARRIED event emitted"), bSawParried);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMShortMemoryExpiredGrips,
	"BJJSimulator.State.HandFSM.Contact.ShortMemoryExpiredGrips",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMShortMemoryExpiredGrips::RunTest(const FString&)
{
	FBJJHandFSM Primed = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	Primed.State = EBJJHandState::Contact;
	Primed.Target = EBJJGripZone::SleeveR;
	Primed.StateEnteredMs = 500; // 450 ms after prior parry → outside window
	Primed.LastParriedZone = EBJJGripZone::SleeveR;
	Primed.LastParriedAtMs = 50;

	FBJJHandTickInput In = BaseInput();
	In.NowMs = 500; In.TriggerValue = 1.f; In.TargetZone = EBJJGripZone::SleeveR;
	FBJJHandFSM Next;
	TArray<FBJJHandTickEvent> Ev;
	BJJ::HandFSM::Tick(Primed, In, Next, Ev);
	TestEqual(TEXT("grips once memory expired"),
		(uint8)Next.State, (uint8)EBJJHandState::Gripped);
	return true;
}

// -----------------------------------------------------------------------------
// §2.1.4 GRIPPED break conditions
// -----------------------------------------------------------------------------

namespace
{
	FBJJHandFSM UpToGripped()
	{
		const FBJJHandFSM Hand = BJJ::HandFSM::Initial(EBJJHandSide::Right, 0);
		auto Step = [](int64 T, EBJJGripZone Zone, float Trig)
		{
			FBJJHandTickInput In = BaseInput();
			In.NowMs = T; In.TriggerValue = Trig; In.TargetZone = Zone; return In;
		};
		return RunSequence(Hand, {
			Step(0,            EBJJGripZone::Belt, 1.f),
			Step(REACH_MID,    EBJJGripZone::Belt, 1.f),
			Step(REACH_MID+16, EBJJGripZone::Belt, 1.f),
		}).Final;
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMTriggerReleasedBreaksGrip,
	"BJJSimulator.State.HandFSM.Gripped.TriggerReleaseBreaksGrip",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMTriggerReleasedBreaksGrip::RunTest(const FString&)
{
	const FBJJHandFSM Gripped = UpToGripped();
	FBJJHandTickInput In = BaseInput();
	In.NowMs = 1000; In.TriggerValue = 0.f; In.TargetZone = EBJJGripZone::None;
	FBJJHandFSM Next;
	TArray<FBJJHandTickEvent> Ev;
	BJJ::HandFSM::Tick(Gripped, In, Next, Ev);

	TestEqual(TEXT("state RETRACT"), (uint8)Next.State, (uint8)EBJJHandState::Retract);
	bool bSaw = false;
	for (const auto& E : Ev) {
		if (E.Kind == EBJJHandEventKind::GripBroken && E.GripBrokenReason == EBJJGripBrokenReason::TriggerReleased) { bSaw = true; break; }
	}
	TestTrue(TEXT("GRIP_BROKEN(TRIGGER_RELEASED)"), bSaw);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMForceReleaseBreaksGrip,
	"BJJSimulator.State.HandFSM.Gripped.ForceReleaseBreaksGrip",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMForceReleaseBreaksGrip::RunTest(const FString&)
{
	const FBJJHandFSM Gripped = UpToGripped();
	FBJJHandTickInput In = BaseInput();
	In.NowMs = 1000; In.TriggerValue = 1.f; In.TargetZone = EBJJGripZone::Belt;
	In.bForceReleaseAll = true;
	FBJJHandFSM Next;
	TArray<FBJJHandTickEvent> Ev;
	BJJ::HandFSM::Tick(Gripped, In, Next, Ev);

	TestEqual(TEXT("forced to RETRACT"), (uint8)Next.State, (uint8)EBJJHandState::Retract);
	bool bSaw = false;
	for (const auto& E : Ev) {
		if (E.Kind == EBJJHandEventKind::GripBroken && E.GripBrokenReason == EBJJGripBrokenReason::ForceRelease) { bSaw = true; break; }
	}
	TestTrue(TEXT("GRIP_BROKEN(FORCE_RELEASE)"), bSaw);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMOpponentCutBreaksGrip,
	"BJJSimulator.State.HandFSM.Gripped.OpponentCutBreaksGrip",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMOpponentCutBreaksGrip::RunTest(const FString&)
{
	const FBJJHandFSM Gripped = UpToGripped();
	FBJJHandTickInput In = BaseInput();
	In.NowMs = 1000; In.TriggerValue = 1.f; In.TargetZone = EBJJGripZone::Belt;
	In.bOpponentCutSucceeded = true;
	FBJJHandFSM Next;
	TArray<FBJJHandTickEvent> Ev;
	BJJ::HandFSM::Tick(Gripped, In, Next, Ev);

	bool bSaw = false;
	for (const auto& E : Ev) {
		if (E.Kind == EBJJHandEventKind::GripBroken && E.GripBrokenReason == EBJJGripBrokenReason::OpponentCut) { bSaw = true; break; }
	}
	TestTrue(TEXT("GRIP_BROKEN(OPPONENT_CUT)"), bSaw);
	return true;
}

// -----------------------------------------------------------------------------
// §2.1.2 RETRACT blocks new REACHING
// -----------------------------------------------------------------------------

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
	FBJJHandFSMRetractBlocksReaching,
	"BJJSimulator.State.HandFSM.Retract.BlocksNewReaching",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FBJJHandFSMRetractBlocksReaching::RunTest(const FString&)
{
	// Drive to RETRACT via the defended-zone path.
	FBJJHandFSM H = BJJ::HandFSM::Initial(EBJJHandSide::Left, 0);
	auto Step = [](int64 T, EBJJGripZone Zone, float Trig, bool bDef = false)
	{
		FBJJHandTickInput In = BaseInput();
		In.NowMs = T; In.TriggerValue = Trig; In.TargetZone = Zone; In.bOpponentDefendsThisZone = bDef;
		return In;
	};
	FBJJHandFSM AfterRetract = RunSequence(H, {
		Step(0,            EBJJGripZone::SleeveR, 1.f),
		Step(REACH_MID,    EBJJGripZone::SleeveR, 1.f),
		Step(REACH_MID+16, EBJJGripZone::SleeveR, 1.f, true),
		Step(REACH_MID+32, EBJJGripZone::SleeveR, 1.f),
	}).Final;
	TestEqual(TEXT("is RETRACT"), (uint8)AfterRetract.State, (uint8)EBJJHandState::Retract);

	// Mid-retract: new REACH attempt must be ignored.
	FBJJHandTickInput In = BaseInput();
	In.NowMs = AfterRetract.StateEnteredMs + BJJ::DEFAULT_HAND_TIMING.RetractMs / 2;
	In.TriggerValue = 1.f; In.TargetZone = EBJJGripZone::CollarL;
	FBJJHandFSM Next;
	TArray<FBJJHandTickEvent> Ev;
	BJJ::HandFSM::Tick(AfterRetract, In, Next, Ev);
	TestEqual(TEXT("stays RETRACT"), (uint8)Next.State, (uint8)EBJJHandState::Retract);
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
