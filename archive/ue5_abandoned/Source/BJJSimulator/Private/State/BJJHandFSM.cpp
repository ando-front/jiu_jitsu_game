// Copyright (c) BJJ Simulator project. See LICENSE.
//
// Ported 1:1 from src/prototype/web/src/state/hand_fsm.ts.
// Order of checks inside Tick() intentionally matches the TS source so
// a diff against Stage 1 behaviour stays legible.

#include "State/BJJHandFSM.h"

namespace
{
	FBJJHandFSM EnterIdle(const FBJJHandFSM& Prev, int64 NowMs)
	{
		FBJJHandFSM Next = Prev;
		Next.State = EBJJHandState::Idle;
		Next.Target = EBJJGripZone::None;
		Next.StateEnteredMs = NowMs;
		Next.ReachDurationMs = 0;
		return Next;
	}

	FBJJHandFSM EnterRetract(const FBJJHandFSM& Prev, int64 NowMs)
	{
		FBJJHandFSM Next = Prev;
		Next.State = EBJJHandState::Retract;
		Next.Target = EBJJGripZone::None;
		Next.StateEnteredMs = NowMs;
		Next.ReachDurationMs = 0;
		return Next;
	}

	int32 ChooseReachDuration(const BJJ::FHandTiming& Timing)
	{
		// §C.1.2 — "200–350ms, distance-dependent, linear". Stage 1 has no
		// world positions either; midpoint is deterministic and matches TS.
		return (Timing.ReachMinMs + Timing.ReachMaxMs) / 2;
	}
}

FBJJHandFSM BJJ::HandFSM::Initial(EBJJHandSide Side, int64 NowMs)
{
	FBJJHandFSM Hand;
	Hand.Side = Side;
	Hand.State = EBJJHandState::Idle;
	Hand.Target = EBJJGripZone::None;
	Hand.StateEnteredMs = NowMs;
	Hand.ReachDurationMs = 0;
	Hand.LastParriedZone = EBJJGripZone::None;
	Hand.LastParriedAtMs = BJJ::SENTINEL_TIME_MS;
	return Hand;
}

