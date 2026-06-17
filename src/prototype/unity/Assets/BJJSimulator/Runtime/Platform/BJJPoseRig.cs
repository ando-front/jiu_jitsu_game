// PLATFORM — full-skeleton BlockMan rig driven by BJJPose.ComputeScenePoses.
//
// Replaces the old offset-only BJJAvatarBinder. Each LateUpdate it:
//   1. Assembles BottomPoseInputs / TopPoseInputs from BJJGameManager's
//      CurrentGameState + the provider's last Layer-B Intent / DefenseIntent.
//   2. Calls BJJPose.ComputeScenePoses → the same procedural pose synthesis
//      the Stage-1 web prototype (pose.ts) uses, ported 1:1 to BJJPose.cs.
//   3. Smooths every joint-angle channel with damped springs whose tuning
//      mirrors blockman.ts (arm 5.5/0.65, leg 4.0/0.80, torso 3.0/0.90,
//      pelvis 4.0/1.0), then adds the post-smoothing 13 Hz tremor jitter.
//   4. Runs forward kinematics with BJJPose.Matrix3 — the EXACT matrices the
//      validated FK / IK tests use — to get world-space joint positions, and
//      places sphere joints + cylinder bones there. Driving by FK *position*
//      (rather than re-applying Euler angles to Unity Transforms) sidesteps
//      the Three.js↔Unity handedness/rotation-order mismatch entirely: the
//      coordinates come straight out of BJJPose's own space.
//
// The skeleton is built procedurally in Awake, so no Inspector wiring is
// needed — the scene runs headless and BJJ → Setup Scene just adds the
// component.
//
// Bottom = guard-side player (attacker intent). Top = passer-side (defender).

