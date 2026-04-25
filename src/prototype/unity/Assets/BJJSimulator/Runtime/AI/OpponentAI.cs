// Ported 1:1 from src/prototype/web/src/ai/opponent_ai.ts.
// PURE — opponent AI intent generator.
// Reference: docs/design/opponent_ai_v1.md.
//
// Rule-based priority table: no DOM / no rAF.
// Given GameState + role, always produces the same output (deterministic).
//
// Returns AIOutput (tagged union via Kind enum).

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Tagged union: Bottom or Top output
    // -------------------------------------------------------------------------

    public enum AIOutputRole { Bottom, Top }

    public struct AIOutput
    {
        public AIOutputRole Role;

        // Bottom role
        public Intent      Intent;
        public Technique?  ConfirmedTechnique;

        // Top role
        public DefenseIntent     Defense;
        public CounterTechnique? ConfirmedCounter;
    }

    // -------------------------------------------------------------------------
    // Pure AI logic
    // -------------------------------------------------------------------------

    public static class OpponentAI
    {
        private static readonly Intent NeutralIntent = new Intent
        {
            Hip      = HipIntent.Zero,
            Grip     = GripIntent.Zero,
            Discrete = System.Array.Empty<DiscreteIntent>(),
        };

        // -----------------------------------------------------------------------
        // Main entry
        // -----------------------------------------------------------------------

        public static AIOutput OpponentIntentFor(GameState game, AIOutputRole role)
        {
            if (role == AIOutputRole.Top)
            {
                CounterTechnique? counterCommit = null;
                if (game.CounterWindow.State == CounterWindowState.Open &&
                    game.CounterWindow.Candidates.Length > 0)
                    counterCommit = game.CounterWindow.Candidates[0];

                return new AIOutput
                {
                    Role             = AIOutputRole.Top,
                    Defense          = TopDecide(game),
                    ConfirmedCounter = counterCommit,
                };
            }
            else
            {
                Technique? techniqueCommit = null;
                if (game.JudgmentWindow.State == JudgmentWindowState.Open &&
                    game.JudgmentWindow.Candidates.Length > 0)
                    techniqueCommit = game.JudgmentWindow.Candidates[0];

                return new AIOutput
                {
                    Role               = AIOutputRole.Bottom,
                    Intent             = BottomDecide(game),
                    ConfirmedTechnique = techniqueCommit,
                };
            }
        }

        // -----------------------------------------------------------------------
        // Top (defender) decision
        // -----------------------------------------------------------------------

        private static DefenseIntent TopDecide(GameState g)
        {
            // Priority 1: counter window OPEN → commit first candidate
            if (g.CounterWindow.State == CounterWindowState.Open &&
                g.CounterWindow.Candidates.Length > 0)
                return CounterCommitIntent(g.CounterWindow.Candidates[0], g.AttackerSweepLateralSign);

            // Priority 2: attacker TRIANGLE lit → feed stack to accumulate
            if (g.JudgmentWindow.State == JudgmentWindowState.Open &&
                System.Array.IndexOf(g.JudgmentWindow.Candidates, Technique.Triangle) >= 0)
                return CounterCommitIntent(CounterTechnique.TriangleEarlyStack, 0);

            // Priority 3: recover from sagittal posture break
            if (g.Top.PostureBreak.Y >= 0.5f)
                return new DefenseIntent
                {
                    Hip      = new TopHipIntent { WeightForward = 1f, WeightLateral = 0f },
                    Base     = TopBaseIntent.Zero,
                    Discrete = new[] { new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.RecoveryHold } },
                };

            // Priority 4: attacker has a GRIPPED hand and cut slot is free
            bool cutIdleL  = g.CutAttempts.Left.Kind  == CutSlotKind.Idle;
            bool cutIdleR  = g.CutAttempts.Right.Kind == CutSlotKind.Idle;
            var  targetRs  = PickAttackerGripped(g);
            if (targetRs.HasValue && (cutIdleL || cutIdleR))
            {
                HandSide defSide = cutIdleL ? HandSide.L : HandSide.R;
                return new DefenseIntent
                {
                    Hip      = TopHipIntent.Zero,
                    Base     = TopBaseIntent.Zero,
                    Discrete = new[]
                    {
                        new DefenseDiscreteIntent
                        {
                            Kind    = DefenseDiscreteIntentKind.CutAttempt,
                            CutSide = defSide,
                            Rs      = targetRs.Value,
                        }
                    },
                };
            }

            // Priority 5: arm extracted → apply bicep base on that side
            if (g.Top.ArmExtractedLeft || g.Top.ArmExtractedRight)
            {
                BaseZone side = g.Top.ArmExtractedLeft ? BaseZone.BicepL : BaseZone.BicepR;
                return new DefenseIntent
                {
                    Hip  = TopHipIntent.Zero,
                    Base = new TopBaseIntent
                    {
                        LHandTarget   = side,
                        LBasePressure = 0.8f,
                        RHandTarget   = BaseZone.None,
                        RBasePressure = 0f,
                    },
                    Discrete = System.Array.Empty<DefenseDiscreteIntent>(),
                };
            }

            // Priority 6: pass-preparation base setup
            int  bottomGrips   = (g.Bottom.LeftHand.State  == HandState.Gripped ? 1 : 0) +
                                 (g.Bottom.RightHand.State == HandState.Gripped ? 1 : 0);
            bool bothFeetLocked = g.Bottom.LeftFoot.State  == FootState.Locked &&
                                  g.Bottom.RightFoot.State == FootState.Locked;
            if (bothFeetLocked && bottomGrips < 2 && g.Top.Stamina >= 0.5f)
                return new DefenseIntent
                {
                    Hip  = TopHipIntent.Zero,
                    Base = new TopBaseIntent
                    {
                        LHandTarget   = BaseZone.BicepL,
                        LBasePressure = 0.7f,
                        RHandTarget   = BaseZone.KneeR,
                        RBasePressure = 0.7f,
                    },
                    Discrete = System.Array.Empty<DefenseDiscreteIntent>(),
                };

            // Priority 7: breathe below threshold
            if (g.Top.Stamina < 0.3f)
                return new DefenseIntent
                {
                    Hip      = TopHipIntent.Zero,
                    Base     = TopBaseIntent.Zero,
                    Discrete = new[] { new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.BreathStart } },
                };

            // Priority 8: idle
            return DefenseIntent.Zero;
        }

        private static DefenseIntent CounterCommitIntent(CounterTechnique counter, int sweepSign)
        {
            if (counter == CounterTechnique.ScissorCounter)
            {
                int sign = sweepSign >= 0 ? -1 : 1;
                return new DefenseIntent
                {
                    Hip      = new TopHipIntent { WeightForward = 0f, WeightLateral = sign },
                    Base     = TopBaseIntent.Zero,
                    Discrete = System.Array.Empty<DefenseDiscreteIntent>(),
                };
            }
            // TRIANGLE_EARLY_STACK
            return new DefenseIntent
            {
                Hip      = new TopHipIntent { WeightForward = 1f, WeightLateral = 0f },
                Base     = TopBaseIntent.Zero,
                Discrete = new[] { new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.RecoveryHold } },
            };
        }

        private static Vec2? PickAttackerGripped(GameState g)
        {
            var l = g.Bottom.LeftHand;
            if (l.State == HandState.Gripped && l.Target != GripZone.None)
                return ZoneDirection(l.Target);
            var r = g.Bottom.RightHand;
            if (r.State == HandState.Gripped && r.Target != GripZone.None)
                return ZoneDirection(r.Target);
            return null;
        }

        private static Vec2 ZoneDirection(GripZone zone)
        {
            switch (zone)
            {
                case GripZone.SleeveL:      return new Vec2(-0.7f, -0.7f);
                case GripZone.SleeveR:      return new Vec2( 0.7f, -0.7f);
                case GripZone.CollarL:      return new Vec2(-0.7f,  0.7f);
                case GripZone.CollarR:      return new Vec2( 0.7f,  0.7f);
                case GripZone.WristL:       return new Vec2(-1f,    0f);
                case GripZone.WristR:       return new Vec2( 1f,    0f);
                case GripZone.Belt:         return new Vec2( 0f,   -1f);
                case GripZone.PostureBreak: return new Vec2( 0f,    1f);
                default:                    return Vec2.Zero;
            }
        }

        // -----------------------------------------------------------------------
        // Bottom (attacker) decision
        // -----------------------------------------------------------------------

        private static Intent BottomDecide(GameState g)
        {
            // Priority 1: judgment window OPEN → commit first candidate
            if (g.JudgmentWindow.State == JudgmentWindowState.Open &&
                g.JudgmentWindow.Candidates.Length > 0)
                return TechniqueCommitIntent(g.JudgmentWindow.Candidates[0]);

            HandState lState = g.Bottom.LeftHand.State;
            HandState rState = g.Bottom.RightHand.State;
            int grips = (lState == HandState.Gripped ? 1 : 0) + (rState == HandState.Gripped ? 1 : 0);

            // Priority 2: no grips, stamina OK → reach SLEEVE_R with L
            if (grips == 0 && g.Bottom.Stamina >= 0.5f && lState == HandState.Idle)
                return new Intent
                {
                    Hip  = HipIntent.Zero,
                    Grip = new GripIntent
                    {
                        LHandTarget   = GripZone.SleeveR,
                        LGripStrength = 0.8f,
                        RHandTarget   = GripZone.None,
                        RGripStrength = 0f,
                    },
                    Discrete = System.Array.Empty<DiscreteIntent>(),
                };

            // Priority 3: one GRIPPED → reach the mirrored collar with the free hand
            if (grips == 1)
            {
                bool useLeft   = lState != HandState.Gripped;
                GripZone collar = useLeft ? GripZone.CollarL : GripZone.CollarR;
                return new Intent
                {
                    Hip  = HipIntent.Zero,
                    Grip = useLeft
                        ? new GripIntent
                          {
                              LHandTarget   = collar,
                              LGripStrength = 0.8f,
                              RHandTarget   = g.Bottom.RightHand.Target,
                              RGripStrength = g.Bottom.RightHand.State == HandState.Gripped ? 0.8f : 0f,
                          }
                        : new GripIntent
                          {
                              LHandTarget   = g.Bottom.LeftHand.Target,
                              LGripStrength = g.Bottom.LeftHand.State == HandState.Gripped ? 0.8f : 0f,
                              RHandTarget   = collar,
                              RGripStrength = 0.8f,
                          },
                    Discrete = System.Array.Empty<DiscreteIntent>(),
                };
            }

            // Priority 4: both GRIPPED, break < 0.4 → push hip forward
            float breakMag = g.Top.PostureBreak.Magnitude;
            if (grips == 2 && breakMag < 0.4f)
                return new Intent
                {
                    Hip = new HipIntent { HipAngleTarget = 0f, HipPush = 0.6f, HipLateral = 0f },
                    Grip = new GripIntent
                    {
                        LHandTarget   = g.Bottom.LeftHand.Target,
                        LGripStrength = 0.8f,
                        RHandTarget   = g.Bottom.RightHand.Target,
                        RGripStrength = 0.8f,
                    },
                    Discrete = System.Array.Empty<DiscreteIntent>(),
                };

            // Priority 5: low stamina → breathe
            if (g.Bottom.Stamina < 0.3f)
                return new Intent
                {
                    Hip      = HipIntent.Zero,
                    Grip     = GripIntent.Zero,
                    Discrete = new[] { new DiscreteIntent { Kind = DiscreteIntentKind.BreathStart } },
                };

            // Priority 6: idle — hold current grips
            return new Intent
            {
                Hip = HipIntent.Zero,
                Grip = new GripIntent
                {
                    LHandTarget   = g.Bottom.LeftHand.Target,
                    LGripStrength = lState == HandState.Gripped ? 0.8f : 0f,
                    RHandTarget   = g.Bottom.RightHand.Target,
                    RGripStrength = rState == HandState.Gripped ? 0.8f : 0f,
                },
                Discrete = System.Array.Empty<DiscreteIntent>(),
            };
        }

        private static Intent TechniqueCommitIntent(Technique t)
        {
            switch (t)
            {
                case Technique.ScissorSweep:
                    return new Intent
                    {
                        Hip      = new HipIntent { HipAngleTarget = 0f, HipPush = 0f, HipLateral = 1f },
                        Grip     = GripIntent.Zero,
                        Discrete = new[] { new DiscreteIntent { Kind = DiscreteIntentKind.FootHookToggle, FootSide = FootSide.L } },
                    };
                case Technique.FlowerSweep:
                    return new Intent
                    {
                        Hip      = new HipIntent { HipAngleTarget = 0f, HipPush = 1f, HipLateral = 0f },
                        Grip     = GripIntent.Zero,
                        Discrete = new[] { new DiscreteIntent { Kind = DiscreteIntentKind.FootHookToggle, FootSide = FootSide.R } },
                    };
                case Technique.Triangle:
                    return new Intent
                    {
                        Hip      = HipIntent.Zero,
                        Grip     = GripIntent.Zero,
                        Discrete = new[] { new DiscreteIntent { Kind = DiscreteIntentKind.BaseHold } },
                    };
                case Technique.Omoplata:
                    return new Intent
                    {
                        Hip      = HipIntent.Zero,
                        Grip     = GripIntent.Zero,
                        Discrete = new[] { new DiscreteIntent { Kind = DiscreteIntentKind.FootHookToggle, FootSide = FootSide.L } },
                    };
                default:
                    return NeutralIntent;
            }
        }
    }
}
