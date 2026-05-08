// EDITOR — bulk Mixamo animation importer + Animator Controller wiring.
//
// Usage:
//   1. Drop downloaded Mixamo FBX files into Assets/BJJSimulator/Art/Animations/.
//      Use the Y Bot rig and "Without Skin" download option (~50KB per clip).
//      The filename's "@<clip name>" suffix drives the state mapping below
//      (e.g. `Y Bot@Reach Forward.fbx` → Reaching state).
//   2. Menu: BJJ → Import Animations
//      - Forces every Y Bot@*.fbx to Humanoid + CopyFromOther YBotAvatar.
//      - Adds matching states to BJJAvatar.controller and wires AnyState
//        transitions on LHandState / RHandState.
//      - Idempotent: re-running just refreshes motion references and
//        skips already-present transitions.
//
// Filename → state map: case-insensitive substring match against the
// trailing `@xxx` segment of the FBX filename. First match wins. Update
// FILENAME_TO_STATE below when adding new clip families.
//
// Why one importer, not the auto AssetPostprocessor:
//   The Mixamo download flow is iterative — the user fetches a few clips,
//   wires them up, decides if the motion fits, then fetches more. A
//   one-shot menu lets the iteration cycle finish without re-running on
//   every Asset reimport.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BJJSimulator.EditorTools
{
    public static class BJJAnimationImporter
    {
        private const string AnimationsFolder    = "Assets/BJJSimulator/Art/Animations";
        private const string CharactersFolder    = "Assets/BJJSimulator/Art/Characters";
        private const string ControllerPath      = "Assets/BJJSimulator/Art/Animations/BJJAvatar.controller";
        private const string YBotFbxPath         = "Assets/BJJSimulator/Art/Characters/Y Bot.fbx";

        // Animator parameter names — must match BJJAvatarBinder / BJJAnimatorBinder.
        private const string PLHandState = "LHandState";
        private const string PRHandState = "RHandState";

        // HandState ordinals (mirrors BJJSimulator.HandState in BJJCoreTypes.cs).
        private const int HandStateIdle      = 0;
        private const int HandStateReaching  = 1;
        private const int HandStateContact   = 2;
        private const int HandStateGripped   = 3;
        private const int HandStateParried   = 4;
        private const int HandStateRetract   = 5;

        // -----------------------------------------------------------------
        // Filename → state mapping
        // -----------------------------------------------------------------

        private struct StateBinding
        {
            public string StateName;
            public int    HandStateValue;        // value of LHandState / RHandState that enters this state
            public bool   LoopTime;              // expected to loop while held in this state
            public float  Speed;                 // playback speed
            public bool   ReplaceExistingMotion; // true → overwrite existing state's motion (e.g. Gripped placeholder)
        }

        // Substring tokens checked against the @<clip-name> portion of the
        // FBX filename. ORDER MATTERS — first hit wins, so put more
        // specific tokens before more general ones (e.g. "Pulling" before
        // "Pull" so the more specific match doesn't get swallowed).
        private static readonly (string Token, StateBinding Binding)[] FILENAME_TO_STATE = new[]
        {
            ("Reach",     new StateBinding { StateName = "Reaching", HandStateValue = HandStateReaching, LoopTime = true,  Speed = 1f }),
            ("Punch",     new StateBinding { StateName = "Reaching", HandStateValue = HandStateReaching, LoopTime = false, Speed = 1f }),
            ("Stab",      new StateBinding { StateName = "Reaching", HandStateValue = HandStateReaching, LoopTime = false, Speed = 1f }),
            ("Grappling", new StateBinding { StateName = "Contact",  HandStateValue = HandStateContact,  LoopTime = true,  Speed = 1f }),
            ("Contact",   new StateBinding { StateName = "Contact",  HandStateValue = HandStateContact,  LoopTime = true,  Speed = 1f }),
            ("Wrist",     new StateBinding { StateName = "Contact",  HandStateValue = HandStateContact,  LoopTime = true,  Speed = 1f }),
            ("Removing",  new StateBinding { StateName = "Contact",  HandStateValue = HandStateContact,  LoopTime = true,  Speed = 1f }),
            ("Pulling",   new StateBinding { StateName = "Gripped",  HandStateValue = HandStateGripped,  LoopTime = true,  Speed = 1f, ReplaceExistingMotion = true }),
            ("Pull",      new StateBinding { StateName = "Gripped",  HandStateValue = HandStateGripped,  LoopTime = true,  Speed = 1f, ReplaceExistingMotion = true }),
            ("Grip",      new StateBinding { StateName = "Gripped",  HandStateValue = HandStateGripped,  LoopTime = true,  Speed = 1f, ReplaceExistingMotion = true }),
            ("Hold",      new StateBinding { StateName = "Gripped",  HandStateValue = HandStateGripped,  LoopTime = true,  Speed = 1f, ReplaceExistingMotion = true }),
            ("Retract",   new StateBinding { StateName = "Retract",  HandStateValue = HandStateRetract,  LoopTime = false, Speed = 1f }),
            ("Recoil",    new StateBinding { StateName = "Retract",  HandStateValue = HandStateRetract,  LoopTime = false, Speed = 1f }),
            ("Withdraw",  new StateBinding { StateName = "Retract",  HandStateValue = HandStateRetract,  LoopTime = false, Speed = 1f }),
        };

        // -----------------------------------------------------------------
        // Menu entry
        // -----------------------------------------------------------------

        [MenuItem("BJJ/Import Animations")]
        public static void ImportAndWireAll()
        {
            var avatar = LoadYBotAvatar();
            if (avatar == null)
            {
                EditorUtility.DisplayDialog("BJJ Animation Importer",
                    $"Could not find a Humanoid Avatar inside {YBotFbxPath}. " +
                    "Make sure the Y Bot FBX is imported as Humanoid first " +
                    "(Inspector → Rig → Animation Type = Humanoid → Apply).",
                    "OK");
                return;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                EditorUtility.DisplayDialog("BJJ Animation Importer",
                    $"Could not find {ControllerPath}. Aborting.", "OK");
                return;
            }

            int importedCount = 0;
            int wiredCount    = 0;
            var errors = new List<string>();

            foreach (var fbxPath in Directory.GetFiles(AnimationsFolder, "*.fbx"))
            {
                var assetPath = fbxPath.Replace('\\', '/');
                var binding   = ResolveBinding(assetPath);
                if (binding == null) continue; // unrecognised filename — skip rather than guess

                try
                {
                    if (ApplyImportSettings(assetPath, avatar, binding.Value.LoopTime))
                        importedCount++;
                }
                catch (System.Exception ex)
                {
                    errors.Add($"{assetPath}: import failed — {ex.Message}");
                    continue;
                }
            }

            // Reimport pass must finish before we resolve AnimationClip subassets.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (var fbxPath in Directory.GetFiles(AnimationsFolder, "*.fbx"))
            {
                var assetPath = fbxPath.Replace('\\', '/');
                var binding   = ResolveBinding(assetPath);
                if (binding == null) continue;

                var clip = LoadClipFromFbx(assetPath);
                if (clip == null)
                {
                    errors.Add($"{assetPath}: no AnimationClip subasset found after reimport");
                    continue;
                }

                if (WireStateAndTransitions(controller, binding.Value, clip))
                    wiredCount++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var msg = $"Import settings applied: {importedCount} clip(s)\n" +
                      $"Animator states wired:   {wiredCount} clip(s)";
            if (errors.Count > 0) msg += "\n\nErrors:\n  " + string.Join("\n  ", errors);
            Debug.Log("[BJJ Animation Importer] " + msg);
            EditorUtility.DisplayDialog("BJJ Animation Importer", msg, "OK");
        }

        // -----------------------------------------------------------------
        // Filename → binding resolution
        // -----------------------------------------------------------------

        private static StateBinding? ResolveBinding(string fbxPath)
        {
            // We only want clip files like `Y Bot@Reach Forward.fbx` — skip the
            // base mesh `Y Bot.fbx` itself (no `@`) and any other character FBX.
            var fileName = Path.GetFileNameWithoutExtension(fbxPath);
            int atIdx = fileName.IndexOf('@');
            if (atIdx < 0) return null;
            var clipPart = fileName.Substring(atIdx + 1);

            foreach (var (token, binding) in FILENAME_TO_STATE)
                if (clipPart.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return binding;
            return null;
        }

        // -----------------------------------------------------------------
        // Step 1 — force Humanoid + CopyFromOther YBotAvatar on each FBX
        // -----------------------------------------------------------------

        private static bool ApplyImportSettings(string fbxPath, Avatar avatar, bool loopTime)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return false;

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                changed = true;
            }
            if (importer.sourceAvatar != avatar)
            {
                importer.sourceAvatar = avatar;
                changed = true;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                // Animation FBX doesn't need its own materials — Avatar_Bottom /
                // Avatar_Top inherit from Y Bot.fbx + BJJAvatarTint override.
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }

            // Loop-time on each AnimationClip inside the FBX.
            var clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i].loopTime != loopTime)
                {
                    clips[i].loopTime = loopTime;
                    changed = true;
                }
            }
            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
            return changed;
        }

        private static AnimationClip LoadClipFromFbx(string fbxPath)
        {
            // Pick the first non-preview AnimationClip subasset. Mixamo FBX
            // typically contains exactly one clip; preview clips are filtered
            // out so the Animator never points at the Editor preview proxy.
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (sub is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }

        private static Avatar LoadYBotAvatar()
        {
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(YBotFbxPath))
                if (sub is Avatar avatar) return avatar;
            return null;
        }

        // -----------------------------------------------------------------
        // Step 2 — wire state + transitions in the controller
        // -----------------------------------------------------------------

        private static bool WireStateAndTransitions(
            AnimatorController controller, StateBinding binding, AnimationClip clip)
        {
            var sm = controller.layers[0].stateMachine;

            // Locate or create the destination state.
            AnimatorState target = null;
            foreach (var s in sm.states)
                if (s.state.name == binding.StateName) { target = s.state; break; }

            bool stateNewlyCreated = false;
            if (target == null)
            {
                target = sm.AddState(binding.StateName, NewStatePosition(sm));
                stateNewlyCreated = true;
            }

            // Motion assignment: only overwrite if newly created OR the
            // binding declares ReplaceExistingMotion (so a placeholder
            // Gripped using Y Bot@Idle gets upgraded when a real Grip clip
            // arrives, but Reaching never loses an earlier-imported clip
            // unless the user re-runs after deleting the FBX manually).
            if (stateNewlyCreated || binding.ReplaceExistingMotion || target.motion == null)
            {
                target.motion = clip;
                target.speed  = binding.Speed;
            }

            // Idle state ref for the exit transitions.
            AnimatorState idle = null;
            foreach (var s in sm.states)
                if (s.state.name == "Idle") { idle = s.state; break; }
            if (idle == null) return false; // controller schema mismatch

            // -- AnyState → target on (LHandState == V) and (RHandState == V).
            EnsureAnyStateTransition(sm, target, PLHandState, binding.HandStateValue);
            EnsureAnyStateTransition(sm, target, PRHandState, binding.HandStateValue);

            // -- target → Idle when both hands have moved off this HandState.
            EnsureExitToIdle(target, idle, binding.HandStateValue);

            return true;
        }

        private static void EnsureAnyStateTransition(
            AnimatorStateMachine sm, AnimatorState target, string param, int handStateValue)
        {
            foreach (var t in sm.anyStateTransitions)
            {
                if (t.destinationState != target) continue;
                foreach (var c in t.conditions)
                    if (c.parameter == param &&
                        c.mode      == AnimatorConditionMode.Equals &&
                        Mathf.Approximately(c.threshold, handStateValue))
                        return; // already wired
            }

            var nt = sm.AddAnyStateTransition(target);
            nt.hasExitTime         = false;
            nt.duration            = 0.08f;
            nt.canTransitionToSelf = false;
            nt.AddCondition(AnimatorConditionMode.Equals, handStateValue, param);
        }

        private static void EnsureExitToIdle(
            AnimatorState target, AnimatorState idle, int handStateValue)
        {
            foreach (var t in target.transitions)
            {
                if (t.destinationState != idle) continue;
                bool hasL = false, hasR = false;
                foreach (var c in t.conditions)
                {
                    if (c.parameter == PLHandState && c.mode == AnimatorConditionMode.NotEqual &&
                        Mathf.Approximately(c.threshold, handStateValue)) hasL = true;
                    if (c.parameter == PRHandState && c.mode == AnimatorConditionMode.NotEqual &&
                        Mathf.Approximately(c.threshold, handStateValue)) hasR = true;
                }
                if (hasL && hasR) return; // already wired
            }

            var nt = target.AddTransition(idle);
            nt.hasExitTime = false;
            nt.duration    = 0.08f;
            nt.AddCondition(AnimatorConditionMode.NotEqual, handStateValue, PLHandState);
            nt.AddCondition(AnimatorConditionMode.NotEqual, handStateValue, PRHandState);
        }

        // Lay newly-added states out in a column so the Animator window
        // remains readable. Position is purely cosmetic.
        private static Vector3 NewStatePosition(AnimatorStateMachine sm)
        {
            float maxY = 0f;
            foreach (var s in sm.states)
                if (s.position.y > maxY) maxY = s.position.y;
            return new Vector3(450f, maxY + 80f, 0f);
        }
    }
}
