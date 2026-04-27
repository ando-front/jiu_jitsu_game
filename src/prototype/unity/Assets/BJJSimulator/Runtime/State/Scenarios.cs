// Ported 1:1 from src/prototype/web/src/state/scenarios.ts.
// PURE — practice scenario builders.
// Reference: inline comments in scenarios.ts.
//
// Each builder returns a GameState pre-seeded so that one specific
// technique's judgment-window firing condition is met or one step away.
// Use these in tests and in the game's "practice / quick-start" flow.

namespace BJJSimulator
{
    public enum ScenarioName
    {
        ScissorReady,
        FlowerReady,
        TriangleReady,
        OmoplataReady,
        HipBumpReady,
        CrossCollarReady,
        PassDefense,
    }

    public static class Scenarios
    {
        // -----------------------------------------------------------------------
        // Description strings (shown in scenario-select UI)
        // -----------------------------------------------------------------------

        public static string Describe(ScenarioName name)
        {
            switch (name)
            {
                case ScenarioName.ScissorReady:
                    return "両足 LOCKED + L-hand GRIPPED(SLEEVE_R)+ 横崩し 0.5。腰を横に振るだけで SCISSOR_SWEEP 判断窓が開く。";
                case ScenarioName.FlowerReady:
                    return "両足 LOCKED + R-hand GRIPPED(WRIST_L)+ 前崩し 0.6。次フレームで FLOWER_SWEEP 条件成立。";
                case ScenarioName.TriangleReady:
                    return "L-foot UNLOCKED + L-hand GRIPPED(COLLAR_R)+ top.armExtractedRight=true。TRIANGLE 条件成立。";
                case ScenarioName.OmoplataReady:
                    return "L-hand GRIPPED(SLEEVE_R)+ 前崩し 0.7 + 横 -0.4(符号一致)。L-Stick を左に大きく倒して腰をひねる(|hip_yaw| ≥ π/3)と OMOPLATA 窓が開く。";
                case ScenarioName.HipBumpReady:
                    return "前崩し 0.8 + sustainedHipPushMs=350ms(300ms 閾値突破済)。押し続けるだけで HIP_BUMP 条件成立。";
                case ScenarioName.CrossCollarReady:
                    return "両 COLLAR GRIPPED 強度0.7++ 崩し 0.5+。十字絞め判断窓候補に乗る。";
                case ScenarioName.PassDefense:
                    return "TOP視点の練習。BOTTOMが片足UNLOCKEDでパスが決まる一歩手前。";
                default:
                    return "";
            }
        }

        // -----------------------------------------------------------------------
        // Main builder
        // -----------------------------------------------------------------------

        public static GameState Build(ScenarioName name, long nowMs)
        {
            var g = GameStateOps.InitialGameState(nowMs);
            switch (name)
            {
                case ScenarioName.ScissorReady:    return ScissorReady(g, nowMs);
                case ScenarioName.FlowerReady:     return FlowerReady(g, nowMs);
                case ScenarioName.TriangleReady:   return TriangleReady(g, nowMs);
                case ScenarioName.OmoplataReady:   return OmoplataReady(g, nowMs);
                case ScenarioName.HipBumpReady:    return HipBumpReady(g, nowMs);
                case ScenarioName.CrossCollarReady: return CrossCollarReady(g, nowMs);
                case ScenarioName.PassDefense:     return PassDefense(g, nowMs);
                default:                            return g;
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static HandFSM GrippedHand(HandSide side, GripZone zone, long nowMs) =>
            new HandFSM
            {
                Side             = side,
                State            = HandState.Gripped,
                Target           = zone,
                StateEnteredMs   = nowMs,
                ReachDurationMs  = 250,
                LastParriedZone  = GripZone.None,
                LastParriedAtMs  = BJJConst.SentinelTimeMs,
            };

        private static FootFSM Foot(FootSide side, FootState state, long nowMs) =>
            new FootFSM { Side = side, State = state, StateEnteredMs = nowMs };

        // -----------------------------------------------------------------------
        // Individual scenarios
        // -----------------------------------------------------------------------

        private static GameState ScissorReady(GameState g, long nowMs)
        {
            var bottom = g.Bottom;
            bottom.LeftHand  = GrippedHand(HandSide.L, GripZone.SleeveR, nowMs);
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Locked, nowMs);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked, nowMs);
            var top = g.Top;
            top.PostureBreak = new Vec2(0.5f, 0.1f);
            return new GameState { Bottom = bottom, Top = top,
                TopArmExtracted = g.TopArmExtracted, Sustained = g.Sustained,
                JudgmentWindow = g.JudgmentWindow, CounterWindow = g.CounterWindow,
                PassAttempt = g.PassAttempt, CutAttempts = g.CutAttempts,
                Control = g.Control, Time = g.Time };
        }