using System.Collections.Generic;
using UnityEngine;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJPoseRig : MonoBehaviour
    {
        [Header("Colours")]
        [SerializeField] private Color bottomColor = new Color(0.35f, 0.55f, 1.00f);
        [SerializeField] private Color topColor    = new Color(0.79f, 0.71f, 0.54f);

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

            var bottomIn = BuildBottomInputs(g, intent, nowMs);
            var topIn    = BuildTopInputs(g, defense, nowMs);
            var scene    = BJJPose.ComputeScenePoses(bottomIn, topIn);

            DriveBody(_bottom, _bottomSprings, scene.Bottom, BJJPose.BottomPlacement, nowMs, dt);
            DriveBody(_top,    _topSprings,    scene.Top,    BJJPose.TopPlacement,    nowMs, dt);
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

        // Public entry for the Editor capture tool: settle the rig onto a
        // GameState in one shot (springs jump straight to target, no easing).
        public void ApplyImmediate(GameState g, Intent? intent, DefenseIntent? defense, float nowMs)
        {
            if (_bottom == null) BuildRig();
            var bottomIn = BuildBottomInputs(g, intent, nowMs);
            var topIn    = BuildTopInputs(g, defense, nowMs);
            var scene    = BJJPose.ComputeScenePoses(bottomIn, topIn);
            _bottomSprings.Reset();
            _topSprings.Reset();
            DriveBody(_bottom, _bottomSprings, scene.Bottom, BJJPose.BottomPlacement, nowMs, 0f);
            DriveBody(_top,    _topSprings,    scene.Top,    BJJPose.TopPlacement,    nowMs, 0f);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Per-body drive: spring-smooth angles → FK → place primitives
        // ─────────────────────────────────────────────────────────────────────

        private void DriveBody(Skeleton sk, SpringSet sp, BodyPose target,
                               RigPlacement place, float nowMs, float dt)
        {
            // 1. Smooth every angle channel toward the target pose.
            BodyPose p = sp.Step(target, dt);

            // 2. Tremor / strain jitter, added *after* smoothing (a low-freq
            //    spring would swallow the 13 Hz wobble). Mirrors blockman.ts.
            float treL = Jitter(nowMs, target.ArmL.Tremor, 0.0f);
            float treR = Jitter(nowMs, target.ArmR.Tremor, 2.4f);
            float strain = Jitter(nowMs, target.TorsoTremor, 1.7f);

            ArmPose armL = p.ArmL; armL.ShoulderPitch += treL; armL.ElbowBend += treL * 1.5f;
            ArmPose armR = p.ArmR; armR.ShoulderPitch += treR; armR.ElbowBend += treR * 1.5f;
            p.ArmL = armL; p.ArmR = armR;
            p.TorsoPitch += strain;
            p.TorsoRoll  += strain * 0.7f;

            // 3. Forward kinematics in BJJPose space (same Matrix3 the tests use).
            Matrix3 root = place.YawPi ? Matrix3.RotY(Mathf.PI) : Matrix3.Identity;
            Vector3 pelvisPos = place.Origin + new Vector3(p.PelvisX, p.PelvisY, p.PelvisZ);
            Matrix3 pelvisRot = Matrix3.Mul(root, Matrix3.EulerXYZ(p.PelvisPitch, p.PelvisYaw, p.PelvisRoll));
            Vector3 torsoPos = pelvisPos + pelvisRot.MulV(new Vector3(0, BJJPose.PelvisToTorso, 0));
            Matrix3 torsoRot = Matrix3.Mul(pelvisRot, Matrix3.EulerXYZ(p.TorsoPitch, p.TorsoYaw, p.TorsoRoll));

            // Breath raises the shoulder line and swells the chest.
            float breath01 = target.Breath * 0.5f + 0.5f;
            float shoulderY = BJJPose.ShoulderY + breath01 * 0.012f;

            Vector3 neckPos = torsoPos + torsoRot.MulV(new Vector3(0, BJJPose.HeadY, 0));
            Matrix3 headRot = Matrix3.Mul(torsoRot, Matrix3.EulerXYZ(p.HeadPitch, p.HeadYaw, 0f));
            Vector3 headPos = neckPos + headRot.MulV(new Vector3(0, BJJPose.HeadCenterY, 0));

            ArmFK(p.ArmL, -1f, torsoPos, torsoRot, shoulderY, out Vector3 shL, out Vector3 elL, out Vector3 haL);
            ArmFK(p.ArmR,  1f, torsoPos, torsoRot, shoulderY, out Vector3 shR, out Vector3 elR, out Vector3 haR);
            LegFK(p.LegL, -1f, pelvisPos, pelvisRot, out Vector3 hpL, out Vector3 knL, out Vector3 anL, out Vector3 toL);
            LegFK(p.LegR,  1f, pelvisPos, pelvisRot, out Vector3 hpR, out Vector3 knR, out Vector3 anR, out Vector3 toR);

            // 4. Place joints.
            sk.Pelvis.position = pelvisPos;
            sk.Chest.position  = torsoPos;
            sk.Head.position   = headPos;
            sk.ShoulderL.position = shL; sk.ShoulderR.position = shR;
            sk.ElbowL.position = elL;    sk.ElbowR.position = elR;
            sk.HandL.position  = haL;    sk.HandR.position  = haR;
            sk.HipL.position   = hpL;    sk.HipR.position   = hpR;
            sk.KneeL.position  = knL;    sk.KneeR.position  = knR;
            sk.AnkleL.position = anL;    sk.AnkleR.position = anR;

            // Grip → hand swell (open palm splayed, fist compact). blockman.ts.
            sk.HandL.localScale = HandScale(p.ArmL.Grip);
            sk.HandR.localScale = HandScale(p.ArmR.Grip);
            // Chest swells with the breath.
            sk.Chest.localScale = Vector3.one * (jointRadius * 2f) * (1f + breath01 * 0.10f);

            // 5. Stretch bones between joints.
            Bone(sk.SpineBone, pelvisPos, torsoPos);
            // Neck/spine column: span the full chest→head gap so the head reads
            // as attached (the head pivot sits HeadY above the chest).
            Bone(sk.NeckBone,  torsoPos,  headPos);
            Bone(sk.ClavLBone, torsoPos,  shL);  Bone(sk.ClavRBone, torsoPos,  shR);
            Bone(sk.UpArmLBone, shL, elL);        Bone(sk.UpArmRBone, shR, elR);
            Bone(sk.LoArmLBone, elL, haL);        Bone(sk.LoArmRBone, elR, haR);
            Bone(sk.PelvLBone, pelvisPos, hpL);   Bone(sk.PelvRBone, pelvisPos, hpR);
            Bone(sk.ThighLBone, hpL, knL);        Bone(sk.ThighRBone, hpR, knR);
            Bone(sk.ShinLBone,  knL, anL);        Bone(sk.ShinRBone,  knR, anR);
            Bone(sk.FootLBone,  anL, toL);        Bone(sk.FootRBone,  anR, toR);
        }

        private static void ArmFK(ArmPose arm, float side, Vector3 torsoPos, Matrix3 torsoRot,
                                  float shoulderY, out Vector3 shoulder, out Vector3 elbow, out Vector3 hand)
        {
            shoulder = torsoPos + torsoRot.MulV(new Vector3(BJJPose.ShoulderX * side, shoulderY, 0));
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
            // Short foot segment: ankle plantarflex (+) points the toes.
            Matrix3 footRot = Matrix3.Mul(shinRot, Matrix3.RotX(leg.Ankle));
            toe = ankle + footRot.MulV(new Vector3(0, 0, 0.12f));
        }

        private Vector3 HandScale(float grip)
        {
            float g = Mathf.Clamp01(grip);
            float d = jointRadius * 2f;
            return new Vector3(d * (1.55f - g * 0.75f), d * (0.62f + g * 0.33f), d * (1.42f - g * 0.62f));
        }

        private static float Jitter(float nowMs, float amp, float phase)
        {
            if (amp <= 0f) return 0f;
            return Mathf.Sin((nowMs / 1000f) * 2f * Mathf.PI * 13f + phase) * amp * 0.06f;
        }

        private void Bone(Transform bone, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            bone.position = (a + b) * 0.5f;
            if (len > 1e-5f) bone.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            // Default cylinder is 2 units tall along Y → scale.y = len / 2.
            bone.localScale = new Vector3(boneRadius * 2f, Mathf.Max(len * 0.5f, 1e-4f), boneRadius * 2f);
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
            var sk = new Skeleton();

            sk.Pelvis    = Joint(root, "Pelvis", mat);
            sk.Chest     = Joint(root, "Chest", mat);
            sk.Head      = Joint(root, "Head", mat, 1.4f);
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

        private class Skeleton
        {
            public Transform Pelvis, Chest, Head;
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
