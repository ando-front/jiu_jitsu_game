// EDITOR — One-click scene assembly for the BJJ Stage 2 prototype.
//
// Replaces the manual 6-step procedure documented in
// src/prototype/unity/README.md §"Create the BJJ scene". Open Unity, then
// pick BJJ → Setup Scene from the menu bar to:
//
//   1. Create (or replace) Assets/Scenes/BJJ.unity from the Empty template.
//   2. Spawn a "BJJ_GameManager" GameObject with the four Platform
//      MonoBehaviours attached in [RequireComponent] order:
//        BJJSessionLifecycle → BJJInputProvider → BJJGameManager → BJJDebugHud
//   3. Wire BJJInputProvider.actionsAsset to BJJInputActions.inputactions.
//   4. Wire BJJGameManager.hud to the BJJDebugHud on the same GameObject.
//   5. Save and add the scene to Build Settings (index 0).
//
// Idempotent: running the menu item again wipes any existing BJJ.unity scene
// and rebuilds from scratch, so a botched manual edit can always be reset.

using System.IO;
using BJJSimulator.Platform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
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

        [MenuItem("BJJ/Setup Scene", priority = 0)]
        public static void SetupScene()
        {
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

            var go = new GameObject(GameManagerName);
            SceneManager.MoveGameObjectToScene(go, scene);

            go.AddComponent<BJJSessionLifecycle>();
            var provider = go.AddComponent<BJJInputProvider>();
            var manager  = go.AddComponent<BJJGameManager>();
            var hud      = go.AddComponent<BJJDebugHud>();

            AssignSerialized(provider, "actionsAsset", inputActions);
            AssignSerialized(manager,  "hud",          hud);

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
                $"Scene built at {ScenePath}.\n\nPress ▶ to play.",
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

        // ---------------------------------------------------------------------

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
            var so = new SerializedObject(component);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError(
                    $"BJJSceneSetup: field '{fieldName}' not found on {component.GetType().Name}. " +
                    "Did the field name change?");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var existing = EditorBuildSettings.scenes;
            foreach (var s in existing)
            {
                if (s.path == path)
                {
                    // Already in list — make sure it's enabled and stays at front.
                    s.enabled = true;
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
