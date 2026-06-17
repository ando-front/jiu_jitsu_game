// EDITOR — One-click scene assembly for the BJJ Stage 2 prototype.
//
// Replaces the manual 6-step procedure documented in
// src/prototype/unity/README.md §"Create the BJJ scene". Open Unity, then
// pick BJJ → Setup Scene from the menu bar to:
//
//   1.  Ensure the BJJ_URP scripting define symbol is set (enables URP post-process
//       code in the Platform scripts).
//   2.  Create (or replace) Assets/Scenes/BJJ.unity from the Empty template.
//   3.  Spawn "BJJ_GameManager" with all Platform MonoBehaviours:
//         BJJSessionLifecycle, BJJInputProvider, BJJGameManager, BJJDebugHud (disabled),
//         BJJVolumeController, BJJImpactFeedback, BJJAvatarBinder,
//         UIDocument (UI Toolkit HUD), BJJHud.
//   4.  Wire BJJInputProvider.actionsAsset → BJJInputActions.inputactions.
//   5.  Wire BJJGameManager.hud → BJJDebugHud (kept for compatibility, disabled).
//   6.  Create a URP Volume Profile (WhiteBalance, Vignette, ColorAdjustments,
//       ChromaticAberration) and a Global Volume GameObject; wire both
//       BJJVolumeController.globalVolume and BJJImpactFeedback.globalVolume.
//   7.  Add a Main Camera; wire BJJImpactFeedback.mainCamera.
//   8.  Spawn a minimal blockman rig (Capsule + Sphere primitives) for Bottom
//       and Top; wire all BJJAvatarBinder Transform / Renderer slots.
//   9.  Create PanelSettings + wire UIDocument with BJJHud.uxml.
//  10.  Save and add the scene to Build Settings (index 0).
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
using UnityEngine.UIElements;

namespace BJJSimulator.EditorTools
{
    public static class BJJSceneSetup
    {
        private const string ScenesFolder        = "Assets/Scenes";
        private const string ScenePath           = "Assets/Scenes/BJJ.unity";
        private const string GameManagerName     = "BJJ_GameManager";
        private const string InputActionsPath    =
            "Assets/BJJSimulator/Runtime/Input/BJJInputActions.inputactions";
        private const string RenderFolder        = "Assets/BJJSimulator/Runtime/Render";
        private const string VolumeProfilePath   =
            "Assets/BJJSimulator/Runtime/Render/BJJVolumeProfile.asset";
        private const string UiFolder            = "Assets/BJJSimulator/Runtime/UI";
        private const string HudUxmlPath         =
            "Assets/BJJSimulator/Runtime/UI/BJJHud.uxml";
        private const string PanelSettingsPath   =
            "Assets/BJJSimulator/Runtime/UI/BJJHudPanelSettings.asset";

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
            // BJJPoseRig builds its own full BlockMan skeleton procedurally and
            // drives it from BJJPose.ComputeScenePoses — no Inspector wiring.
            go.AddComponent<BJJPoseRig>();

            // NOTE: actionsAsset assignment is deferred to after all AssetDatabase
            // operations to avoid the reference being invalidated by SaveAssets().
            // See §6 (below) for the deferred assignment.
            AssignSerialized(manager,  "hud",          hud);

            // ── 1b. UI Toolkit HUD (BJJHud) ──────────────────────────────────────
            EnsureFolder(UiFolder);

            // Create a fresh PanelSettings so the UIDocument renders correctly.
            AssetDatabase.DeleteAsset(PanelSettingsPath);
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            // ConstantPixelSize + scale 1: clean 1:1 pixel mapping for the debug overlay.
            panelSettings.scaleMode  = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale      = 1f;
            AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            AssetDatabase.SaveAssets();

            var hudUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            if (hudUxml == null)
            {
                Debug.LogWarning(
                    $"[BJJSceneSetup] BJJHud.uxml not found at {HudUxmlPath}. " +
                    "UI Toolkit HUD will be missing; run Setup Scene again after import completes.");
            }
            else
            {
                var uiDoc = go.AddComponent<UIDocument>();
                uiDoc.panelSettings   = panelSettings;
                uiDoc.visualTreeAsset = hudUxml;
                EditorUtility.SetDirty(uiDoc);
                go.AddComponent<BJJHud>();
                // BJJHud (UI Toolkit) is now the primary HUD; disable the legacy IMGUI overlay.
                hud.enabled = false;
                Debug.Log("[BJJSceneSetup] BJJHud UI Toolkit wired. BJJDebugHud disabled.");
            }

