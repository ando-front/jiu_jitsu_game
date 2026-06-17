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
            RenderFrame(g, intent, defense, nowMs, 0f);
        }

        private void RenderFrame(GameState g, Intent? intent, DefenseIntent? defense, float nowMs, float dt)
        {
            var bottomIn = BuildBottomInputs(g, intent, nowMs);
            var topIn    = BuildTopInputs(g, defense, nowMs);
            var scene    = BJJPose.ComputeScenePoses(bottomIn, topIn);

            float fatigueB = Mathf.Clamp01(1f - g.Bottom.Stamina);
            float fatigueT = Mathf.Clamp01(1f - g.Top.Stamina);

            RenderPose bp = ComputeRender(_bottomSprings, scene.Bottom, BJJPose.BottomPlacement, nowMs, dt, fatigueB);
            RenderPose tp = ComputeRender(_topSprings,    scene.Top,    BJJPose.TopPlacement,    nowMs, dt, fatigueT);

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

        private RenderPose ComputeRender(SpringSet sp, BodyPose target, RigPlacement place,
                                         float nowMs, float dt, float fatigue)
        {
            BodyPose p = sp.Step(target, dt);

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

            Matrix3 root = place.YawPi ? Matrix3.RotY(Mathf.PI) : Matrix3.Identity;
            Vector3 pelvisPos = place.Origin + new Vector3(p.PelvisX, p.PelvisY, p.PelvisZ);
            Matrix3 pelvisRot = Matrix3.Mul(root, Matrix3.EulerXYZ(p.PelvisPitch, p.PelvisYaw, p.PelvisRoll));
            Vector3 torsoPos = pelvisPos + pelvisRot.MulV(new Vector3(0, BJJPose.PelvisToTorso, 0));
            Matrix3 torsoRot = Matrix3.Mul(pelvisRot, Matrix3.EulerXYZ(p.TorsoPitch, p.TorsoYaw, p.TorsoRoll));

            // Breath: chest swells, shoulders rise and abduct (scapular spread).
            float breath01 = target.Breath * 0.5f + 0.5f;
            float shoulderY = BJJPose.ShoulderY + breath01 * 0.012f;
            float shoulderXMul = 1f + breath01 * 0.06f; // scapula abduction

            Vector3 neckPos = torsoPos + torsoRot.MulV(new Vector3(0, BJJPose.HeadY, 0));
            Matrix3 headRot = Matrix3.Mul(torsoRot, Matrix3.EulerXYZ(p.HeadPitch, p.HeadYaw, 0f));
            Vector3 headPos = neckPos + headRot.MulV(new Vector3(0, BJJPose.HeadCenterY, 0));

            var rp = new RenderPose
            {
                Pelvis = pelvisPos, Chest = torsoPos, Head = headPos, NeckPivot = neckPos,
                TorsoRot = torsoRot, Breath01 = breath01,
                GripL = p.ArmL.Grip, GripR = p.ArmR.Grip,
                HeadPitch = p.HeadPitch, HeadYaw = p.HeadYaw,
            };
            ArmFK(p.ArmL, -1f, torsoPos, torsoRot, shoulderY, shoulderXMul, out rp.ShoulderL, out rp.ElbowL, out rp.HandL);
            ArmFK(p.ArmR,  1f, torsoPos, torsoRot, shoulderY, shoulderXMul, out rp.ShoulderR, out rp.ElbowR, out rp.HandR);
            LegFK(p.LegL, -1f, pelvisPos, pelvisRot, out rp.HipL, out rp.KneeL, out rp.AnkleL, out rp.ToeL);
            LegFK(p.LegR,  1f, pelvisPos, pelvisRot, out rp.HipR, out rp.KneeR, out rp.AnkleR, out rp.ToeR);
            return rp;
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
            rp.Pelvis += d; rp.Chest += d; rp.Head += d; rp.NeckPivot += d;
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
                                  out Vector3 shoulder, out Vector3 elbow, out Vector3 hand)
        {
            shoulder = torsoPos + torsoRot.MulV(new Vector3(BJJPose.ShoulderX * shoulderXMul * side, shoulderY, 0));
            Matrix3 armRot = Matrix3.Mul(torsoRot,
                Matrix3.EulerYXZ(arm.ShoulderPitch, arm.ShoulderYaw * side, arm.ShoulderRoll * side));
            elbow = shoulder + armRot.MulV(new Vector3(0, -BJJPose.UpperArm, 0));
            Matrix3 foreRot = Matrix3.Mul(armRot, Matrix3.RotX(-arm.ElbowBend));
            hand = elbow + foreRot.MulV(new Vector3(0, -BJJPose.ForeArm, 0));
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
            // Chest swells with the breath (x +4%, z +6% — matches blockman.ts).
            float d = jointRadius * 2f;
            sk.Chest.localScale = new Vector3(d * (1f + rp.Breath01 * 0.04f), d, d * (1f + rp.Breath01 * 0.06f));

            // Bones. Arm bones thicken with grip (muscle tension).
            float gripMulL = 1f + Mathf.Clamp01(rp.GripL) * 0.5f;
            float gripMulR = 1f + Mathf.Clamp01(rp.GripR) * 0.5f;
            Bone(sk.SpineBone, rp.Pelvis, rp.Chest);
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

            sk.SpineBone  = Bone(root, "Spine", mat);
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
            return sk;
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
            public Vector3 Pelvis, Chest, Head, NeckPivot;
            public Vector3 ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR;
            public Vector3 HipL, HipR, KneeL, KneeR, AnkleL, AnkleR, ToeL, ToeR;
            public Vector3 GazeNose;
            public Matrix3 TorsoRot;
            public float Breath01, GripL, GripR, HeadPitch, HeadYaw;
        }

        private class Skeleton
        {
            public Transform Pelvis, Chest, Head, Nose;
            public Transform ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR;
            public Transform HipL, HipR, KneeL, KneeR, AnkleL, AnkleR;
            public Transform SpineBone, NeckBone, ClavLBone, ClavRBone;
            public Transform UpArmLBone, UpArmRBone, LoArmLBone, LoArmRBone;
            public Transform PelvLBone, PelvRBone, ThighLBone, ThighRBone;
            public Transform ShinLBone, ShinRBone, FootLBone, FootRBone;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-channel damped-spring smoothing. Mirrors blockman.ts stepSpring:
    // semi-implicit Euler, 8 ms substeps, group tuning. One spring per scalar
    // angle of BodyPose; tremor/breath are passed through untouched.
    // ─────────────────────────────────────────────────────────────────────────

    internal sealed class SpringSet
    {
        // Channel layout (must match Step / Build below).
        const int PvX=0, PvY=1, PvZ=2, PvP=3, PvYw=4, PvR=5;
        const int ToP=6, ToY=7, ToR=8, HdP=9, HdY=10;
        const int LSp=11, LSr=12, LSy=13, LEb=14, LGr=15;
        const int RSp=16, RSr=17, RSy=18, REb=19, RGr=20;
        const int LHp=21, LHy=22, LHr=23, LKb=24, LAk=25;
        const int RHp=26, RHy=27, RHr=28, RKb=29, RAk=30;
        const int N = 31;

        const float SubstepS = 0.008f;

        // Tuning groups: arm 5.5/0.65, leg 4.0/0.80, torso 3.0/0.90, pelvis 4.0/1.0.
        static readonly float[] Freq = BuildFreq();
        static readonly float[] Zeta = BuildZeta();

        readonly float[] _x = new float[N];
        readonly float[] _v = new float[N];
        bool _init;

        public void Reset() => _init = false;

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
                    StepSpring(i, tgt[i], dt, Freq[i], Zeta[i]);
            }
            return Rebuild(t);
        }

        void StepSpring(int i, float target, float dtS, float freqHz, float zeta)
        {
            float w = 2f * Mathf.PI * freqHz;
            float remaining = Mathf.Min(dtS, 0.1f);
            while (remaining > 0f)
            {
                float h = Mathf.Min(SubstepS, remaining);
                float a = -w * w * (_x[i] - target) - 2f * zeta * w * _v[i];
                _v[i] += a * h;
                _x[i] += _v[i] * h;
                remaining -= h;
            }
        }

        static float[] Flatten(BodyPose p)
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
        BodyPose Rebuild(BodyPose t)
        {
            return new BodyPose
            {
                PelvisX=_x[PvX], PelvisY=_x[PvY], PelvisZ=_x[PvZ],
                PelvisPitch=_x[PvP], PelvisYaw=_x[PvYw], PelvisRoll=_x[PvR],
                TorsoPitch=_x[ToP], TorsoYaw=_x[ToY], TorsoRoll=_x[ToR],
                TorsoTremor=t.TorsoTremor,
                HeadPitch=_x[HdP], HeadYaw=_x[HdY],
                Breath=t.Breath,
                ArmL = new ArmPose { ShoulderPitch=_x[LSp], ShoulderRoll=_x[LSr], ShoulderYaw=_x[LSy], ElbowBend=_x[LEb], Tremor=t.ArmL.Tremor, Grip=_x[LGr] },
                ArmR = new ArmPose { ShoulderPitch=_x[RSp], ShoulderRoll=_x[RSr], ShoulderYaw=_x[RSy], ElbowBend=_x[REb], Tremor=t.ArmR.Tremor, Grip=_x[RGr] },
                LegL = new LegPose { HipPitch=_x[LHp], HipYaw=_x[LHy], HipRoll=_x[LHr], KneeBend=_x[LKb], Ankle=_x[LAk] },
                LegR = new LegPose { HipPitch=_x[RHp], HipYaw=_x[RHy], HipRoll=_x[RHr], KneeBend=_x[RKb], Ankle=_x[RAk] },
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
}
