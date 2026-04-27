// EDITOR — One-click scene assembly for the BJJ Stage 2 prototype.
//
// Replaces the manual 6-step procedure documented in
// src/prototype/unity/README.md §"Create the BJJ scene". Open Unity, then
// pick BJJ → Setup Scene from the menu bar to:
//
//   1.  Ensure the BJJ_URP scripting define symbol is set (enables URP post-process
//       code in the Platform scripts).
//   2.  Create (or replace) Assets/Scenes/BJJ.unity from the Empty template.
//   3.  Spawn "BJJ_GameManager" with all seven Platform MonoBehaviours:
//         BJJSessionLifecycle, BJJInputProvider, BJJGameManager, BJJDebugHud,
//         BJJVolumeController, BJJImpactFeedback, BJJAvatarBinder.
//   4.  Wire BJJInputProvider.actionsAsset → BJJInputActions.inputactions.
//   5.  Wire BJJGameManager.hud → BJJDebugHud on the same GameObject.
//   6.  Create a URP Volume Profile (WhiteBalance, Vignette, ColorAdjustments,
//       ChromaticAberration) and a Global Volume GameObject; wire both
//       BJJVolumeController.globalVolume and BJJImpactFeedback.globalVolume.
//   7.  Add a Main Camera; wire BJJImpactFeedback.mainCamera.
//   8.  Spawn a minimal blockman rig (Capsule + Sphere primitives) for Bottom
//       and Top; wire all BJJAvatarBinder Transform / Renderer slots.
//   9.  Save and add the scene to Build Settings (index 0).
//
// Idempotent: running the menu item again wipes any existing BJJ.unity scene
// and rebuilds from scratch, so a botched manual edit can always be reset.

