// PLATFORM — full-skeleton BlockMan rig driven by BJJPose.ComputeScenePoses.
//
// Replaces the old offset-only BJJAvatarBinder. Each LateUpdate it:
//   1. Assembles BottomPoseInputs / TopPoseInputs from BJJGameManager's
//      CurrentGameState + the provider's last Layer-B Intent / DefenseIntent.
//   2. Calls BJJPose.ComputeScenePoses → the same procedural pose synthesis
//      the Stage-1 web prototype (pose.ts) uses, ported 1:1 to BJJPose.cs.
//   3. Smooths every joint-angle channel with damped springs whose tuning
//      mirrors blockman.ts (arm 5.5/0.65, leg 4.0/0.80, torso 3.0/0.90,
//      pelvis 4.0/1.0), then adds the post-smoothing tremor jitter.
//   4. Runs forward kinematics with BJJPose.Matrix3 — the EXACT matrices the
//      validated FK / IK tests use — to get world-space joint positions, and
//      places sphere joints + cylinder bones there.
//
// Tier-7 realism layer (this file, visual only; pure logic lives in BJJPose):
//   - Breathing: chest swells (x +4%, z +6%) and the shoulders rise / abduct
//     with the breath oscillator (matches blockman.ts constants).
//   - Muscle tension: grip strength swells the forearm/upper-arm radius;
//     fatigue raises the tremor frequency and lowers its amplitude.
//   - Ground contact: the stance (top) body is lifted so its lowest foot/knee
//     rests on the mat (BJJPose.GroundRestOffsetY), and nudged back over its
//     support base when the COM drifts out (BJJPose.ComInsideSupport).
//   - Head look-at: a face indicator aims at the opponent — top → opponent
//     head, bottom → opponent hip — via BJJPose.GazeTo (blend 0.6, ±60°).
//
// The skeleton is built procedurally in Awake, so no Inspector wiring is
// needed — the scene runs headless and BJJ → Setup Scene just adds the
// component.
//
// Bottom = guard-side player (attacker intent). Top = passer-side (defender).

