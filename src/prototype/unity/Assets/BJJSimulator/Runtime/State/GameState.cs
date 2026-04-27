// Ported 1:1 from src/prototype/web/src/state/game_state.ts.
// PURE — GameState aggregate and the top-level StepSimulation tick function.
// Reference: docs/design/architecture_overview_v1.md §7,
//            docs/design/state_machines_v1.md §10.
//
// Evaluation order (§10):
//   0. CutAttempts (before bottom actor so CUT_SUCCEEDED forces RETRACT same frame)
//   1. ActorState FSMs (hands / feet)
//   2. posture_break continuous update
//   3. stamina continuous update
//   4. arm_extracted flag update
//   5. sustained counters
//   6. GuardFSM check
//   7. JudgmentWindowFSM
//   7b. CounterWindow
//   8. ControlLayer (initiative)
//   9. PassAttempt
//   10. Session termination

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Guard state (one-way CLOSED → OPEN when both bottom feet are UNLOCKED)
    // -------------------------------------------------------------------------

    public enum GuardState { Closed, Open }

    // -------------------------------------------------------------------------
    // Shared actor state (owned by GameState, referenced by predicate callers)
    // -------------------------------------------------------------------------

    public struct ActorState
    {
        public HandFSM LeftHand;
        public HandFSM RightHand;
        public FootFSM LeftFoot;
        public FootFSM RightFoot;
        public Vec2    PostureBreak;
        public float   Stamina;
        public bool    ArmExtractedLeft;
        public bool    ArmExtractedRight;
    }

    // -------------------------------------------------------------------------
    // Time context
    // -------------------------------------------------------------------------

    public struct TimeContext
    {
        public float Scale;
        public float RealDtMs;
        public float GameDtMs;

        public static readonly TimeContext Initial = new TimeContext
        {
            Scale     = 1f,
            RealDtMs  = 0f,
            GameDtMs  = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Sustained counters (hip_bump 300ms rolling push)
    // -------------------------------------------------------------------------

    public struct SustainedCounters
    {
        public float HipPushMs;

        public static readonly SustainedCounters Initial = new SustainedCounters { HipPushMs = 0f };
    }

    // -------------------------------------------------------------------------
    // Top-level game state
    // -------------------------------------------------------------------------

    public struct GameState
    {
        public ActorState      Bottom;
        public ActorState      Top;
        public GuardState      Guard;
        public JudgmentWindow  JudgmentWindow;
        public CounterWindow   CounterWindow;
        public PassAttemptState PassAttempt;
        public CutAttempts     CutAttempts;
        public bool            SessionEnded;
        // §D.2 — sign snapshot of the attacker's lateral hip captured at OPENING.
        public int             AttackerSweepLateralSign;
        public TimeContext     Time;
        public SustainedCounters Sustained;
        public ArmExtractedState TopArmExtracted;
        public ControlLayer    Control;
        public int             FrameIndex;
        public long            NowMs;
    }

    // -------------------------------------------------------------------------
    // Session end reason
    // -------------------------------------------------------------------------

    public enum SessionEndReason { PassSuccess, TechniqueFinished, GuardOpened }

    // -------------------------------------------------------------------------
    // Step options
    // -------------------------------------------------------------------------

    public struct StepOptions
    {
        public float            RealDtMs;
        public float            GameDtMs;
        public Technique?       ConfirmedTechnique;  // null = no confirm
        public DefenseIntent?   DefenseIntent;       // null = passive top
        public CounterTechnique? ConfirmedCounter;   // null = no counter
    }

    // -------------------------------------------------------------------------
    // Unified simulation event (fat struct covering all sub-event types)
    // -------------------------------------------------------------------------

    public enum SimEventKind
    {
        // Hand
        HandReachStarted, HandContact, HandGripped, HandParried, HandGripBroken,
        // Foot
        FootUnlocked, FootLockingStarted, FootLockSucceeded, FootLockFailed,
        // JudgmentWindow
        WindowOpening, WindowOpen, WindowClosing, WindowClosed, TechniqueConfirmed,
        // CounterWindow
        CounterWindowOpening, CounterWindowOpen, CounterWindowClosing,
        CounterWindowClosed, CounterConfirmed,
        // PassAttempt
        PassStarted, PassFailed, PassSucceeded,
        // CutAttempt
        CutStarted, CutSucceeded, CutFailed,
        // GameState
        GuardOpened, SessionEnded,
    }

    public struct SimEvent
    {
        public SimEventKind Kind;

        // Hand fields
        public HandSide         HandSide;
        public GripZone         HandZone;
        public GripBrokenReason GripBrokenReason;

        // Foot fields
        public FootSide FootSide;

        // JudgmentWindow fields
        public Technique[]       WindowCandidates;
        public WindowSide        WindowFiredBy;
        public WindowCloseReason WindowCloseReason;
        public Technique         ConfirmedTechnique;

        // CounterWindow fields
        public CounterTechnique[] CounterCandidates;
        public CounterCloseReason CounterCloseReason;
        public CounterTechnique   ConfirmedCounter;

        // CutAttempt fields
        public HandSide CutDefenderSide;
        public HandSide CutAttackerSide;
        public GripZone CutZone;

        // SessionEnded
        public SessionEndReason SessionEndReason;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class GameStateOps
    {
        public static ActorState InitialActorState(long nowMs = 0) =>
            new ActorState
            {
                LeftHand          = HandFSMOps.Initial(HandSide.L, nowMs),
                RightHand         = HandFSMOps.Initial(HandSide.R, nowMs),
                LeftFoot          = FootFSMOps.Initial(FootSide.L, nowMs),
                RightFoot         = FootFSMOps.Initial(FootSide.R, nowMs),
                PostureBreak      = Vec2.Zero,
                Stamina           = 1f,
                ArmExtractedLeft  = false,
                ArmExtractedRight = false,
            };

        public static GameState InitialGameState(long nowMs = 0) =>
            new GameState
            {
                Bottom                    = InitialActorState(nowMs),
                Top                       = InitialActorState(nowMs),
                Guard                     = GuardState.Closed,
                JudgmentWindow            = JudgmentWindow.Initial,
                CounterWindow             = CounterWindow.Initial,
                PassAttempt               = PassAttemptState.Idle,
                CutAttempts               = CutAttempts.Initial,
                SessionEnded              = false,
                AttackerSweepLateralSign  = 0,
                Time                      = TimeContext.Initial,
                Sustained                 = SustainedCounters.Initial,
                TopArmExtracted           = ArmExtractedState.Initial,
                Control                   = ControlLayer.Initial,
                FrameIndex                = 0,
                NowMs                     = nowMs,
            };

        /// <summary>
        /// Advance simulation by one tick. Returns (nextState, events).
        /// </summary>
        public static (GameState NextState, SimEvent[] Events) Step(
            GameState prev,
            InputFrame frame,
            Intent intent,
            StepOptions opts = default)
        {
            var events = new List<SimEvent>(32);
            long nowMs = frame.Timestamp;
            var defense = opts.DefenseIntent;

            // §5.3 — clamp trigger values by stamina ceiling.
            float bottomCeiling   = StaminaOps.GripStrengthCeiling(prev.Bottom.Stamina);
            float effectiveTriggerL = System.Math.Min(frame.LTrigger, bottomCeiling);
            float effectiveTriggerR = System.Math.Min(frame.RTrigger, bottomCeiling);

            // ----------------------------------------------------------------
            // 0. Cut-attempt tick (before bottom actor HandFSM).
            // ----------------------------------------------------------------
            CutCommit? leftCutCommit  = null;
            CutCommit? rightCutCommit = null;
            if (defense.HasValue)
            {
                foreach (var d in defense.Value.Discrete ?? System.Array.Empty<DefenseDiscreteIntent>())
                {
                    if (d.Kind == DefenseDiscreteIntentKind.CutAttempt)
                    {
                        if (d.CutSide == HandSide.L) leftCutCommit  = new CutCommit { Rs = d.Rs };
                        else                          rightCutCommit = new CutCommit { Rs = d.Rs };
                    }
                }
            }
            var cutEvents = new List<CutTickEvent>();
            var nextCutAttempts = CutAttemptOps.Tick(prev.CutAttempts, new CutTickInput
            {
                NowMs            = nowMs,
                LeftCommit       = leftCutCommit,
                RightCommit      = rightCutCommit,
                AttackerLeft     = prev.Bottom.LeftHand,
                AttackerRight    = prev.Bottom.RightHand,
                AttackerTriggerL = effectiveTriggerL,
                AttackerTriggerR = effectiveTriggerR,
            }, cutEvents);
            PushCutEvents(events, cutEvents);

            bool cutSucceededLeft  = false;
            bool cutSucceededRight = false;
            foreach (var e in cutEvents)
            {
                if (e.Kind == CutEventKind.CutSucceeded && e.AttackerSide == HandSide.L) cutSucceededLeft  = true;
                if (e.Kind == CutEventKind.CutSucceeded && e.AttackerSide == HandSide.R) cutSucceededRight = true;
            }

            // ----------------------------------------------------------------
            // 1. ActorState FSMs.
            // ----------------------------------------------------------------
            var nextBottomFsm = TickBottomActor(
                prev.Bottom, prev.Top, frame, intent, nowMs, events,
                effectiveTriggerL, effectiveTriggerR,
                cutSucceededLeft, cutSucceededRight);

            var nextTopFsm = TickTopActorPassive(prev.Top, nowMs);

            // ----------------------------------------------------------------
            // 2. posture_break.
            // ----------------------------------------------------------------
            var gripPulls = new List<Vec2>(2);
            if (nextBottomFsm.LeftHand.State  == HandState.Gripped && nextBottomFsm.LeftHand.Target  != GripZone.None)
                gripPulls.Add(PostureBreakOps.GripPullVector(nextBottomFsm.LeftHand.Target,  effectiveTriggerL));
            if (nextBottomFsm.RightHand.State == HandState.Gripped && nextBottomFsm.RightHand.Target != GripZone.None)
                gripPulls.Add(PostureBreakOps.GripPullVector(nextBottomFsm.RightHand.Target, effectiveTriggerR));

            var defenderRecovery = ComputeDefenderRecovery(defense);
            var nextTopPosture   = PostureBreakOps.Update(prev.Top.PostureBreak, new PostureBreakInputs
            {
                DtMs             = opts.GameDtMs,
                AttackerHip      = intent.Hip,
                GripPulls        = gripPulls.ToArray(),
                DefenderRecovery = defenderRecovery,
            });

            // ----------------------------------------------------------------
            // 3. arm_extracted.
            // ----------------------------------------------------------------
            var nextArmExtracted = ArmExtractedOps.Update(prev.TopArmExtracted, new ArmExtractedInputs
            {
                NowMs           = nowMs,
                DtMs            = opts.GameDtMs,
                BottomLeftHand  = nextBottomFsm.LeftHand,
                BottomRightHand = nextBottomFsm.RightHand,
                TriggerL        = effectiveTriggerL,
                TriggerR        = effectiveTriggerR,
                AttackerHip     = intent.Hip,
                DefenderBaseHold = DefenderIsBasingBicep(defense),
            });

            var nextTop = new ActorState
            {
                LeftHand          = nextTopFsm.LeftHand,
                RightHand         = nextTopFsm.RightHand,
                LeftFoot          = nextTopFsm.LeftFoot,
                RightFoot         = nextTopFsm.RightFoot,
                PostureBreak      = nextTopPosture,
                Stamina           = nextTopFsm.Stamina,
                ArmExtractedLeft  = nextArmExtracted.Left,
                ArmExtractedRight = nextArmExtracted.Right,
            };

            // ----------------------------------------------------------------
            // 4. Stamina.
            // ----------------------------------------------------------------
            bool breathPressed = (frame.Buttons & ButtonBit.BtnBreath) != 0;
            float nextBottomStamina = StaminaOps.UpdateStamina(prev.Bottom.Stamina, new StaminaInputs
            {
                DtMs          = opts.GameDtMs,
                Actor         = nextBottomFsm,
                AttackerHip   = intent.Hip,
                TriggerL      = effectiveTriggerL,
                TriggerR      = effectiveTriggerR,
                BreathPressed = breathPressed,
            });

            float nextTopStamina = prev.Top.Stamina;
            if (defense.HasValue)
            {
                bool topBreath = false;
                foreach (var d in defense.Value.Discrete ?? System.Array.Empty<DefenseDiscreteIntent>())
                    if (d.Kind == DefenseDiscreteIntentKind.BreathStart) { topBreath = true; break; }

                nextTopStamina = StaminaOps.UpdateStaminaDefender(prev.Top.Stamina, new StaminaDefenderInputs
                {
                    DtMs               = opts.GameDtMs,
                    Actor              = nextTop,
                    LeftBasePressure   = defense.Value.Base.LBasePressure,
                    RightBasePressure  = defense.Value.Base.RBasePressure,
                    WeightForward      = defense.Value.Hip.WeightForward,
                    WeightLateral      = defense.Value.Hip.WeightLateral,
                    BreathPressed      = topBreath,
                });
            }

            // ----------------------------------------------------------------
            // 5. Sustained counters.
            // ----------------------------------------------------------------
            bool pushActive = intent.Hip.HipPush >= 0.5f;
            var nextSustained = new SustainedCounters
            {
                HipPushMs = pushActive ? prev.Sustained.HipPushMs + opts.GameDtMs : 0f,
            };

            // ----------------------------------------------------------------
            // 6. GuardFSM.
            // ----------------------------------------------------------------
            var nextGuard = prev.Guard;
            if (prev.Guard == GuardState.Closed &&
                nextBottomFsm.LeftFoot.State  == FootState.Unlocked &&
                nextBottomFsm.RightFoot.State == FootState.Unlocked)
            {
                nextGuard = GuardState.Open;
                events.Add(new SimEvent { Kind = SimEventKind.GuardOpened });
            }

            var nextBottom = new ActorState
            {
                LeftHand          = nextBottomFsm.LeftHand,
                RightHand         = nextBottomFsm.RightHand,
                LeftFoot          = nextBottomFsm.LeftFoot,
                RightFoot         = nextBottomFsm.RightFoot,
                PostureBreak      = nextBottomFsm.PostureBreak,
                Stamina           = nextBottomStamina,
                ArmExtractedLeft  = nextBottomFsm.ArmExtractedLeft,
                ArmExtractedRight = nextBottomFsm.ArmExtractedRight,
            };

            // ----------------------------------------------------------------
            // 7. JudgmentWindowFSM.
            // ----------------------------------------------------------------
            var ctx = new JudgmentContext
            {
                Bottom             = nextBottom,
                Top                = nextTop,
                BottomHipYaw       = intent.Hip.HipAngleTarget,
                BottomHipPush      = intent.Hip.HipPush,
                SustainedHipPushMs = nextSustained.HipPushMs,
            };
            var satisfied = JudgmentWindowOps.EvaluateAllTechniques(ctx, effectiveTriggerL, effectiveTriggerR);
            bool dismissRequested = (frame.ButtonEdges & ButtonBit.BtnRelease) != 0;

            var judgeEvents = new List<JudgmentTickEvent>();
            float judgeTimeScale;
            var nextJudgeWindow = JudgmentWindowOps.Tick(
                prev.JudgmentWindow,
                satisfied,
                new JudgmentTickInput
                {
                    NowMs              = nowMs,
                    ConfirmedTechnique = opts.ConfirmedTechnique,
                    DismissRequested   = dismissRequested,
                },
                judgeEvents,
                out judgeTimeScale);
            PushJudgmentEvents(events, judgeEvents);

            // ----------------------------------------------------------------
            // 7b. CounterWindow.
            // ----------------------------------------------------------------
            Technique[] openingCandidates = null;
            bool attackerWindowActive =
                nextJudgeWindow.State == JudgmentWindowState.Opening ||
                nextJudgeWindow.State == JudgmentWindowState.Open;

            foreach (var e in judgeEvents)
            {
                if (e.Kind == JudgmentEventKind.WindowOpening)
                {
                    openingCandidates = e.Candidates;
                    break;
                }
            }

            var counterOpeningSeed = openingCandidates != null
                ? CounterWindowOps.CounterCandidatesFor(openingCandidates)
                : System.Array.Empty<CounterTechnique>();

            var counterEvents = new List<CounterTickEvent>();
            float counterTimeScale;
            var nextCounterWindow = CounterWindowOps.Tick(
                prev.CounterWindow,
                new CounterTickInput
                {
                    NowMs              = nowMs,
                    OpenAttackerWindow = attackerWindowActive,
                    OpeningSeed        = counterOpeningSeed,
                    ConfirmedCounter   = opts.ConfirmedCounter,
                    DismissRequested   = dismissRequested,
                },
                counterEvents,
                out counterTimeScale);
            PushCounterEvents(events, counterEvents);

            // Snapshot attacker lateral sign at attacker OPENING.
            int attackerSweepLateralSign = prev.AttackerSweepLateralSign;
            if (openingCandidates != null)
                attackerSweepLateralSign = intent.Hip.HipLateral > 0f ? 1 : intent.Hip.HipLateral < 0f ? -1 : 0;

            // Counter success side-effects (§D.2).
            var finalJudgeWindow     = nextJudgeWindow;
            var armExtractedAfterCounter = nextArmExtracted;

            foreach (var e in counterEvents)
            {
                if (e.Kind == CounterEventKind.CounterConfirmed)
                {
                    // Force attacker's window into CLOSING.
                    if (finalJudgeWindow.State == JudgmentWindowState.Open ||
                        finalJudgeWindow.State == JudgmentWindowState.Opening)
                    {
                        finalJudgeWindow = new JudgmentWindow
                        {
                            State           = JudgmentWindowState.Closing,
                            StateEnteredMs  = nowMs,
                            Candidates      = finalJudgeWindow.Candidates,
                            CooldownUntilMs = finalJudgeWindow.CooldownUntilMs,
                            FiredBy         = finalJudgeWindow.FiredBy,
                        };
                    }
                    if (e.ConfirmedCounter == CounterTechnique.TriangleEarlyStack)
                    {
                        // §D.2 — clear arm_extracted both sides.
                        armExtractedAfterCounter = new ArmExtractedState
                        {
                            Left           = false,
                            Right          = false,
                            LeftSustainMs  = 0f,
                            RightSustainMs = 0f,
                            LeftSetAtMs    = BJJConst.SentinelTimeMs,
                            RightSetAtMs   = BJJConst.SentinelTimeMs,
                        };
                    }
                    break; // only one counter can fire per tick
                }
            }

            // §5.2 — confirm cost on attacker stamina.
            bool confirmedThisTick = false;
            foreach (var e in judgeEvents)
                if (e.Kind == JudgmentEventKind.TechniqueConfirmed) { confirmedThisTick = true; break; }

            var bottomAfterConfirm = nextBottom;
            if (confirmedThisTick)
                bottomAfterConfirm.Stamina = StaminaOps.ApplyConfirmCost(nextBottom.Stamina);

            // Counter confirm cost on defender stamina.
            bool counterConfirmedThisTick = false;
            foreach (var e in counterEvents)
                if (e.Kind == CounterEventKind.CounterConfirmed) { counterConfirmedThisTick = true; break; }

            float topStaminaFinal = counterConfirmedThisTick
                ? StaminaOps.ApplyConfirmCost(nextTopStamina)
                : nextTopStamina;

            var topAfterCounter = new ActorState
            {
                LeftHand          = nextTop.LeftHand,
                RightHand         = nextTop.RightHand,
                LeftFoot          = nextTop.LeftFoot,
                RightFoot         = nextTop.RightFoot,
                PostureBreak      = nextTop.PostureBreak,
                Stamina           = topStaminaFinal,
                ArmExtractedLeft  = armExtractedAfterCounter.Left,
                ArmExtractedRight = armExtractedAfterCounter.Right,
            };

            // Combined time scale (§D.3 "slow-mo doesn't double up").
            float combinedScale = System.Math.Min(judgeTimeScale, counterTimeScale);
            var nextTime = new TimeContext
            {
                Scale    = combinedScale,
                RealDtMs = opts.RealDtMs,
                GameDtMs = opts.GameDtMs,
            };

            // ----------------------------------------------------------------
            // 8. ControlLayer.
            // ----------------------------------------------------------------
            bool defenderCutInProgress =
                nextCutAttempts.Left.Kind  == CutSlotKind.InProgress ||
                nextCutAttempts.Right.Kind == CutSlotKind.InProgress;

            var nextControl = ControlLayerOps.Update(prev.Control, new ControlLayerInputs
            {
                JudgmentWindow        = finalJudgeWindow,
                Bottom                = bottomAfterConfirm,
                Top                   = topAfterCounter,
                DefenderCutInProgress = defenderCutInProgress,
            });

            // ----------------------------------------------------------------
            // 9. PassAttempt.
            // ----------------------------------------------------------------
            bool passCommitRequested = false;
            if (defense.HasValue)
                foreach (var d in defense.Value.Discrete ?? System.Array.Empty<DefenseDiscreteIntent>())
                    if (d.Kind == DefenseDiscreteIntentKind.PassCommit) { passCommitRequested = true; break; }

            bool passEligible = false;
            if (defense.HasValue)
            {
                passEligible = PassAttemptOps.IsPassEligible(new PassEligibilityParams
                {
                    Bottom             = bottomAfterConfirm,
                    Top                = topAfterCounter,
                    DefenderStamina    = topAfterCounter.Stamina,
                    LeftBasePressure   = defense.Value.Base.LBasePressure,
                    RightBasePressure  = defense.Value.Base.RBasePressure,
                    LeftBaseZone       = defense.Value.Base.LHandTarget,
                    RightBaseZone      = defense.Value.Base.RHandTarget,
                    RsY                = frame.Rs.Y,
                    Guard              = nextGuard,
                });
            }

            bool triangleThisTick = false;
            foreach (var e in judgeEvents)
                if (e.Kind == JudgmentEventKind.TechniqueConfirmed && e.ConfirmedTechnique == Technique.Triangle)
                    { triangleThisTick = true; break; }

            var passEvents = new List<PassTickEvent>();
            var nextPassAttempt = PassAttemptOps.Tick(prev.PassAttempt, new PassTickInput
            {
                NowMs                         = nowMs,
                CommitRequested               = passCommitRequested,
                EligibleNow                   = passEligible,
                AttackerTriangleConfirmedThisTick = triangleThisTick,
            }, passEvents);
            PushPassEvents(events, passEvents);

            // ----------------------------------------------------------------
            // 10. Session termination.
            // ----------------------------------------------------------------
            bool sessionEnded = prev.SessionEnded;
            if (!sessionEnded)
            {
                bool passSucceeded = false;
                foreach (var e in passEvents) if (e.Kind == PassEventKind.PassSucceeded) { passSucceeded = true; break; }

                if (passSucceeded)
                {
                    sessionEnded = true;
                    events.Add(new SimEvent { Kind = SimEventKind.SessionEnded, SessionEndReason = SessionEndReason.PassSuccess });
                }
                else if (confirmedThisTick)
                {
                    sessionEnded = true;
                    events.Add(new SimEvent { Kind = SimEventKind.SessionEnded, SessionEndReason = SessionEndReason.TechniqueFinished });
                }
                else if (nextGuard == GuardState.Open && prev.Guard == GuardState.Closed)
                {
                    sessionEnded = true;
                    events.Add(new SimEvent { Kind = SimEventKind.SessionEnded, SessionEndReason = SessionEndReason.GuardOpened });
                }
            }

            var nextState = new GameState
            {
                Bottom                   = bottomAfterConfirm,
                Top                      = topAfterCounter,
                Guard                    = nextGuard,
                JudgmentWindow           = finalJudgeWindow,
                CounterWindow            = nextCounterWindow,
                PassAttempt              = nextPassAttempt,
                CutAttempts              = nextCutAttempts,
                SessionEnded             = sessionEnded,
                AttackerSweepLateralSign = attackerSweepLateralSign,
                Time                     = nextTime,
                Sustained                = nextSustained,
                TopArmExtracted          = armExtractedAfterCounter,
                Control                  = nextControl,
                FrameIndex               = prev.FrameIndex + 1,
                NowMs                    = nowMs,
            };

            return (nextState, events.ToArray());
        }

        // -----------------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------------

        static ActorState TickBottomActor(
            ActorState prev,
            ActorState top,
            InputFrame frame,
            Intent intent,
            long nowMs,
            List<SimEvent> events,
            float effectiveTriggerL,
            float effectiveTriggerR,
            bool cutSucceededOnLeft,
            bool cutSucceededOnRight)
        {
            bool forceReleaseAll = (frame.ButtonEdges & ButtonBit.BtnRelease) != 0;

            var handEvents = new List<HandTickEvent>();
            var nextLeftHand = HandFSMOps.Tick(prev.LeftHand, new HandTickInput
            {
                NowMs               = nowMs,
                TriggerValue        = effectiveTriggerL,
                TargetZone          = intent.Grip.LHandTarget,
                ForceReleaseAll     = forceReleaseAll,
                OpponentDefendsThisZone = false,
                OpponentCutSucceeded = cutSucceededOnLeft,
                TargetOutOfReach    = false,
            }, handEvents);
            PushHandEvents(events, handEvents);
            handEvents.Clear();

            var nextRightHand = HandFSMOps.Tick(prev.RightHand, new HandTickInput
            {
                NowMs               = nowMs,
                TriggerValue        = effectiveTriggerR,
                TargetZone          = intent.Grip.RHandTarget,
                ForceReleaseAll     = forceReleaseAll,
                OpponentDefendsThisZone = false,
                OpponentCutSucceeded = cutSucceededOnRight,
                TargetOutOfReach    = false,
            }, handEvents);
            PushHandEvents(events, handEvents);

            bool leftBumperEdge  = false;
            bool rightBumperEdge = false;
            foreach (var d in intent.Discrete ?? System.Array.Empty<DiscreteIntent>())
            {
                if (d.Kind == DiscreteIntentKind.FootHookToggle && d.FootSide == FootSide.L) leftBumperEdge  = true;
                if (d.Kind == DiscreteIntentKind.FootHookToggle && d.FootSide == FootSide.R) rightBumperEdge = true;
            }

            var footEvents = new List<FootTickEvent>();
            var nextLeftFoot = FootFSMOps.Tick(prev.LeftFoot, new FootTickInput
            {
                NowMs                   = nowMs,
                BumperEdge              = leftBumperEdge,
                OpponentPostureSagittal = top.PostureBreak.Y,
            }, footEvents);
            PushFootEvents(events, footEvents);
            footEvents.Clear();

            var nextRightFoot = FootFSMOps.Tick(prev.RightFoot, new FootTickInput
            {
                NowMs                   = nowMs,
                BumperEdge              = rightBumperEdge,
                OpponentPostureSagittal = top.PostureBreak.Y,
            }, footEvents);
            PushFootEvents(events, footEvents);

            return new ActorState
            {
                LeftHand          = nextLeftHand,
                RightHand         = nextRightHand,
                LeftFoot          = nextLeftFoot,
                RightFoot         = nextRightFoot,
                PostureBreak      = prev.PostureBreak,
                Stamina           = prev.Stamina,
                ArmExtractedLeft  = prev.ArmExtractedLeft,
                ArmExtractedRight = prev.ArmExtractedRight,
            };
        }

        static ActorState TickTopActorPassive(ActorState prev, long nowMs)
        {
            var handEvents = new List<HandTickEvent>();
            var rest = new HandTickInput
            {
                NowMs               = nowMs,
                TriggerValue        = 0f,
                TargetZone          = GripZone.None,
                ForceReleaseAll     = false,
                OpponentDefendsThisZone = false,
                OpponentCutSucceeded = false,
                TargetOutOfReach    = false,
            };
            var leftHand  = HandFSMOps.Tick(prev.LeftHand,  rest, handEvents);
            handEvents.Clear();
            var rightHand = HandFSMOps.Tick(prev.RightHand, rest, handEvents);

            var footEvents = new List<FootTickEvent>();
            var footRest = new FootTickInput { NowMs = nowMs, BumperEdge = false, OpponentPostureSagittal = 0f };
            var leftFoot  = FootFSMOps.Tick(prev.LeftFoot,  footRest, footEvents);
            footEvents.Clear();
            var rightFoot = FootFSMOps.Tick(prev.RightFoot, footRest, footEvents);

            return new ActorState
            {
                LeftHand          = leftHand,
                RightHand         = rightHand,
                LeftFoot          = leftFoot,
                RightFoot         = rightFoot,
                PostureBreak      = prev.PostureBreak,
                Stamina           = prev.Stamina,
                ArmExtractedLeft  = prev.ArmExtractedLeft,
                ArmExtractedRight = prev.ArmExtractedRight,
            };
        }

        // --- defender recovery helpers ---------------------------------------

        static Vec2 ComputeDefenderRecovery(DefenseIntent? defense)
        {
            if (!defense.HasValue) return Vec2.Zero;
            float x = defense.Value.Hip.WeightLateral;
            float y = defense.Value.Hip.WeightForward;

            if (defense.Value.Base.LHandTarget == BaseZone.Chest || defense.Value.Base.LHandTarget == BaseZone.Hip)
                y += defense.Value.Base.LBasePressure * 0.5f;
            if (defense.Value.Base.RHandTarget == BaseZone.Chest || defense.Value.Base.RHandTarget == BaseZone.Hip)
                y += defense.Value.Base.RBasePressure * 0.5f;

            float mag = (float)System.Math.Sqrt(x * x + y * y);
            if (mag > 1f) { float s = 1f / mag; x *= s; y *= s; }
            return new Vec2(x, y);
        }

        static bool DefenderIsBasingBicep(DefenseIntent? defense)
        {
            if (!defense.HasValue) return false;
            bool lBicep =
                (defense.Value.Base.LHandTarget == BaseZone.BicepL || defense.Value.Base.LHandTarget == BaseZone.BicepR) &&
                defense.Value.Base.LBasePressure >= 0.6f;
            bool rBicep =
                (defense.Value.Base.RHandTarget == BaseZone.BicepL || defense.Value.Base.RHandTarget == BaseZone.BicepR) &&
                defense.Value.Base.RBasePressure >= 0.6f;
            bool recoveryHold = false;
            foreach (var d in defense.Value.Discrete ?? System.Array.Empty<DefenseDiscreteIntent>())
                if (d.Kind == DefenseDiscreteIntentKind.RecoveryHold) { recoveryHold = true; break; }
            return lBicep || rBicep || recoveryHold;
        }

        // --- event push helpers ----------------------------------------------

        static void PushHandEvents(List<SimEvent> target, List<HandTickEvent> src)
        {
            foreach (var e in src)
            {
                var se = new SimEvent { HandSide = e.Side, HandZone = e.Zone, GripBrokenReason = e.GripBrokenReason };
                se.Kind = e.Kind switch
                {
                    HandEventKind.ReachStarted => SimEventKind.HandReachStarted,
                    HandEventKind.Contact      => SimEventKind.HandContact,
                    HandEventKind.Gripped      => SimEventKind.HandGripped,
                    HandEventKind.Parried      => SimEventKind.HandParried,
                    HandEventKind.GripBroken   => SimEventKind.HandGripBroken,
                    _                          => SimEventKind.HandReachStarted,
                };
                target.Add(se);
            }
        }

        static void PushFootEvents(List<SimEvent> target, List<FootTickEvent> src)
        {
            foreach (var e in src)
            {
                var se = new SimEvent { FootSide = e.Side };
                se.Kind = e.Kind switch
                {
                    FootEventKind.Unlocked      => SimEventKind.FootUnlocked,
                    FootEventKind.LockingStarted => SimEventKind.FootLockingStarted,
                    FootEventKind.LockSucceeded  => SimEventKind.FootLockSucceeded,
                    FootEventKind.LockFailed     => SimEventKind.FootLockFailed,
                    _                            => SimEventKind.FootUnlocked,
                };
                target.Add(se);
            }
        }

        static void PushJudgmentEvents(List<SimEvent> target, List<JudgmentTickEvent> src)
        {
            foreach (var e in src)
            {
                var se = new SimEvent
                {
                    WindowCandidates  = e.Candidates,
                    WindowFiredBy     = e.FiredBy,
                    WindowCloseReason = e.CloseReason,
                    ConfirmedTechnique = e.ConfirmedTechnique,
                };
                se.Kind = e.Kind switch
                {
                    JudgmentEventKind.WindowOpening      => SimEventKind.WindowOpening,
                    JudgmentEventKind.WindowOpen         => SimEventKind.WindowOpen,
                    JudgmentEventKind.WindowClosing      => SimEventKind.WindowClosing,
                    JudgmentEventKind.WindowClosed       => SimEventKind.WindowClosed,
                    JudgmentEventKind.TechniqueConfirmed => SimEventKind.TechniqueConfirmed,
                    _                                    => SimEventKind.WindowOpening,
                };
                target.Add(se);
            }
        }

        static void PushCounterEvents(List<SimEvent> target, List<CounterTickEvent> src)
        {
            foreach (var e in src)
            {
                var se = new SimEvent
                {
                    CounterCandidates = e.Candidates,
                    CounterCloseReason = e.CloseReason,
                    ConfirmedCounter  = e.ConfirmedCounter,
                };
                se.Kind = e.Kind switch
                {
                    CounterEventKind.CounterWindowOpening => SimEventKind.CounterWindowOpening,
                    CounterEventKind.CounterWindowOpen    => SimEventKind.CounterWindowOpen,
                    CounterEventKind.CounterWindowClosing => SimEventKind.CounterWindowClosing,
                    CounterEventKind.CounterWindowClosed  => SimEventKind.CounterWindowClosed,
                    CounterEventKind.CounterConfirmed     => SimEventKind.CounterConfirmed,
                    _                                     => SimEventKind.CounterWindowOpening,
                };
                target.Add(se);
            }
        }

        static void PushCutEvents(List<SimEvent> target, List<CutTickEvent> src)
        {
            foreach (var e in src)
            {
                var se = new SimEvent
                {
                    CutDefenderSide = e.DefenderSide,
                    CutAttackerSide = e.AttackerSide,
                    CutZone         = e.Zone,
                };
                se.Kind = e.Kind switch
                {
                    CutEventKind.CutStarted   => SimEventKind.CutStarted,
                    CutEventKind.CutSucceeded => SimEventKind.CutSucceeded,
                    CutEventKind.CutFailed    => SimEventKind.CutFailed,
                    _                         => SimEventKind.CutStarted,
                };
                target.Add(se);
            }
        }

        static void PushPassEvents(List<SimEvent> target, List<PassTickEvent> src)
        {
            foreach (var e in src)
            {
                var se = new SimEvent();
                se.Kind = e.Kind switch
                {
                    PassEventKind.PassStarted   => SimEventKind.PassStarted,
                    PassEventKind.PassFailed    => SimEventKind.PassFailed,
                    PassEventKind.PassSucceeded => SimEventKind.PassSucceeded,
                    _                           => SimEventKind.PassStarted,
                };
                target.Add(se);
            }
        }
    }
}
