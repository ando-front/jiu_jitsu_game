// Ported 1:1 from src/prototype/web/src/state/score.ts.
// PURE — IBJJF-style position score. The guard passer (top) scores 3 for a
// completed pass into side control; the guard player (bottom) scores 2 for a
// sweep. Pure value transforms, mirroring the TS implementation.

namespace BJJSimulator
{
    [System.Serializable]
    public struct BJJScore
    {
        public int Top;
        public int Bottom;
    }

    public static class BJJScoreOps
    {
        // IBJJF point values.
        public const int PassPoints = 3;
        public const int SweepPoints = 2;

        public static readonly BJJScore Initial = new BJJScore { Top = 0, Bottom = 0 };

        public static BJJScore ApplyPass(BJJScore s) =>
            new BJJScore { Top = s.Top + PassPoints, Bottom = s.Bottom };

        public static BJJScore ApplySweep(BJJScore s) =>
            new BJJScore { Top = s.Top, Bottom = s.Bottom + SweepPoints };
    }
}
