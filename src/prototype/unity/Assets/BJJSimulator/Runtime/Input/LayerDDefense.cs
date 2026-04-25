// Ported 1:1 from src/prototype/web/src/input/layerD_defense.ts.
// PURE — Layer D (defender): counter-window commit resolution.
// Reference: docs/design/input_system_defense_v1.md §D.2.
//
// Only resolves commits while the defender's counter window is OPEN.
// Technique table:
//   SCISSOR_COUNTER       — LS max OPPOSITE to attacker's sweep direction
//   TRIANGLE_EARLY_STACK  — BtnBase held ≥ 500ms AND LS straight up

namespace BJJSimulator
{
    public struct LayerDDefenseTiming
    {
        public float StackHoldMs;

        public static readonly LayerDDefenseTiming Default = new LayerDDefenseTiming
        {
            StackHoldMs = 500f,
        };
    }

    public struct LayerDDefenseThresholds
    {
        public float LsOppositeAbs;
        public float LsUp;

        public static readonly LayerDDefenseThresholds Default = new LayerDDefenseThresholds
        {
            LsOppositeAbs = 0.8f,
            LsUp          = 0.8f,
        };
    }

    public struct LayerDDefenseState
    {
        public float BtnBaseHeldMs;

        public static readonly LayerDDefenseState Initial = new LayerDDefenseState
        {
            BtnBaseHeldMs = 0f,
        };
    }

    public struct LayerDDefenseInputs
    {
        public long               NowMs;
        public float              DtMs;
        public InputFrame         Frame;
        public CounterTechnique[] Candidates;
        public bool               WindowIsOpen;
        /// <summary>
        /// §D.2 — sign of the attacker's hip_lateral at window OPENING.
        /// -1 = attacker sweeping left, +1 = right, 0 = not applicable.
        /// </summary>
        public int                AttackerSweepLateralSign;
    }

    public static class LayerDDefenseOps
    {
        public static (LayerDDefenseState NextState, CounterTechnique? ConfirmedCounter) Resolve(
            LayerDDefenseState prev,
            LayerDDefenseInputs inp,
            LayerDDefenseTiming    timing = default,
            LayerDDefenseThresholds thresh = default)
        {
            if (timing.StackHoldMs == 0f)   timing = LayerDDefenseTiming.Default;
            if (thresh.LsOppositeAbs == 0f) thresh = LayerDDefenseThresholds.Default;

            bool  baseHeld = (inp.Frame.Buttons & ButtonBit.BtnBase) != 0;
            float baseMs   = baseHeld ? prev.BtnBaseHeldMs + inp.DtMs : 0f;
            var   next     = new LayerDDefenseState { BtnBaseHeldMs = baseMs };

            if (!inp.WindowIsOpen)
                return (next, null);

            var cand = new System.Collections.Generic.HashSet<CounterTechnique>(
                inp.Candidates ?? System.Array.Empty<CounterTechnique>());

            // SCISSOR_COUNTER: LS at max in the direction OPPOSITE to the sweep
            if (cand.Contains(CounterTechnique.ScissorCounter) &&
                inp.AttackerSweepLateralSign != 0 &&
                System.Math.Sign(inp.Frame.Ls.X) == -System.Math.Sign(inp.AttackerSweepLateralSign) &&
                System.Math.Abs(inp.Frame.Ls.X) >= thresh.LsOppositeAbs)
                return (next, CounterTechnique.ScissorCounter);

            // TRIANGLE_EARLY_STACK: BtnBase held ≥ 500ms AND LS up
            if (cand.Contains(CounterTechnique.TriangleEarlyStack) &&
                next.BtnBaseHeldMs >= timing.StackHoldMs &&
                inp.Frame.Ls.Y >= thresh.LsUp)
                return (next, CounterTechnique.TriangleEarlyStack);

            return (next, null);
        }
    }
}
