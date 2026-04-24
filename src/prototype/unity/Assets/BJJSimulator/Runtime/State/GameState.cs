// Ported from src/prototype/web/src/state/game_state.ts.
// See docs/design/state_machines_v1.md §10 for evaluation order.
//
// This file contains:
//   - ActorState     (aggregate per-actor snapshot)
//   - GuardState     (one-way CLOSED→OPEN)
//   - TimeContext
//   - SustainedCounters
//   - GameState      (full simulation snapshot)
//   - GameStateOps   (StepSimulation entry point)
//
// For the pure sub-system functions (Stamina, PostureBreak, etc.) see their
// own files in Runtime/State/.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Actor state aggregate (§10 — one per actor: bottom / top)
    // -------------------------------------------------------------------------

    public struct ActorState
    {
        public HandFSM LeftHand;
        public HandFSM RightHand;
        public FootFSM LeftFoot;
        public FootFSM RightFoot;
        /// <summary>(lateral, sagittal) posture-break vector; see PostureBreak.cs.</summary>
        public Vec2    PostureBreak;
        /// <summary>Scalar [0, 1]; 1 = full stamina.</summary>
        public float   Stamina;
        public bool    ArmExtractedLeft;
        public bool    ArmExtractedRight;

        public static ActorState Initial(long nowMs = 0) => new ActorState
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
    }

    // -------------------------------------------------------------------------
    // Guard state (§6 M1 scope)
    // -------------------------------------------------------------------------

    public enum GuardState { Closed, Open }

    // -------------------------------------------------------------------------
    // Time context
    // -------------------------------------------------------------------------

    public struct TimeContext
    {
        /// <summary>Current time scale; 1.0 = normal, 0.3 = judgment-window slow-mo.</summary>
        public float Scale;
        public float RealDtMs;
        public float GameDtMs;

        public static readonly TimeContext Initial = new TimeContext
        {
            Scale    = 1f,
            RealDtMs = 0f,
            GameDtMs = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Sustained counters (hip_bump's 300ms rolling push — §D.1.1)
    // -------------------------------------------------------------------------

    public struct SustainedCounters
    {
        public float HipPushMs;

        public static readonly SustainedCounters Initial = new SustainedCounters { HipPushMs = 0f };
    }

    // -------------------------------------------------------------------------
    // Full game state snapshot
    // -------------------------------------------------------------------------

    public struct GameState
    {
        public ActorState        Bottom;
        public ActorState        Top;
        public GuardState        Guard;
        public JudgmentWindow    JudgmentWindow;
        public CounterWindow     CounterWindow;
        public PassAttemptState  PassAttempt;
        public CutAttempts       CutAttempts;
        public bool              SessionEnded;
        /// <summary>
        /// Sign of the attacker's lateral hip input captured at attacker-window
        /// OPENING. Used by LayerD_defense for SCISSOR_COUNTER evaluation.
        /// </summary>
        public int               AttackerSweepLateralSign;
        public TimeContext       Time;
        public SustainedCounters Sustained;
        public ArmExtractedState TopArmExtracted;
        public ControlLayer      Control;
        public int               FrameIndex;
        public long              NowMs;

        public static GameState Initial(long nowMs = 0) => new GameState
        {
            Bottom                   = ActorState.Initial(nowMs),
            Top                      = ActorState.Initial(nowMs),
            Guard                    = GuardState.Closed,
            JudgmentWindow           = JudgmentWindowOps.Initial,
            CounterWindow            = CounterWindowOps.Initial,
            PassAttempt              = PassAttemptOps.Initial,
            CutAttempts              = CutAttemptOps.Initial,
            SessionEnded             = false,
            AttackerSweepLateralSign = 0,
            Time                     = TimeContext.Initial,
            Sustained                = SustainedCounters.Initial,
            TopArmExtracted          = ArmExtractedOps.Initial,
            Control                  = ControlLayerOps.Initial,
            FrameIndex               = 0,
            NowMs                    = nowMs,
        };
    }

    // -------------------------------------------------------------------------
    // Sim event union (fat-struct; Kind selects payload)
    // -------------------------------------------------------------------------

    public enum SimEventKind
    {
        // Hand events (mirrors HandEventKind)
        ReachStarted, Contact, Gripped, Parried, GripBroken,
        // Foot events (mirrors FootEventKind)
        Unlocked, LockingStarted, LockSucceeded, LockFailed,
        // Judgment window events
        WindowOpening, WindowOpen, WindowClosing, WindowClosed, TechniqueConfirmed,
        // Counter window events
        CounterWindowOpening, CounterWindowOpen,
        CounterWindowClosing, CounterWindowClosed, CounterConfirmed,
        // Pass attempt events
        PassStarted, PassFailed, PassSucceeded,
        // Cut attempt events
        CutStarted, CutSucceeded, CutFailed,
        // Guard / session
        GuardOpened, SessionEnded,
    }

    public struct SimEvent
    {
        public SimEventKind Kind;
        // Payload fields — which ones are meaningful depends on Kind.
        public HandSide          HandSide;
        public FootSide          FootSide;
        public GripZone          Zone;
        public GripBrokenReason  GripBrokenReason;
        public Technique         Technique;
        public WindowCloseReason WindowCloseReason;
        public CounterTechnique  Counter;
        public CounterCloseReason CounterCloseReason;
        public HandSide          CutDefenderSide;
        public HandSide          CutAttackerSide;
        public string            SessionEndReason;
    }

    // -------------------------------------------------------------------------
    // Step options
    // -------------------------------------------------------------------------

    public struct StepOptions
    {
        public float            RealDtMs;
        public float            GameDtMs;
        public Technique        ConfirmedTechnique;   // Technique.None = not confirmed
        public bool             HasConfirmedTechnique;
        public DefenseIntent?   DefenseIntent;
        public CounterTechnique ConfirmedCounter;     // CounterTechnique.None = not confirmed
        public bool             HasConfirmedCounter;
    }

    // -------------------------------------------------------------------------
    // Pure step function
    // -------------------------------------------------------------------------

    public static class GameStateOps
    {
        public static (GameState Next, List<SimEvent> Events) Step(
            GameState prev,
            InputFrame frame,
            Intent intent,
            StepOptions opts)
        {
            var events = new List<SimEvent>();
            long nowMs = frame.TimestampMs;
            var defense = opts.DefenseIntent;

            // §5.3 — clamp trigger values by stamina ceiling.
            float bottomCeiling  = StaminaOps.GripStrengthCeiling(prev.Bottom.Stamina);
            float effTriggerL    = System.Math.Min(frame.LTrigger, bottomCeiling);
            float effTriggerR    = System.Math.Min(frame.RTrigger, bottomCeiling);

            // 0. Cut-attempt tick (before attacker HandFSM so CUT_SUCCEEDED can
            //    force-RETRACT the targeted hand in the same frame).
            Vec2? cutCommitLRS = null;
            Vec2? cutCommitRRS = null;
            if (defense.HasValue && defense.Value.Discrete != null)
            {
                foreach (var d in defense.Value.Discrete)
                {
                    if (d.Kind == DefenseDiscreteKind.CutAttempt)
                    {
                        if (d.CutSide == HandSide.L) cutCommitLRS = d.RS;
                        else                          cutCommitRRS = d.RS;
                    }
                }
            }
            var cutEvList = new List<CutTickEvent>();
            var cutNext = CutAttemptOps.Tick(prev.CutAttempts, new CutTickInput
            {
                NowMs          = nowMs,
                LeftCommit     = cutCommitLRS,
                RightCommit    = cutCommitRRS,
                AttackerLeft   = prev.Bottom.LeftHand,
                AttackerRight  = prev.Bottom.RightHand,
                AttackerTriggerL = effTriggerL,
                AttackerTriggerR = effTriggerR,
            }, cutEvList);
            foreach (var e in cutEvList) events.Add(CutEventToSim(e));

            bool cutSucceededL = false, cutSucceededR = false;
            foreach (var e in cutEvList)
            {
                if (e.Kind == CutEventKind.CutSucceeded)
                {
                    if (e.AttackerSide == HandSide.L) cutSucceededL = true;
                    else                              cutSucceededR = true;
                }
            }

            // 1. ActorState FSMs.
            var handEvList  = new List<HandTickEvent>();
            var footEvList  = new List<FootTickEvent>();
            var nextBottomFsm = TickBottomActor(
                prev.Bottom, prev.Top, frame, intent, nowMs,
                handEvList, footEvList, effTriggerL, effTriggerR,
                cutSucceededL, cutSucceededR);
            foreach (var e in handEvList) events.Add(HandEventToSim(e));
            foreach (var e in footEvList) events.Add(FootEventToSim(e));

            var nextTopFsm = TickTopActorPassive(prev.Top, nowMs);

            // 2. posture_break — bottom's inputs drive the TOP actor's break.
            var gripPulls = new List<Vec2>();
            if (nextBottomFsm.LeftHand.State  == HandState.Gripped &&
                nextBottomFsm.LeftHand.Target != GripZone.None)
                gripPulls.Add(PostureBreakOps.GripPullVector(nextBottomFsm.LeftHand.Target, effTriggerL));
            if (nextBottomFsm.RightHand.State  == HandState.Gripped &&
                nextBottomFsm.RightHand.Target != GripZone.None)
                gripPulls.Add(PostureBreakOps.GripPullVector(nextBottomFsm.RightHand.Target, effTriggerR));

            Vec2 defenderRecovery = ComputeDefenderRecovery(defense);
            Vec2 nextTopPosture   = PostureBreakOps.Update(prev.Top.PostureBreak, new PostureBreakInputs
            {
                DtMs              = opts.GameDtMs,
                AttackerHip       = intent.Hip,
                GripPulls         = gripPulls,
                DefenderRecovery  = defenderRecovery,
            });

            // 3. arm_extracted (sits on the TOP actor).
            bool defBaseHold = IsDefenderBasingBicep(defense);
            var nextArmExtracted = ArmExtractedOps.Update(prev.TopArmExtracted, new ArmExtractedInputs
            {
                NowMs           = nowMs,
                DtMs            = opts.GameDtMs,
                BottomLeftHand  = nextBottomFsm.LeftHand,
                BottomRightHand = nextBottomFsm.RightHand,
                TriggerL        = effTriggerL,
                TriggerR        = effTriggerR,
                AttackerHip     = intent.Hip,
                DefenderBaseHold = defBaseHold,
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

            // 4. Stamina.
            bool breathPressed    = frame.IsDown(ButtonBit.BtnBreath);
            float nextBottomStamina = StaminaOps.Update(prev.Bottom.Stamina, new StaminaInputs
            {
                DtMs         = opts.GameDtMs,
                Actor        = nextBottomFsm,
                AttackerHip  = intent.Hip,
                TriggerL     = effTriggerL,
                TriggerR     = effTriggerR,
                BreathPressed = breathPressed,
            });

            float nextTopStamina = prev.Top.Stamina;
            if (defense.HasValue)
            {
                var def = defense.Value;
                bool defBreath = def.HasDiscrete(DefenseDiscreteKind.BreathStart);
                nextTopStamina = StaminaOps.UpdateDefender(prev.Top.Stamina, new StaminaDefenderInputs
                {
                    DtMs              = opts.GameDtMs,
                    Actor             = nextTop,
                    LeftBasePressure  = def.Base.LBasePressure,
                    RightBasePressure = def.Base.RBasePressure,
                    WeightForward     = def.Hip.WeightForward,
                    WeightLateral     = def.Hip.WeightLateral,
                    BreathPressed     = defBreath,
                });
            }

            // 5. Sustained counters.
            bool pushActive  = intent.Hip.HipPush >= 0.5f;
            var nextSustained = new SustainedCounters
            {
                HipPushMs = pushActive ? prev.Sustained.HipPushMs + opts.GameDtMs : 0f,
            };

            // 6. GuardFSM — one-way CLOSED → OPEN when both feet UNLOCKED.
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

            // 7. JudgmentWindowFSM.
            var ctx = new JudgmentContext
            {
                Bottom             = nextBottom,
                Top                = nextTop,
                BottomHipYaw       = intent.Hip.HipAngleTarget,
                BottomHipPush      = intent.Hip.HipPush,
                SustainedHipPushMs = nextSustained.HipPushMs,
            };
            var satisfied = JudgmentWindowOps.EvaluateAllTechniques(ctx, effTriggerL, effTriggerR);
            bool dismissReq = frame.WasPressed(ButtonBit.BtnRelease);

            var winEvList = new List<JudgmentTickEvent>();
            var (winNext, winTimeScale) = JudgmentWindowOps.Tick(
                prev.JudgmentWindow, satisfied,
                new JudgmentTickInput
                {
                    NowMs               = nowMs,
                    ConfirmedTechnique  = opts.HasConfirmedTechnique ? opts.ConfirmedTechnique : (Technique?)null,
                    DismissRequested    = dismissReq,
                }, winEvList);
            foreach (var e in winEvList) events.Add(WindowEventToSim(e));

            // 7b. Counter window.
            bool attackerOpeningThisTick = false;
            var openingSeed = new List<CounterTechnique>();
            foreach (var e in winEvList)
            {
                if (e.Kind == JudgmentEventKind.WindowOpening)
                {
                    attackerOpeningThisTick = true;
                    CounterWindowOps.CounterCandidatesFor(e.Candidates, openingSeed);
                    break;
                }
            }
            bool attackerWindowActive =
                winNext.State == JudgmentWindowState.Opening ||
                winNext.State == JudgmentWindowState.Open;

            var counterEvList = new List<CounterTickEvent>();
            var (counterNext, counterTimeScale) = CounterWindowOps.Tick(
                prev.CounterWindow, new CounterTickInput
                {
                    NowMs              = nowMs,
                    OpenAttackerWindow = attackerWindowActive,
                    OpeningSeed        = openingSeed,
                    ConfirmedCounter   = opts.HasConfirmedCounter ? opts.ConfirmedCounter : (CounterTechnique?)null,
                    DismissRequested   = dismissReq,
                }, counterEvList);
            foreach (var e in counterEvList) events.Add(CounterEventToSim(e));

            // Snapshot lateral sign at OPENING.
            int attackerSweepSign = prev.AttackerSweepLateralSign;
            if (attackerOpeningThisTick)
                attackerSweepSign = intent.Hip.HipLateral > 0f ? 1
                                  : intent.Hip.HipLateral < 0f ? -1
                                  : 0;

            // Counter success side-effects (§D.2).
            var finalJudgmentWindow  = winNext;
            var armExtractedAfterCtr = nextArmExtracted;
            CounterTickEvent counterConfirmed = default;
            bool hasCounterConfirmed = false;
            foreach (var e in counterEvList)
            {
                if (e.Kind == CounterEventKind.CounterConfirmed)
                {
                    counterConfirmed   = e;
                    hasCounterConfirmed = true;
                    break;
                }
            }
            if (hasCounterConfirmed)
            {
                if (finalJudgmentWindow.State == JudgmentWindowState.Open ||
                    finalJudgmentWindow.State == JudgmentWindowState.Opening)
                {
                    finalJudgmentWindow = new JudgmentWindow
                    {
                        State          = JudgmentWindowState.Closing,
                        StateEnteredMs = nowMs,
                        Candidates     = finalJudgmentWindow.Candidates,
                        CooldownUntilMs = finalJudgmentWindow.CooldownUntilMs,
                        FiredBy        = finalJudgmentWindow.FiredBy,
                    };
                }
                if (counterConfirmed.Counter == CounterTechnique.TriangleEarlyStack)
                {
                    armExtractedAfterCtr = new ArmExtractedState
                    {
                        Left           = false,
                        Right          = false,
                        LeftSustainMs  = 0f,
                        RightSustainMs = 0f,
                        LeftSetAtMs    = BJJConst.SentinelTimeMs,
                        RightSetAtMs   = BJJConst.SentinelTimeMs,
                    };
                }
            }

            // §5.2 last row — technique confirm costs stamina.
            bool confirmedThisTick = false;
            foreach (var e in winEvList)
                if (e.Kind == JudgmentEventKind.TechniqueConfirmed) { confirmedThisTick = true; break; }

            ActorState bottomAfterConfirm = nextBottom;
            if (confirmedThisTick)
                bottomAfterConfirm.Stamina = StaminaOps.ApplyConfirmCost(nextBottom.Stamina);

            bool counterConfirmedThisTick = hasCounterConfirmed;
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
                ArmExtractedLeft  = armExtractedAfterCtr.Left,
                ArmExtractedRight = armExtractedAfterCtr.Right,
            };

            // Combined time scale.
            float combinedScale = System.Math.Min(winTimeScale, counterTimeScale);
            var nextTime = new TimeContext
            {
                Scale    = combinedScale,
                RealDtMs = opts.RealDtMs,
                GameDtMs = opts.GameDtMs,
            };

            // 8. ControlLayer.
            bool defenderCutInProgress =
                cutNext.Left.Kind  == CutSlotKind.InProgress ||
                cutNext.Right.Kind == CutSlotKind.InProgress;
            var nextControl = ControlLayerOps.Update(prev.Control, new ControlLayerInputs
            {
                JudgmentWindow       = finalJudgmentWindow,
                Bottom               = bottomAfterConfirm,
                Top                  = topAfterCounter,
                DefenderCutInProgress = defenderCutInProgress,
            });

            // 9. Pass attempt.
            bool passCommitRequested = false;
            if (defense.HasValue && defense.Value.Discrete != null)
            {
                foreach (var d in defense.Value.Discrete)
                    if (d.Kind == DefenseDiscreteKind.PassCommit) { passCommitRequested = true; break; }
            }
            bool passEligible = false;
            if (defense.HasValue)
            {
                var def = defense.Value;
                passEligible = PassAttemptOps.IsPassEligible(new PassEligibilityParams
                {
                    Bottom             = bottomAfterConfirm,
                    Top                = topAfterCounter,
                    DefenderStamina    = topAfterCounter.Stamina,
                    LeftBasePressure   = def.Base.LBasePressure,
                    RightBasePressure  = def.Base.RBasePressure,
                    LeftBaseZone       = def.Base.LHandTarget,
                    RightBaseZone      = def.Base.RHandTarget,
                    RsY                = frame.RS.Y,
                    Guard              = nextGuard,
                });
            }
            bool triangleConfirmedThisTick = false;
            foreach (var e in winEvList)
            {
                if (e.Kind == JudgmentEventKind.TechniqueConfirmed &&
                    e.Technique == Technique.Triangle)
                { triangleConfirmedThisTick = true; break; }
            }
            var passEvList = new List<PassTickEvent>();
            var passNext = PassAttemptOps.Tick(prev.PassAttempt, new PassTickInput
            {
                NowMs                              = nowMs,
                CommitRequested                    = passCommitRequested,
                EligibleNow                        = passEligible,
                AttackerTriangleConfirmedThisTick  = triangleConfirmedThisTick,
            }, passEvList);
            foreach (var e in passEvList) events.Add(PassEventToSim(e));

            // Session termination.
            bool sessionEnded = prev.SessionEnded;
            if (!sessionEnded)
            {
                bool passSucceeded = false;
                foreach (var e in passEvList)
                    if (e.Kind == PassEventKind.PassSucceeded) { passSucceeded = true; break; }

                if (passSucceeded)
                {
                    sessionEnded = true;
                    events.Add(new SimEvent { Kind = SimEventKind.SessionEnded, SessionEndReason = "PASS_SUCCESS" });
                }
                else if (confirmedThisTick)
                {
                    sessionEnded = true;
                    events.Add(new SimEvent { Kind = SimEventKind.SessionEnded, SessionEndReason = "TECHNIQUE_FINISHED" });
                }
                else if (nextGuard == GuardState.Open && prev.Guard == GuardState.Closed)
                {
                    sessionEnded = true;
                    events.Add(new SimEvent { Kind = SimEventKind.SessionEnded, SessionEndReason = "GUARD_OPENED" });
                }
            }

            var nextState = new GameState
            {
                Bottom                   = bottomAfterConfirm,
                Top                      = topAfterCounter,
                Guard                    = nextGuard,
                JudgmentWindow           = finalJudgmentWindow,
                CounterWindow            = counterNext,
                PassAttempt              = passNext,
                CutAttempts              = cutNext,
                SessionEnded             = sessionEnded,
                AttackerSweepLateralSign = attackerSweepSign,
                Time                     = nextTime,
                Sustained                = nextSustained,
                TopArmExtracted          = armExtractedAfterCtr,
                Control                  = nextControl,
                FrameIndex               = prev.FrameIndex + 1,
                NowMs                    = nowMs,
            };

            return (nextState, events);
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static ActorState TickBottomActor(
            ActorState prev, ActorState top,
            InputFrame frame, Intent intent, long nowMs,
            List<HandTickEvent> handEvOut,
            List<FootTickEvent> footEvOut,
            float effTriggerL, float effTriggerR,
            bool cutSucceededL, bool cutSucceededR)
        {
            bool forceRelease = frame.WasPressed(ButtonBit.BtnRelease);

            var lh = HandFSMOps.Tick(prev.LeftHand, new HandTickInput
            {
                NowMs                   = nowMs,
                TriggerValue            = effTriggerL,
                TargetZone              = intent.Grip.LHandTarget,
                ForceReleaseAll         = forceRelease,
                OpponentDefendsThisZone = false,
                OpponentCutSucceeded    = cutSucceededL,
                TargetOutOfReach        = false,
            }, handEvOut);

            var rh = HandFSMOps.Tick(prev.RightHand, new HandTickInput
            {
                NowMs                   = nowMs,
                TriggerValue            = effTriggerR,
                TargetZone              = intent.Grip.RHandTarget,
                ForceReleaseAll         = forceRelease,
                OpponentDefendsThisZone = false,
                OpponentCutSucceeded    = cutSucceededR,
                TargetOutOfReach        = false,
            }, handEvOut);

            bool lBumper = intent.HasFootToggle(FootSide.L);
            bool rBumper = intent.HasFootToggle(FootSide.R);

            var lf = FootFSMOps.Tick(prev.LeftFoot, new FootTickInput
            {
                NowMs                  = nowMs,
                BumperEdge             = lBumper,
                OpponentPostureSagittal = top.PostureBreak.Y,
            }, footEvOut);

            var rf = FootFSMOps.Tick(prev.RightFoot, new FootTickInput
            {
                NowMs                  = nowMs,
                BumperEdge             = rBumper,
                OpponentPostureSagittal = top.PostureBreak.Y,
            }, footEvOut);

            var next    = prev;
            next.LeftHand  = lh;
            next.RightHand = rh;
            next.LeftFoot  = lf;
            next.RightFoot = rf;
            return next;
        }

        private static ActorState TickTopActorPassive(ActorState prev, long nowMs)
        {
            var restFoot = new FootTickInput
            {
                NowMs                  = nowMs,
                BumperEdge             = false,
                OpponentPostureSagittal = 0f,
            };
            var noEvFoot = new List<FootTickEvent>();
            var lf = FootFSMOps.Tick(prev.LeftFoot,  restFoot, noEvFoot);
            var rf = FootFSMOps.Tick(prev.RightFoot, restFoot, noEvFoot);

            var restHand = new HandTickInput
            {
                NowMs                   = nowMs,
                TriggerValue            = 0f,
                TargetZone              = GripZone.None,
                ForceReleaseAll         = false,
                OpponentDefendsThisZone = false,
                OpponentCutSucceeded    = false,
                TargetOutOfReach        = false,
            };
            var noEvHand = new List<HandTickEvent>();
            var lh = HandFSMOps.Tick(prev.LeftHand,  restHand, noEvHand);
            var rh = HandFSMOps.Tick(prev.RightHand, restHand, noEvHand);

            var next    = prev;
            next.LeftHand  = lh;
            next.RightHand = rh;
            next.LeftFoot  = lf;
            next.RightFoot = rf;
            return next;
        }

        private static Vec2 ComputeDefenderRecovery(DefenseIntent? defense)
        {
            if (!defense.HasValue) return Vec2.Zero;
            var def = defense.Value;
            float x = def.Hip.WeightLateral;
            float y = def.Hip.WeightForward;

            if (def.Base.LHandTarget == BaseZone.Chest || def.Base.LHandTarget == BaseZone.Hip)
                y += def.Base.LBasePressure * 0.5f;
            if (def.Base.RHandTarget == BaseZone.Chest || def.Base.RHandTarget == BaseZone.Hip)
                y += def.Base.RBasePressure * 0.5f;

            float mag = (float)System.Math.Sqrt(x * x + y * y);
            if (mag > 1f) { float s = 1f / mag; x *= s; y *= s; }
            return new Vec2(x, y);
        }

        private static bool IsDefenderBasingBicep(DefenseIntent? defense)
        {
            if (!defense.HasValue) return false;
            var def = defense.Value;
            return (def.Base.LHandTarget == BaseZone.BicepL ||
                    def.Base.LHandTarget == BaseZone.BicepR ||
                    def.Base.RHandTarget == BaseZone.BicepL ||
                    def.Base.RHandTarget == BaseZone.BicepR) &&
                   (def.Base.LBasePressure >= 0.5f || def.Base.RBasePressure >= 0.5f);
        }

        // -- Event conversion helpers --

        private static SimEvent HandEventToSim(HandTickEvent e)
        {
            var k = e.Kind switch
            {
                HandEventKind.ReachStarted => SimEventKind.ReachStarted,
                HandEventKind.Contact      => SimEventKind.Contact,
                HandEventKind.Gripped      => SimEventKind.Gripped,
                HandEventKind.Parried      => SimEventKind.Parried,
                HandEventKind.GripBroken   => SimEventKind.GripBroken,
                _                          => SimEventKind.ReachStarted,
            };
            return new SimEvent
            {
                Kind             = k,
                HandSide         = e.Side,
                Zone             = e.Zone,
                GripBrokenReason = e.GripBrokenReason,
            };
        }

        private static SimEvent FootEventToSim(FootTickEvent e)
        {
            var k = e.Kind switch
            {
                FootEventKind.Unlocked       => SimEventKind.Unlocked,
                FootEventKind.LockingStarted => SimEventKind.LockingStarted,
                FootEventKind.LockSucceeded  => SimEventKind.LockSucceeded,
                FootEventKind.LockFailed     => SimEventKind.LockFailed,
                _                            => SimEventKind.Unlocked,
            };
            return new SimEvent { Kind = k, FootSide = e.Side };
        }

        private static SimEvent WindowEventToSim(JudgmentTickEvent e)
        {
            return e.Kind switch
            {
                JudgmentEventKind.WindowOpening      => new SimEvent { Kind = SimEventKind.WindowOpening },
                JudgmentEventKind.WindowOpen         => new SimEvent { Kind = SimEventKind.WindowOpen },
                JudgmentEventKind.WindowClosing      => new SimEvent { Kind = SimEventKind.WindowClosing, WindowCloseReason = e.CloseReason },
                JudgmentEventKind.WindowClosed       => new SimEvent { Kind = SimEventKind.WindowClosed },
                JudgmentEventKind.TechniqueConfirmed => new SimEvent { Kind = SimEventKind.TechniqueConfirmed, Technique = e.Technique },
                _                                    => new SimEvent { Kind = SimEventKind.WindowClosed },
            };
        }

        private static SimEvent CounterEventToSim(CounterTickEvent e)
        {
            return e.Kind switch
            {
                CounterEventKind.CounterWindowOpening => new SimEvent { Kind = SimEventKind.CounterWindowOpening },
                CounterEventKind.CounterWindowOpen    => new SimEvent { Kind = SimEventKind.CounterWindowOpen },
                CounterEventKind.CounterWindowClosing => new SimEvent { Kind = SimEventKind.CounterWindowClosing, CounterCloseReason = e.CloseReason },
                CounterEventKind.CounterWindowClosed  => new SimEvent { Kind = SimEventKind.CounterWindowClosed },
                CounterEventKind.CounterConfirmed     => new SimEvent { Kind = SimEventKind.CounterConfirmed, Counter = e.Counter },
                _                                    => new SimEvent { Kind = SimEventKind.CounterWindowClosed },
            };
        }

        private static SimEvent PassEventToSim(PassTickEvent e)
        {
            return e.Kind switch
            {
                PassEventKind.PassStarted   => new SimEvent { Kind = SimEventKind.PassStarted },
                PassEventKind.PassFailed    => new SimEvent { Kind = SimEventKind.PassFailed },
                PassEventKind.PassSucceeded => new SimEvent { Kind = SimEventKind.PassSucceeded },
                _                          => new SimEvent { Kind = SimEventKind.PassStarted },
            };
        }

        private static SimEvent CutEventToSim(CutTickEvent e)
        {
            return e.Kind switch
            {
                CutEventKind.CutStarted   => new SimEvent { Kind = SimEventKind.CutStarted,   CutDefenderSide = e.DefenderSide, CutAttackerSide = e.AttackerSide, Zone = e.Zone },
                CutEventKind.CutSucceeded => new SimEvent { Kind = SimEventKind.CutSucceeded, CutDefenderSide = e.DefenderSide, CutAttackerSide = e.AttackerSide },
                CutEventKind.CutFailed    => new SimEvent { Kind = SimEventKind.CutFailed,    CutDefenderSide = e.DefenderSide },
                _                        => new SimEvent { Kind = SimEventKind.CutFailed },
            };
        }
    }
}
