// EDITOR — lightweight HTTP API server for external tooling.
// See docs/editor-api.md for endpoint specification.
//
// Starts automatically when the Unity Editor loads via [InitializeOnLoad].
// Accepts connections on a background thread; all Unity API calls are
// dispatched to the main thread via EditorApplication.update so there are
// no thread-safety violations.
//
// Usage: no manual setup required. The server starts on port 6400
// (configurable via BJJ_EDITOR_API_PORT environment variable).
// Check the Console for "[BJJEditorApi] Listening on http://localhost:XXXX/"
// or an error if the port is already in use.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_INCLUDE_TESTS
using UnityEditor.TestTools.TestRunner.Api;
#endif

namespace BJJSimulator.EditorTools
{
    [InitializeOnLoad]
    public static class BJJEditorApiServer
    {
        private const string LogTag     = "[BJJEditorApi]";
        private const int    DefaultPort = 6400;

        private static HttpListener                              _listener;
        private static Thread                                    _acceptThread;
        private static readonly ConcurrentQueue<HttpListenerContext> _queue =
            new ConcurrentQueue<HttpListenerContext>();

#if UNITY_INCLUDE_TESTS
        private static string _lastTestJson  = "{\"status\":\"no_run\"}";
        private static bool   _testRunning   = false;
#endif

        // ------------------------------------------------------------------
        // Startup — called once per Editor domain reload
        // ------------------------------------------------------------------

        static BJJEditorApiServer()
        {
            // Guard against multiple registrations across domain reloads.
            EditorApplication.update   -= ProcessQueue;
            EditorApplication.update   += ProcessQueue;
            EditorApplication.quitting -= Stop;
            EditorApplication.quitting += Stop;

            TryStart();
        }

        private static void TryStart()
        {
            Stop(); // stop any previous instance

            int port = DefaultPort;
            var envPort = Environment.GetEnvironmentVariable("BJJ_EDITOR_API_PORT");
            if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var parsed))
                port = parsed;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();