void BJJ::HandFSM::Tick(
	const FBJJHandFSM& Prev,
	const FBJJHandTickInput& Input,
	FBJJHandFSM& OutNext,
	TArray<FBJJHandTickEvent>& OutEvents,
	const BJJ::FHandTiming& Timing)
{
	OutNext = Prev;

	// Global escape: BTN_RELEASE forces engaged hands back through RETRACT
	// (§B.3 "事故の出口").
	const bool bWasEngaged =
		Prev.State == EBJJHandState::Gripped ||
		Prev.State == EBJJHandState::Contact ||
		Prev.State == EBJJHandState::Reaching;

	if (Input.bForceReleaseAll && bWasEngaged)
	{
		if (Prev.State == EBJJHandState::Gripped && Prev.Target != EBJJGripZone::None)
		{
			FBJJHandTickEvent Ev;
			Ev.Kind = EBJJHandEventKind::GripBroken;
			Ev.Side = Prev.Side;
			Ev.Zone = Prev.Target;
			Ev.GripBrokenReason = EBJJGripBrokenReason::ForceRelease;
			OutEvents.Add(Ev);
		}
		OutNext = EnterRetract(Prev, Input.NowMs);
		return;
	}

	switch (Prev.State)
	{
	case EBJJHandState::Idle:
	{
		// §2.1.2 — trigger press + a target zone kicks off REACHING.
		if (Input.TriggerValue > 0.f && Input.TargetZone != EBJJGripZone::None)
		{
			OutNext.State = EBJJHandState::Reaching;
			OutNext.Target = Input.TargetZone;
			OutNext.StateEnteredMs = Input.NowMs;
			OutNext.ReachDurationMs = ChooseReachDuration(Timing);

			FBJJHandTickEvent Ev;
			Ev.Kind = EBJJHandEventKind::ReachStarted;
			Ev.Side = Prev.Side;
			Ev.Zone = Input.TargetZone;
			OutEvents.Add(Ev);
		}
		break;
	}

	case EBJJHandState::Reaching:
	{
		// Abort back to IDLE if trigger released mid-reach. Nothing contacted,
		// so we skip RETRACT (quiet cancel).
		if (Input.TriggerValue == 0.f)
		{
			OutNext.State = EBJJHandState::Idle;
			OutNext.Target = EBJJGripZone::None;
			OutNext.StateEnteredMs = Input.NowMs;
			break;
		}
		// Zone re-aim: swing RS mid-reach → restart.
		if (Input.TargetZone != EBJJGripZone::None && Input.TargetZone != Prev.Target)
		{
			OutNext.Target = Input.TargetZone;
			OutNext.StateEnteredMs = Input.NowMs;
			OutNext.ReachDurationMs = ChooseReachDuration(Timing);

			FBJJHandTickEvent Ev;
			Ev.Kind = EBJJHandEventKind::ReachStarted;
			Ev.Side = Prev.Side;
			Ev.Zone = Input.TargetZone;
			OutEvents.Add(Ev);
			break;
		}
		// Reach timer expired → CONTACT (1 frame).
		if (Input.NowMs - Prev.StateEnteredMs >= Prev.ReachDurationMs)
		{
			OutNext.State = EBJJHandState::Contact;
			OutNext.StateEnteredMs = Input.NowMs;
			if (Prev.Target != EBJJGripZone::None)
			{
				FBJJHandTickEvent Ev;
				Ev.Kind = EBJJHandEventKind::Contact;
				Ev.Side = Prev.Side;
				Ev.Zone = Prev.Target;
				OutEvents.Add(Ev);
			}
		}
		break;
	}

	case EBJJHandState::Contact:
	{
		// §2.1.3 — resolution next frame. Priority: opponent-defends >
		// short-memory > grip.
		if (Prev.Target == EBJJGripZone::None)
		{
			OutNext = EnterIdle(Prev, Input.NowMs);
			break;
		}

		// §2.5 sentinel guard: skip the time delta when LastParriedAtMs is
		// the sentinel; otherwise INT64_MIN subtraction overflows.
		const bool bHasParryMemory = Prev.LastParriedAtMs != BJJ::SENTINEL_TIME_MS;
		const bool bRecentlyParried =
			bHasParryMemory
			&& Prev.LastParriedZone == Prev.Target
			&& (Input.NowMs - Prev.LastParriedAtMs) < Timing.ShortMemoryMs;

		if (Input.bOpponentDefendsThisZone || bRecentlyParried)
		{
			OutNext.State = EBJJHandState::Parried;
			OutNext.StateEnteredMs = Input.NowMs;
			OutNext.LastParriedZone = Prev.Target;
			OutNext.LastParriedAtMs = Input.NowMs;

			FBJJHandTickEvent Ev;
			Ev.Kind = EBJJHandEventKind::Parried;
			Ev.Side = Prev.Side;
			Ev.Zone = Prev.Target;
			OutEvents.Add(Ev);
		}
		else
		{
			OutNext.State = EBJJHandState::Gripped;
			OutNext.StateEnteredMs = Input.NowMs;

			FBJJHandTickEvent Ev;
			Ev.Kind = EBJJHandEventKind::Gripped;
			Ev.Side = Prev.Side;
			Ev.Zone = Prev.Target;
			OutEvents.Add(Ev);
		}
		break;
	}

	case EBJJHandState::Parried:
	{
		// §2.1.2 — PARRIED is 1 frame; next tick → RETRACT.
		OutNext = EnterRetract(Prev, Input.NowMs);
		break;
	}

	case EBJJHandState::Gripped:
	{
		// §2.1.4 — break conditions route back through RETRACT.
		const EBJJGripZone Zone = Prev.Target;
		auto EmitGripBroken = [&](EBJJGripBrokenReason Reason)
		{
			if (Zone == EBJJGripZone::None) return;
			FBJJHandTickEvent Ev;
			Ev.Kind = EBJJHandEventKind::GripBroken;
			Ev.Side = Prev.Side;
			Ev.Zone = Zone;
			Ev.GripBrokenReason = Reason;
			OutEvents.Add(Ev);
		};

		if (Input.bOpponentCutSucceeded)
		{
			EmitGripBroken(EBJJGripBrokenReason::OpponentCut);
			OutNext = EnterRetract(Prev, Input.NowMs);
			break;
		}
		if (Input.bTargetOutOfReach)
		{
			EmitGripBroken(EBJJGripBrokenReason::OutOfReach);
			OutNext = EnterRetract(Prev, Input.NowMs);
			break;
		}
		if (Input.TriggerValue == 0.f)
		{
			EmitGripBroken(EBJJGripBrokenReason::TriggerReleased);
			OutNext = EnterRetract(Prev, Input.NowMs);
			break;
		}
		// GRIPPED persists — strength is a pure function of the live trigger
		// read by downstream layers.
		break;
	}

	case EBJJHandState::Retract:
	{
		if (Input.NowMs - Prev.StateEnteredMs >= Timing.RetractMs)
		{
			OutNext = EnterIdle(Prev, Input.NowMs);
		}
		break;
	}
	}
}