            // ── 2. Main Camera ──────────────────────────────────────────────────
            var camGo = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            camGo.tag = "MainCamera";
            // Guard-side framing (mirrors blockman.ts): supine player in the
            // foreground at the origin, kneeling opponent behind at z ≈ −0.5.
            camGo.transform.position = new Vector3(0f, 1.25f, 1.9f);
            camGo.transform.LookAt(new Vector3(0f, 0.5f, -0.5f));
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

            // ── 5. Rig ───────────────────────────────────────────────────────────
            // The full BlockMan skeleton is built procedurally at runtime by
            // BJJPoseRig (added to the GameManager GameObject above); nothing to
            // wire here.

            // ── 6. Save & build settings ─────────────────────────────────────────
            // Deferred assignment: reload inputActions from AssetDatabase now that all
            // AssetDatabase.SaveAssets() calls are done, to avoid stale references.
            AssetDatabase.Refresh();
            var freshInputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (freshInputActions != null)
                AssignSerialized(provider, "actionsAsset", freshInputActions);
            else
                Debug.LogWarning("[BJJSceneSetup] Could not re-load InputActions for final assignment.");

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

        /// <summary>
        /// Patch the open BJJ scene: assign BJJInputActions.inputactions to the
        /// BJJInputProvider component without rebuilding the whole scene.
        /// </summary>
        [MenuItem("BJJ/Fix ActionsAsset", priority = 5)]
        public static void FixActionsAsset()
        {
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
            {
                EditorUtility.DisplayDialog("Fix ActionsAsset",
                    $"BJJInputActions not found at:\n{InputActionsPath}", "OK");
                return;
            }

            // Find the provider in the open scene.
            var provider = Object.FindAnyObjectByType<BJJInputProvider>();
            if (provider == null)
            {
                EditorUtility.DisplayDialog("Fix ActionsAsset",
                    "BJJInputProvider not found in the open scene.\n" +
                    "Make sure BJJ.unity is open.", "OK");
                return;
            }

            // Record undo and set dirty so Unity serialises the reference.
            UnityEditor.Undo.RecordObject(provider, "Fix actionsAsset");
            var so   = new SerializedObject(provider);
            var prop = so.FindProperty("actionsAsset");
            prop.objectReferenceValue = inputActions;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(provider);
            EditorSceneManager.MarkSceneDirty(provider.gameObject.scene);

            // Save.
            bool ok = EditorSceneManager.SaveScene(
                provider.gameObject.scene, ScenePath);

            string guid = AssetDatabase.AssetPathToGUID(InputActionsPath);
            long   fid  = 0;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                inputActions, out string _, out fid);

            Debug.Log($"[FixActionsAsset] assigned {inputActions.name}  " +
                      $"guid={guid}  fileID={fid}  saved={ok}");

            EditorUtility.DisplayDialog("Fix ActionsAsset",
                $"actionsAsset assigned to {inputActions.name}.\n" +
                $"fileID={fid}  guid={guid}\nScene saved: {ok}", "OK");
        }

        // ────────────────────────────────────────────────────────────────────────
        // Runtime debug helpers (Play mode only)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// During Play mode: skip the role-select Prompt screen and jump
        /// directly to the Active (gameplay) phase. Equivalent to pressing
        /// Space in-game but works without game-view OS keyboard focus.
        /// </summary>
        [MenuItem("BJJ/Debug/Start Gameplay _F5", priority = 20)]
        public static void DebugStartGameplay()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Debug: Start Gameplay",
                    "This item only works in Play mode.\n\nPress ▶ first.", "OK");
                return;
            }

            var mgr = Object.FindAnyObjectByType<BJJGameManager>();
            if (mgr == null)
            {
                Debug.LogError("[Debug] BJJGameManager not found in open scene.");
                return;
            }

            if (mgr.Lifecycle.CurrentPhase != LifecyclePhase.Prompt)
            {
                Debug.Log($"[Debug] DismissPrompt skipped — phase is already {mgr.Lifecycle.CurrentPhase}");
                return;
            }

            mgr.Lifecycle.DismissPrompt();
            Debug.Log("[Debug] DismissPrompt called → phase Active");
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
            Undo.RecordObject(component, $"Assign {fieldName}");
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
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
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
