# BJJ Simulator — Unity Editor HTTP API

`BJJEditorApiServer.cs` (`Assets/BJJSimulator/Editor/`) starts an HTTP server
automatically when the Unity Editor loads.  All endpoints are JSON-over-HTTP on
`http://localhost:6400` (default).

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `BJJ_EDITOR_API_PORT` | `6400` | Override the listen port (set before launching Unity) |

## Endpoints

### `GET /status`

Returns current Editor state.

**Response 200**
```json
{
  "ok": true,
  "unityVersion": "6000.0.23f1",
  "activeScene": "Assets/Scenes/BJJSimulator.unity",
  "sceneName": "BJJSimulator",
  "isPlaying": false,
  "isPaused": false
}
```

---

### `POST /scene/load`

Opens a scene by asset path. Fails if the Editor is in Play Mode.

**Request body**
```json
{ "scene": "Assets/Scenes/BJJSimulator.unity" }
```

**Response 200**
```json
{ "ok": true, "loaded": "Assets/Scenes/BJJSimulator.unity" }
```

**Error 400** — missing `scene` field  
**Error 409** — Editor is currently in Play Mode

---

### `POST /playmode`

Controls Editor Play Mode.

**Request body**
```json
{ "action": "enter" }
```

| `action` | Effect |
|---|---|
| `"enter"` | `EditorApplication.isPlaying = true` |
| `"exit"` | `EditorApplication.isPlaying = false` |
| `"pause"` | Toggles `EditorApplication.isPaused` |

**Response 200**
```json
{ "ok": true, "action": "enter" }
```

**Error 400** — unknown `action` value

---

### `GET /tests/run[?mode=EditMode|PlayMode]`

Triggers the Unity Test Runner and streams back results.  Because test
execution is asynchronous, the server responds immediately with `202 Accepted`
and the client should poll `GET /tests/run` until `status` is `"complete"`.

| Query param | Default | Values |
|---|---|---|
| `mode` | `EditMode` | `EditMode`, `PlayMode` |

**Response 202** — run started
```json
{ "status": "started", "mode": "EditMode", "poll": "GET /tests/run" }
```

**Response 200** — run in progress (polling)
```json
{ "status": "running" }
```

**Response 200** — run complete (polling)
```json
{
  "status": "complete",
  "results": [
    {
      "name": "BJJSimulator.Tests.HandFSMTest.Idle_To_Reaching",
      "result": "Passed",
      "message": "",
      "duration": 0.001
    }
  ]
}
```

**Response 200** — no run yet
```json
{ "status": "no_run" }
```

**Response 501** — `UNITY_INCLUDE_TESTS` symbol not defined (test framework absent)

---

### `POST /asset/reimport`

Forces Unity to reimport an asset by path (calls `AssetDatabase.ImportAsset`
with `ImportAssetOptions.ForceUpdate`).

**Request body**
```json
{ "path": "Assets/BJJSimulator/Runtime/Input/BJJInputActions.inputactions" }
```

**Response 200**
```json
{ "ok": true, "reimported": "Assets/BJJSimulator/Runtime/Input/BJJInputActions.inputactions" }
```

**Error 400** — missing `path` field

---

## Common error shape

All error responses use a consistent body:
```json
{ "error": "description of the problem" }
```

## CORS

All responses include `Access-Control-Allow-Origin: *` so the API is
callable from browser-based tools (e.g. claude.ai web app) without a proxy.

## Security note

The server binds to `localhost` only and is intended for local development.
Do not expose port 6400 through a firewall or reverse proxy.
