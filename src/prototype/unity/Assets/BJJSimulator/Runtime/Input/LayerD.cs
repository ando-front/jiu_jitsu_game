// Ported 1:1 from src/prototype/web/src/input/layerD.ts.
// PURE — Layer D: judgment-window commit resolution.
// Reference: docs/design/input_system_v1.md §D.1.1.
//
// Resolves technique commits only while the judgment window is OPEN.
// Owns a small temporal state for long-press and rapid-press detection.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Timing / thresholds (§D.1.1)
    // -------------------------------------------------------------------------

    public struct LayerDTiming
    {
        public float TriangleHoldMs;
        public float HipBumpPressWindowMs;

        public static readonly LayerDTiming Default = new LayerDTiming
        {
            TriangleHoldMs       = 500f,
            HipBumpPressWindowMs = 200f,
        };
    }

    public struct LayerDThresholds
    {
        public float LsHorizontalAbs;
        public float LsUp;
        public float RsUp;
        public float HipBumpReleasedMaxTrigger;
        public float HipBumpPressedMinTrigger;
        public float CrossCollarBothMin;

        public static readonly LayerDThresholds Default = new LayerDThresholds
        {
            LsHorizontalAbs          = 0.8f,
            LsUp                     = 0.8f,
            RsUp                     = 0.8f,
            HipBumpReleasedMaxTrigger = 0.1f,
            HipBumpPressedMinTrigger  = 0.9f,
            CrossCollarBothMin       = 0.95f,
        };
    }

    // -------------------------------------------------------------------------
    // Per-player temporal state
    // -------------------------------------------------------------------------

    public struct LayerDState
    {
        public float BtnBaseHeldMs;
        public long  RTriggerLastReleasedMs; // BJJConst.SentinelTimeMs until first release
        public float RTriggerPrevValue;

        public static readonly LayerDState Initial = new LayerDState
        {
            BtnBaseHeldMs           = 0f,
            RTriggerLastReleasedMs  = BJJConst.SentinelTimeMs,
            RTriggerPrevValue       = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Tick inputs
    // -------------------------------------------------------------------------

    public struct LayerDInputs
    {
        public long        NowMs;
        public float       DtMs;
        public InputFrame  Frame;
        public HipIntent   Hip;
        public Technique[] Candidates;
        public bool        WindowIsOpen;
    }

    // -------------------------------------------------------------------------
    // Pure resolver
    // -------------------------------------------------------------------------

    public static class LayerDOps
    {
        public static (LayerDState NextState, Technique? ConfirmedTechnique) Resolve(
            LayerDState prev,
            LayerDInputs inp,
            LayerDTiming   timing     = default,
            LayerDThresholds thresh   = default)
        {
            if (timing.TriangleHoldMs == 0f)       timing = LayerDTiming.Default;
            if (thresh.LsHorizontalAbs == 0f)      thresh = LayerDThresholds.Default;

            // Always update tracking state (even when window is closed) so
            // rapid-press detection accumulates before OPENING fires.
            var next = UpdateTracking(prev, inp, timing, thresh);

            if (!inp.WindowIsOpen)
                return (next, null);

            var cand = new System.Collections.Generic.HashSet<Technique>(
                inp.Candidates ?? System.Array.Empty<Technique>());

            // Resolve in §D.1.1 table order — first match wins.

            // SCISSOR_SWEEP: LBumper edge + LS pure horizontal
            if (cand.Contains(Technique.ScissorSweep) &&
                (inp.Frame.ButtonEdges & ButtonBit.LBumper) != 0 &&
                System.Math.Abs(inp.Frame.Ls.X) >= thresh.LsHorizontalAbs)
                return (next, Technique.ScissorSweep);

            // FLOWER_SWEEP: RBumper edge + LS up
            if (cand.Contains(Technique.FlowerSweep) &&
                (inp.Frame.ButtonEdges & ButtonBit.RBumper) != 0 &&
                inp.Frame.Ls.Y >= thresh.LsUp)
                return (next, Technique.FlowerSweep);

            // TRIANGLE: BtnBase held ≥ 500ms
            if (cand.Contains(Technique.Triangle) &&
                next.BtnBaseHeldMs >= timing.TriangleHoldMs)
                return (next, Technique.Triangle);

            // OMOPLATA: LBumper edge + RS up
            if (cand.Contains(Technique.Omoplata) &&
                (inp.Frame.ButtonEdges & ButtonBit.LBumper) != 0 &&
                inp.Frame.Rs.Y >= thresh.RsUp)
                return (next, Technique.Omoplata);

            // HIP_BUMP: RTrigger 0→1 within 200ms window
            if (cand.Contains(Technique.HipBump) &&
                inp.Frame.RTrigger >= thresh.HipBumpPressedMinTrigger &&
                prev.RTriggerPrevValue < thresh.HipBumpPressedMinTrigger &&
                prev.RTriggerLastReleasedMs != BJJConst.SentinelTimeMs &&
                inp.NowMs - prev.RTriggerLastReleasedMs <= (long)timing.HipBumpPressWindowMs)
                return (next, Technique.HipBump);

            // CROSS_COLLAR: both triggers simultaneously at max
            if (cand.Contains(Technique.CrossCollar) &&
                inp.Frame.LTrigger >= thresh.CrossCollarBothMin &&
                inp.Frame.RTrigger >= thresh.CrossCollarBothMin)
                return (next, Technique.CrossCollar);

            return (next, null);
        }

        // -----------------------------------------------------------------------

        private static LayerDState UpdateTracking(
            LayerDState prev, LayerDInputs inp,
            LayerDTiming timing, LayerDThresholds thresh)
        {
            bool baseHeld = (inp.Frame.Buttons & ButtonBit.BtnBase) != 0;
            float baseMs  = baseHeld ? prev.BtnBaseHeldMs + inp.DtMs : 0f;

            bool  rel           = inp.Frame.RTrigger <= thresh.HipBumpReleasedMaxTrigger;
            long  lastReleasedMs = rel ? inp.NowMs : prev.RTriggerLastReleasedMs;

            return new LayerDState
            {
                BtnBaseHeldMs          = baseMs,
                RTriggerLastReleasedMs = lastReleasedMs,
                RTriggerPrevValue      = inp.Frame.RTrigger,
            };
        }
    }
}