using UnityEngine;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJPoseRig : MonoBehaviour
    {
        [Header("Colours")]
        [SerializeField] private Color bottomColor = new Color(0.35f, 0.55f, 1.00f);
        [SerializeField] private Color topColor    = new Color(0.79f, 0.71f, 0.54f);
        [SerializeField] private Color noseColor   = new Color(1.00f, 0.85f, 0.30f);

        [Header("Rig dimensions")]
        [SerializeField, Range(0.01f, 0.1f)] private float jointRadius = 0.045f;
        [SerializeField, Range(0.01f, 0.1f)] private float boneRadius  = 0.028f;

        private BJJGameManager _manager;
        private Skeleton _bottom;
        private Skeleton _top;
        private readonly SpringSet _bottomSprings = new SpringSet();
        private readonly SpringSet _topSprings    = new SpringSet();
        // Tier-8 secondary-motion (inertia lag / follow-through) layer.
        private readonly SecondaryLayer _bottomSecondary = new SecondaryLayer();
        private readonly SecondaryLayer _topSecondary    = new SecondaryLayer();

        // Tier-8 anticipation + impact ripple bookkeeping, per body.
        private readonly Anim _bottomAnim = new Anim();
        private readonly Anim _topAnim    = new Anim();

        // Per-body transient animation state (anticipation windup + body wave).
        private sealed class Anim
        {
            public int   ActionSig = int.MinValue;  // changes when a big move is selected
            public float AnticipStartMs = -1f;       // when the current windup began
            public float RipplePrevMs = -1f;         // last frame's elapsed-since-impact
            public float RippleStartMs = -1f;        // when the body wave was kicked (-1 = none)
            public bool  Initialised;
        }

        // Body-wave timing: pelvis → torso → shoulders → hands, 50 ms apart.
        private const float RippleStepMs = 50f;
        private const float AnticipWindowMs = 130f; // 0.13 s reverse load

        void Awake()
        {
            _manager = GetComponent<BJJGameManager>();
            BuildRig();
        }

        void LateUpdate()
        {
            if (_manager == null || _bottom == null) return;
            var g = _manager.CurrentGameState;
            float nowMs = g.NowMs;
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            Intent? intent = _manager.Provider != null ? _manager.Provider.LastIntent : null;
            DefenseIntent? defense = _manager.Provider != null ? _manager.Provider.LastDefense : null;
            RenderFrame(g, intent, defense, nowMs, dt);
        }

        // Public entry for the Editor capture tool: settle the rig onto a
        // GameState in one shot (springs jump straight to target, no easing).
        public void ApplyImmediate(GameState g, Intent? intent, DefenseIntent? defense, float nowMs)
        {
            if (_bottom == null) BuildRig();
            _bottomSprings.Reset();
            _topSprings.Reset();
            _bottomSecondary.Reset();
            _topSecondary.Reset();
            ResetAnim(_bottomAnim);
            ResetAnim(_topAnim);
            RenderFrame(g, intent, defense, nowMs, 0f);
        }

        private static void ResetAnim(Anim a)
        {
            a.ActionSig = int.MinValue; a.AnticipStartMs = -1f;
            a.RipplePrevMs = -1f; a.RippleStartMs = -1f; a.Initialised = false;
        }

        private void RenderFrame(GameState g, Intent? intent, DefenseIntent? defense, float nowMs, float dt)
        {
            var bottomIn = BuildBottomInputs(g, intent, nowMs);
            var topIn    = BuildTopInputs(g, defense, nowMs);
            var scene    = BJJPose.ComputeScenePoses(bottomIn, topIn);

            float fatigueB = Mathf.Clamp01(1f - g.Bottom.Stamina);
            float fatigueT = Mathf.Clamp01(1f - g.Top.Stamina);

            // --- Anticipation: a big-move selection loads weight back briefly ---
            UpdateAnticipation(_bottomAnim, BottomActionSig(g), nowMs);
            UpdateAnticipation(_topAnim,    TopActionSig(g),    nowMs);

            // --- Impact ripple: poll the sim's events for big state changes ----
            PollRippleEvents(nowMs);

            // Spine roundedness: the guard-bottom turtles its back (rounds
            // forward); the passer-top stacks tall and extends, rounding only
            // as its posture is broken down.
            float roundB = 0.55f;
            float roundT = -0.45f + Mathf.Clamp01(g.Top.PostureBreak.Y) * 1.1f;

            RenderPose bp = ComputeRender(_bottomSprings, _bottomSecondary, _bottomAnim,
                scene.Bottom, BJJPose.BottomPlacement, nowMs, dt, fatigueB, roundB);
            RenderPose tp = ComputeRender(_topSprings, _topSecondary, _topAnim,
                scene.Top, BJJPose.TopPlacement, nowMs, dt, fatigueT, roundT);

            // Ground contact + balance recovery — stance (top) body only; the
            // supine bottom player rests on its back, not its feet.
            GroundAndBalance(ref tp);

            // Mutual head look-at: top eyes the opponent's head, bottom eyes the
            // opponent's hip (it is looking up past the passer's base).
            AimGaze(ref tp, bp.Head);
            AimGaze(ref bp, tp.Pelvis);

            Place(_bottom, bp);
            Place(_top, tp);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Anticipation + impact-ripple drivers (Tier 8)
        // ─────────────────────────────────────────────────────────────────────

        // A coarse hash of "what big thing is this body doing right now". When
        // it changes, a new move has been committed → fire the windup.
        private static int BottomActionSig(GameState g)
        {
            int sig = (int)g.Guard * 31;
            var jw = g.JudgmentWindow;
            bool open = jw.State == JudgmentWindowState.Open || jw.State == JudgmentWindowState.Opening;
            if (open && jw.Candidates != null && jw.Candidates.Length > 0)
                sig = sig * 131 + ((int)jw.Candidates[0] + 1);
            return sig;
        }

        private static int TopActionSig(GameState g)
        {
            int sig = g.PassAttempt.Kind == PassAttemptKind.InProgress ? 7 : 0;
            if (g.CutAttempts.Left.Kind  == CutSlotKind.InProgress) sig += 17;
            if (g.CutAttempts.Right.Kind == CutSlotKind.InProgress) sig += 53;
            return sig;
        }

        private static void UpdateAnticipation(Anim a, int sig, float nowMs)
        {
            if (!a.Initialised) { a.ActionSig = sig; a.Initialised = true; return; }
            if (sig != a.ActionSig)
            {
                a.ActionSig = sig;
                a.AnticipStartMs = nowMs; // begin a fresh reverse-load windup
            }
        }

        // Walk the sim's last-step events; a big momentum swing kicks a body
        // wave that travels pelvis → torso → shoulders → hands.
        private void PollRippleEvents(float nowMs)
        {
            if (_manager == null) return;
            var events = _manager.LastStepEvents;
            if (events == null) return;
            for (int i = 0; i < events.Length; i++)
            {
                switch (events[i].Kind)
                {
                    // The bottom lands a sweep/submission → both bodies lurch.
                    case SimEventKind.TechniqueConfirmed:
                    case SimEventKind.CounterConfirmed:
                        _bottomAnim.RippleStartMs = nowMs; _bottomAnim.RipplePrevMs = -1f;
                        _topAnim.RippleStartMs    = nowMs; _topAnim.RipplePrevMs    = -1f;
                        break;
                    // The pass / leg lock resolves → the swept/passed body lurches.
                    case SimEventKind.PassSucceeded:
                    case SimEventKind.PassFailed:
                    case SimEventKind.FootLockSucceeded:
                        _bottomAnim.RippleStartMs = nowMs; _bottomAnim.RipplePrevMs = -1f;
                        break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Input assembly — CurrentGameState (+ last intent) → pose inputs
        // ─────────────────────────────────────────────────────────────────────

        private static BottomPoseInputs BuildBottomInputs(GameState g, Intent? intent, float nowMs)
        {
            long now = g.NowMs;
            bool windowOpen =
                g.JudgmentWindow.State == JudgmentWindowState.Open ||
                g.JudgmentWindow.State == JudgmentWindowState.Opening;
            var cands = g.JudgmentWindow.Candidates;
            bool hasTech = windowOpen && cands != null && cands.Length > 0;

            var hip  = intent.HasValue ? intent.Value.Hip  : HipIntent.Zero;
            var grip = intent.HasValue ? intent.Value.Grip : GripIntent.Zero;

            return new BottomPoseInputs
            {
                NowMs              = nowMs,
                Stamina            = g.Bottom.Stamina,
                Guard              = g.Guard,
                LeftHand           = LimbSnapshot.Of(g.Bottom.LeftHand,  now),
                RightHand          = LimbSnapshot.Of(g.Bottom.RightHand, now),
                LeftFoot           = g.Bottom.LeftFoot.State,
                RightFoot          = g.Bottom.RightFoot.State,
                HipAngle           = hip.HipAngleTarget,
                HipPush            = hip.HipPush,
                HipLateral         = hip.HipLateral,
                GripStrengthL      = grip.LGripStrength,
                GripStrengthR      = grip.RGripStrength,
                WindowOpen         = windowOpen,
                HasWindowTechnique = hasTech,
                WindowTechnique    = hasTech ? cands[0] : default,
            };
        }

        private static TopPoseInputs BuildTopInputs(GameState g, DefenseIntent? defense, float nowMs)
        {
            long now = g.NowMs;
            var hip = defense.HasValue ? defense.Value.Hip : TopHipIntent.Zero;

            bool hasPass = g.PassAttempt.Kind == PassAttemptKind.InProgress;
            bool hasCutL = g.CutAttempts.Left.Kind  == CutSlotKind.InProgress;
            bool hasCutR = g.CutAttempts.Right.Kind == CutSlotKind.InProgress;
            bool counterOpen =
                g.CounterWindow.State == CounterWindowState.Open ||
                g.CounterWindow.State == CounterWindowState.Opening;

            return new TopPoseInputs
            {
                NowMs            = nowMs,
                Stamina          = g.Top.Stamina,
                LeftHand         = LimbSnapshot.Of(g.Top.LeftHand,  now),
                RightHand        = LimbSnapshot.Of(g.Top.RightHand, now),
                PostureBreakX    = g.Top.PostureBreak.X,
                PostureBreakY    = g.Top.PostureBreak.Y,
                WeightForward    = hip.WeightForward,
                WeightLateral    = hip.WeightLateral,
                ArmExtractedL    = g.Top.ArmExtractedLeft,
                ArmExtractedR    = g.Top.ArmExtractedRight,
                HasPass          = hasPass,
                PassElapsedMs    = hasPass ? nowMs - g.PassAttempt.StartedMs : 0f,
                HasCutL          = hasCutL,
                CutElapsedLMs    = hasCutL ? nowMs - g.CutAttempts.Left.StartedMs  : 0f,
                HasCutR          = hasCutR,
                CutElapsedRMs    = hasCutR ? nowMs - g.CutAttempts.Right.StartedMs : 0f,
                CounterWindowOpen = counterOpen,
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Compute one body's world-space render pose (springs → FK)
        // ─────────────────────────────────────────────────────────────────────

        private RenderPose ComputeRender(SpringSet sp, SecondaryLayer sec, Anim anim,
                                         BodyPose target, RigPlacement place,
                                         float nowMs, float dt, float fatigue, float spineRound)
        {
            // A freshly committed move briefly underdamps the limbs so the reach
            // whips out and overshoots before settling.
            if (anim.AnticipStartMs >= 0f && nowMs - anim.AnticipStartMs < AnticipWindowMs)
                sp.ExciteLimbs();

            BodyPose p = sp.Step(target, dt);

            // Secondary motion: a lighter, underdamped (8 Hz / ζ0.4) oscillator
            // chases the primary smoother. Heavy parts (pelvis, torso) blend in
            // more of it, so they lag and follow through; light limbs barely do.
            // Body-wave impulses are kicked into this layer (see PollRippleEvents).
            FireRipple(sec, anim, nowMs);
            p = sec.Step(p, dt);

            // Tremor / strain jitter, added *after* smoothing. Fatigue raises the
            // frequency (faster shudder) and lowers the amplitude (finer shake).
            float treL = Jitter(nowMs, target.ArmL.Tremor, 0.0f, fatigue);
            float treR = Jitter(nowMs, target.ArmR.Tremor, 2.4f, fatigue);
            float strain = Jitter(nowMs, target.TorsoTremor, 1.7f, fatigue);

            ArmPose armL = p.ArmL; armL.ShoulderPitch += treL; armL.ElbowBend += treL * 1.5f;
            ArmPose armR = p.ArmR; armR.ShoulderPitch += treR; armR.ElbowBend += treR * 1.5f;
            p.ArmL = armL; p.ArmR = armR;
            p.TorsoPitch += strain;
            p.TorsoRoll  += strain * 0.7f;

            // Anticipation: a brief reverse weight-load on the pelvis + a torso
            // un-curl just before the move springs forward.
            float antElapsed = anim.AnticipStartMs >= 0f ? nowMs - anim.AnticipStartMs : -1f;
            float antic = BJJPose.AnticipationOffset(antElapsed, AnticipWindowMs, 1f);
            p.PelvisZ    += antic * 0.04f;
            p.TorsoPitch += antic * 0.18f;

            Matrix3 root = place.YawPi ? Matrix3.RotY(Mathf.PI) : Matrix3.Identity;
            Vector3 pelvisPos = place.Origin + new Vector3(p.PelvisX, p.PelvisY, p.PelvisZ);
            Matrix3 pelvisRot = Matrix3.Mul(root, Matrix3.EulerXYZ(p.PelvisPitch, p.PelvisYaw, p.PelvisRoll));

            // Spine S-curve: split TorsoPitch across three segments (sum
            // preserved → chest orientation unchanged) and walk the bent chain
            // so the back visibly rounds (bottom) or extends (top).
            BJJPose.SpineSCurve(p.TorsoPitch, spineRound, out float spLo, out float spMid, out float spUp);
            float seg = BJJPose.PelvisToTorso / 3f;
            Matrix3 rotLo  = Matrix3.Mul(pelvisRot, Matrix3.RotX(spLo));
            Vector3 lowerSpine = pelvisPos + rotLo.MulV(new Vector3(0, seg, 0));
            Matrix3 rotMid = Matrix3.Mul(rotLo, Matrix3.RotX(spMid));
            Vector3 midSpine   = lowerSpine + rotMid.MulV(new Vector3(0, seg, 0));
            Matrix3 rotUp  = Matrix3.Mul(rotMid, Matrix3.RotX(spUp));
            Vector3 torsoPos   = midSpine + rotUp.MulV(new Vector3(0, seg, 0));
            // Chest frame = the upper-spine tip + the torso yaw/roll (its pitch is
            // already baked into rotUp). Identical to the old single-bone torso.
            Matrix3 torsoRot = Matrix3.Mul(rotUp, Matrix3.Mul(Matrix3.RotY(p.TorsoYaw), Matrix3.RotZ(p.TorsoRoll)));

            // Breath: chest swells, shoulders rise and abduct (scapular spread).
            float breath01 = target.Breath * 0.5f + 0.5f;
            float shoulderY = BJJPose.ShoulderY + breath01 * 0.012f;
            float shoulderXMul = 1f + breath01 * 0.06f; // scapula abduction

            Vector3 neckPos = torsoPos + torsoRot.MulV(new Vector3(0, BJJPose.HeadY, 0));
            Matrix3 headRot = Matrix3.Mul(torsoRot, Matrix3.EulerXYZ(p.HeadPitch, p.HeadYaw, 0f));
            Vector3 headPos = neckPos + headRot.MulV(new Vector3(0, BJJPose.HeadCenterY, 0));

            var rp = new RenderPose
            {
                Pelvis = pelvisPos, LowerSpine = lowerSpine, MidSpine = midSpine,
                Chest = torsoPos, Head = headPos, NeckPivot = neckPos,
                TorsoRot = torsoRot, Breath01 = breath01,
                GripL = p.ArmL.Grip, GripR = p.ArmR.Grip,
                HeadPitch = p.HeadPitch, HeadYaw = p.HeadYaw,
            };
            ArmFK(p.ArmL, -1f, torsoPos, torsoRot, shoulderY, shoulderXMul,
                  out rp.ShoulderL, out rp.ElbowL, out rp.HandL, out rp.HandRotL);
            ArmFK(p.ArmR,  1f, torsoPos, torsoRot, shoulderY, shoulderXMul,
                  out rp.ShoulderR, out rp.ElbowR, out rp.HandR, out rp.HandRotR);
            LegFK(p.LegL, -1f, pelvisPos, pelvisRot, out rp.HipL, out rp.KneeL, out rp.AnkleL, out rp.ToeL);
            LegFK(p.LegR,  1f, pelvisPos, pelvisRot, out rp.HipR, out rp.KneeR, out rp.AnkleR, out rp.ToeR);
            return rp;
        }

        // Kick the body-wave impulses into the secondary layer as each 50 ms
        // segment trigger is crossed (pelvis → torso → shoulders → hands).
        private static void FireRipple(SecondaryLayer sec, Anim anim, float nowMs)
        {
            if (anim.RippleStartMs < 0f) return;
            float elapsed = nowMs - anim.RippleStartMs;
            float prev = anim.RipplePrevMs < 0f ? -2f : anim.RipplePrevMs;
            if (BJJPose.RippleFired(prev, elapsed, 0, RippleStepMs)) sec.Kick(SecondaryLayer.Group.Pelvis,    6f);
            if (BJJPose.RippleFired(prev, elapsed, 1, RippleStepMs)) sec.Kick(SecondaryLayer.Group.Torso,     5f);
            if (BJJPose.RippleFired(prev, elapsed, 2, RippleStepMs)) sec.Kick(SecondaryLayer.Group.Shoulders, 7f);
            if (BJJPose.RippleFired(prev, elapsed, 3, RippleStepMs)) sec.Kick(SecondaryLayer.Group.Hands,     9f);
            anim.RipplePrevMs = elapsed;
            if (elapsed > 4 * RippleStepMs + 200f) { anim.RippleStartMs = -1f; anim.RipplePrevMs = -1f; }
        }

        // Lift the body so its lowest foot/knee/hand rests on the mat, then nudge
        // it back over the support base if the COM has drifted outside.
        private void GroundAndBalance(ref RenderPose rp)
        {
            float lift = BJJPose.GroundRestOffsetY(0f,
                rp.AnkleL.y, rp.AnkleR.y, rp.ToeL.y, rp.ToeR.y,
                rp.KneeL.y, rp.KneeR.y, rp.HandL.y, rp.HandR.y);
            if (lift > 0f) Shift(ref rp, new Vector3(0f, lift, 0f));

            // Support base = whatever is (now) near the mat; COM ≈ chest+pelvis.
            var contacts = NearGround(rp);
            if (contacts.Length >= 3)
            {
                Vector3 com = (rp.Chest + rp.Pelvis) * 0.5f;
                Vector2 comXZ = new Vector2(com.x, com.z);
                if (!BJJPose.ComInsideSupport(comXZ, contacts))
                {
                    Vector2 centroid = Centroid(contacts);
                    Vector3 nudge = new Vector3(centroid.x - comXZ.x, 0f, centroid.y - comXZ.y) * 0.18f;
                    nudge = Vector3.ClampMagnitude(nudge, 0.08f);
                    Shift(ref rp, nudge);
                }
            }
        }

        private Vector2[] NearGround(RenderPose rp)
        {
            // Joints within 6 cm of the mat count as support contacts.
            var cands = new[] { rp.AnkleL, rp.AnkleR, rp.ToeL, rp.ToeR, rp.KneeL, rp.KneeR, rp.HandL, rp.HandR };
            int n = 0;
            for (int i = 0; i < cands.Length; i++) if (cands[i].y < 0.06f) n++;
            var outArr = new Vector2[n];
            int j = 0;
            for (int i = 0; i < cands.Length; i++)
                if (cands[i].y < 0.06f) outArr[j++] = new Vector2(cands[i].x, cands[i].z);
            return outArr;
        }

        private static Vector2 Centroid(Vector2[] pts)
        {
            Vector2 s = Vector2.zero;
            for (int i = 0; i < pts.Length; i++) s += pts[i];
            return s / pts.Length;
        }

        private static void Shift(ref RenderPose rp, Vector3 d)
        {
            rp.Pelvis += d; rp.LowerSpine += d; rp.MidSpine += d; rp.Chest += d; rp.Head += d; rp.NeckPivot += d;
            rp.ShoulderL += d; rp.ShoulderR += d; rp.ElbowL += d; rp.ElbowR += d;
            rp.HandL += d; rp.HandR += d;
            rp.HipL += d; rp.HipR += d; rp.KneeL += d; rp.KneeR += d;
            rp.AnkleL += d; rp.AnkleR += d; rp.ToeL += d; rp.ToeR += d;
        }

        // Aim the face indicator at a world target (blend 0.6, ±60° yaw).
        private void AimGaze(ref RenderPose rp, Vector3 target)
        {
            BJJPose.GazeTo(rp.NeckPivot, rp.TorsoRot, target,
                rp.HeadPitch, rp.HeadYaw, 0.6f, 1.0472f, -0.6f, 1.1f,
                out float pitch, out float yaw);
            // Face = torsoRot · Rx(pitch) · Ry(yaw) · ẑ.
            Matrix3 faceRot = Matrix3.Mul(rp.TorsoRot, Matrix3.EulerXYZ(pitch, yaw, 0f));
            rp.GazeNose = rp.Head + faceRot.MulV(new Vector3(0, 0, jointRadius * 1.6f));
        }

        private static void ArmFK(ArmPose arm, float side, Vector3 torsoPos, Matrix3 torsoRot,
                                  float shoulderY, float shoulderXMul,
                                  out Vector3 shoulder, out Vector3 elbow, out Vector3 hand,
                                  out Matrix3 handRot)
        {
            shoulder = torsoPos + torsoRot.MulV(new Vector3(BJJPose.ShoulderX * shoulderXMul * side, shoulderY, 0));
            Matrix3 armRot = Matrix3.Mul(torsoRot,
                Matrix3.EulerYXZ(arm.ShoulderPitch, arm.ShoulderYaw * side, arm.ShoulderRoll * side));
            elbow = shoulder + armRot.MulV(new Vector3(0, -BJJPose.UpperArm, 0));
            Matrix3 foreRot = Matrix3.Mul(armRot, Matrix3.RotX(-arm.ElbowBend));
            hand = elbow + foreRot.MulV(new Vector3(0, -BJJPose.ForeArm, 0));
            handRot = foreRot; // forearm frame: -y points down the hand, +z palm-forward
        }

        private static void LegFK(LegPose leg, float side, Vector3 pelvisPos, Matrix3 pelvisRot,
                                  out Vector3 hip, out Vector3 knee, out Vector3 ankle, out Vector3 toe)
        {
            hip = pelvisPos + pelvisRot.MulV(new Vector3(BJJPose.HipX * side, BJJPose.HipY, 0));
            Matrix3 legRot = Matrix3.Mul(pelvisRot,
                Matrix3.EulerYXZ(leg.HipPitch, leg.HipYaw * side, leg.HipRoll * side));
            knee = hip + legRot.MulV(new Vector3(0, -BJJPose.Thigh, 0));
            Matrix3 shinRot = Matrix3.Mul(legRot, Matrix3.RotX(leg.KneeBend));
            ankle = knee + shinRot.MulV(new Vector3(0, -BJJPose.Shin, 0));
            Matrix3 footRot = Matrix3.Mul(shinRot, Matrix3.RotX(leg.Ankle));
            toe = ankle + footRot.MulV(new Vector3(0, 0, 0.12f));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Place primitives from a computed RenderPose
        // ─────────────────────────────────────────────────────────────────────

        private void Place(Skeleton sk, RenderPose rp)
        {
            sk.Pelvis.position = rp.Pelvis;
            sk.LowerSpine.position = rp.LowerSpine;
            sk.MidSpine.position   = rp.MidSpine;
            sk.Chest.position  = rp.Chest;
            sk.Head.position   = rp.Head;
            sk.ShoulderL.position = rp.ShoulderL; sk.ShoulderR.position = rp.ShoulderR;
            sk.ElbowL.position = rp.ElbowL;       sk.ElbowR.position = rp.ElbowR;
            sk.HandL.position  = rp.HandL;        sk.HandR.position  = rp.HandR;
            sk.HipL.position   = rp.HipL;         sk.HipR.position   = rp.HipR;
            sk.KneeL.position  = rp.KneeL;        sk.KneeR.position  = rp.KneeR;
            sk.AnkleL.position = rp.AnkleL;       sk.AnkleR.position = rp.AnkleR;
            sk.Nose.position   = rp.GazeNose;

            // Grip → hand swell (open palm splayed, fist compact). blockman.ts.
            sk.HandL.localScale = HandScale(rp.GripL);
            sk.HandR.localScale = HandScale(rp.GripR);
            // Fingers curl shut with the grip channel (FingerCurl per knuckle).
            PlaceFingers(sk.FingersL, rp.HandL, rp.HandRotL, -1f, rp.GripL);
            PlaceFingers(sk.FingersR, rp.HandR, rp.HandRotR,  1f, rp.GripR);
            // Chest swells with the breath (x +4%, z +6% — matches blockman.ts).
            float d = jointRadius * 2f;
            sk.Chest.localScale = new Vector3(d * (1f + rp.Breath01 * 0.04f), d, d * (1f + rp.Breath01 * 0.06f));

            // Bones. Arm bones thicken with grip (muscle tension).
            float gripMulL = 1f + Mathf.Clamp01(rp.GripL) * 0.5f;
            float gripMulR = 1f + Mathf.Clamp01(rp.GripR) * 0.5f;
            // Three spine segments form the S-curve (lumbar → thoracic → chest).
            Bone(sk.SpineLoBone,  rp.Pelvis,     rp.LowerSpine);
            Bone(sk.SpineMidBone, rp.LowerSpine, rp.MidSpine);
            Bone(sk.SpineUpBone,  rp.MidSpine,   rp.Chest);
            Bone(sk.NeckBone,  rp.Chest,  rp.Head); // full chest→head span
            Bone(sk.ClavLBone, rp.Chest,  rp.ShoulderL);  Bone(sk.ClavRBone, rp.Chest,  rp.ShoulderR);
            Bone(sk.UpArmLBone, rp.ShoulderL, rp.ElbowL, gripMulL); Bone(sk.UpArmRBone, rp.ShoulderR, rp.ElbowR, gripMulR);
            Bone(sk.LoArmLBone, rp.ElbowL, rp.HandL, gripMulL);     Bone(sk.LoArmRBone, rp.ElbowR, rp.HandR, gripMulR);
            Bone(sk.PelvLBone, rp.Pelvis, rp.HipL);   Bone(sk.PelvRBone, rp.Pelvis, rp.HipR);
            Bone(sk.ThighLBone, rp.HipL, rp.KneeL);   Bone(sk.ThighRBone, rp.HipR, rp.KneeR);
            Bone(sk.ShinLBone,  rp.KneeL, rp.AnkleL); Bone(sk.ShinRBone,  rp.KneeR, rp.AnkleR);
            Bone(sk.FootLBone,  rp.AnkleL, rp.ToeL);  Bone(sk.FootRBone,  rp.AnkleR, rp.ToeR);
        }

        private Vector3 HandScale(float grip)
        {
            float g = Mathf.Clamp01(grip);
            float d = jointRadius * 2f;
            return new Vector3(d * (1.55f - g * 0.75f), d * (0.62f + g * 0.33f), d * (1.42f - g * 0.62f));
        }

        // Curl four 3-knuckle fingers off the hand. Each knuckle folds by
        // FingerCurl(grip, joint) about the hand's local x toward the palm, so
        // grip 0 splays the hand open and grip 1 clenches a fist that "bites"
        // into the gi. The hand frame is the forearm frame: -y runs down the
        // hand, +z is palm-forward.
        private void PlaceFingers(FingerRig fingers, Vector3 hand, Matrix3 handRot, float side, float grip)
        {
            const float segLen = 0.020f;
            float[] spread = { -0.6f, -0.2f, 0.2f, 0.6f }; // fan across the palm
            for (int f = 0; f < 4; f++)
            {
                // Mirror the fan for the left/right hand so both splay outward.
                Vector3 knuckle = hand + handRot.MulV(new Vector3(spread[f] * 0.03f * side, -jointRadius * 0.9f, 0f));
                Matrix3 rot = handRot;
                Vector3 p = knuckle;
                for (int j = 0; j < 3; j++)
                {
                    rot = Matrix3.Mul(rot, Matrix3.RotX(-BJJPose.FingerCurl(grip, j)));
                    Vector3 next = p + rot.MulV(new Vector3(0, -segLen, 0));
                    int idx = f * 3 + j;
                    Bone(fingers.Bones[idx], p, next, 0.35f);
                    fingers.Joints[idx].position = next;
                    p = next;
                }
            }
        }

        // Fatigue raises the shudder frequency and trims its amplitude.
        private static float Jitter(float nowMs, float amp, float phase, float fatigue)
        {
            if (amp <= 0f) return 0f;
            float hz = 13f * (1f + fatigue * 0.6f);
            float ampScale = 1f - fatigue * 0.4f;
            return Mathf.Sin((nowMs / 1000f) * 2f * Mathf.PI * hz + phase) * amp * 0.06f * ampScale;
        }

        private void Bone(Transform bone, Vector3 a, Vector3 b, float radiusMul = 1f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            bone.position = (a + b) * 0.5f;
            if (len > 1e-5f) bone.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            // Default cylinder is 2 units tall along Y → scale.y = len / 2.
            float r = boneRadius * 2f * radiusMul;
            bone.localScale = new Vector3(r, Mathf.Max(len * 0.5f, 1e-4f), r);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Rig construction (procedural — no Inspector wiring needed)
        // ─────────────────────────────────────────────────────────────────────

        private void BuildRig()
        {
            var charsExisting = transform.Find("BJJPoseRig_Characters");
            if (charsExisting != null) DestroyImmediate(charsExisting.gameObject);

            var chars = new GameObject("BJJPoseRig_Characters").transform;
            chars.SetParent(transform, false);

            _bottom = BuildSkeleton(chars, "Bottom", bottomColor);
            _top    = BuildSkeleton(chars, "Top",    topColor);
        }

        private Skeleton BuildSkeleton(Transform parent, string name, Color color)
        {
            var rootGo = new GameObject(name);
            rootGo.transform.SetParent(parent, false);
            var root = rootGo.transform;

            var mat = MakeMaterial(color);
            var noseMat = MakeMaterial(noseColor);
            var sk = new Skeleton();

            sk.Pelvis    = Joint(root, "Pelvis", mat);
            sk.LowerSpine = Joint(root, "LowerSpine", mat, 0.85f);
            sk.MidSpine   = Joint(root, "MidSpine", mat, 0.9f);
            sk.Chest     = Joint(root, "Chest", mat);
            sk.Head      = Joint(root, "Head", mat, 1.4f);
            sk.Nose      = Joint(root, "Nose", noseMat, 0.5f); // gaze indicator
            sk.ShoulderL = Joint(root, "ShoulderL", mat);
            sk.ShoulderR = Joint(root, "ShoulderR", mat);
            sk.ElbowL    = Joint(root, "ElbowL", mat);
            sk.ElbowR    = Joint(root, "ElbowR", mat);
            sk.HandL     = Joint(root, "HandL", mat);
            sk.HandR     = Joint(root, "HandR", mat);
            sk.HipL      = Joint(root, "HipL", mat);
            sk.HipR      = Joint(root, "HipR", mat);
            sk.KneeL     = Joint(root, "KneeL", mat);
            sk.KneeR     = Joint(root, "KneeR", mat);
            sk.AnkleL    = Joint(root, "AnkleL", mat);
            sk.AnkleR    = Joint(root, "AnkleR", mat);

            sk.SpineLoBone  = Bone(root, "SpineLo", mat);
            sk.SpineMidBone = Bone(root, "SpineMid", mat);
            sk.SpineUpBone  = Bone(root, "SpineUp", mat);
            sk.NeckBone   = Bone(root, "Neck", mat);
            sk.ClavLBone  = Bone(root, "ClavL", mat);
            sk.ClavRBone  = Bone(root, "ClavR", mat);
            sk.UpArmLBone = Bone(root, "UpArmL", mat);
            sk.UpArmRBone = Bone(root, "UpArmR", mat);
            sk.LoArmLBone = Bone(root, "LoArmL", mat);
            sk.LoArmRBone = Bone(root, "LoArmR", mat);
            sk.PelvLBone  = Bone(root, "PelvL", mat);
            sk.PelvRBone  = Bone(root, "PelvR", mat);
            sk.ThighLBone = Bone(root, "ThighL", mat);
            sk.ThighRBone = Bone(root, "ThighR", mat);
            sk.ShinLBone  = Bone(root, "ShinL", mat);
            sk.ShinRBone  = Bone(root, "ShinR", mat);
            sk.FootLBone  = Bone(root, "FootL", mat);
            sk.FootRBone  = Bone(root, "FootR", mat);

            sk.FingersL = BuildFingers(root, "FingerL", mat);
            sk.FingersR = BuildFingers(root, "FingerR", mat);
            return sk;
        }

        private FingerRig BuildFingers(Transform root, string prefix, Material mat)
        {
            var fr = new FingerRig();
            for (int i = 0; i < 12; i++)
            {
                fr.Joints[i] = Joint(root, $"{prefix}_J{i}", mat, 0.4f);
                fr.Bones[i]  = Bone(root, $"{prefix}_B{i}", mat);
            }
            return fr;
        }

        private Transform Joint(Transform parent, string name, Material mat, float scaleMul = 1f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * (jointRadius * 2f * scaleMul);
            StripCollider(go);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        private Transform Bone(Transform parent, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name + "Bone";
            go.transform.SetParent(parent, false);
            StripCollider(go);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
        }

        private static Material MakeMaterial(Color color)
        {
            // Pick the shader that matches the *active* render pipeline so the
            // material never falls back to the magenta error shader: URP/Lit
            // under URP, built-in Standard otherwise.
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                     ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            Shader sh = rp != null ? Shader.Find("Universal Render Pipeline/Lit") : null;
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Data
        // ─────────────────────────────────────────────────────────────────────

        private struct RenderPose
        {
            public Vector3 Pelvis, LowerSpine, MidSpine, Chest, Head, NeckPivot;
            public Vector3 ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR;
            public Vector3 HipL, HipR, KneeL, KneeR, AnkleL, AnkleR, ToeL, ToeR;
            public Vector3 GazeNose;
            public Matrix3 TorsoRot, HandRotL, HandRotR;
            public float Breath01, GripL, GripR, HeadPitch, HeadYaw;
        }

        private class Skeleton
        {
            public Transform Pelvis, LowerSpine, MidSpine, Chest, Head, Nose;
            public Transform ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR;
            public Transform HipL, HipR, KneeL, KneeR, AnkleL, AnkleR;
            public Transform SpineLoBone, SpineMidBone, SpineUpBone, NeckBone, ClavLBone, ClavRBone;
            public Transform UpArmLBone, UpArmRBone, LoArmLBone, LoArmRBone;
            public Transform PelvLBone, PelvRBone, ThighLBone, ThighRBone;
            public Transform ShinLBone, ShinRBone, FootLBone, FootRBone;
            public FingerRig FingersL, FingersR;
        }

        // 4 fingers × 3 knuckles = 12 joint spheres + 12 thin bones per hand.
        private class FingerRig
        {
            public Transform[] Joints = new Transform[12];
            public Transform[] Bones  = new Transform[12];
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-channel damped-spring smoothing. Mirrors blockman.ts stepSpring:
    // semi-implicit Euler, 8 ms substeps, group tuning. One spring per scalar
    // angle of BodyPose; tremor/breath are passed through untouched.
    // ─────────────────────────────────────────────────────────────────────────

    internal sealed class SpringSet
    {
        // Channel layout (must match Step / Build below). Shared with
        // SecondaryLayer, so internal.
        internal const int PvX=0, PvY=1, PvZ=2, PvP=3, PvYw=4, PvR=5;
        internal const int ToP=6, ToY=7, ToR=8, HdP=9, HdY=10;
        internal const int LSp=11, LSr=12, LSy=13, LEb=14, LGr=15;
        internal const int RSp=16, RSr=17, RSy=18, REb=19, RGr=20;
        internal const int LHp=21, LHy=22, LHr=23, LKb=24, LAk=25;
        internal const int RHp=26, RHy=27, RHr=28, RKb=29, RAk=30;
        internal const int N = 31;

        // Tuning groups: arm 5.5/0.65, leg 4.0/0.80, torso 3.0/0.90, pelvis 4.0/1.0.
        static readonly float[] Freq = BuildFreq();
        static readonly float[] Zeta = BuildZeta();

        readonly float[] _x = new float[N];
        readonly float[] _v = new float[N];
        bool _init;
        bool _excite; // one-step flag: under-damp the limbs for a snappy reach

        public void Reset() { _init = false; _excite = false; }

        // Temporarily loosen the arm/leg damping so a freshly committed move
        // whips out and overshoots. Set per frame during the windup window;
        // consumed (and cleared) by the next Step.
        public void ExciteLimbs() => _excite = true;

        public BodyPose Step(BodyPose t, float dt)
        {
            float[] tgt = Flatten(t);
            if (!_init)
            {
                for (int i = 0; i < N; i++) { _x[i] = tgt[i]; _v[i] = 0f; }
                _init = true;
            }
            else
            {
                for (int i = 0; i < N; i++)
                {
                    // While excited, the limbs (arms LSp..RGr, legs LHp..RAk)
                    // drop to 0.6× zeta for a visible overshoot; the spine and
                    // pelvis keep their stable tuning.
                    float zeta = (_excite && i >= LSp) ? Zeta[i] * 0.6f : Zeta[i];
                    StepSpring(i, tgt[i], dt, Freq[i], zeta);
                }
            }
            _excite = false;
            return Rebuild(t);
        }

        void StepSpring(int i, float target, float dtS, float freqHz, float zeta)
        {
            BJJPose.IntegrateSpring(ref _x[i], ref _v[i], target, freqHz, zeta, dtS);
        }

        internal static float[] Flatten(BodyPose p)
        {
            var a = new float[N];
            a[PvX]=p.PelvisX; a[PvY]=p.PelvisY; a[PvZ]=p.PelvisZ;
            a[PvP]=p.PelvisPitch; a[PvYw]=p.PelvisYaw; a[PvR]=p.PelvisRoll;
            a[ToP]=p.TorsoPitch; a[ToY]=p.TorsoYaw; a[ToR]=p.TorsoRoll;
            a[HdP]=p.HeadPitch; a[HdY]=p.HeadYaw;
            a[LSp]=p.ArmL.ShoulderPitch; a[LSr]=p.ArmL.ShoulderRoll; a[LSy]=p.ArmL.ShoulderYaw; a[LEb]=p.ArmL.ElbowBend; a[LGr]=p.ArmL.Grip;
            a[RSp]=p.ArmR.ShoulderPitch; a[RSr]=p.ArmR.ShoulderRoll; a[RSy]=p.ArmR.ShoulderYaw; a[REb]=p.ArmR.ElbowBend; a[RGr]=p.ArmR.Grip;
            a[LHp]=p.LegL.HipPitch; a[LHy]=p.LegL.HipYaw; a[LHr]=p.LegL.HipRoll; a[LKb]=p.LegL.KneeBend; a[LAk]=p.LegL.Ankle;
            a[RHp]=p.LegR.HipPitch; a[RHy]=p.LegR.HipYaw; a[RHr]=p.LegR.HipRoll; a[RKb]=p.LegR.KneeBend; a[RAk]=p.LegR.Ankle;
            return a;
        }

        // Rebuild a BodyPose from the smoothed channels, carrying through the
        // un-sprung fields (Breath, Tremor) from the live target.
        BodyPose Rebuild(BodyPose t) => Rebuild(_x, t);

        internal static BodyPose Rebuild(float[] x, BodyPose t)
        {
            return new BodyPose
            {
                PelvisX=x[PvX], PelvisY=x[PvY], PelvisZ=x[PvZ],
                PelvisPitch=x[PvP], PelvisYaw=x[PvYw], PelvisRoll=x[PvR],
                TorsoPitch=x[ToP], TorsoYaw=x[ToY], TorsoRoll=x[ToR],
                TorsoTremor=t.TorsoTremor,
                HeadPitch=x[HdP], HeadYaw=x[HdY],
                Breath=t.Breath,
                ArmL = new ArmPose { ShoulderPitch=x[LSp], ShoulderRoll=x[LSr], ShoulderYaw=x[LSy], ElbowBend=x[LEb], Tremor=t.ArmL.Tremor, Grip=x[LGr] },
                ArmR = new ArmPose { ShoulderPitch=x[RSp], ShoulderRoll=x[RSr], ShoulderYaw=x[RSy], ElbowBend=x[REb], Tremor=t.ArmR.Tremor, Grip=x[RGr] },
                LegL = new LegPose { HipPitch=x[LHp], HipYaw=x[LHy], HipRoll=x[LHr], KneeBend=x[LKb], Ankle=x[LAk] },
                LegR = new LegPose { HipPitch=x[RHp], HipYaw=x[RHy], HipRoll=x[RHr], KneeBend=x[RKb], Ankle=x[RAk] },
            };
        }

        static float[] BuildFreq()
        {
            var f = new float[N];
            for (int i = PvX; i <= PvR; i++) f[i] = 4.0f;   // pelvis
            for (int i = ToP; i <= HdY; i++) f[i] = 3.0f;   // torso + head
            for (int i = LSp; i <= RGr; i++) f[i] = 5.5f;   // arms
            for (int i = LHp; i <= RAk; i++) f[i] = 4.0f;   // legs
            return f;
        }

        static float[] BuildZeta()
        {
            var z = new float[N];
            for (int i = PvX; i <= PvR; i++) z[i] = 1.0f;   // pelvis
            for (int i = ToP; i <= HdY; i++) z[i] = 0.90f;  // torso + head
            for (int i = LSp; i <= RGr; i++) z[i] = 0.65f;  // arms
            for (int i = LHp; i <= RAk; i++) z[i] = 0.80f;  // legs
            return z;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Secondary motion (Tier 8): a lighter, under-damped (8 Hz / ζ0.4) oscillator
    // that chases the already-smoothed primary pose. The render value is a blend
    // primary + (secondary − primary)·lag, so heavy parts (pelvis / torso) trail
    // and follow through while light limbs barely lag. Body-wave impulses
    // (Kick) inject velocity here so an impact ripples out and decays naturally.
    // Reuses SpringSet's channel layout / Flatten / Rebuild.
    // ─────────────────────────────────────────────────────────────────────────

    internal sealed class SecondaryLayer
    {
        public enum Group { Pelvis, Torso, Shoulders, Hands }

        const float Freq = 8f;
        const float Zeta = 0.4f;

        static readonly float[] Lag = BuildLag();

        readonly float[] _x = new float[SpringSet.N];
        readonly float[] _v = new float[SpringSet.N];
        bool _init;

        public void Reset()
        {
            _init = false;
            for (int i = 0; i < _v.Length; i++) _v[i] = 0f;
        }

        // Kick velocity into a body region — the impact ripple's per-segment
        // impulse. No-op before the first Step (the channel has no state yet).
        public void Kick(Group g, float dv)
        {
            if (!_init) return;
            switch (g)
            {
                case Group.Pelvis:    _v[SpringSet.PvP] += dv; _v[SpringSet.PvZ] += dv * 0.4f; break;
                case Group.Torso:     _v[SpringSet.ToP] += dv; break;
                case Group.Shoulders: _v[SpringSet.LSp] += dv; _v[SpringSet.RSp] += dv; break;
                case Group.Hands:     _v[SpringSet.LEb] += dv; _v[SpringSet.REb] += dv; break;
            }
        }

        public BodyPose Step(BodyPose primary, float dt)
        {
            float[] tgt = SpringSet.Flatten(primary);
            if (!_init)
            {
                for (int i = 0; i < tgt.Length; i++) { _x[i] = tgt[i]; _v[i] = 0f; }
                _init = true;
                return primary; // no lag on the first settled frame
            }
            var blended = new float[tgt.Length];
            for (int i = 0; i < tgt.Length; i++)
            {
                BJJPose.IntegrateSpring(ref _x[i], ref _v[i], tgt[i], Freq, Zeta, dt);
                blended[i] = tgt[i] + (_x[i] - tgt[i]) * Lag[i];
            }
            return SpringSet.Rebuild(blended, primary);
        }

        static float[] BuildLag()
        {
            var l = new float[SpringSet.N];
            for (int i = SpringSet.PvX; i <= SpringSet.PvR; i++) l[i] = 0.45f; // pelvis — heavy, trails
            for (int i = SpringSet.ToP; i <= SpringSet.HdY; i++) l[i] = 0.50f; // torso/head — heaviest
            for (int i = SpringSet.LSp; i <= SpringSet.RGr; i++) l[i] = 0.28f; // arms — light, snappy
            for (int i = SpringSet.LHp; i <= SpringSet.RAk; i++) l[i] = 0.32f; // legs
            return l;
        }
    }
}