using System.IO;
using BJJSimulator.Platform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BJJSimulator.EditorTools
{
    public static class BJJSceneSetup
    {
        private const string ScenesFolder      = "Assets/Scenes";
        private const string ScenePath         = "Assets/Scenes/BJJ.unity";
        private const string GameManagerName   = "BJJ_GameManager";
        private const string InputActionsPath  =
            "Assets/BJJSimulator/Runtime/Input/BJJInputActions.inputactions";
        private const string RenderFolder      = "Assets/BJJSimulator/Runtime/Render";
        private const string VolumeProfilePath =
            "Assets/BJJSimulator/Runtime/Render/BJJVolumeProfile.asset";

        // ────────────────────────────────────────────────────────────────────────
        // Menu items
        // ────────────────────────────────────────────────────────────────────────

        [MenuItem("BJJ/Setup Scene", priority = 0)]
        public static void SetupScene()
        {
            // 0. Ensure BJJ_URP define is active (enables URP code in Platform scripts).
            EnsureScriptingDefine("BJJ_URP");

            EnsureFolder(ScenesFolder);

            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
            {
                EditorUtility.DisplayDialog(
                    "BJJ Setup Scene",
                    $"BJJInputActions asset not found at:\n{InputActionsPath}\n\n" +
                    "Make sure the project has finished importing, then try again.",
                    "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 1. BJJ_GameManager with all Platform components ─────────────────
            var go = new GameObject(GameManagerName);
            SceneManager.MoveGameObjectToScene(go, scene);

            go.AddComponent<BJJSessionLifecycle>();
            var provider = go.AddComponent<BJJInputProvider>();
            var manager  = go.AddComponent<BJJGameManager>();
            var hud      = go.AddComponent<BJJDebugHud>();
            var volCtrl  = go.AddComponent<BJJVolumeController>();
            var impact   = go.AddComponent<BJJImpactFeedback>();
            var binder   = go.AddComponent<BJJAvatarBinder>();

            AssignSerialized(provider, "actionsAsset", inputActions);
            AssignSerialized(manager,  "hud",          hud);

            // ── 2. Main Camera ──────────────────────────────────────────────────
            var camGo = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 2f, -3f);
            camGo.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();

            // ── 3. Directional Light ────────────────────────────────────────────
            var lightGo = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;

            // ── 4. Global Volume ────────────────────────────────────────────────
            EnsureFolder(RenderFolder);

            // Delete stale profile asset so we get a clean one each run.
            AssetDatabase.DeleteAsset(VolumeProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<WhiteBalance>(true);
            profile.Add<Vignette>(true);
            profile.Add<ColorAdjustments>(true);
            profile.Add<ChromaticAberration>(true);
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            AssetDatabase.SaveAssets();

            var volGo = new GameObject("Global Volume");
            SceneManager.MoveGameObjectToScene(volGo, scene);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal      = true;
            vol.priority      = 1f;
            vol.sharedProfile = profile;

            // Wire Volume to controllers
            AssignSerialized(volCtrl, "globalVolume", vol);
            AssignSerialized(impact,  "globalVolume", vol);
            AssignSerialized(impact,  "mainCamera",   cam);

            // ── 5. Blockman rig ─────────────────────────────────────────────────
            var chars = new GameObject("Characters");
            SceneManager.MoveGameObjectToScene(chars, scene);

            // Bottom (Guard) — starts at x = −0.4
            var bottomRoot  = CreateBody(scene, chars.transform, "Bottom",
                                         new Vector3(-0.4f, 0f, 0f));
            var bLeftHand   = CreateJoint(scene, bottomRoot, "LeftHand",
                                          new Vector3(-0.25f, 0.5f, 0f));
            var bRightHand  = CreateJoint(scene, bottomRoot, "RightHand",
                                          new Vector3( 0.25f, 0.5f, 0f));
            var bLeftFoot   = CreateJoint(scene, bottomRoot, "LeftFoot",
                                          new Vector3(-0.15f, 0f,   0f));
            var bRightFoot  = CreateJoint(scene, bottomRoot, "RightFoot",
                                          new Vector3( 0.15f, 0f,   0f));

            var bLeftFootRend  = bLeftFoot.GetComponent<Renderer>();
            var bRightFootRend = bRightFoot.GetComponent<Renderer>();

            // Top (Passer) — starts at x = +0.4
            var topRoot  = CreateBody(scene, chars.transform, "Top",
                                      new Vector3(0.4f, 0f, 0f));
            var topSpine = CreateJoint(scene, topRoot, "Spine",
                                       new Vector3(0f, 0.7f, 0f));

            // Wire binder
            AssignSerialized(binder, "bottomRoot",              bottomRoot);
            AssignSerialized(binder, "bottomLeftHand",          bLeftHand);
            AssignSerialized(binder, "bottomRightHand",         bRightHand);
            AssignSerialized(binder, "bottomLeftFoot",          bLeftFoot);
            AssignSerialized(binder, "bottomRightFoot",         bRightFoot);
            AssignSerialized(binder, "bottomLeftFootRenderer",  bLeftFootRend);
            AssignSerialized(binder, "bottomRightFootRenderer", bRightFootRend);
            AssignSerialized(binder, "topRoot",                 topRoot);
            AssignSerialized(binder, "topSpine",                topSpine);

            // ── 6. Save & build settings ─────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!saved)
            {
                EditorUtility.DisplayDialog(
                    "BJJ Setup Scene",
                    $"Failed to save scene to {ScenePath}.",
                    "OK");
                return;
            }

            AddSceneToBuildSettings(ScenePath);

            EditorUtility.DisplayDialog(
                "BJJ Setup Scene",
                $"Scene built at {ScenePath}.\n\n" +
                "All Platform components wired.\nPress ▶ to play.",
                "OK");
        }

        [MenuItem("BJJ/Open Scene", priority = 10)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (EditorUtility.DisplayDialog(
                    "BJJ Open Scene",
                    $"{ScenePath} does not exist yet.\n\nRun BJJ → Setup Scene first?",
                    "Setup now", "Cancel"))
                {
                    SetupScene();
                }
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Primitive helpers
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>Capsule body for a character root.</summary>
        private static Transform CreateBody(
            Scene scene, Transform parent, string name, Vector3 worldPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(parent);
            go.transform.localPosition = worldPos;
            go.transform.localScale    = new Vector3(0.3f, 0.5f, 0.3f);
            // Remove collider — this is a visual-only rig.
            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
            return go.transform;
        }

        /// <summary>Sphere joint / end-effector.</summary>
        private static Transform CreateJoint(
            Scene scene, Transform parent, string name, Vector3 localPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localScale    = Vector3.one * 0.12f;
            Object.DestroyImmediate(go.GetComponent<SphereCollider>());
            return go.transform;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Utility
        // ────────────────────────────────────────────────────────────────────────

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            string leaf   = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void AssignSerialized(Object component, string fieldName, Object value)
        {
            var so   = new SerializedObject(component);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError(
                    $"BJJSceneSetup: field '{fieldName}' not found on " +
                    $"{component.GetType().Name}. Did the field name change?");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureScriptingDefine(string define)
        {
            var target      = EditorUserBuildSettings.activeBuildTarget;
            var group       = BuildPipeline.GetBuildTargetGroup(target);
            var namedTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            PlayerSettings.GetScriptingDefineSymbols(namedTarget, out string[] defines);
            foreach (var d in defines)
                if (d == define) return;
            var next = new string[defines.Length + 1];
            System.Array.Copy(defines, next, defines.Length);
            next[defines.Length] = define;
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, next);
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var existing = EditorBuildSettings.scenes;
            foreach (var s in existing)
            {
                if (s.path == path)
                {
                    s.enabled              = true;
                    EditorBuildSettings.scenes = existing;
                    return;
                }
            }
            var next = new EditorBuildSettingsScene[existing.Length + 1];
            next[0] = new EditorBuildSettingsScene(path, true);
            for (int i = 0; i < existing.Length; i++) next[i + 1] = existing[i];
            EditorBuildSettings.scenes = next;
        }
    }
}
