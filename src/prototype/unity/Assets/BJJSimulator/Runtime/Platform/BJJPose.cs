// PURE — procedural pose synthesis, ported 1:1 from
// src/prototype/web/src/scene/pose.ts (Stage 1, fully unit-tested there).
//
// Maps the sim's FSM states (hand / foot / posture break / stamina) plus the
// live Layer-B intent onto joint-angle targets for an articulated rig, then
// (computeScenePoses) plants hands on the opponent with two-bone IK, tracks
// gaze, loads technique-specific judgment-window entries, and posts a hand on
// the mat for balance recovery.
//
// This file is engine-light: it uses UnityEngine.Vector3 / Mathf only for
// vector math, and never touches the scene graph. BJJAvatarBinder consumes
// BodyPose and drives the rig.
//
// Conventions (mirror blockman.ts):
//   - pitch rotX. Limbs hang along -y, so negative shoulder/hip pitch swings
//     them toward the chest front; torso/head extend along +y so positive
//     torso/head pitch curls them forward.
//   - roll rotZ, + = limb swings out (rig mirrors per side).
//   - yaw rotY, + = outward (rig mirrors).
//   - elbowBend / kneeBend >= 0, + = joint folds.
//   - pelvis offsets are world-axis metres from the rig's base spot.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BJJSimulator.Platform
{
    public struct ArmPose
    {
        public float ShoulderPitch;
        public float ShoulderRoll;
        public float ShoulderYaw;
        public float ElbowBend;
        public float Tremor; // 0..1 post-smoothing jitter amplitude
        public float Grip;   // 0 = open splayed hand, 1 = clenched fist
    }

    public struct LegPose
    {
        public float HipPitch;
        public float HipYaw;
        public float HipRoll;
        public float KneeBend;
        public float Ankle; // + plantarflex (toes point, hooking), - dorsiflex
    }

    public struct BodyPose
    {
        public float PelvisX, PelvisY, PelvisZ;
        public float PelvisPitch, PelvisYaw, PelvisRoll;
        public float TorsoPitch, TorsoYaw, TorsoRoll, TorsoTremor;
        public float HeadPitch, HeadYaw;
        public float Breath; // raw -1..1 oscillator; rig maps it to chest scale
        public ArmPose ArmL, ArmR;
        public LegPose LegL, LegR;
    }

    // state + the hand's current grip target + ms spent in the current state.
    public struct LimbSnapshot
    {
        public HandState State;
        public GripZone Target;
        public float SinceMs; // <0 / large means "long settled"

        public static LimbSnapshot Of(HandFSM fsm, long nowMs) => new LimbSnapshot
        {
            State = fsm.State,
            Target = fsm.Target,
            SinceMs = nowMs - fsm.StateEnteredMs,
        };
    }

    public struct BottomPoseInputs
    {
        public float NowMs;
        public float Stamina;       // 0..1
        public GuardState Guard;
        public LimbSnapshot LeftHand;
        public LimbSnapshot RightHand;
        public FootState LeftFoot;
        public FootState RightFoot;
        public float HipAngle;
        public float HipPush;
        public float HipLateral;
        public float GripStrengthL;
        public float GripStrengthR;
        public bool WindowOpen;
        public bool HasWindowTechnique;
        public Technique WindowTechnique;
    }

    public struct TopPoseInputs
    {
        public float NowMs;
        public float Stamina;
        public LimbSnapshot LeftHand;
        public LimbSnapshot RightHand;
        public float PostureBreakX;
        public float PostureBreakY;
        public float WeightForward;
        public float WeightLateral;
        public bool ArmExtractedL;
        public bool ArmExtractedR;
        public bool HasPass;
        public float PassElapsedMs;
        public bool HasCutL;
        public float CutElapsedLMs;
        public bool HasCutR;
        public float CutElapsedRMs;
        public bool CounterWindowOpen;
    }

    public struct BodyFrames
    {
        public Vector3 PelvisPos; public Matrix3 PelvisRot;
        public Vector3 TorsoPos;  public Matrix3 TorsoRot;
        public Vector3 HeadPos;
        public Vector3 ShoulderL, ShoulderR;
        public Vector3 HandL, HandR;
        public Vector3 BicepL, BicepR;
        public Vector3 KneeL, KneeR;
    }

    public struct RigPlacement
    {
        public Vector3 Origin;
        public bool YawPi;
    }

    public struct ScenePoses
    {
        public BodyPose Bottom;
        public BodyPose Top;
    }

    // Finish-tableau selector — Technique | CounterTechnique | PASS | SCRAMBLE,
    // mirroring the TS `FinishKind` union (pose.ts).
    public enum FinishKind
    {
        Triangle, Omoplata, ScissorSweep, FlowerSweep, HipBump, CrossCollar,
        ScissorCounter, TriangleEarlyStack, Pass, Scramble,
    }

    public struct FinishPoses
    {
        public BodyPose Bottom;
        public BodyPose Top;
    }

    // 3x3 row-major rotation matrix (transpose == inverse for pure rotations).
    public struct Matrix3
    {
        public float M0, M1, M2, M3, M4, M5, M6, M7, M8;

        public static readonly Matrix3 Identity = new Matrix3 { M0 = 1, M4 = 1, M8 = 1 };

        public static Matrix3 Mul(Matrix3 a, Matrix3 b)
        {
            return new Matrix3
            {
                M0 = a.M0 * b.M0 + a.M1 * b.M3 + a.M2 * b.M6,
                M1 = a.M0 * b.M1 + a.M1 * b.M4 + a.M2 * b.M7,
                M2 = a.M0 * b.M2 + a.M1 * b.M5 + a.M2 * b.M8,
                M3 = a.M3 * b.M0 + a.M4 * b.M3 + a.M5 * b.M6,
                M4 = a.M3 * b.M1 + a.M4 * b.M4 + a.M5 * b.M7,
                M5 = a.M3 * b.M2 + a.M4 * b.M5 + a.M5 * b.M8,
                M6 = a.M6 * b.M0 + a.M7 * b.M3 + a.M8 * b.M6,
                M7 = a.M6 * b.M1 + a.M7 * b.M4 + a.M8 * b.M7,
                M8 = a.M6 * b.M2 + a.M7 * b.M5 + a.M8 * b.M8,
            };
        }

        public Vector3 MulV(Vector3 v) => new Vector3(
            M0 * v.x + M1 * v.y + M2 * v.z,
            M3 * v.x + M4 * v.y + M5 * v.z,
            M6 * v.x + M7 * v.y + M8 * v.z);

        public Matrix3 Transpose() => new Matrix3
        {
            M0 = M0, M1 = M3, M2 = M6,
            M3 = M1, M4 = M4, M5 = M7,
            M6 = M2, M7 = M5, M8 = M8,
        };

        public static Matrix3 RotX(float t)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            return new Matrix3 { M0 = 1, M4 = c, M5 = -s, M7 = s, M8 = c };
        }

        public static Matrix3 RotY(float t)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            return new Matrix3 { M0 = c, M2 = s, M4 = 1, M6 = -s, M8 = c };
        }

        public static Matrix3 RotZ(float t)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            return new Matrix3 { M0 = c, M1 = -s, M3 = s, M4 = c, M8 = 1 };
        }

        // three.js "XYZ": R = Rx*Ry*Rz (pelvis / torso groups).
        public static Matrix3 EulerXYZ(float x, float y, float z) =>
            Mul(RotX(x), Mul(RotY(y), RotZ(z)));

        // Rig joint groups "YXZ": R = Ry*Rx*Rz (hips, shoulders).
        public static Matrix3 EulerYXZ(float x, float y, float z) =>
            Mul(RotY(y), Mul(RotX(x), RotZ(z)));
    }

    public static class BJJPose
    {
        const float TWO_PI = Mathf.PI * 2f;

        static float Clamp01(float v) => Mathf.Max(0f, Mathf.Min(1f, v));
        static float ClampUnit(float v) => Mathf.Max(-1f, Mathf.Min(1f, v));
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ---- Reach tables -----------------------------------------------------

        struct ReachTarget { public float Pitch, Roll, Yaw, Elbow; }

        static ReachTarget RT(float p, float r, float y, float e) =>
            new ReachTarget { Pitch = p, Roll = r, Yaw = y, Elbow = e };

        static ReachTarget AttackZone(GripZone z)
        {
            switch (z)
            {
                case GripZone.CollarL: return RT(-0.85f, 0.10f, -0.20f, 0.50f);
                case GripZone.CollarR: return RT(-0.85f, 0.10f, -0.20f, 0.50f);
                case GripZone.SleeveL: return RT(-0.65f, 0.12f, 0.10f, 0.55f);
                case GripZone.SleeveR: return RT(-0.65f, 0.12f, 0.10f, 0.55f);
                case GripZone.WristL: return RT(-0.60f, 0.35f, 0.15f, 0.40f);
                case GripZone.WristR: return RT(-0.60f, 0.35f, 0.15f, 0.40f);
                case GripZone.Belt: return RT(-0.40f, 0.05f, -0.25f, 0.60f);
                case GripZone.PostureBreak: return RT(-0.95f, 0.05f, -0.10f, 0.85f);
                default: return AttackRest;
            }
        }

        // Defender base zones are GripZone-free in C# state; the binder passes
        // a GripZone-like enum only for attacker grips. Defender posts use the
        // canned rest unless an explicit base anchor applies (see IK section).
        static readonly ReachTarget AttackRest = RT(-0.70f, 0.14f, 0.05f, 0.80f);
        static readonly ReachTarget DefenseRest = RT(-0.70f, 0.14f, 0f, 0.35f);
        static readonly ReachTarget ParriedArm = RT(-0.75f, 0.90f, 0.30f, 0.30f);
        static readonly ReachTarget ExtractedArm = RT(-1.30f, 0.05f, -0.60f, 0.50f);

        static ArmPose MkArm(float pitch, float roll, float yaw, float elbow,
                             float tremor = 0f, float grip = 0.25f) =>
            new ArmPose
            {
                ShoulderPitch = pitch, ShoulderRoll = roll, ShoulderYaw = yaw,
                ElbowBend = elbow, Tremor = tremor, Grip = grip,
            };

        static ArmPose LerpArm(ArmPose a, ArmPose b, float t) => new ArmPose
        {
            ShoulderPitch = Lerp(a.ShoulderPitch, b.ShoulderPitch, t),
            ShoulderRoll = Lerp(a.ShoulderRoll, b.ShoulderRoll, t),
            ShoulderYaw = Lerp(a.ShoulderYaw, b.ShoulderYaw, t),
            ElbowBend = Lerp(a.ElbowBend, b.ElbowBend, t),
            Tremor = Lerp(a.Tremor, b.Tremor, t),
            Grip = Lerp(a.Grip, b.Grip, t),
        };

        static LegPose LerpLeg(LegPose a, LegPose b, float t) => new LegPose
        {
            HipPitch = Lerp(a.HipPitch, b.HipPitch, t),
            HipYaw = Lerp(a.HipYaw, b.HipYaw, t),
            HipRoll = Lerp(a.HipRoll, b.HipRoll, t),
            KneeBend = Lerp(a.KneeBend, b.KneeBend, t),
            Ankle = Lerp(a.Ankle, b.Ankle, t),
        };

        static bool Busy(HandState s) =>
            s == HandState.Reaching || s == HandState.Contact || s == HandState.Gripped;

        static bool HandBusyState(LimbSnapshot h) => Busy(h.State);

        // Deterministic [0,1) hash — same action-start time → same "style".
        static float Hash01(float x)
        {
            float s = Mathf.Sin(x * 0.0173f + 1.0f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        // Two-octave breathing in [-1,1]; phase offsets the two bodies.
        static float BreathOscillator(float nowMs, float hz, float phase = 0f)
        {
            float t = nowMs / 1000f;
            return 0.82f * Mathf.Sin(t * TWO_PI * hz + phase) +
                   0.18f * Mathf.Sin(t * TWO_PI * hz * 0.5f + phase * 1.7f);
        }

        // Three-octave idle sway; phaseSet 0 = bottom, 1 = top.
        static float IdleSway(float nowMs, float fatigue, int phaseSet)
        {
            float t = nowMs / 1000f;
            float amp = 1f + fatigue * 1.2f;
            float p0 = phaseSet == 0 ? 1.3f : 3.1f;
            float p1 = phaseSet == 0 ? 0.0f : 0.7f;
            float p2 = phaseSet == 0 ? 5.5f : 2.3f;
            return (Mathf.Sin(t * TWO_PI * 0.31f + p0) * 0.5f +
                    Mathf.Sin(t * TWO_PI * 0.47f + p1) * 0.32f +
                    Mathf.Sin(t * TWO_PI * 0.17f + p2) * 0.18f) * amp;
        }

        static float IdleSway2(float nowMs, float fatigue, int phaseSet)
        {
            float t = nowMs / 1000f;
            float amp = 1f + fatigue * 1.2f;
            float baseP = phaseSet == 0 ? 4.0f : 1.9f;
            return (Mathf.Sin(t * TWO_PI * 0.23f + baseP) * 0.7f +
                    Mathf.Sin(t * TWO_PI * 0.13f + baseP * 0.6f) * 0.3f) * amp;
        }

        static ArmPose ArmPoseFrom(LimbSnapshot hand, bool attacker, float gripStrength, float nowMs)
        {
            ReachTarget reach = attacker
                ? (hand.Target != GripZone.None ? AttackZone(hand.Target) : AttackRest)
                : DefenseRest;
            ReachTarget rest = attacker ? AttackRest : DefenseRest;
            float sinceMs = hand.SinceMs >= 0f ? hand.SinceMs : 1000f;
            float startMs = nowMs - sinceMs;
            float styleA = Hash01(startMs) * 2f - 1f;
            float styleB = Hash01(startMs + 137f) * 2f - 1f;

            switch (hand.State)
            {
                case HandState.Reaching:
                    float windupMs = 110f + styleA * 25f;
                    if (sinceMs < windupMs)
                        return MkArm(rest.Pitch + 0.20f, rest.Roll + 0.10f, rest.Yaw,
                                     rest.Elbow + 0.18f, 0f, 0.1f);
                    return MkArm(reach.Pitch + styleB * 0.05f, reach.Roll + styleA * 0.08f,
                                 reach.Yaw + styleB * 0.10f, reach.Elbow * (0.45f + styleA * 0.12f), 0f, 0.0f);
                case HandState.Contact:
                    return MkArm(reach.Pitch, reach.Roll + styleA * 0.05f, reach.Yaw + styleB * 0.06f,
                                 reach.Elbow, 0.15f, 0.55f);
                case HandState.Gripped:
                    float pumpHz = 0.7f + (styleB * 0.5f + 0.5f) * 0.6f;
                    float pump = Mathf.Sin((nowMs / 1000f) * TWO_PI * pumpHz + styleA * Mathf.PI);
                    return MkArm(reach.Pitch + 0.10f, reach.Roll, reach.Yaw, reach.Elbow + 0.35f + pump * 0.07f,
                                 0.25f + Clamp01(gripStrength) * 0.45f,
                                 0.85f + Clamp01(gripStrength) * 0.15f);
                case HandState.Parried:
                    return MkArm(ParriedArm.Pitch, ParriedArm.Roll, ParriedArm.Yaw, ParriedArm.Elbow, 0f, 0.1f);
                default:
                    return MkArm(rest.Pitch, rest.Roll, rest.Yaw, rest.Elbow, 0f, 0.3f);
            }
        }

        static LegPose ShuffleLeg(LegPose leg, float squeeze)
        {
            if (squeeze == 0f) return leg;
            leg.KneeBend += squeeze;
            leg.HipRoll -= squeeze * 0.4f;
            return leg;
        }

        // ---- Leg solver -------------------------------------------------------

        static Vector3 V3Norm(Vector3 v)
        {
            float len = v.magnitude;
            return len < 1e-9f ? new Vector3(0, -1, 0) : v / len;
        }

        static Vector3 V3Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

        public static LegPose SolveLeg(Vector3 d1raw, Vector3 d2raw)
        {
            Vector3 d1 = V3Norm(d1raw);
            Vector3 d2 = V3Norm(d2raw);
            Vector3 crossed = Vector3.Cross(d1, d2);
            float crossLen = crossed.magnitude;
            Vector3 xAxis;
            if (crossLen < 1e-5f)
            {
                Vector3 fallback = Vector3.Cross(d1, new Vector3(0, 0, 1));
                float fbLen = fallback.magnitude;
                xAxis = fbLen < 1e-5f ? new Vector3(1, 0, 0) : V3Norm(fallback);
            }
            else
            {
                xAxis = crossed / crossLen;
            }
            Vector3 yAxis = new Vector3(-d1.x, -d1.y, -d1.z);
            Vector3 zAxis = Vector3.Cross(xAxis, yAxis);

            float m13 = zAxis.x, m23 = zAxis.y, m33 = zAxis.z;
            float m21 = xAxis.y, m22 = yAxis.y;
            float m11 = xAxis.x, m31 = xAxis.z;
            float hipPitch = Mathf.Asin(-ClampUnit(m23));
            float hipYaw, hipRoll;
            if (Mathf.Abs(m23) < 0.9999f)
            {
                hipYaw = Mathf.Atan2(m13, m33);
                hipRoll = Mathf.Atan2(m21, m22);
            }
            else
            {
                hipYaw = Mathf.Atan2(-m31, m11);
                hipRoll = 0f;
            }
            float kneeBend = Mathf.Acos(ClampUnit(Vector3.Dot(d1, d2)));
            return new LegPose { HipPitch = hipPitch, HipYaw = hipYaw, HipRoll = hipRoll, KneeBend = kneeBend, Ankle = 0f };
        }

        public static void LegDirections(LegPose pose, out Vector3 thigh, out Vector3 shin)
        {
            Matrix3 rot = Matrix3.EulerYXZ(pose.HipPitch, pose.HipYaw, pose.HipRoll);
            thigh = rot.MulV(new Vector3(0, -1, 0));
            float ck = Mathf.Cos(pose.KneeBend), sk = Mathf.Sin(pose.KneeBend);
            shin = rot.MulV(new Vector3(0, -ck, -sk));
        }

        static readonly Vector3 LegDirLockedThigh = new Vector3(0.31f, -0.76f, 0.56f);
        static readonly Vector3 LegDirLockedShin = new Vector3(-0.78f, -0.62f, -0.10f);
        static readonly Vector3 LegDirUnlockedThigh = new Vector3(0.52f, -0.69f, 0.49f);
        static readonly Vector3 LegDirUnlockedShin = new Vector3(-0.75f, -0.66f, 0.0f);
        static readonly Vector3 LegDirOpenThigh = new Vector3(0.62f, -0.65f, 0.35f);
        static readonly Vector3 LegDirOpenShin = new Vector3(-0.45f, -0.85f, -0.2f);

        static LegPose WithAnkle(LegPose leg, float ankle) { leg.Ankle = ankle; return leg; }

        static LegPose BottomLegPose(FootState foot, GuardState guard, float nowMs)
        {
            Vector3 ulThigh = guard == GuardState.Open ? LegDirOpenThigh : LegDirUnlockedThigh;
            Vector3 ulShin = guard == GuardState.Open ? LegDirOpenShin : LegDirUnlockedShin;
            switch (foot)
            {
                case FootState.Locked:
                    return WithAnkle(SolveLeg(LegDirLockedThigh, LegDirLockedShin), 0.55f);
                case FootState.Locking:
                    float mix = 0.6f + Mathf.Sin((nowMs / 1000f) * TWO_PI * 3.2f) * 0.25f;
                    return WithAnkle(
                        SolveLeg(V3Lerp(ulThigh, LegDirLockedThigh, mix), V3Lerp(ulShin, LegDirLockedShin, mix)),
                        0.2f + mix * 0.3f);
                default:
                    return WithAnkle(SolveLeg(ulThigh, ulShin), -0.35f);
            }
        }

        static bool IsActiveHigh(LimbSnapshot hand)
        {
            bool high = hand.Target == GripZone.CollarL || hand.Target == GripZone.CollarR ||
                        hand.Target == GripZone.PostureBreak;
            return high && Busy(hand.State);
        }

        // ---- Window entry -----------------------------------------------------

        struct WindowEntry
        {
            public bool HasPelvisY; public float PelvisY;
            public bool HasPelvisRoll; public float PelvisRoll;
            public bool HasPelvisYaw; public float PelvisYaw;
            public bool HasTorsoPitch; public float TorsoPitch;
            public bool HasTorsoRoll; public float TorsoRoll;
            public bool HasLegL; public LegPose LegL;
            public bool HasLegR; public LegPose LegR;
            public bool HasArmL; public ArmPose ArmL;
            public bool HasArmR; public ArmPose ArmR;
        }

        static WindowEntry WindowEntryFor(Technique tech)
        {
            var e = new WindowEntry();
            switch (tech)
            {
                case Technique.Triangle:
                    e.HasPelvisY = true; e.PelvisY = 0.06f;
                    e.HasTorsoPitch = true; e.TorsoPitch = 0.18f;
                    e.HasLegL = true; e.LegL = WithAnkle(SolveLeg(new Vector3(0.22f, -0.45f, 0.86f), new Vector3(-0.78f, -0.30f, 0.08f)), 0.4f);
                    e.HasArmL = true; e.ArmL = MkArm(-0.65f, 0.12f, -0.14f, 0.85f, 0.3f, 0.6f);
                    e.HasArmR = true; e.ArmR = MkArm(-0.65f, 0.12f, -0.14f, 0.85f, 0.3f, 0.6f);
                    break;
                case Technique.Omoplata:
                    e.HasPelvisYaw = true; e.PelvisYaw = 0.45f;
                    e.HasTorsoPitch = true; e.TorsoPitch = 0.10f;
                    e.HasLegL = true; e.LegL = WithAnkle(SolveLeg(new Vector3(-0.20f, -0.55f, 0.78f), new Vector3(-0.55f, -0.62f, -0.30f)), 0.3f);
                    break;
                case Technique.HipBump:
                    e.HasPelvisY = true; e.PelvisY = 0.08f;
                    e.HasTorsoPitch = true; e.TorsoPitch = 0.55f;
                    e.HasArmR = true; e.ArmR = MkArm(0.45f, 0.30f, 0f, 0.20f, 0f, 0.2f);
                    e.HasArmL = true; e.ArmL = MkArm(-0.90f, 0.25f, -0.10f, 0.45f, 0.2f, 0.7f);
                    break;
                case Technique.CrossCollar:
                    e.HasTorsoPitch = true; e.TorsoPitch = 0.22f;
                    e.HasArmL = true; e.ArmL = MkArm(-0.80f, 0.06f, -0.45f, 0.70f, 0.3f, 0.85f);
                    e.HasArmR = true; e.ArmR = MkArm(-0.80f, 0.06f, -0.45f, 0.70f, 0.3f, 0.85f);
                    break;
                case Technique.ScissorSweep:
                    e.HasPelvisRoll = true; e.PelvisRoll = 0.18f;
                    e.HasTorsoRoll = true; e.TorsoRoll = 0.14f;
                    e.HasLegR = true; e.LegR = WithAnkle(SolveLeg(new Vector3(0.55f, -0.60f, 0.45f), new Vector3(-0.30f, -0.80f, -0.20f)), -0.1f);
                    break;
                case Technique.FlowerSweep:
                    e.HasPelvisRoll = true; e.PelvisRoll = -0.18f;
                    e.HasTorsoRoll = true; e.TorsoRoll = -0.14f;
                    e.HasArmR = true; e.ArmR = MkArm(-0.40f, 0.30f, -0.45f, 0.50f, 0.2f, 0.4f);
                    break;
            }
            return e;
        }

        // ---- Bottom / Top pose ------------------------------------------------

        public static BodyPose ComputeBottomPose(BottomPoseInputs input)
        {
            float fatigue = Clamp01(1f - input.Stamina);
            float breathHz = 0.28f + fatigue * 0.55f;
            float breath = BreathOscillator(input.NowMs, breathHz);
            float sway = IdleSway(input.NowMs, fatigue, 0);
            float sway2 = IdleSway2(input.NowMs, fatigue, 0);

            float sitUp = (IsActiveHigh(input.LeftHand) || IsActiveHigh(input.RightHand)) ? 0.22f : 0f;
            bool legsLocked = input.LeftFoot == FootState.Locked && input.RightFoot == FootState.Locked;
            float coil = input.WindowOpen ? 1f : 0f;

            float reachTwist = ReachTwist(input.LeftHand, -1f) + ReachTwist(input.RightHand, 1f);

            bool hasEntry = input.WindowOpen && input.HasWindowTechnique;
            WindowEntry entry = hasEntry ? WindowEntryFor(input.WindowTechnique) : new WindowEntry();

            // Guard-retention shuffle: a locked idle guard never sits still.
            bool idleGuard = !input.WindowOpen &&
                             !HandBusyState(input.LeftHand) && !HandBusyState(input.RightHand) && legsLocked;
            float shuffleT = (input.NowMs / 1000f) * TWO_PI;
            float shuffle = idleGuard ? 1f : 0f;
            float shuffleYaw = shuffle * Mathf.Sin(shuffleT * 0.6f + 0.5f) * 0.05f;
            float shuffleRoll = shuffle * Mathf.Sin(shuffleT * 0.45f) * 0.04f;
            float shuffleSqueeze = shuffle * (0.5f + 0.5f * Mathf.Sin(shuffleT * 0.8f)) * 0.12f;
            float headScan = idleGuard ? Mathf.Sin(shuffleT * 0.35f + 2.0f) * 0.22f : 0f;

            var pose = new BodyPose
            {
                PelvisX = input.HipLateral * 0.20f + sway * 0.012f,
                PelvisY = 0.26f + (legsLocked ? 0.10f : 0f) + Mathf.Abs(input.HipPush) * 0.03f + coil * 0.04f +
                          (entry.HasPelvisY ? entry.PelvisY : 0f),
                PelvisZ = input.HipPush * 0.42f + sway2 * 0.008f,
                PelvisPitch = -Mathf.PI / 2f + 0.15f,
                PelvisYaw = input.HipAngle + (entry.HasPelvisYaw ? entry.PelvisYaw : 0f) + shuffleYaw,
                PelvisRoll = input.HipLateral * 0.45f + (entry.HasPelvisRoll ? entry.PelvisRoll : 0f) + shuffleRoll,
                TorsoPitch = 0.20f + sitUp * 0.7f + coil * 0.10f + breath * 0.04f - fatigue * 0.15f +
                             (entry.HasTorsoPitch ? entry.TorsoPitch : 0f),
                TorsoYaw = input.HipAngle * 0.35f + sway2 * 0.02f + reachTwist,
                TorsoRoll = input.HipLateral * 0.28f + sway * 0.015f + (entry.HasTorsoRoll ? entry.TorsoRoll : 0f),
                TorsoTremor = 0f,
                HeadPitch = 0.45f + sitUp * 0.4f - fatigue * 0.35f + breath * 0.02f,
                HeadYaw = input.HipAngle * 0.3f + headScan,
                Breath = breath,
            };

            pose.ArmL = Busy(input.LeftHand.State)
                ? ArmPoseFrom(input.LeftHand, true, input.GripStrengthL, input.NowMs)
                : (entry.HasArmL ? entry.ArmL : ArmPoseFrom(input.LeftHand, true, input.GripStrengthL, input.NowMs));
            pose.ArmR = Busy(input.RightHand.State)
                ? ArmPoseFrom(input.RightHand, true, input.GripStrengthR, input.NowMs)
                : (entry.HasArmR ? entry.ArmR : ArmPoseFrom(input.RightHand, true, input.GripStrengthR, input.NowMs));
            pose.LegL = ShuffleLeg(entry.HasLegL ? entry.LegL : BottomLegPose(input.LeftFoot, input.Guard, input.NowMs), shuffleSqueeze);
            pose.LegR = ShuffleLeg(entry.HasLegR ? entry.LegR : BottomLegPose(input.RightFoot, input.Guard, input.NowMs), shuffleSqueeze);
            return pose;
        }

        static float ReachTwist(LimbSnapshot hand, float side)
        {
            if (hand.State != HandState.Reaching) return 0f;
            float sinceMs = hand.SinceMs >= 0f ? hand.SinceMs : 1000f;
            return sinceMs < 320f ? side * 0.09f : side * 0.03f;
        }

        // Cut chop keyframes (defender).
        static readonly ArmPose CutWindup = MkArm(-1.25f, 0.55f, 0.2f, 0.5f, 0.1f);
        static readonly ArmPose CutStrike = MkArm(-0.5f, 0.05f, -0.45f, 0.2f, 0f);

        static ArmPose CutChopArm(ArmPose b, float elapsedMs)
        {
            if (elapsedMs < 280f) return LerpArm(b, CutWindup, elapsedMs / 280f);
            if (elapsedMs < 620f) return LerpArm(CutWindup, CutStrike, (elapsedMs - 280f) / 340f);
            return LerpArm(CutStrike, b, Clamp01((elapsedMs - 620f) / 880f));
        }

        // Balance recovery: post a free hand on the mat when broken down hard.
        public static bool BalancePost(TopPoseInputs input, out char side, out float t)
        {
            side = 'R'; t = 0f;
            float pbMag = Mathf.Sqrt(input.PostureBreakX * input.PostureBreakX + input.PostureBreakY * input.PostureBreakY);
            if (pbMag < 0.5f) return false;
            side = input.PostureBreakX >= 0f ? 'R' : 'L';
            bool busyL = input.ArmExtractedL || input.HasCutL ||
                         input.LeftHand.State == HandState.Contact || input.LeftHand.State == HandState.Gripped;
            bool busyR = input.ArmExtractedR || input.HasCutR ||
                         input.RightHand.State == HandState.Contact || input.RightHand.State == HandState.Gripped;
            if (side == 'L' && busyL) return false;
            if (side == 'R' && busyR) return false;
            t = Clamp01((pbMag - 0.5f) / 0.4f);
            return true;
        }

        static readonly ArmPose PostArm = MkArm(0.15f, 0.65f, 0.1f, 0.18f, 0.2f, 0.0f);

        public static BodyPose ComputeTopPose(TopPoseInputs input)
        {
            float fatigue = Clamp01(1f - input.Stamina);
            float breathHz = 0.28f + fatigue * 0.55f;
            float breath = BreathOscillator(input.NowMs, breathHz, Mathf.PI * 0.6f);
            float sway = IdleSway(input.NowMs, fatigue, 1);
            float sway2 = IdleSway2(input.NowMs, fatigue, 1);

            float pbX = input.PostureBreakX, pbY = input.PostureBreakY;
            float pbMag = Clamp01(Mathf.Sqrt(pbX * pbX + pbY * pbY));
            float strain = pbMag > 0.45f ? (pbMag - 0.45f) * 0.9f : 0f;

            // Pressure weave: a passer hunting the angle, before the drive.
            bool searching = pbMag < 0.4f && !input.HasPass;
            float weaveT = (input.NowMs / 1000f) * TWO_PI;
            float weave = searching ? 1f : 0f;
            float weaveX = weave * Mathf.Sin(weaveT * 0.5f + 0.4f) * 0.06f;
            float weaveProbe = weave * (0.5f + 0.5f * Mathf.Sin(weaveT * 0.7f)) * 0.05f;
            float weaveRoll = weave * Mathf.Sin(weaveT * 0.5f + 0.4f) * 0.08f;

            ArmPose armL = input.ArmExtractedL
                ? MkArm(ExtractedArm.Pitch, ExtractedArm.Roll, ExtractedArm.Yaw, ExtractedArm.Elbow, 0.25f)
                : ArmPoseFrom(input.LeftHand, false, 0.5f, input.NowMs);
            ArmPose armR = input.ArmExtractedR
                ? MkArm(ExtractedArm.Pitch, ExtractedArm.Roll, ExtractedArm.Yaw, ExtractedArm.Elbow, 0.25f)
                : ArmPoseFrom(input.RightHand, false, 0.5f, input.NowMs);
            if (input.HasCutL) armL = CutChopArm(armL, input.CutElapsedLMs);
            if (input.HasCutR) armR = CutChopArm(armR, input.CutElapsedRMs);

            if (BalancePost(input, out char postSide, out float postT))
            {
                if (postSide == 'L') armL = LerpArm(armL, PostArm, postT);
                else armR = LerpArm(armR, PostArm, postT);
            }

            LegPose kneel = new LegPose { HipPitch = -0.55f + input.WeightForward * 0.15f, HipYaw = 0f, HipRoll = 0.30f, KneeBend = 1.65f, Ankle = 0f };
            float passT = !input.HasPass ? 0f : Clamp01(input.PassElapsedMs / 400f);
            float passSurge = !input.HasPass ? 0f : Mathf.Sin((input.PassElapsedMs / 1000f) * TWO_PI * 1.5f) * 0.04f;
            bool driveRight = input.WeightLateral >= 0f;
            LegPose leadLeg = LerpLeg(kneel, new LegPose { HipPitch = -1.15f, HipYaw = 0f, HipRoll = 0.25f, KneeBend = 1.45f }, passT);
            LegPose trailLeg = LerpLeg(kneel, new LegPose { HipPitch = -0.20f, HipYaw = 0f, HipRoll = 0.30f, KneeBend = 0.70f }, passT);

            float brace = input.CounterWindowOpen ? 1f : 0f;

            float postLoadL = (input.LeftHand.State == HandState.Contact || input.LeftHand.State == HandState.Gripped) ? -1f * 0.028f : 0f;
            float postLoadR = (input.RightHand.State == HandState.Contact || input.RightHand.State == HandState.Gripped) ? 1f * 0.028f : 0f;
            float weightShiftX = postLoadL + postLoadR +
                                 (input.ArmExtractedL ? -0.045f : 0f) + (input.ArmExtractedR ? 0.045f : 0f);
            float extractDip = (input.ArmExtractedL ? -0.07f : 0f) + (input.ArmExtractedR ? 0.07f : 0f);

            return new BodyPose
            {
                PelvisX = pbX * 0.25f + input.WeightLateral * 0.22f + (driveRight ? 1f : -1f) * passT * 0.10f + sway * 0.014f + weightShiftX + weaveX,
                PelvisY = 0.50f - fatigue * 0.04f - pbMag * 0.06f - passT * 0.06f + brace * 0.02f,
                PelvisZ = pbY * 0.30f + input.WeightForward * 0.22f + passT * (0.18f + passSurge) + sway2 * 0.010f + weaveProbe,
                PelvisPitch = 0f,
                PelvisYaw = 0f,
                PelvisRoll = input.WeightLateral * 0.10f + sway * 0.018f + extractDip + weaveRoll,
                TorsoPitch = 0.10f + pbY * 0.55f + input.WeightForward * 0.10f + fatigue * 0.12f + breath * 0.03f + passT * 0.30f - brace * 0.08f,
                TorsoYaw = pbX * 0.20f + sway2 * 0.025f,
                TorsoRoll = pbX * 0.45f + sway * 0.02f,
                TorsoTremor = strain + passT * 0.15f,
                HeadPitch = 0.45f + fatigue * 0.25f + Mathf.Max(0f, pbY) * 0.20f + breath * 0.02f,
                HeadYaw = -pbX * 0.25f,
                Breath = breath,
                ArmL = armL,
                ArmR = armR,
                LegL = driveRight ? trailLeg : leadLeg,
                LegR = driveRight ? leadLeg : trailLeg,
            };
        }

        // ---- Rig dimensions & FK ---------------------------------------------

        public const float PelvisToTorso = 0.10f;
        public const float ShoulderX = 0.24f;
        public const float ShoulderY = 0.44f;
        public const float HeadY = 0.56f;
        public const float HeadCenterY = 0.12f;
        public const float UpperArm = 0.28f;
        public const float ForeArm = 0.27f;
        public const float HipX = 0.11f;
        public const float HipY = -0.05f;
        public const float Thigh = 0.38f;
        public const float Shin = 0.36f;

        public static readonly RigPlacement BottomPlacement = new RigPlacement { Origin = Vector3.zero, YawPi = true };
        public static readonly RigPlacement TopPlacement = new RigPlacement { Origin = new Vector3(0, 0, -0.5f), YawPi = false };

        public static BodyFrames ComputeBodyFrames(BodyPose pose, RigPlacement place)
        {
            Matrix3 root = place.YawPi ? Matrix3.RotY(Mathf.PI) : Matrix3.Identity;
            Vector3 pelvisPos = place.Origin + new Vector3(pose.PelvisX, pose.PelvisY, pose.PelvisZ);
            Matrix3 pelvisRot = Matrix3.Mul(root, Matrix3.EulerXYZ(pose.PelvisPitch, pose.PelvisYaw, pose.PelvisRoll));
            Vector3 torsoPos = pelvisPos + pelvisRot.MulV(new Vector3(0, PelvisToTorso, 0));
            Matrix3 torsoRot = Matrix3.Mul(pelvisRot, Matrix3.EulerXYZ(pose.TorsoPitch, pose.TorsoYaw, pose.TorsoRoll));
            Vector3 headPos = torsoPos + torsoRot.MulV(new Vector3(0, HeadY + HeadCenterY, 0));

            ArmPoint(pose.ArmL, -1f, torsoPos, torsoRot, out Vector3 shL, out Vector3 biL, out Vector3 haL);
            ArmPoint(pose.ArmR, 1f, torsoPos, torsoRot, out Vector3 shR, out Vector3 biR, out Vector3 haR);

            return new BodyFrames
            {
                PelvisPos = pelvisPos, PelvisRot = pelvisRot,
                TorsoPos = torsoPos, TorsoRot = torsoRot,
                HeadPos = headPos,
                ShoulderL = shL, ShoulderR = shR,
                HandL = haL, HandR = haR,
                BicepL = biL, BicepR = biR,
                KneeL = KneePoint(pose.LegL, -1f, pelvisPos, pelvisRot),
                KneeR = KneePoint(pose.LegR, 1f, pelvisPos, pelvisRot),
            };
        }

        static void ArmPoint(ArmPose arm, float sideSign, Vector3 torsoPos, Matrix3 torsoRot,
                             out Vector3 shoulder, out Vector3 bicep, out Vector3 hand)
        {
            shoulder = torsoPos + torsoRot.MulV(new Vector3(ShoulderX * sideSign, ShoulderY, 0));
            Matrix3 armRot = Matrix3.Mul(torsoRot, Matrix3.EulerYXZ(arm.ShoulderPitch, arm.ShoulderYaw * sideSign, arm.ShoulderRoll * sideSign));
            bicep = shoulder + armRot.MulV(new Vector3(0, -UpperArm / 2f, 0));
            Vector3 elbowPos = shoulder + armRot.MulV(new Vector3(0, -UpperArm, 0));
            Matrix3 foreRot = Matrix3.Mul(armRot, Matrix3.RotX(-arm.ElbowBend));
            hand = elbowPos + foreRot.MulV(new Vector3(0, -ForeArm, 0));
        }

        static Vector3 KneePoint(LegPose leg, float sideSign, Vector3 pelvisPos, Matrix3 pelvisRot)
        {
            Vector3 hip = pelvisPos + pelvisRot.MulV(new Vector3(HipX * sideSign, HipY, 0));
            Matrix3 legRot = Matrix3.Mul(pelvisRot, Matrix3.EulerYXZ(leg.HipPitch, leg.HipYaw * sideSign, leg.HipRoll * sideSign));
            return hip + legRot.MulV(new Vector3(0, -Thigh, 0));
        }

        // ---- Zone anchors -----------------------------------------------------

        public static bool GripZoneAnchor(GripZone zone, BodyFrames opp, out Vector3 anchor)
        {
            switch (zone)
            {
                case GripZone.CollarL: anchor = opp.TorsoPos + opp.TorsoRot.MulV(new Vector3(-0.10f, 0.50f, 0.08f)); return true;
                case GripZone.CollarR: anchor = opp.TorsoPos + opp.TorsoRot.MulV(new Vector3(0.10f, 0.50f, 0.08f)); return true;
                case GripZone.SleeveL: case GripZone.WristL: anchor = opp.HandL; return true;
                case GripZone.SleeveR: case GripZone.WristR: anchor = opp.HandR; return true;
                case GripZone.Belt: anchor = opp.PelvisPos + opp.PelvisRot.MulV(new Vector3(0, 0.02f, 0.12f)); return true;
                case GripZone.PostureBreak: anchor = opp.TorsoPos + opp.TorsoRot.MulV(new Vector3(0, 0.40f, 0.10f)); return true;
                default: anchor = Vector3.zero; return false;
            }
        }

        // World-space anchor for a defender base zone on the attacker's body.
        // Ported 1:1 from pose.ts baseZoneAnchor.
        public static bool BaseZoneAnchor(BaseZone zone, BodyFrames opp, out Vector3 anchor)
        {
            switch (zone)
            {
                case BaseZone.Chest: anchor = opp.TorsoPos + opp.TorsoRot.MulV(new Vector3(0, 0.30f, 0.14f)); return true;
                case BaseZone.Hip: anchor = opp.PelvisPos + opp.PelvisRot.MulV(new Vector3(0, 0, 0.12f)); return true;
                case BaseZone.KneeL: anchor = opp.KneeL; return true;
                case BaseZone.KneeR: anchor = opp.KneeR; return true;
                case BaseZone.BicepL: anchor = opp.BicepL; return true;
                case BaseZone.BicepR: anchor = opp.BicepR; return true;
                default: anchor = Vector3.zero; return false;
            }
        }

        // ---- Realism helpers (Tier 7) ----------------------------------------
        // New pure utilities for ground contact, balance and parametric gaze.
        // No Stage-1 pose.ts counterpart yet; kept pure + EditMode-tested and
        // consumed by BJJPoseRig for the visual layer.

        // How far to lift a body so its lowest contact point rests on groundY.
        // Returns 0 when nothing penetrates the mat.
        public static float GroundRestOffsetY(float groundY, params float[] contactY)
        {
            float min = float.PositiveInfinity;
            for (int i = 0; i < contactY.Length; i++)
                if (contactY[i] < min) min = contactY[i];
            return (min < groundY) ? (groundY - min) : 0f;
        }

        // Is the centre-of-mass XZ projection inside the support polygon (the
        // convex hull of the ground-contact points)? Fewer than 3 contacts is
        // treated as an unstable (line/point) base → false.
        public static bool ComInsideSupport(Vector2 com, Vector2[] contactsXZ)
        {
            if (contactsXZ == null || contactsXZ.Length < 3) return false;
            List<Vector2> hull = ConvexHull(contactsXZ);
            if (hull.Count < 3) return false;
            bool pos = false, neg = false;
            int n = hull.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % n];
                float cross = (b.x - a.x) * (com.y - a.y) - (b.y - a.y) * (com.x - a.x);
                if (cross > 1e-6f) pos = true;
                else if (cross < -1e-6f) neg = true;
                if (pos && neg) return false;
            }
            return true;
        }

        // Andrew's monotone chain convex hull (CCW), for ComInsideSupport.
        static List<Vector2> ConvexHull(Vector2[] ptsIn)
        {
            var pts = new List<Vector2>(ptsIn);
            pts.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
            int n = pts.Count;
            var hull = new List<Vector2>(2 * n);
            // lower
            for (int i = 0; i < n; i++)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pts[i]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(pts[i]);
            }
            // upper
            int lower = hull.Count + 1;
            for (int i = n - 2; i >= 0; i--)
            {
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pts[i]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(pts[i]);
            }
            hull.RemoveAt(hull.Count - 1); // last == first
            return hull;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
            (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        // Parametric gaze: aim the face (+z) at a world target, expressed in the
        // torso frame. Generalises LookAt — `blend` weights the look-at vs the
        // pose's base angle, with separate yaw clamp / pitch limits. Used for
        // mutual head look-at (top → opponent head, bottom → opponent hip).
        public static void GazeTo(Vector3 headOrigin, Matrix3 torsoRot, Vector3 targetWorld,
                                  float basePitch, float baseYaw, float blend,
                                  float yawClampRad, float pitchLo, float pitchHi,
                                  out float pitch, out float yaw)
        {
            Vector3 tLocal = V3Norm(torsoRot.Transpose().MulV(targetWorld - headOrigin));
            float y = Mathf.Atan2(tLocal.x, Mathf.Max(0.15f, tLocal.z));
            float p = -Mathf.Atan2(tLocal.y, Mathf.Sqrt(tLocal.x * tLocal.x + tLocal.z * tLocal.z));
            pitch = Mathf.Clamp(basePitch * (1f - blend) + p * blend, pitchLo, pitchHi);
            yaw = Mathf.Clamp(baseYaw * (1f - blend) + y * blend, -yawClampRad, yawClampRad);
        }

        // ---- Realism helpers (Tier 8: secondary motion, spine, fingers) -------
        // Pure, EditMode-tested cores for the four new visual layers. The rig
        // (BJJPoseRig) owns the per-channel state; these supply the maths.

        // Sub-stepped semi-implicit Euler damped-spring integrator. Identical
        // numerics to SpringSet/blockman.ts stepSpring, factored out so the
        // primary smoother AND the secondary-motion oscillator share one code
        // path. 8 ms substeps keep stiff springs stable across frame hitches.
        public const float SpringSubstepS = 0.008f;

        public static void IntegrateSpring(ref float x, ref float v, float target,
                                           float freqHz, float zeta, float dtS)
        {
            float w = 2f * Mathf.PI * freqHz;
            float remaining = Mathf.Min(dtS, 0.1f);
            while (remaining > 0f)
            {
                float h = Mathf.Min(SpringSubstepS, remaining);
                float a = -w * w * (x - target) - 2f * zeta * w * v;
                v += a * h;
                x += v * h;
                remaining -= h;
            }
        }

        // Spine S-curve: split one torso pitch into lower / mid / upper segment
        // pitches that sum to the original (so the chest still lands where the
        // single-bone FK put it), then add a zero-sum S perturbation scaled by
        // `round`. round > 0 rounds the UPPER back forward while the lumbar
        // arches (guard-bottom turtle); round < 0 extends a tall, stacked
        // passer spine. Sum is preserved for any round.
        public static void SpineSCurve(float torsoPitch, float round,
                                       out float lower, out float mid, out float upper)
        {
            float baseSeg = torsoPitch / 3f;
            const float k = 0.5f; // S amplitude per unit round
            lower = baseSeg - round * k; // lumbar arches the opposite way…
            mid   = baseSeg;             // …pivoting through a neutral mid…
            upper = baseSeg + round * k; // …so the thoracic rounds forward.
        }

        // Finger curl angle (radians) for one knuckle of a clenching hand.
        // joint 0 = MCP (base), 1 = PIP (middle), 2 = DIP (tip). grip 0 = a
        // flat splayed hand, grip 1 = a closed fist. Each joint folds
        // progressively as the grip squeezes; the base knuckle leads.
        static readonly float[] FingerFistCurl = { 1.50f, 1.75f, 1.20f };

        public static float FingerCurl(float grip, int joint)
        {
            int j = joint < 0 ? 0 : (joint > 2 ? 2 : joint);
            return Mathf.Clamp01(grip) * FingerFistCurl[j];
        }

        // Anticipation: a brief reverse offset injected when a big technique is
        // selected — weight loads the *opposite* way before the spring whips
        // forward. Returns 0 outside [0, windowMs]; a smooth negative half-sine
        // peaking at -magnitude at the midpoint inside it.
        public static float AnticipationOffset(float elapsedMs, float windowMs, float magnitude)
        {
            if (windowMs <= 0f || elapsedMs <= 0f || elapsedMs >= windowMs) return 0f;
            return -magnitude * Mathf.Sin(Mathf.PI * (elapsedMs / windowMs));
        }

        // Impact ripple gate: did body-wave segment `segIndex` (which fires at
        // segIndex * stepMs after the impact) get crossed between the previous
        // and current elapsed time? Used to kick a travelling wave through
        // pelvis → torso → shoulder → hand at `stepMs` intervals, exactly once
        // each. Elapsed time is measured from the impact, so an inactive ripple
        // keeps elapsed negative (every segment trigger >= 0 stays un-crossed).
        public static bool RippleFired(float prevElapsedMs, float elapsedMs, int segIndex, float stepMs)
        {
            if (stepMs <= 0f) return false;
            float trigger = segIndex * stepMs;
            return prevElapsedMs < trigger && elapsedMs >= trigger;
        }

        // ---- Two-bone arm IK --------------------------------------------------

        public static ArmPose SolveArmIK(Vector3 targetWorld, Vector3 shoulderWorld, Matrix3 torsoRot,
                                         float sideSign, float baseTremor, float grip = 0.25f)
        {
            float a = UpperArm, b = ForeArm;
            Vector3 tRaw = torsoRot.Transpose().MulV(targetWorld - shoulderWorld);
            float dRaw = tRaw.magnitude;
            float d = Mathf.Max(0.10f, Mathf.Min(a + b - 0.01f, dRaw));
            Vector3 dir = dRaw < 1e-6f ? new Vector3(0, -1, 0) : tRaw / dRaw;

            float elbowBend = Mathf.PI - Mathf.Acos(ClampUnit((a * a + b * b - d * d) / (2f * a * b)));
            float alpha = Mathf.Acos(ClampUnit((a * a + d * d - b * b) / (2f * a * d)));

            Vector3 pole = new Vector3(0.45f * sideSign, -0.8f, -0.35f);
            Vector3 axis = Vector3.Cross(dir, pole);
            if (axis.magnitude < 1e-5f) axis = Vector3.Cross(dir, new Vector3(1, 0, 0));
            axis = V3Norm(axis);
            Vector3 u = dir * Mathf.Cos(alpha) + Vector3.Cross(axis, dir) * Mathf.Sin(alpha);
            Vector3 f = V3Norm(dir * d - u * a);

            Vector3 xAxis = Vector3.Cross(f, u);
            if (xAxis.magnitude < 1e-5f) xAxis = Vector3.Cross(u, new Vector3(0, 0, 1));
            xAxis = V3Norm(xAxis);
            Vector3 yAxis = new Vector3(-u.x, -u.y, -u.z);
            Vector3 zAxis = Vector3.Cross(xAxis, yAxis);

            float m13 = zAxis.x, m23 = zAxis.y, m33 = zAxis.z;
            float m21 = xAxis.y, m22 = yAxis.y;
            float m11 = xAxis.x, m31 = xAxis.z;
            float pitch = Mathf.Asin(-ClampUnit(m23));
            float yaw, roll;
            if (Mathf.Abs(m23) < 0.9999f)
            {
                yaw = Mathf.Atan2(m13, m33);
                roll = Mathf.Atan2(m21, m22);
            }
            else
            {
                yaw = Mathf.Atan2(-m31, m11);
                roll = 0f;
            }
            return new ArmPose
            {
                ShoulderPitch = pitch,
                ShoulderYaw = yaw * sideSign,
                ShoulderRoll = roll * sideSign,
                ElbowBend = elbowBend,
                Tremor = baseTremor,
                Grip = grip,
            };
        }

        static bool IkEngaged(HandState s) =>
            s == HandState.Reaching || s == HandState.Contact || s == HandState.Gripped;

        static ArmPose IkAttackerArm(LimbSnapshot hand, ArmPose canned, BodyFrames opp, BodyFrames own, char side)
        {
            if (!IkEngaged(hand.State) || hand.Target == GripZone.None) return canned;
            if (!GripZoneAnchor(hand.Target, opp, out Vector3 anchor)) return canned;
            float sideSign = side == 'L' ? -1f : 1f;
            Vector3 shoulder = side == 'L' ? own.ShoulderL : own.ShoulderR;
            Vector3 target = anchor;
            if (hand.State == HandState.Gripped)
            {
                Vector3 chest = own.TorsoPos + own.TorsoRot.MulV(new Vector3(0, 0.3f, 0.1f));
                Vector3 toChest = chest - anchor;
                float len = toChest.magnitude;
                if (len > 1e-6f) target = anchor + toChest * (Mathf.Min(0.06f, len) / len);
            }
            return SolveArmIK(target, shoulder, own.TorsoRot, sideSign, canned.Tremor, canned.Grip);
        }

        static void LookAt(BodyPose ownPose, BodyFrames own, Vector3 targetWorld, out float pitch, out float yaw)
        {
            Vector3 headOrigin = own.TorsoPos + own.TorsoRot.MulV(new Vector3(0, HeadY, 0));
            Vector3 tLocal = V3Norm(own.TorsoRot.Transpose().MulV(targetWorld - headOrigin));
            float y = Mathf.Atan2(tLocal.x, Mathf.Max(0.15f, tLocal.z));
            float p = -Mathf.Atan2(tLocal.y, Mathf.Sqrt(tLocal.x * tLocal.x + tLocal.z * tLocal.z));
            pitch = Mathf.Clamp(ownPose.HeadPitch * 0.3f + p * 0.7f, -0.6f, 1.1f);
            yaw = Mathf.Clamp(ownPose.HeadYaw * 0.3f + y * 0.7f, -0.75f, 0.75f);
        }

        public static ScenePoses ComputeScenePoses(BottomPoseInputs bottomIn, TopPoseInputs topIn)
        {
            BodyPose b0 = ComputeBottomPose(bottomIn);
            BodyPose t0 = ComputeTopPose(topIn);

            BodyFrames bF0 = ComputeBodyFrames(b0, BottomPlacement);
            BodyFrames tF0 = ComputeBodyFrames(t0, TopPlacement);

            // 1. Defender hands plant on the attacker (base anchors are not in
            //    the C# state, so only the grip-style anchors that map cleanly
            //    are used; otherwise the canned pose stays).
            ArmPose topArmL = (topIn.HasCutL || topIn.ArmExtractedL) ? t0.ArmL
                : IkAttackerArm(topIn.LeftHand, t0.ArmL, bF0, tF0, 'L');
            ArmPose topArmR = (topIn.HasCutR || topIn.ArmExtractedR) ? t0.ArmR
                : IkAttackerArm(topIn.RightHand, t0.ArmR, bF0, tF0, 'R');

            // Balance post → plant the free hand on the mat.
            bool posted = BalancePost(topIn, out char postSide, out float postT);
            if (posted)
            {
                float sideSign = postSide == 'L' ? -1f : 1f;
                Vector3 shoulder = postSide == 'L' ? tF0.ShoulderL : tF0.ShoulderR;
                Vector3 matPoint = new Vector3(shoulder.x + sideSign * 0.18f, 0.04f, shoulder.z + 0.06f);
                ArmPose planted = SolveArmIK(matPoint, shoulder, tF0.TorsoRot, sideSign, 0.2f, 0.0f);
                if (postSide == 'L') topArmL = LerpArm(topArmL, planted, postT);
                else topArmR = LerpArm(topArmR, planted, postT);
            }

            BodyPose topMid = t0; topMid.ArmL = topArmL; topMid.ArmR = topArmR;

            // 2. Attacker hands plant on the final defender frame.
            BodyFrames tF = ComputeBodyFrames(topMid, TopPlacement);
            BodyPose bottomMid = b0;
            bottomMid.ArmL = IkAttackerArm(bottomIn.LeftHand, b0.ArmL, tF, bF0, 'L');
            bottomMid.ArmR = IkAttackerArm(bottomIn.RightHand, b0.ArmR, tF, bF0, 'R');
            BodyFrames bF = ComputeBodyFrames(bottomMid, BottomPlacement);

            // 3. Grip coupling: a held sleeve/wrist drags the *defender's* arm
            //    along — their wrist is re-solved onto the gripping hand, so the
            //    pull reads on both bodies and the two hands stay connected.
            //    A posting / cut / extracted arm is committed elsewhere — never
            //    re-purpose it as a drag. (pose.ts computeScenePoses step 3.)
            ArmPose topArmLFinal = DragArm('L', topMid.ArmL,
                topIn.HasCutL || topIn.ArmExtractedL || (posted && postSide == 'L'),
                bottomIn, bF, tF);
            ArmPose topArmRFinal = DragArm('R', topMid.ArmR,
                topIn.HasCutR || topIn.ArmExtractedR || (posted && postSide == 'R'),
                bottomIn, bF, tF);
            BodyPose topDragged = topMid;
            topDragged.ArmL = topArmLFinal;
            topDragged.ArmR = topArmRFinal;

            // 4. Gaze.
            LookAt(bottomMid, bF, tF.HeadPos, out float bp, out float by);
            LookAt(topDragged, tF, bF.HeadPos, out float tp, out float ty);
            bottomMid.HeadPitch = bp; bottomMid.HeadYaw = by;
            topDragged.HeadPitch = tp; topDragged.HeadYaw = ty;

            return new ScenePoses { Bottom = bottomMid, Top = topDragged };
        }

        // A bottom hand GRIPPED on the defender's SLEEVE/WRIST (this side) —
        // returns the gripping hand's world position, else null.
        static bool GrippedHand(BottomPoseInputs bottomIn, BodyFrames bF, char zoneSide, out Vector3 grab)
        {
            GripZone sleeve = zoneSide == 'L' ? GripZone.SleeveL : GripZone.SleeveR;
            GripZone wrist  = zoneSide == 'L' ? GripZone.WristL  : GripZone.WristR;
            if (bottomIn.LeftHand.State == HandState.Gripped &&
                (bottomIn.LeftHand.Target == sleeve || bottomIn.LeftHand.Target == wrist))
            { grab = bF.HandL; return true; }
            if (bottomIn.RightHand.State == HandState.Gripped &&
                (bottomIn.RightHand.Target == sleeve || bottomIn.RightHand.Target == wrist))
            { grab = bF.HandR; return true; }
            grab = Vector3.zero;
            return false;
        }

        static ArmPose DragArm(char side, ArmPose current, bool blocked,
                               BottomPoseInputs bottomIn, BodyFrames bF, BodyFrames tF)
        {
            if (blocked) return current;
            if (!GrippedHand(bottomIn, bF, side, out Vector3 grab)) return current;
            Vector3 shoulder = side == 'L' ? tF.ShoulderL : tF.ShoulderR;
            return SolveArmIK(grab, shoulder, tF.TorsoRot, side == 'L' ? -1f : 1f,
                              Mathf.Max(current.Tremor, 0.3f), 0.15f);
        }

        // ---- Finish tableaux --------------------------------------------------
        // Ported 1:1 from pose.ts computeFinishPoses. When a technique / counter
        // / pass resolves, the FSM-driven pose stops telling the story — the
        // bodies need to land in the recognisable end position of that move.
        // tMs is time since confirmation (drives squeeze pulses + heavy breath).

        public static FinishPoses ComputeFinishPoses(FinishKind kind, float tMs)
        {
            float breathB = Mathf.Sin((tMs / 1000f) * TWO_PI * 0.9f);
            float breathT = Mathf.Sin((tMs / 1000f) * TWO_PI * 0.9f + Mathf.PI * 0.6f);
            float squeeze = 0.5f + 0.5f * Mathf.Sin((tMs / 1000f) * TWO_PI * 1.2f);

            BodyPose bottom = new BodyPose
            {
                PelvisX = 0, PelvisY = 0.28f, PelvisZ = 0,
                PelvisPitch = -Mathf.PI / 2f + 0.15f, PelvisYaw = 0, PelvisRoll = 0,
                TorsoPitch = 0.35f + breathB * 0.04f, TorsoYaw = 0, TorsoRoll = 0, TorsoTremor = 0,
                HeadPitch = 0.55f, HeadYaw = 0,
                Breath = breathB,
                ArmL = MkArm(AttackRest.Pitch, AttackRest.Roll, AttackRest.Yaw, AttackRest.Elbow),
                ArmR = MkArm(AttackRest.Pitch, AttackRest.Roll, AttackRest.Yaw, AttackRest.Elbow),
                LegL = SolveLeg(LegDirLockedThigh, LegDirLockedShin),
                LegR = SolveLeg(LegDirLockedThigh, LegDirLockedShin),
            };
            BodyPose top = new BodyPose
            {
                PelvisX = 0, PelvisY = 0.50f, PelvisZ = 0,
                PelvisPitch = 0, PelvisYaw = 0, PelvisRoll = 0,
                TorsoPitch = 0.10f + breathT * 0.03f, TorsoYaw = 0, TorsoRoll = 0, TorsoTremor = 0,
                HeadPitch = 0.45f, HeadYaw = 0,
                Breath = breathT,
                ArmL = MkArm(DefenseRest.Pitch, DefenseRest.Roll, DefenseRest.Yaw, DefenseRest.Elbow),
                ArmR = MkArm(DefenseRest.Pitch, DefenseRest.Roll, DefenseRest.Yaw, DefenseRest.Elbow),
                LegL = new LegPose { HipPitch = -0.55f, HipYaw = 0, HipRoll = 0.30f, KneeBend = 2.0f },
                LegR = new LegPose { HipPitch = -0.55f, HipYaw = 0, HipRoll = 0.30f, KneeBend = 2.0f },
            };

            switch (kind)
            {
                case FinishKind.Triangle:
                    bottom.PelvisY = 0.34f + squeeze * 0.015f;
                    bottom.TorsoPitch = 0.45f;
                    bottom.LegR = SolveLeg(new Vector3(0.18f, -0.50f, 0.85f), new Vector3(-0.90f, -0.25f, -0.35f));
                    bottom.LegL = SolveLeg(new Vector3(0.35f, -0.55f, 0.76f), new Vector3(-0.70f, -0.50f, -0.50f));
                    bottom.ArmL = MkArm(-0.70f, 0.10f, -0.15f, 1.05f + squeeze * 0.08f, 0.5f, 0.9f);
                    bottom.ArmR = MkArm(-0.70f, 0.10f, -0.15f, 1.05f + squeeze * 0.08f, 0.5f, 0.9f);
                    top.PelvisY = 0.42f; top.PelvisZ = 0.18f;
                    top.TorsoPitch = 0.85f; top.HeadPitch = 0.95f; top.TorsoTremor = 0.45f;
                    top.ArmL = MkArm(-0.50f, 0.55f, 0.1f, 0.25f, 0.4f);
                    top.ArmR = MkArm(-0.50f, 0.55f, 0.1f, 0.25f, 0.4f);
                    break;
                case FinishKind.Omoplata:
                    bottom.PelvisYaw = 0.80f; bottom.PelvisY = 0.30f; bottom.TorsoPitch = 0.60f;
                    bottom.LegL = SolveLeg(new Vector3(-0.35f, -0.55f, 0.75f), new Vector3(-0.60f, -0.70f, -0.30f));
                    bottom.LegR = SolveLeg(new Vector3(0.70f, -0.50f, 0.40f), new Vector3(-0.20f, -0.90f, -0.20f));
                    bottom.ArmL = MkArm(-0.50f, 0.15f, -0.10f, 0.70f, 0.3f);
                    bottom.ArmR = MkArm(-0.55f, 0.20f, -0.10f, 0.65f, 0.3f);
                    top.PelvisY = 0.34f; top.PelvisZ = 0.25f;
                    top.TorsoPitch = 1.15f; top.HeadPitch = 1.0f; top.TorsoTremor = 0.35f;
                    top.ArmL = MkArm(0.70f, 0.10f, -0.50f, 0.90f, 0.5f);
                    top.ArmR = MkArm(-0.50f, 0.50f, 0.10f, 0.30f, 0.2f);
                    break;
                case FinishKind.ScissorSweep:
                    bottom.PelvisY = 0.38f; bottom.PelvisZ = -0.12f; bottom.TorsoPitch = 0.85f; bottom.HeadPitch = 0.75f;
                    bottom.LegR = SolveLeg(new Vector3(0.40f, -0.70f, 0.35f), new Vector3(-0.50f, -0.80f, -0.10f));
                    bottom.LegL = SolveLeg(new Vector3(0.10f, -0.95f, 0.15f), new Vector3(0.10f, -0.90f, -0.30f));
                    bottom.ArmL = MkArm(-0.70f, 0.10f, -0.10f, 0.90f, 0.3f, 0.9f);
                    bottom.ArmR = MkArm(-0.70f, 0.10f, -0.10f, 0.90f, 0.3f, 0.9f);
                    top.PelvisX = 0.50f; top.PelvisY = 0.28f; top.PelvisRoll = 1.30f;
                    top.TorsoPitch = -0.15f; top.HeadPitch = -0.20f;
                    top.ArmL = MkArm(-1.20f, 0.70f, 0.0f, 0.25f, 0.2f);
                    top.ArmR = MkArm(-1.20f, 0.70f, 0.0f, 0.25f, 0.2f);
                    top.LegL = new LegPose { HipPitch = -0.90f, HipYaw = 0, HipRoll = 0.35f, KneeBend = 1.0f };
                    top.LegR = new LegPose { HipPitch = -0.40f, HipYaw = 0, HipRoll = 0.40f, KneeBend = 1.4f };
                    break;
                case FinishKind.FlowerSweep:
                    bottom.PelvisY = 0.38f; bottom.PelvisZ = -0.12f; bottom.TorsoPitch = 0.85f; bottom.HeadPitch = 0.75f;
                    bottom.LegL = SolveLeg(new Vector3(0.50f, -0.40f, 0.75f), new Vector3(-0.60f, -0.60f, -0.30f));
                    bottom.LegR = SolveLeg(new Vector3(0.10f, -0.95f, 0.15f), new Vector3(0.10f, -0.90f, -0.30f));
                    bottom.ArmL = MkArm(-0.70f, 0.10f, -0.10f, 0.90f, 0.3f, 0.9f);
                    bottom.ArmR = MkArm(-0.70f, 0.10f, -0.10f, 0.90f, 0.3f, 0.9f);
                    top.PelvisX = -0.50f; top.PelvisY = 0.28f; top.PelvisRoll = -1.30f;
                    top.TorsoPitch = -0.15f; top.HeadPitch = -0.20f;
                    top.ArmL = MkArm(-1.20f, 0.70f, 0.0f, 0.25f, 0.2f);
                    top.ArmR = MkArm(-1.20f, 0.70f, 0.0f, 0.25f, 0.2f);
                    top.LegL = new LegPose { HipPitch = -0.40f, HipYaw = 0, HipRoll = 0.40f, KneeBend = 1.4f };
                    top.LegR = new LegPose { HipPitch = -0.90f, HipYaw = 0, HipRoll = 0.35f, KneeBend = 1.0f };
                    break;
                case FinishKind.HipBump:
                    bottom.PelvisY = 0.42f; bottom.PelvisZ = -0.10f; bottom.TorsoPitch = 1.05f; bottom.HeadPitch = 0.20f;
                    bottom.ArmR = MkArm(0.50f, 0.30f, 0.0f, 0.15f);
                    bottom.ArmL = MkArm(-1.10f, 0.30f, -0.10f, 0.30f, 0.2f);
                    bottom.LegL = SolveLeg(new Vector3(0.55f, -0.75f, 0.25f), new Vector3(-0.40f, -0.85f, -0.15f));
                    bottom.LegR = SolveLeg(new Vector3(0.40f, -0.80f, 0.30f), new Vector3(-0.45f, -0.80f, -0.15f));
                    top.PelvisY = 0.46f; top.PelvisZ = -0.18f; top.TorsoPitch = -0.45f; top.HeadPitch = -0.30f;
                    top.ArmL = MkArm(-1.40f, 0.40f, 0.0f, 0.20f, 0.2f);
                    top.ArmR = MkArm(-1.40f, 0.40f, 0.0f, 0.20f, 0.2f);
                    top.LegL = new LegPose { HipPitch = -0.35f, HipYaw = 0, HipRoll = 0.35f, KneeBend = 1.6f };
                    top.LegR = new LegPose { HipPitch = -0.35f, HipYaw = 0, HipRoll = 0.35f, KneeBend = 1.6f };
                    break;
                case FinishKind.CrossCollar:
                    bottom.TorsoPitch = 0.60f;
                    bottom.ArmL = MkArm(-0.80f, 0.05f, -0.55f, 0.85f + squeeze * 0.06f, 0.6f, 0.9f);
                    bottom.ArmR = MkArm(-0.80f, 0.05f, -0.55f, 0.85f + squeeze * 0.06f, 0.6f, 0.9f);
                    top.PelvisY = 0.44f; top.PelvisZ = 0.12f; top.TorsoPitch = 0.70f; top.HeadPitch = 1.0f; top.TorsoTremor = 0.4f;
                    top.ArmL = MkArm(-0.35f, 0.30f, 0.0f, 0.30f, 0.3f);
                    top.ArmR = MkArm(-0.35f, 0.30f, 0.0f, 0.30f, 0.3f);
                    break;
                case FinishKind.ScissorCounter:
                    bottom.PelvisY = 0.22f; bottom.TorsoPitch = 0.10f; bottom.HeadPitch = 0.30f;
                    bottom.ArmL = MkArm(ParriedArm.Pitch, ParriedArm.Roll, ParriedArm.Yaw, ParriedArm.Elbow);
                    bottom.ArmR = MkArm(ParriedArm.Pitch, ParriedArm.Roll, ParriedArm.Yaw, ParriedArm.Elbow);
                    bottom.LegL = SolveLeg(new Vector3(0.65f, -0.70f, 0.15f), new Vector3(-0.30f, -0.90f, -0.10f));
                    bottom.LegR = SolveLeg(new Vector3(0.65f, -0.70f, 0.15f), new Vector3(-0.30f, -0.90f, -0.10f));
                    top.PelvisY = 0.52f; top.TorsoPitch = -0.10f; top.HeadPitch = 0.35f;
                    top.ArmL = MkArm(-0.55f, 0.20f, 0.0f, 0.20f);
                    top.ArmR = MkArm(-0.55f, 0.20f, 0.0f, 0.20f);
                    break;
                case FinishKind.TriangleEarlyStack:
                    bottom.PelvisY = 0.36f; bottom.TorsoPitch = 0.15f; bottom.HeadPitch = 0.70f;
                    bottom.LegL = SolveLeg(new Vector3(0.25f, -0.35f, 0.90f), new Vector3(-0.75f, -0.35f, -0.40f));
                    bottom.LegR = SolveLeg(new Vector3(0.25f, -0.35f, 0.90f), new Vector3(-0.75f, -0.35f, -0.40f));
                    bottom.ArmL = MkArm(-0.60f, 0.25f, 0.0f, 0.60f, 0.3f);
                    bottom.ArmR = MkArm(-0.60f, 0.25f, 0.0f, 0.60f, 0.3f);
                    top.PelvisY = 0.55f; top.PelvisZ = 0.32f; top.TorsoPitch = 0.75f; top.HeadPitch = 0.7f; top.TorsoTremor = 0.3f;
                    top.ArmL = MkArm(-0.90f, 0.25f, 0.0f, 0.10f, 0.2f);
                    top.ArmR = MkArm(-0.90f, 0.25f, 0.0f, 0.10f, 0.2f);
                    top.LegL = new LegPose { HipPitch = -0.30f, HipYaw = 0, HipRoll = 0.30f, KneeBend = 1.2f };
                    top.LegR = new LegPose { HipPitch = -0.30f, HipYaw = 0, HipRoll = 0.30f, KneeBend = 1.2f };
                    break;
                case FinishKind.Scramble:
                    bottom.PelvisY = 0.24f; bottom.PelvisPitch = -0.85f; bottom.TorsoPitch = 0.70f; bottom.HeadPitch = 0.10f;
                    bottom.LegL = SolveLeg(new Vector3(0.50f, -0.65f, 0.40f), new Vector3(-0.15f, -0.55f, -0.80f));
                    bottom.LegR = SolveLeg(new Vector3(0.50f, -0.65f, 0.40f), new Vector3(-0.15f, -0.55f, -0.80f));
                    bottom.ArmL = MkArm(-0.90f, 0.20f, 0, 0.90f);
                    bottom.ArmR = MkArm(-0.90f, 0.20f, 0, 0.90f);
                    top.PelvisZ = -0.28f; top.PelvisY = 0.55f; top.TorsoPitch = 0.05f; top.HeadPitch = 0.30f;
                    top.ArmL = MkArm(-0.95f, 0.20f, 0, 0.50f);
                    top.ArmR = MkArm(-0.95f, 0.20f, 0, 0.50f);
                    top.LegR = new LegPose { HipPitch = -1.30f, HipYaw = 0, HipRoll = 0.25f, KneeBend = 1.45f };
                    break;
                case FinishKind.Pass:
                    bottom.PelvisY = 0.20f; bottom.TorsoPitch = 0.15f; bottom.HeadPitch = 0.35f;
                    bottom.LegR = SolveLeg(new Vector3(-0.55f, -0.70f, 0.20f), new Vector3(-0.30f, -0.85f, -0.20f));
                    bottom.LegL = SolveLeg(new Vector3(0.60f, -0.75f, 0.15f), new Vector3(0.30f, -0.90f, -0.15f));
                    bottom.ArmL = MkArm(-0.75f, 0.15f, -0.20f, 0.70f, 0.2f, 0.9f);
                    bottom.ArmR = MkArm(-0.60f, 0.20f, -0.35f, 0.55f, 0.2f, 0.9f);
                    top.PelvisX = 0.50f; top.PelvisY = 0.38f; top.PelvisZ = 0.30f; top.PelvisYaw = -0.50f;
                    top.TorsoPitch = 0.55f; top.HeadPitch = 0.55f;
                    top.ArmL = MkArm(-0.75f, 0.20f, -0.10f, 0.60f, 0.2f);
                    top.ArmR = MkArm(-0.75f, 0.20f, -0.10f, 0.60f, 0.2f);
                    top.LegL = new LegPose { HipPitch = -0.15f, HipYaw = 0, HipRoll = 0.30f, KneeBend = 0.5f };
                    top.LegR = new LegPose { HipPitch = -0.15f, HipYaw = 0, HipRoll = 0.35f, KneeBend = 0.6f };
                    break;
            }

            // Execution phase: the first ~450 ms ramps motion finishes from a
            // mid-action keyframe into the settled hold (smoothstep). Submissions
            // stay as isometric holds.
            float phaseT = Clamp01(tMs / 450f);
            float phase = phaseT * phaseT * (3f - 2f * phaseT);
            switch (kind)
            {
                case FinishKind.ScissorSweep:
                case FinishKind.FlowerSweep:
                {
                    float sign = kind == FinishKind.ScissorSweep ? 1f : -1f;
                    top.PelvisRoll = Ramp(sign * 0.35f, top.PelvisRoll, phase);
                    top.PelvisX = Ramp(sign * 0.15f, top.PelvisX, phase);
                    top.PelvisY = Ramp(0.45f, top.PelvisY, phase);
                    bottom.TorsoPitch = Ramp(0.35f, bottom.TorsoPitch, phase);
                    break;
                }
                case FinishKind.HipBump:
                    bottom.TorsoPitch = Ramp(0.30f, bottom.TorsoPitch, phase);
                    top.TorsoPitch = Ramp(0.15f, top.TorsoPitch, phase);
                    top.PelvisZ = Ramp(0f, top.PelvisZ, phase);
                    break;
                case FinishKind.Pass:
                    top.PelvisX = Ramp(0.10f, top.PelvisX, phase);
                    top.PelvisZ = Ramp(0.05f, top.PelvisZ, phase);
                    break;
            }

            return new FinishPoses { Bottom = bottom, Top = top };
        }

        static float Ramp(float from, float to, float phase) => from + (to - from) * phase;
    }
}