                _acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name         = "BJJEditorApiAccept",
                };
                _acceptThread.Start();

                Debug.Log($"{LogTag} Listening on http://localhost:{port}/");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogTag} Could not start on port {port}: {ex.Message}");
            }
        }

        private static void Stop()
        {
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
        }

        // ------------------------------------------------------------------
        // Background accept loop — only network I/O here, no Unity APIs
        // ------------------------------------------------------------------

        private static void AcceptLoop()
        {
            while (_listener is { IsListening: true })
            {
                try
                {
                    var ctx = _listener.GetContext();
                    _queue.Enqueue(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        // ------------------------------------------------------------------
        // Main-thread dispatch
        // ------------------------------------------------------------------

        private static void ProcessQueue()
        {
            while (_queue.TryDequeue(out var ctx))
            {
                try   { Route(ctx); }
                catch (Exception ex)
                {
                    Debug.LogError($"{LogTag} Unhandled error for {ctx.Request.Url}: {ex}");
                    try { RespondError(ctx, 500, ex.Message); } catch { /* ignore */ }
                }
            }
        }

        private static void Route(HttpListenerContext ctx)
        {
            var method = ctx.Request.HttpMethod.ToUpperInvariant();
            var path   = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();

            switch ((method, path))
            {
                case ("GET",  "/status"):         OnGetStatus(ctx);        break;
                case ("POST", "/scene/load"):      OnPostSceneLoad(ctx);    break;
                case ("POST", "/playmode"):        OnPostPlayMode(ctx);     break;
                case ("GET",  "/tests/run"):       OnGetTestsRun(ctx);      break;
                case ("POST", "/asset/reimport"):  OnPostAssetReimport(ctx); break;
                default:
                    RespondJson(ctx, 404, "{\"error\":\"not found\"}");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // GET /status
        // ------------------------------------------------------------------

        private static void OnGetStatus(HttpListenerContext ctx)
        {
            var scene = SceneManager.GetActiveScene();
            RespondJson(ctx, 200,
                $"{{" +
                $"\"ok\":true," +
                $"\"unityVersion\":\"{J(Application.unityVersion)}\"," +
                $"\"activeScene\":\"{J(scene.path)}\"," +
                $"\"sceneName\":\"{J(scene.name)}\"," +
                $"\"isPlaying\":{B(EditorApplication.isPlaying)}," +
                $"\"isPaused\":{B(EditorApplication.isPaused)}" +
                $"}}");
        }

        // ------------------------------------------------------------------
        // POST /scene/load   body: {"scene":"Assets/Foo.unity"}
        // ------------------------------------------------------------------

        private static void OnPostSceneLoad(HttpListenerContext ctx)
        {
            var body  = ReadBody(ctx);
            var scene = ParseString(body, "scene");
            if (string.IsNullOrEmpty(scene)) { RespondError(ctx, 400, "missing 'scene'"); return; }
            if (EditorApplication.isPlaying)  { RespondError(ctx, 409, "exit play mode first"); return; }

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            RespondJson(ctx, 200, $"{{\"ok\":true,\"loaded\":\"{J(scene)}\"}}");
        }

        // ------------------------------------------------------------------
        // POST /playmode   body: {"action":"enter"|"exit"|"pause"}
        // ------------------------------------------------------------------

        private static void OnPostPlayMode(HttpListenerContext ctx)
        {
            var action = ParseString(ReadBody(ctx), "action")?.ToLowerInvariant();
            switch (action)
            {
                case "enter": EditorApplication.isPlaying = true;  break;
                case "exit":  EditorApplication.isPlaying = false; break;
                case "pause": EditorApplication.isPaused  = !EditorApplication.isPaused; break;
                default: RespondError(ctx, 400, "action must be enter|exit|pause"); return;
            }
            RespondJson(ctx, 200, $"{{\"ok\":true,\"action\":\"{J(action)}\"}}");
        }

        // ------------------------------------------------------------------
        // GET /tests/run[?mode=EditMode|PlayMode]
        // ------------------------------------------------------------------

        private static void OnGetTestsRun(HttpListenerContext ctx)
        {
#if UNITY_INCLUDE_TESTS
            if (_testRunning)
            {
                RespondJson(ctx, 200, "{\"status\":\"running\"}");
                return;
            }

            // Parse optional ?mode= query param (default EditMode).
            var modeStr = ctx.Request.QueryString["mode"] ?? "EditMode";
            var mode    = modeStr.Equals("PlayMode", StringComparison.OrdinalIgnoreCase)
                ? TestMode.PlayMode
                : TestMode.EditMode;

            _testRunning  = true;
            _lastTestJson = "{\"status\":\"running\"}";

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new TestCallbacks(api, results =>
            {
                _lastTestJson = BuildTestJson(results);
                _testRunning  = false;
            }));
            api.Execute(new ExecutionSettings(new Filter { testMode = mode }));

            // Respond immediately with 202; client polls GET /tests/run for results.
            RespondJson(ctx, 202,
                $"{{\"status\":\"started\",\"mode\":\"{J(modeStr)}\"," +
                $"\"poll\":\"GET /tests/run\"}}");
#else
            RespondJson(ctx, 501,
                "{\"error\":\"UNITY_INCLUDE_TESTS not defined — test framework unavailable\"}");
#endif
        }

        // ------------------------------------------------------------------
        // POST /asset/reimport   body: {"path":"Assets/..."}
        // ------------------------------------------------------------------

        private static void OnPostAssetReimport(HttpListenerContext ctx)
        {
            var path = ParseString(ReadBody(ctx), "path");
            if (string.IsNullOrEmpty(path)) { RespondError(ctx, 400, "missing 'path'"); return; }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            RespondJson(ctx, 200, $"{{\"ok\":true,\"reimported\":\"{J(path)}\"}}");
        }

        // ------------------------------------------------------------------
        // Test result helpers
        // ------------------------------------------------------------------

#if UNITY_INCLUDE_TESTS
        private static string BuildTestJson(ITestResultAdaptor root)
        {
            var sb  = new StringBuilder("{\"status\":\"complete\",\"results\":[");
            bool first = true;
            AppendLeaves(sb, root, ref first);
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendLeaves(StringBuilder sb, ITestResultAdaptor node, ref bool first)
        {
            if (node.HasChildren)
            {
                foreach (var child in node.Children)
                    AppendLeaves(sb, child, ref first);
                return;
            }
            if (!first) sb.Append(',');
            first = false;
            sb.Append(
                $"{{\"name\":\"{J(node.Test.FullName)}\"," +
                $"\"result\":\"{J(node.ResultState)}\"," +
                $"\"message\":\"{J(node.Message)}\"," +
                $"\"duration\":{node.Duration:0.000}}}");
        }

        private sealed class TestCallbacks : ICallbacks
        {
            private readonly TestRunnerApi                        _api;
            private readonly Action<ITestResultAdaptor>          _done;
            public TestCallbacks(TestRunnerApi api, Action<ITestResultAdaptor> done)
            { _api = api; _done = done; }

            public void RunStarted(ITestAdaptor testsToRun)    { }
            public void RunFinished(ITestResultAdaptor result)
            {
                _done(result);
                UnityEngine.Object.DestroyImmediate(_api);
            }
            public void TestStarted(ITestAdaptor test)         { }
            public void TestFinished(ITestResultAdaptor result){ }
        }
#endif

        // ------------------------------------------------------------------
        // HTTP / JSON helpers
        // ------------------------------------------------------------------

        private static void RespondJson(HttpListenerContext ctx, int code, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode      = code;
            ctx.Response.ContentType     = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void RespondError(HttpListenerContext ctx, int code, string msg)
            => RespondJson(ctx, code, $"{{\"error\":\"{J(msg)}\"}}");

        private static string ReadBody(HttpListenerContext ctx)
        {
            using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        // Minimal string-field extractor: finds "key":"value" in JSON.
        // Not a full JSON parser — sufficient for the simple request bodies used here.
        private static string ParseString(string json, string key)
        {
            var needle = $"\"{key}\":\"";
            var si = json.IndexOf(needle, StringComparison.Ordinal);
            if (si < 0) return null;
            si += needle.Length;
            var ei = json.IndexOf('"', si);
            return ei < 0 ? null : json.Substring(si, ei - si);
        }

        // JSON-escape a string value (handles the common characters).
        private static string J(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                     .Replace("\r", "").Replace("\n", "\\n");

        private static string B(bool b) => b ? "true" : "false";
    }
}
