// EDITOR — headless pose screenshots for the BlockMan rig (BJJPoseRig).
//
// Renders each BJJ scenario from a couple of camera angles to PNG so the
// procedural pose synthesis (BJJPose.ComputeScenePoses) can be eyeballed
// without opening the Editor GUI. Runs in batch mode:
//
//   Unity -batchmode -projectPath . \
//     -executeMethod BJJSimulator.EditorTools.BJJPoseCapture.CaptureAll \
//     -bjjShotDir /tmp/bjj_shots -quit
//
// (Do NOT pass -nographics — rendering needs a graphics device.)
//
// The rig is BJJPoseRig.ApplyImmediate'd onto each Scenarios.Build state, so
// the captured pose is the settled target (springs jump straight to it).

using System.IO;
using BJJSimulator;
using BJJSimulator.Platform;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace BJJSimulator.EditorTools
{
    public static class BJJPoseCapture
    {
        const int W = 1000;
        const int H = 760;

        struct Shot { public string Tag; public Vector3 Pos; public Vector3 Look; }

        static readonly Shot[] Angles =
        {
            new Shot { Tag = "guard", Pos = new Vector3(0f, 1.25f, 1.9f),  Look = new Vector3(0f, 0.5f, -0.5f) },
            new Shot { Tag = "side",  Pos = new Vector3(1.9f, 1.0f, 0.5f), Look = new Vector3(0f, 0.4f, -0.35f) },
            new Shot { Tag = "top",   Pos = new Vector3(0f, 2.4f, -0.3f),  Look = new Vector3(0f, 0.25f, -0.3f) },
        };

        [MenuItem("BJJ/Capture Poses")]
        public static void CaptureAll()
        {
            string dir = ArgValue("-bjjShotDir") ?? "/tmp/bjj_shots";
            Directory.CreateDirectory(dir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting so the BlockMan reads as 3-D.
            var lightGo = new GameObject("Capture Light");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;

            var ambGo = new GameObject("Fill Light");
            SceneManager.MoveGameObjectToScene(ambGo, scene);
            ambGo.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            var fill = ambGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.4f;

            // Ground plane for scale reference.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.localScale = Vector3.one * 2f;
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            // Camera.
            var camGo = new GameObject("Capture Camera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.10f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;

            // Rig (BJJGameManager etc are auto-added by RequireComponent but stay
            // dormant — their Awake never runs in edit mode).
            var rigGo = new GameObject("Capture Rig");
            SceneManager.MoveGameObjectToScene(rigGo, scene);
            var rig = rigGo.AddComponent<BJJPoseRig>();

            int count = 0;
            foreach (ScenarioName name in System.Enum.GetValues(typeof(ScenarioName)))
            {
                GameState g = Scenarios.Build(name, 0L);
                rig.ApplyImmediate(g, null, null, 1200f);

                foreach (var shot in Angles)
                {
                    camGo.transform.position = shot.Pos;
                    camGo.transform.LookAt(shot.Look);
                    string path = Path.Combine(dir, $"{(int)name:D2}_{name}_{shot.Tag}.png");
                    Render(cam, path);
                    count++;
                }
                Debug.Log($"[BJJPoseCapture] captured {name}");
            }

            Debug.Log($"[BJJPoseCapture] wrote {count} screenshots to {dir}");
        }

        static void Render(Camera cam, string path)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            var prevActive = RenderTexture.active;
            var prevTarget = cam.targetTexture;

            cam.targetTexture = rt;

            bool rendered = false;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req))
            {
                cam.SubmitRenderRequest(req);
                rendered = true;
            }
            if (!rendered)
            {
#pragma warning disable CS0618
                cam.Render();
#pragma warning restore CS0618
            }

            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        static string ArgValue(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
    }
}
