// EDITOR — quick scenario launcher menu.
//
// Adds `BJJ → Run Scenario → ...` menu items so practice scenarios can be
// loaded without leaving the Editor for a digit-key chord. Mirrors the
// Digit0–7 keyboard wiring in src/prototype/web/src/main.ts §144 / Stage 2
// BJJGameManager.HandleDigitEdges, but reachable while Play mode focus is
// outside the Game view (e.g. when stepping through Animator transitions).
//
// Each item is gated to Play mode and locates the scene's BJJSessionLifecycle
// at invocation time — running the menu without an Active session is a no-op.

using UnityEditor;
using UnityEngine;
using BJJSimulator;
using BJJSimulator.Platform;

namespace BJJSimulator.EditorTools
{
    public static class BJJScenarioMenu
    {
        // Order matches Stage 1 SCENARIO_ORDER and Stage 2 ScenarioName enum
        // (Scenarios.cs). Keep aligned with HandleDigitEdges' (ScenarioName)(d - 1)
        // cast — index N here corresponds to Digit(N+1) on the keyboard.
        private const string Root = "BJJ/Run Scenario/";

        [MenuItem(Root + "1: ScissorReady",     priority = 100)] private static void S1() => Load(ScenarioName.ScissorReady);
        [MenuItem(Root + "2: FlowerReady",      priority = 101)] private static void S2() => Load(ScenarioName.FlowerReady);
        [MenuItem(Root + "3: TriangleReady",    priority = 102)] private static void S3() => Load(ScenarioName.TriangleReady);
        [MenuItem(Root + "4: OmoplataReady",    priority = 103)] private static void S4() => Load(ScenarioName.OmoplataReady);
        [MenuItem(Root + "5: HipBumpReady",     priority = 104)] private static void S5() => Load(ScenarioName.HipBumpReady);
        [MenuItem(Root + "6: CrossCollarReady", priority = 105)] private static void S6() => Load(ScenarioName.CrossCollarReady);
        [MenuItem(Root + "7: PassDefense",      priority = 106)] private static void S7() => Load(ScenarioName.PassDefense);

        [MenuItem(Root + "0: Restart Neutral",  priority = 120)]
        private static void Restart()
        {
            if (!EnsurePlaying()) return;
            var lifecycle = FindLifecycle();
            if (lifecycle == null) return;
            lifecycle.RestartSession();
        }

        // Validators — grey out the menu items unless the editor is in Play
        // mode AND a BJJSessionLifecycle exists in the active scene. Without
        // these, click would silently no-op and the user would think the
        // wiring is broken.
        [MenuItem(Root + "1: ScissorReady",     validate = true)] private static bool V1() => CanLoad();
        [MenuItem(Root + "2: FlowerReady",      validate = true)] private static bool V2() => CanLoad();
        [MenuItem(Root + "3: TriangleReady",    validate = true)] private static bool V3() => CanLoad();
        [MenuItem(Root + "4: OmoplataReady",    validate = true)] private static bool V4() => CanLoad();
        [MenuItem(Root + "5: HipBumpReady",     validate = true)] private static bool V5() => CanLoad();
        [MenuItem(Root + "6: CrossCollarReady", validate = true)] private static bool V6() => CanLoad();
        [MenuItem(Root + "7: PassDefense",      validate = true)] private static bool V7() => CanLoad();
        [MenuItem(Root + "0: Restart Neutral",  validate = true)] private static bool V0() => CanLoad();

        // ---------------------------------------------------------------------

        private static void Load(ScenarioName name)
        {
            if (!EnsurePlaying()) return;
            var lifecycle = FindLifecycle();
            if (lifecycle == null) return;
            lifecycle.LoadScenario(name);
        }

        private static bool CanLoad() => EditorApplication.isPlaying && FindLifecycle() != null;

        private static bool EnsurePlaying()
        {
            if (EditorApplication.isPlaying) return true;
            EditorUtility.DisplayDialog(
                "BJJ Run Scenario",
                "Enter Play mode first — scenario loaders only work while the sim is running.",
                "OK");
            return false;
        }

        private static BJJSessionLifecycle FindLifecycle() =>
            Object.FindFirstObjectByType<BJJSessionLifecycle>();
    }
}