        private static GameState FlowerReady(GameState g, long nowMs)
        {
            var bottom = g.Bottom;
            bottom.RightHand = GrippedHand(HandSide.R, GripZone.WristL, nowMs);
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Locked, nowMs);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked, nowMs);
            var top = g.Top;
            top.PostureBreak = new Vec2(0f, 0.6f);
            return new GameState { Bottom = bottom, Top = top,
                TopArmExtracted = g.TopArmExtracted, Sustained = g.Sustained,
                JudgmentWindow = g.JudgmentWindow, CounterWindow = g.CounterWindow,
                PassAttempt = g.PassAttempt, CutAttempts = g.CutAttempts,
                Control = g.Control, Time = g.Time };
        }

        private static GameState TriangleReady(GameState g, long nowMs)
        {
            var bottom = g.Bottom;
            bottom.LeftHand  = GrippedHand(HandSide.L, GripZone.CollarR, nowMs);
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Unlocked, nowMs);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked, nowMs);
            var top = g.Top;
            top.ArmExtractedRight = true;
            top.PostureBreak      = new Vec2(0f, 0.3f);
            var topArm = g.TopArmExtracted;
            topArm.Right          = true;
            topArm.RightSustainMs = 2000f;
            topArm.RightSetAtMs   = nowMs;
            return new GameState { Bottom = bottom, Top = top,
                TopArmExtracted = topArm, Sustained = g.Sustained,
                JudgmentWindow = g.JudgmentWindow, CounterWindow = g.CounterWindow,
                PassAttempt = g.PassAttempt, CutAttempts = g.CutAttempts,
                Control = g.Control, Time = g.Time };
        }

        private static GameState OmoplataReady(GameState g, long nowMs)
        {
            var bottom = g.Bottom;
            bottom.LeftHand  = GrippedHand(HandSide.L, GripZone.SleeveR, nowMs);
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Locked, nowMs);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked, nowMs);
            var top = g.Top;
            top.PostureBreak = new Vec2(-0.4f, 0.7f);
            return new GameState { Bottom = bottom, Top = top,
                TopArmExtracted = g.TopArmExtracted, Sustained = g.Sustained,
                JudgmentWindow = g.JudgmentWindow, CounterWindow = g.CounterWindow,
                PassAttempt = g.PassAttempt, CutAttempts = g.CutAttempts,
                Control = g.Control, Time = g.Time };
        }

        private static GameState HipBumpReady(GameState g, long nowMs)
        {
            var bottom = g.Bottom;
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Locked, nowMs);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked, nowMs);
            var top = g.Top;
            top.PostureBreak = new Vec2(0f, 0.8f);
            var sustained = g.Sustained;
            sustained.HipPushMs = 350f;
            return new GameState { Bottom = bottom, Top = top,
                TopArmExtracted = g.TopArmExtracted, Sustained = sustained,
                JudgmentWindow = g.JudgmentWindow, CounterWindow = g.CounterWindow,
                PassAttempt = g.PassAttempt, CutAttempts = g.CutAttempts,
                Control = g.Control, Time = g.Time };
        }

        private static GameState CrossCollarReady(GameState g, long nowMs)
        {
            var bottom = g.Bottom;
            bottom.LeftHand  = GrippedHand(HandSide.L, GripZone.CollarR, nowMs);
            bottom.RightHand = GrippedHand(HandSide.R, GripZone.CollarL, nowMs);
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Locked, nowMs);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked, nowMs);
            var top = g.Top;
            top.PostureBreak = new Vec2(0.1f, 0.5f);
            return new GameState { Bottom = bottom, Top = top,
                TopArmExtracted = g.TopArmExtracted, Sustained = g.Sustained,
                JudgmentWindow = g.JudgmentWindow, CounterWindow = g.CounterWindow,
                PassAttempt = g.PassAttempt, CutAttempts = g.CutAttempts,
                Control = g.Control, Time = g.Time };
        }

        private static GameState PassDefense(GameState g, long nowMs)
        {
            var bottom = g.Bottom;
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Unlocked, nowMs);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked, nowMs);
            bottom.Stamina   = 0.45f;
            var top = g.Top;
            top.Stamina      = 0.8f;
            top.PostureBreak = new Vec2(0f, 0.1f);
            return new GameState { Bottom = bottom, Top = top,
                TopArmExtracted = g.TopArmExtracted, Sustained = g.Sustained,
                JudgmentWindow = g.JudgmentWindow, CounterWindow = g.CounterWindow,
                PassAttempt = g.PassAttempt, CutAttempts = g.CutAttempts,
                Control = g.Control, Time = g.Time };
        }
    }
}
