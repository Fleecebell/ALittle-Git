#if UNITY_EDITOR
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.CSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EditorMcpBridge
{
    /// <summary>
    /// 一个极简的本地 HTTP/JSON-RPC 桥，把 Unity 编辑器的实时状态（层级、选中、控制台、
    /// 场景对象及其变量）以 JSON 形式暴露到 http://127.0.0.1:35353 。
    /// 编辑器打开时自动启动，供外部 AI / 脚本通过普通 HTTP GET 读取。
    /// </summary>
    [InitializeOnLoad]
    public static class EditorMcpBridgeServer
    {
        private const int Port = 35353;
        private const int MaxLogs = 500;

        private static TcpListener _listener;
        private static readonly Queue<Request> _pending = new Queue<Request>();
        private static readonly List<LogEntry> _logs = new List<LogEntry>();

        private class LogEntry
        {
            public string type;
            public string message;
            public string stackTrace;
            public string time;
        }

        private class Request
        {
            public string route;
            public Dictionary<string, string> query;
            public string body;
            public string method;
            public ManualResetEventSlim done = new ManualResetEventSlim(false);
            public string result;
        }

        static EditorMcpBridgeServer()
        {
            // 资源导入子进程（Unity 会为每个 project 起若干个 AssetImportWorker）也会加载编辑器脚本，
            // 如果它们也来 bind 35353，主编辑器就抢不到端口了 —— 表现是端口 LISTEN 着、
            // 但请求全部超时（因为子进程的主线程在忙着导入资源，永远轮不到处理请求）。
            // 注意：改这里之后必须让 Unity 重新编译一次（域重载）才会重新绑定端口。
            if (UnityEditor.AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            Application.logMessageReceived += OnLog;
            EditorApplication.update += OnUpdate;
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
            Start();
        }

        private static void OnDomainUnload(object sender, EventArgs e)
        {
            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (_logs)
            {
                _logs.Add(new LogEntry
                {
                    type = type.ToString(),
                    message = condition,
                    stackTrace = stackTrace ?? "",
                    time = EditorApplication.timeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)
                });
                if (_logs.Count > MaxLogs) _logs.RemoveAt(0);
            }
        }

        private static void OnUpdate()
        {
            Request req = null;
            lock (_pending)
            {
                if (_pending.Count > 0) req = _pending.Dequeue();
            }
            if (req == null) return;
            try
            {
                req.result = Process(req);
            }
            catch (Exception e)
            {
                req.result = Error("internal error: " + e.Message);
            }
            req.done.Set();
        }

        private static void Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                var thread = new Thread(ServerLoop) { IsBackground = true, Name = "EditorMcpBridge" };
                thread.Start();
                Debug.Log("[EditorMcpBridge] listening on http://127.0.0.1:" + Port);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EditorMcpBridge] failed to start: " + e.Message);
            }
        }

        private static void ServerLoop()
        {
            while (_listener != null)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    break;
                }
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }

        private static void HandleClient(object state)
        {
            var client = (TcpClient)state;
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    stream.ReadTimeout = 10000;

                    string head = ReadUntil(stream, "\r\n\r\n");
                    if (head == null) return;

                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;
                    var reqLine = lines[0].Split(' ');
                    if (reqLine.Length < 2) return;

                    string method = reqLine[0].ToUpperInvariant();
                    string pathAndQuery = reqLine[1];

                    int contentLength = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        int idx = line.IndexOf(':');
                        if (idx < 0) continue;
                        string key = line.Substring(0, idx).Trim();
                        string val = line.Substring(idx + 1).Trim();
                        if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(val, out contentLength);
                    }

                    string body = "";
                    if (contentLength > 0 && contentLength < 1024 * 1024)
                    {
                        var buf = new byte[contentLength];
                        int read = 0;
                        while (read < contentLength)
                        {
                            int r = stream.Read(buf, read, contentLength - read);
                            if (r <= 0) break;
                            read += r;
                        }
                        body = Encoding.UTF8.GetString(buf, 0, read);
                    }

                    if (method == "OPTIONS")
                    {
                        WriteResponse(stream, 200, "");
                        return;
                    }

                    int q = pathAndQuery.IndexOf('?');
                    string route = q < 0 ? pathAndQuery : pathAndQuery.Substring(0, q);
                    var query = q < 0 ? new Dictionary<string, string>() : ParseQuery(pathAndQuery.Substring(q + 1));
                    route = route.TrimStart('/');

                    var req = new Request { route = route, query = query, body = body, method = method };
                    lock (_pending) { _pending.Enqueue(req); }

                    if (!req.done.Wait(10000))
                    {
                        req.result = Error("timeout waiting for main thread");
                    }
                    WriteResponse(stream, 200, req.result);
                }
            }
            catch (Exception e)
            {
                try { WriteResponse(client.GetStream(), 500, Error(e.Message)); } catch { }
            }
        }

        private static void WriteResponse(NetworkStream stream, int status, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(status == 200 ? " OK" : " Error").Append("\r\n");
            sb.Append("Content-Type: application/json; charset=utf-8\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            var headBytes = Encoding.UTF8.GetBytes(sb.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        private static string ReadUntil(NetworkStream stream, string delimiter)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            int guard = 0;
            while (guard++ < 128 * 1024)
            {
                int r;
                try { r = stream.Read(one, 0, 1); }
                catch { return null; }
                if (r <= 0) return null;
                sb.Append((char)one[0]);
                if (sb.Length >= delimiter.Length &&
                    string.CompareOrdinal(sb.ToString(sb.Length - delimiter.Length, delimiter.Length), delimiter) == 0)
                    return sb.ToString();
            }
            return null;
        }

        private static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>();
            foreach (string pair in q.Split('&'))
            {
                if (string.IsNullOrEmpty(pair)) continue;
                int idx = pair.IndexOf('=');
                string k = idx < 0 ? pair : pair.Substring(0, idx);
                string v = idx < 0 ? "" : pair.Substring(idx + 1);
                d[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v);
            }
            return d;
        }

        // ------------------------------------------------------------------ routing

        private static string Process(Request req)
        {
            switch (req.route.ToLowerInvariant())
            {
                case "":
                case "health":
                case "ping":
                    return Health();
                case "help":
                    return Help();
                case "state":
                    return State();
                case "scene":
                    return SceneInfo();
                case "hierarchy":
                    return Hierarchy(GetDepth(req));
                case "selection":
                    return SelectionInfo();
                case "console":
                    return ConsoleLogs(req);
                case "object":
                    return ObjectDetail(req);
                case "find":
                    return Find(req);
                case "eval":
                case "exec":
                    return Eval(req);
                case "invoke":
                    return InvokeStatic(req);
                case "set_field":
                    return SetField(req);
                case "create_object":
                    return CreateObject(req);
                case "delete_object":
                    return DeleteObject(req);
                case "add_component":
                    return AddComponent(req);
                case "remove_component":
                    return RemoveComponent(req);
                case "set_parent":
                    return SetParent(req);
                case "set_active":
                    return SetActiveObject(req);
                case "set_transform":
                    return SetTransform(req);
                case "playmode":
                    return SetPlayMode(req);
                case "save_scene":
                    return SaveScene(req);
                case "refresh":
                    return RefreshAssets(req);
                case "run_menu":
                    return RunMenu(req);
                default:
                    return Error("unknown route: " + req.route + " (see /help)");
            }
        }

        private static int GetDepth(Request req)
        {
            int d = -1;
            if (req.query != null && req.query.TryGetValue("depth", out string ds))
                int.TryParse(ds, out d);
            return d;
        }

        // ------------------------------------------------------------------ endpoints

        private static string Health()
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"bridge\":\"EditorMcpBridge\"");
            sb.Append(",\"unity\":" + Quote(Application.unityVersion));
            sb.Append(",\"productName\":" + Quote(Application.productName));
            sb.Append(",\"isPlaying\":" + (EditorApplication.isPlaying ? "true" : "false"));
            sb.Append(",\"isPaused\":" + (EditorApplication.isPaused ? "true" : "false"));
            sb.Append(",\"isCompiling\":" + (EditorApplication.isCompiling ? "true" : "false"));
            sb.Append(",\"scene\":" + Quote(SceneManager.GetActiveScene().name));
            sb.Append("}");
            return sb.ToString();
        }

        private static string Help()
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"routes\":[");
            sb.Append(Quote("/health")).Append(",");
            sb.Append(Quote("/state")).Append(",");
            sb.Append(Quote("/scene")).Append(",");
            sb.Append(Quote("/hierarchy?depth=N")).Append(",");
            sb.Append(Quote("/selection")).Append(",");
            sb.Append(Quote("/console?clear=1")).Append(",");
            sb.Append(Quote("/object?id=INSTANCEID|name=NAME")).Append(",");
            sb.Append(Quote("/find?name=SUBSTRING")).Append(",");
            sb.Append(Quote("/eval?code=C#")).Append(",");
            sb.Append(Quote("/invoke?type=T&method=M&args=[...]")).Append(",");
            sb.Append(Quote("/set_field?id=&component=&path=&value=")).Append(",");
            sb.Append(Quote("/create_object?name=&parent=")).Append(",");
            sb.Append(Quote("/delete_object?id=")).Append(",");
            sb.Append(Quote("/add_component?id=&type=")).Append(",");
            sb.Append(Quote("/remove_component?id=&type=")).Append(",");
            sb.Append(Quote("/set_parent?id=&parent=")).Append(",");
            sb.Append(Quote("/set_active?id=&active=")).Append(",");
            sb.Append(Quote("/set_transform?id=&position=x,y,z")).Append(",");
            sb.Append(Quote("/playmode?mode=enter|exit|toggle|pause|unpause")).Append(",");
            sb.Append(Quote("/save_scene?all=1")).Append(",");
            sb.Append(Quote("/refresh")).Append(",");
            sb.Append(Quote("/run_menu?path=Menu/Path"));
            sb.Append("]}");
            return sb.ToString();
        }

        private static string State()
        {
            var sb = new StringBuilder();
            sb.Append("{\"isPlaying\":" + (EditorApplication.isPlaying ? "true" : "false"));
            sb.Append(",\"isPaused\":" + (EditorApplication.isPaused ? "true" : "false"));
            sb.Append(",\"isCompiling\":" + (EditorApplication.isCompiling ? "true" : "false"));
            sb.Append(",\"isUpdating\":" + (EditorApplication.isUpdating ? "true" : "false"));
            sb.Append(",\"playMode\":" + Quote(EditorApplication.isPlayingOrWillChangePlaymode ? "playing-or-will" : "edit"));
            sb.Append(",\"timeSinceStartup\":" + EditorApplication.timeSinceStartup.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append(",\"sceneCount\":" + SceneManager.sceneCount);
            sb.Append(",\"selectedCount\":" + Selection.objects.Length);
            lock (_logs) { sb.Append(",\"logCount\":" + _logs.Count); }
            sb.Append("}");
            return Ok(sb.ToString());
        }

        private static string SceneInfo()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.Append("{\"name\":" + Quote(scene.name));
            sb.Append(",\"path\":" + Quote(scene.path));
            sb.Append(",\"rootCount\":" + scene.rootCount);
            sb.Append(",\"isLoaded\":" + (scene.isLoaded ? "true" : "false"));
            sb.Append(",\"isDirty\":" + (scene.isDirty ? "true" : "false"));
            sb.Append(",\"buildIndex\":" + scene.buildIndex);
            sb.Append(",\"scenes\":[");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (i > 0) sb.Append(",");
                sb.Append("{\"name\":" + Quote(s.name) + ",\"path\":" + Quote(s.path) + ",\"rootCount\":" + s.rootCount + "}");
            }
            sb.Append("]}");
            return Ok(sb.ToString());
        }

        private static string Hierarchy(int maxDepth)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var roots = SceneManager.GetSceneAt(i).GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    AppendNode(sb, root, maxDepth, 0);
                }
            }
            sb.Append("]");
            return Ok(sb.ToString());
        }

        private static void AppendNode(StringBuilder sb, GameObject go, int maxDepth, int depth)
        {
            sb.Append("{");
            sb.Append("\"name\":" + Quote(go.name));
            sb.Append(",\"instanceId\":" + go.GetInstanceID());
            sb.Append(",\"activeSelf\":" + (go.activeSelf ? "true" : "false"));
            sb.Append(",\"activeInHierarchy\":" + (go.activeInHierarchy ? "true" : "false"));
            sb.Append(",\"layer\":" + go.layer);
            sb.Append(",\"components\":[");
            var comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Quote(comps[i] == null ? "MissingScript" : comps[i].GetType().Name));
            }
            sb.Append("]");
            sb.Append(",\"children\":[");
            if (maxDepth < 0 || depth < maxDepth)
            {
                int count = go.transform.childCount;
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendNode(sb, go.transform.GetChild(i).gameObject, maxDepth, depth + 1);
                }
            }
            sb.Append("]}");
        }

        private static string SelectionInfo()
        {
            var sb = new StringBuilder();
            sb.Append("{\"activeGameObject\":");
            sb.Append(Selection.activeGameObject == null ? "null" : Quote(Selection.activeGameObject.name));
            sb.Append(",\"activeObject\":");
            sb.Append(Selection.activeObject == null
                ? "null"
                : ("{\"type\":" + Quote(Selection.activeObject.GetType().Name) + ",\"name\":" + Quote(Selection.activeObject.name) + "}"));
            sb.Append(",\"gameObjects\":[");
            var gos = Selection.gameObjects;
            for (int i = 0; i < gos.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"name\":" + Quote(gos[i].name) + ",\"instanceId\":" + gos[i].GetInstanceID() + ",\"path\":" + Quote(GetPath(gos[i])) + "}");
            }
            sb.Append("],\"objects\":[");
            var objs = Selection.objects;
            bool first = true;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"type\":" + Quote(objs[i].GetType().Name) + ",\"name\":" + Quote(objs[i].name) + ",\"instanceId\":" + objs[i].GetInstanceID() + "}");
            }
            sb.Append("]}");
            return Ok(sb.ToString());
        }

        private static string ConsoleLogs(Request req)
        {
            bool clear = req.query != null && req.query.TryGetValue("clear", out string cv) && cv == "1";
            var sb = new StringBuilder();
            sb.Append("[");
            lock (_logs)
            {
                for (int i = 0; i < _logs.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    LogEntry l = _logs[i];
                    sb.Append("{\"type\":" + Quote(l.type) + ",\"time\":" + Quote(l.time) + ",\"message\":" + Quote(l.message) + ",\"stackTrace\":" + Quote(l.stackTrace) + "}");
                }
                if (clear) _logs.Clear();
            }
            sb.Append("]");
            return Ok(sb.ToString());
        }

        private static string ObjectDetail(Request req)
        {
            GameObject go = null;
            if (req.query != null)
            {
                if (req.query.TryGetValue("id", out string id) && int.TryParse(id, out int iid))
                {
                    UnityEngine.Object o = EditorUtility.InstanceIDToObject(iid);
                    if (o is GameObject g) go = g;
                    else if (o is Component comp) go = comp.gameObject;
                }
                if (go == null && req.query.TryGetValue("name", out string name) && !string.IsNullOrEmpty(name))
                    go = FindByName(name);
            }
            if (go == null) return Error("object not found");

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"name\":" + Quote(go.name));
            sb.Append(",\"instanceId\":" + go.GetInstanceID());
            sb.Append(",\"path\":" + Quote(GetPath(go)));
            sb.Append(",\"activeSelf\":" + (go.activeSelf ? "true" : "false"));
            sb.Append(",\"activeInHierarchy\":" + (go.activeInHierarchy ? "true" : "false"));
            sb.Append(",\"tag\":" + Quote(go.tag));
            sb.Append(",\"layer\":" + go.layer);
            sb.Append(",\"scene\":" + Quote(go.scene.name));
            Transform tr = go.transform;
            sb.Append(",\"transform\":{");
            sb.Append("\"position\":" + Vector3Json(tr.position));
            sb.Append(",\"localPosition\":" + Vector3Json(tr.localPosition));
            sb.Append(",\"eulerAngles\":" + Vector3Json(tr.rotation.eulerAngles));
            sb.Append(",\"localEulerAngles\":" + Vector3Json(tr.localRotation.eulerAngles));
            sb.Append(",\"lossyScale\":" + Vector3Json(tr.lossyScale));
            sb.Append(",\"childCount\":" + tr.childCount);
            sb.Append("}");
            sb.Append(",\"components\":[");
            var comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0) sb.Append(",");
                if (comps[i] == null) { sb.Append("{\"type\":\"MissingScript\"}"); continue; }
                sb.Append(ComponentJson(comps[i]));
            }
            sb.Append("]}");
            return Ok(sb.ToString());
        }

        private static string ComponentJson(Component c)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"type\":" + Quote(c.GetType().Name));
            sb.Append(",\"serialized\":[");
            SerializedFields(sb, c);
            sb.Append("],\"properties\":[");
            PublicProperties(sb, c);
            sb.Append("]}");
            return sb.ToString();
        }

        private static void SerializedFields(StringBuilder sb, Component c)
        {
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool first = true;
            int n = 0;
            while (it.NextVisible(true))
            {
                if (it.depth < 0) continue;
                if (n++ >= 300) break;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"name\":" + Quote(it.displayName));
                sb.Append(",\"path\":" + Quote(it.propertyPath));
                sb.Append(",\"type\":" + Quote(it.propertyType.ToString()));
                sb.Append(",\"value\":" + SerializedValue(it));
                sb.Append("}");
            }
        }

        private static string SerializedValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return p.floatValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return Quote(p.stringValue);
                case SerializedPropertyType.Color: return "{\"r\":" + F(p.colorValue.r) + ",\"g\":" + F(p.colorValue.g) + ",\"b\":" + F(p.colorValue.b) + ",\"a\":" + F(p.colorValue.a) + "}";
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null
                        ? "null"
                        : ("{\"type\":" + Quote(p.objectReferenceValue.GetType().Name) + ",\"name\":" + Quote(p.objectReferenceValue.name) + ",\"instanceId\":" + p.objectReferenceValue.GetInstanceID() + "}");
                case SerializedPropertyType.LayerMask: return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Enum:
                    return Quote(p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length ? p.enumDisplayNames[p.enumValueIndex] : p.enumValueIndex.ToString());
                case SerializedPropertyType.Vector2: return "{\"x\":" + F(p.vector2Value.x) + ",\"y\":" + F(p.vector2Value.y) + "}";
                case SerializedPropertyType.Vector3: return "{\"x\":" + F(p.vector3Value.x) + ",\"y\":" + F(p.vector3Value.y) + ",\"z\":" + F(p.vector3Value.z) + "}";
                case SerializedPropertyType.Vector4: return "{\"x\":" + F(p.vector4Value.x) + ",\"y\":" + F(p.vector4Value.y) + ",\"z\":" + F(p.vector4Value.z) + ",\"w\":" + F(p.vector4Value.w) + "}";
                case SerializedPropertyType.Rect: return "{\"x\":" + F(p.rectValue.x) + ",\"y\":" + F(p.rectValue.y) + ",\"w\":" + F(p.rectValue.width) + ",\"h\":" + F(p.rectValue.height) + "}";
                case SerializedPropertyType.Bounds: return "{\"center\":" + Vector3Json(p.boundsValue.center) + ",\"size\":" + Vector3Json(p.boundsValue.size) + "}";
                case SerializedPropertyType.Quaternion: return "{\"x\":" + F(p.quaternionValue.x) + ",\"y\":" + F(p.quaternionValue.y) + ",\"z\":" + F(p.quaternionValue.z) + ",\"w\":" + F(p.quaternionValue.w) + "}";
                case SerializedPropertyType.Vector2Int: return "{\"x\":" + p.vector2IntValue.x + ",\"y\":" + p.vector2IntValue.y + "}";
                case SerializedPropertyType.Vector3Int: return "{\"x\":" + p.vector3IntValue.x + ",\"y\":" + p.vector3IntValue.y + ",\"z\":" + p.vector3IntValue.z + "}";
                case SerializedPropertyType.RectInt: return "{\"x\":" + p.rectIntValue.x + ",\"y\":" + p.rectIntValue.y + ",\"w\":" + p.rectIntValue.width + ",\"h\":" + p.rectIntValue.height + "}";
                case SerializedPropertyType.BoundsInt: return "{\"position\":" + ToJsonValue(p.boundsIntValue.position, 0) + ",\"size\":" + ToJsonValue(p.boundsIntValue.size, 0) + "}";
                case SerializedPropertyType.Character: return Quote(p.stringValue);
                case SerializedPropertyType.ExposedReference: return p.exposedReferenceValue == null ? "null" : Quote(p.exposedReferenceValue.name);
                case SerializedPropertyType.FixedBufferSize: return p.fixedBufferSize.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Hash128: return Quote(p.hash128Value.ToString());
                default: return Quote(p.type);
            }
        }

        private static void PublicProperties(StringBuilder sb, Component c)
        {
            var props = c.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            bool first = true;
            foreach (var p in props)
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (!p.CanRead) continue;
                if (p.Name == "name" || p.Name == "transform" || p.Name == "gameObject" ||
                    p.Name == "tag" || p.Name == "hideFlags") continue;
                object val;
                try { val = p.GetValue(c, null); }
                catch { continue; }
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"name\":" + Quote(p.Name) + ",\"value\":" + ToJsonValue(val, 0) + "}");
            }
        }

        private static string Find(Request req)
        {
            string name = "";
            req.query?.TryGetValue("name", out name);
            if (string.IsNullOrEmpty(name)) return Error("missing 'name' parameter");

            var results = new StringBuilder();
            results.Append("[");
            bool first = true;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var roots = SceneManager.GetSceneAt(s).GetRootGameObjects();
                foreach (var root in roots)
                    CollectMatches(root, name, ref first, results);
            }
            results.Append("]");
            return Ok(results.ToString());
        }

        private static void CollectMatches(GameObject go, string name, ref bool first, StringBuilder results)
        {
            if (go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!first) results.Append(",");
                first = false;
                results.Append("{\"name\":" + Quote(go.name) + ",\"instanceId\":" + go.GetInstanceID() + ",\"path\":" + Quote(GetPath(go)) + "}");
            }
            for (int i = 0; i < go.transform.childCount; i++)
                CollectMatches(go.transform.GetChild(i).gameObject, name, ref first, results);
        }

        private static GameObject FindByName(string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var roots = SceneManager.GetSceneAt(i).GetRootGameObjects();
                foreach (var root in roots)
                {
                    GameObject found = FindInTree(root, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static GameObject FindInTree(GameObject go, string name)
        {
            if (string.Equals(go.name, name, StringComparison.Ordinal)) return go;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject found = FindInTree(go.transform.GetChild(i).gameObject, name);
                if (found != null) return found;
            }
            return null;
        }

        private static string GetPath(GameObject go)
        {
            var parts = new List<string>();
            Transform t = go.transform;
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return "/" + string.Join("/", parts);
        }

        // ------------------------------------------------------------------ JSON helpers

        private static string Ok(string data) => "{\"ok\":true,\"data\":" + data + "}";

        private static string Error(string msg) => "{\"ok\":false,\"error\":" + Quote(msg) + "}";

        private static string Vector3Json(Vector3 v) => "{\"x\":" + F(v.x) + ",\"y\":" + F(v.y) + ",\"z\":" + F(v.z) + "}";

        private static string F(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return Quote(f.ToString(CultureInfo.InvariantCulture));
            return f.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ToJsonValue(object val, int depth)
        {
            if (val == null) return "null";
            if (depth > 3) return Quote(SafeToString(val));

            if (val is string s) return Quote(s);
            if (val is bool b) return b ? "true" : "false";
            if (val is char ch) return Quote(ch.ToString());

            Type t = val.GetType();
            if (t.IsEnum) return Quote(val.ToString());

            if (val is UnityEngine.Object uo)
            {
                if (uo == null) return "null";
                return "{\"type\":" + Quote(uo.GetType().Name) + ",\"name\":" + Quote(uo.name) + ",\"instanceId\":" + uo.GetInstanceID() + "}";
            }

            if (val is float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) return Quote(f.ToString(CultureInfo.InvariantCulture));
                return f.ToString("R", CultureInfo.InvariantCulture);
            }
            if (val is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) return Quote(d.ToString(CultureInfo.InvariantCulture));
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
            if (val is decimal dec) return dec.ToString(CultureInfo.InvariantCulture);
            if (val is byte || val is sbyte || val is short || val is ushort ||
                val is int || val is uint || val is long || val is ulong)
                return Convert.ToString(val, CultureInfo.InvariantCulture);

            if (val is Vector2 v2) return "{\"x\":" + F(v2.x) + ",\"y\":" + F(v2.y) + "}";
            if (val is Vector3 v3) return "{\"x\":" + F(v3.x) + ",\"y\":" + F(v3.y) + ",\"z\":" + F(v3.z) + "}";
            if (val is Vector4 v4) return "{\"x\":" + F(v4.x) + ",\"y\":" + F(v4.y) + ",\"z\":" + F(v4.z) + ",\"w\":" + F(v4.w) + "}";
            if (val is Quaternion q) return "{\"x\":" + F(q.x) + ",\"y\":" + F(q.y) + ",\"z\":" + F(q.z) + ",\"w\":" + F(q.w) + "}";
            if (val is Color col) return "{\"r\":" + F(col.r) + ",\"g\":" + F(col.g) + ",\"b\":" + F(col.b) + ",\"a\":" + F(col.a) + "}";
            if (val is Color32 c32) return "{\"r\":" + c32.r + ",\"g\":" + c32.g + ",\"b\":" + c32.b + ",\"a\":" + c32.a + "}";
            if (val is Vector2Int v2i) return "{\"x\":" + v2i.x + ",\"y\":" + v2i.y + "}";
            if (val is Vector3Int v3i) return "{\"x\":" + v3i.x + ",\"y\":" + v3i.y + ",\"z\":" + v3i.z + "}";
            if (val is Rect r) return "{\"x\":" + F(r.x) + ",\"y\":" + F(r.y) + ",\"w\":" + F(r.width) + ",\"h\":" + F(r.height) + "}";
            if (val is Bounds bnd) return "{\"center\":" + ToJsonValue(bnd.center, depth + 1) + ",\"size\":" + ToJsonValue(bnd.size, depth + 1) + "}";

            if (val is System.Collections.IDictionary dict)
            {
                var sb = new StringBuilder("{");
                bool first = true;
                int n = 0;
                foreach (System.Collections.DictionaryEntry de in dict)
                {
                    if (n++ >= 50) break;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(Quote(SafeToString(de.Key))).Append(":").Append(ToJsonValue(de.Value, depth + 1));
                }
                sb.Append("}");
                return sb.ToString();
            }

            if (val is System.Collections.IEnumerable en)
            {
                var sb = new StringBuilder("[");
                bool first = true;
                int n = 0;
                foreach (var item in en)
                {
                    if (n++ >= 50) break;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(ToJsonValue(item, depth + 1));
                }
                sb.Append("]");
                return sb.ToString();
            }

            return Quote(SafeToString(val));
        }

        private static string SafeToString(object o)
        {
            try { return o == null ? "" : o.ToString(); }
            catch { return ""; }
        }

        private static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ======================================================================
        // control / write / execute endpoints
        // ======================================================================

        private static string Q(Request req, string key)
        {
            if (req.query != null && req.query.TryGetValue(key, out string v)) return v;
            return null;
        }

        private static float QFloat(Request req, string key, float def)
        {
            if (req.query != null && req.query.TryGetValue(key, out string v) &&
                float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return f;
            return def;
        }

        private static GameObject ResolveByAny(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (int.TryParse(key, out int iid))
            {
                var o = EditorUtility.InstanceIDToObject(iid);
                if (o is GameObject g) return g;
                if (o is Component c) return c.gameObject;
            }
            GameObject byName = FindByName(key);
            if (byName != null) return byName;
            return FindByPath(key);
        }

        private static GameObject ResolveObject(Request req)
        {
            if (req.query == null) return null;
            GameObject g = ResolveByAny(Q(req, "id"));
            if (g != null) return g;
            g = ResolveByAny(Q(req, "name"));
            if (g != null) return g;
            return ResolveByAny(Q(req, "path"));
        }

        private static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.TrimStart('/');
            string[] parts = path.Split('/');
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var roots = SceneManager.GetSceneAt(i).GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (root.name == parts[0])
                    {
                        if (parts.Length == 1) return root;
                        GameObject found = FindByPathFrom(root.transform, parts, 1);
                        if (found != null) return found;
                    }
                }
            }
            return null;
        }

        private static GameObject FindByPathFrom(Transform t, string[] parts, int idx)
        {
            if (idx >= parts.Length) return t.gameObject;
            foreach (Transform child in t)
            {
                if (child.name == parts[idx])
                {
                    GameObject found = FindByPathFrom(child, parts, idx + 1);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static Type FindType(string name)
        {
            Type t = Type.GetType(name);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(name, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        private static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Type t = FindType(name);
            if (t != null) return t;
            t = FindType("UnityEngine." + name);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var tp in asm.GetTypes())
                        if (tp.Name == name) return tp;
                }
                catch { }
            }
            return null;
        }

        private static UnityEngine.Object ResolveAssetOrObject(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (int.TryParse(value, out int iid))
            {
                var o = EditorUtility.InstanceIDToObject(iid);
                if (o != null) return o;
            }
            var guids = AssetDatabase.FindAssets(value + " t:Object");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var o = AssetDatabase.LoadMainAssetAtPath(p);
                if (o != null && o.name == value) return o;
            }
            return FindByName(value);
        }

        // ---------------------------------------------------------------- eval

        private static string Eval(Request req)
        {
            string code = Q(req, "code");
            if (string.IsNullOrEmpty(code)) code = req.body;
            if (string.IsNullOrEmpty(code)) return Error("missing 'code' param or POST body");

            var logs = new List<string>();
            Application.LogCallback cb = (cond, st, type) => { lock (logs) logs.Add("[" + type + "] " + cond); };
            Application.logMessageReceived += cb;
            try
            {
                object result = RunCSharp(code);
                var sb = new StringBuilder();
                sb.Append("{\"result\":");
                sb.Append(result == null ? "null" : Quote(result.ToString()));
                sb.Append(",\"logs\":[");
                lock (logs)
                {
                    for (int i = 0; i < logs.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append(Quote(logs[i]));
                    }
                }
                sb.Append("]}");
                return Ok(sb.ToString());
            }
            catch (Exception e)
            {
                string inner = e.InnerException != null ? e.InnerException.Message : "";
                string msg = e.Message + (inner.Length > 0 ? " | inner: " + inner : "");
                if (msg.IndexOf("CS0012", StringComparison.Ordinal) >= 0 ||
                    msg.IndexOf("CS0433", StringComparison.Ordinal) >= 0 ||
                    msg.IndexOf("netstandard", StringComparison.OrdinalIgnoreCase) >= 0)
                    msg += " [hint: /eval cannot use UnityEngine/UnityEditor types reliably in this .NET Standard 2.1 project — use /invoke or the write endpoints instead]";
                return Error("exec error: " + msg);
            }
            finally
            {
                Application.logMessageReceived -= cb;
            }
        }

        private static object RunCSharp(string code)
        {
            CSharpCodeProvider provider;
            try { provider = new CSharpCodeProvider(); }
            catch (Exception e) { throw new Exception("CSharpCodeProvider unavailable (" + e.Message + "); use /invoke or write scripts to disk"); }

            var parameters = new CompilerParameters();
            parameters.GenerateInMemory = true;
            parameters.GenerateExecutable = false;
            // Reference a curated set: core libs + netstandard + engine/editor/user assemblies.
            // This keeps the compiler command line short (avoids "filename too long") while letting
            // eval use UnityEngine/UnityEditor APIs. Generic collections (List/Stack/Dictionary)
            // may still hit CS0433 because of mscorlib vs netstandard facades — use /invoke then.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    string loc = asm.Location;
                    if (string.IsNullOrEmpty(loc)) continue;
                    string name = asm.GetName().Name;
                    if (name != "mscorlib" && name != "System" && name != "System.Core" && name != "netstandard" &&
                        !name.StartsWith("UnityEngine", StringComparison.Ordinal) &&
                        !name.StartsWith("UnityEditor", StringComparison.Ordinal) &&
                        !name.StartsWith("Unity.", StringComparison.Ordinal) &&
                        !name.StartsWith("Assembly-", StringComparison.Ordinal))
                        continue;
                    parameters.ReferencedAssemblies.Add(loc);
                }
                catch { }
            }

            CompilerResults results = provider.CompileAssemblyFromSource(parameters, WrapCode(code));
            if (results.Errors.HasErrors)
            {
                var errs = new StringBuilder();
                foreach (CompilerError err in results.Errors)
                    if (!err.IsWarning) errs.AppendLine(err.ErrorNumber + ": " + err.ErrorText + " (line " + err.Line + ")");
                throw new Exception("compile failed:\n" + errs);
            }

            Type type = results.CompiledAssembly.GetType("EditorMcpBridge.DynamicExecutor");
            MethodInfo method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            return method.Invoke(null, null);
        }

        private static string WrapCode(string code)
        {
            return
                "using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing UnityEngine.SceneManagement;\n" +
                "using UnityEditor;\nusing UnityEditor.SceneManagement;\n" +
                "namespace EditorMcpBridge\n{\npublic static class DynamicExecutor\n{\npublic static object Run()\n{\n" +
                code +
                "\n}\n}\n}";
        }

        // ---------------------------------------------------------------- invoke

        private static string InvokeStatic(Request req)
        {
            string typeName = Q(req, "type");
            string methodName = Q(req, "method");
            string argsJson = Q(req, "args");
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
                return Error("missing 'type' and/or 'method'");

            Type type = ResolveType(typeName);
            if (type == null) return Error("type not found: " + typeName);

            object[] raw = string.IsNullOrEmpty(argsJson) ? new object[0] : ParseJsonArray(argsJson);

            MethodInfo method = null;
            MethodInfo[] all = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var m in all)
                if (m.Name == methodName && m.GetParameters().Length == raw.Length) { method = m; break; }
            if (method == null)
                foreach (var m in all)
                    if (m.Name == methodName) { method = m; break; }
            if (method == null) return Error("static method not found: " + typeName + "." + methodName);

            object result = method.Invoke(null, ConvertArgs(raw, method.GetParameters()));
            return Ok(ToJsonValue(result, 0));
        }

        private static object[] ConvertArgs(object[] raw, ParameterInfo[] methodParams)
        {
            if (raw.Length != methodParams.Length)
                throw new Exception("arg count mismatch: got " + raw.Length + ", expected " + methodParams.Length);
            var converted = new object[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                converted[i] = ConvertArg(raw[i], methodParams[i].ParameterType);
            return converted;
        }

        private static object ConvertArg(object val, Type target)
        {
            if (val == null) return null;
            if (target.IsInstanceOfType(val)) return val;
            if (target == typeof(string)) return val.ToString();
            if (target.IsEnum) return Enum.Parse(target, val.ToString());
            if (target == typeof(int)) return Convert.ToInt32(val, CultureInfo.InvariantCulture);
            if (target == typeof(float)) return Convert.ToSingle(val, CultureInfo.InvariantCulture);
            if (target == typeof(double)) return Convert.ToDouble(val, CultureInfo.InvariantCulture);
            if (target == typeof(bool)) return Convert.ToBoolean(val, CultureInfo.InvariantCulture);
            if (target == typeof(long)) return Convert.ToInt64(val, CultureInfo.InvariantCulture);
            if (val is Dictionary<string, object> d)
            {
                if (target == typeof(Vector2)) return new Vector2(ConvF(d, "x"), ConvF(d, "y"));
                if (target == typeof(Vector3)) return new Vector3(ConvF(d, "x"), ConvF(d, "y"), ConvF(d, "z"));
                if (target == typeof(Vector2Int)) return new Vector2Int(ConvI(d, "x"), ConvI(d, "y"));
                if (target == typeof(Vector3Int)) return new Vector3Int(ConvI(d, "x"), ConvI(d, "y"), ConvI(d, "z"));
                if (target == typeof(Color)) return new Color(ConvF(d, "r"), ConvF(d, "g"), ConvF(d, "b"), ConvF(d, "a"));
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(target))
            {
                var o = ResolveAssetOrObject(val.ToString());
                if (o != null && target.IsInstanceOfType(o)) return o;
                return null;
            }
            return Convert.ChangeType(val, target, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- set_field

        private static string SetField(Request req)
        {
            GameObject go = ResolveObject(req);
            if (go == null) return Error("target object not found");
            string component = Q(req, "component");
            string path = Q(req, "path");
            string value = Q(req, "value");
            if (string.IsNullOrEmpty(component)) return Error("missing 'component'");
            if (string.IsNullOrEmpty(path)) return Error("missing 'path'");
            if (value == null) value = req.body;

            Component comp = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && (c.GetType().Name == component || c.GetType().FullName == component))
                { comp = c; break; }
            }
            if (comp == null) return Error("component not found on object: " + component);

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(path);
            if (prop == null) return Error("property not found: " + path);

            bool changed = false;
            string detail = "";
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (int.TryParse(value, out int iv)) { prop.intValue = iv; changed = true; detail = iv.ToString(); }
                    break;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(value, out bool bv)) { prop.boolValue = bv; changed = true; detail = bv ? "true" : "false"; }
                    break;
                case SerializedPropertyType.Float:
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv)) { prop.floatValue = fv; changed = true; detail = fv.ToString(CultureInfo.InvariantCulture); }
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value; changed = true; detail = value;
                    break;
                case SerializedPropertyType.Enum:
                    int eidx = -1;
                    if (int.TryParse(value, out int eiv)) eidx = eiv;
                    else { eidx = Array.IndexOf(prop.enumDisplayNames, value); if (eidx < 0) eidx = Array.IndexOf(prop.enumNames, value); }
                    if (eidx >= 0) { prop.enumValueIndex = eidx; changed = true; detail = prop.enumDisplayNames[eidx]; }
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = ParseColor(value); changed = true; detail = value;
                    break;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = ParseVector2(value); changed = true; detail = value;
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = ParseVector3(value); changed = true; detail = value;
                    break;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = ParseVector4(value); changed = true; detail = value;
                    break;
                case SerializedPropertyType.Vector2Int:
                    prop.vector2IntValue = ParseVector2Int(value); changed = true; detail = value;
                    break;
                case SerializedPropertyType.Vector3Int:
                    prop.vector3IntValue = ParseVector3Int(value); changed = true; detail = value;
                    break;
                case SerializedPropertyType.Quaternion:
                    prop.quaternionValue = Quaternion.Euler(ParseVector3(value)); changed = true; detail = value;
                    break;
                case SerializedPropertyType.LayerMask:
                    if (int.TryParse(value, out int lm)) { prop.intValue = lm; changed = true; detail = lm.ToString(); }
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (value == "null" || string.IsNullOrEmpty(value)) { prop.objectReferenceValue = null; changed = true; detail = "null"; }
                    else { var oref = ResolveAssetOrObject(value); if (oref != null) { prop.objectReferenceValue = oref; changed = true; detail = oref.name; } }
                    break;
                default:
                    return Error("unsupported property type: " + prop.propertyType);
            }

            if (!changed) return Error("could not set property '" + path + "' to '" + value + "'");

            Undo.RecordObject(comp, "Set " + path);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            if (!EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(go.scene);

            return Ok("{\"component\":" + Quote(comp.GetType().Name) + ",\"path\":" + Quote(path) + ",\"value\":" + Quote(detail) + "}");
        }

        // ---------------------------------------------------------------- object ops

        private static string CreateObject(Request req)
        {
            string name = Q(req, "name");
            if (string.IsNullOrEmpty(name)) name = "New GameObject";
            GameObject parent = ResolveByAny(Q(req, "parent"));

            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);

            string comps = Q(req, "components");
            if (!string.IsNullOrEmpty(comps))
            {
                foreach (string cname in comps.Split(','))
                {
                    string cn = cname.Trim();
                    if (cn.Length == 0) continue;
                    Type t = ResolveType(cn);
                    if (t != null && typeof(Component).IsAssignableFrom(t) && go.GetComponent(t) == null)
                        go.AddComponent(t);
                }
            }
            return Ok("{\"instanceId\":" + go.GetInstanceID() + ",\"name\":" + Quote(go.name) + ",\"path\":" + Quote(GetPath(go)) + "}");
        }

        private static string DeleteObject(Request req)
        {
            GameObject go = ResolveObject(req);
            if (go == null) return Error("target object not found");
            string name = go.name;
            Undo.DestroyObjectImmediate(go);
            return Ok(Quote("deleted " + name));
        }

        private static string AddComponent(Request req)
        {
            GameObject go = ResolveObject(req);
            if (go == null) return Error("target object not found");
            string typeName = Q(req, "type");
            if (string.IsNullOrEmpty(typeName)) return Error("missing 'type'");
            Type t = ResolveType(typeName);
            if (t == null || !typeof(Component).IsAssignableFrom(t)) return Error("component type not found: " + typeName);
            if (go.GetComponent(t) != null) return Error("component already present: " + t.Name);
            Component added = go.AddComponent(t);
            Undo.RegisterCreatedObjectUndo(added, "Add " + t.Name);
            return Ok(Quote("added " + t.Name + " to " + go.name));
        }

        private static string RemoveComponent(Request req)
        {
            GameObject go = ResolveObject(req);
            if (go == null) return Error("target object not found");
            string typeName = Q(req, "type");
            if (string.IsNullOrEmpty(typeName)) return Error("missing 'type'");
            Type t = ResolveType(typeName);
            if (t == null || !typeof(Component).IsAssignableFrom(t)) return Error("component type not found: " + typeName);
            if (t == typeof(Transform)) return Error("cannot remove Transform");
            Component c = go.GetComponent(t);
            if (c == null) return Error("component not present: " + t.Name);
            Undo.DestroyObjectImmediate(c);
            return Ok(Quote("removed " + t.Name + " from " + go.name));
        }

        private static string SetParent(Request req)
        {
            GameObject go = ResolveObject(req);
            if (go == null) return Error("target object not found");
            GameObject parent = ResolveByAny(Q(req, "parent"));
            bool worldStays = false;
            if (req.query != null && req.query.TryGetValue("worldStays", out string ws))
                bool.TryParse(ws, out worldStays);
            Undo.RecordObject(go.transform, "Set Parent");
            go.transform.SetParent(parent != null ? parent.transform : null, worldStays);
            return Ok(Quote("reparented " + go.name + " -> " + (parent != null ? parent.name : "(root)")));
        }

        private static string SetActiveObject(Request req)
        {
            GameObject go = ResolveObject(req);
            if (go == null) return Error("target object not found");
            bool active = true;
            if (req.query != null && req.query.TryGetValue("active", out string av))
                bool.TryParse(av, out active);
            bool was = go.activeSelf;
            Undo.RecordObject(go, "SetActive");
            go.SetActive(active);
            return Ok("{\"active\":" + (go.activeSelf ? "true" : "false") + ",\"was\":" + (was ? "true" : "false") + "}");
        }

        private static string SetTransform(Request req)
        {
            GameObject go = ResolveObject(req);
            if (go == null) return Error("target object not found");
            Transform t = go.transform;
            Undo.RecordObject(t, "Set Transform");
            if (req.query != null)
            {
                if (req.query.TryGetValue("position", out string pos)) t.position = ParseVector3(pos);
                if (req.query.TryGetValue("localPosition", out string lpos)) t.localPosition = ParseVector3(lpos);
                if (req.query.TryGetValue("eulerAngles", out string eul)) t.eulerAngles = ParseVector3(eul);
                if (req.query.TryGetValue("localEulerAngles", out string leul)) t.localEulerAngles = ParseVector3(leul);
                if (req.query.TryGetValue("localScale", out string lsc)) t.localScale = ParseVector3(lsc);
            }
            return Ok("{\"position\":" + Vector3Json(t.position) + ",\"localPosition\":" + Vector3Json(t.localPosition) + ",\"eulerAngles\":" + Vector3Json(t.eulerAngles) + ",\"localScale\":" + Vector3Json(t.localScale) + "}");
        }

        // ---------------------------------------------------------------- editor ops

        private static string SetPlayMode(Request req)
        {
            string mode = Q(req, "mode");
            if (string.IsNullOrEmpty(mode)) mode = "toggle";
            string before = EditorApplication.isPlaying ? "playing" : "edit";
            switch (mode.ToLowerInvariant())
            {
                case "enter": case "play":
                    if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
                    break;
                case "exit": case "stop":
                    if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                    break;
                case "toggle":
                    EditorApplication.isPlaying = !EditorApplication.isPlaying;
                    break;
                case "pause":
                    EditorApplication.isPaused = true;
                    break;
                case "unpause":
                    EditorApplication.isPaused = false;
                    break;
                default:
                    return Error("unknown mode: " + mode + " (enter|exit|toggle|pause|unpause)");
            }
            return Ok("{\"was\":" + Quote(before) + ",\"isPlaying\":" + (EditorApplication.isPlaying ? "true" : "false") + ",\"isPaused\":" + (EditorApplication.isPaused ? "true" : "false") + "}");
        }

        private static string SaveScene(Request req)
        {
            bool all = req.query != null && req.query.TryGetValue("all", out string av) && av == "1";
            if (all)
            {
                return EditorSceneManager.SaveOpenScenes() ? Ok(Quote("saved all open scenes")) : Error("save failed");
            }
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) return Error("active scene has no path (unsaved scene)");
            return EditorSceneManager.SaveScene(scene) ? Ok(Quote("saved " + scene.path)) : Error("save failed");
        }

        private static string RefreshAssets(Request req)
        {
            AssetDatabase.Refresh();
            return Ok(Quote("refreshed"));
        }

        private static string RunMenu(Request req)
        {
            string path = Q(req, "path");
            if (string.IsNullOrEmpty(path)) return Error("missing 'path' (menu item path, e.g. 'File/Save Project')");
            return EditorApplication.ExecuteMenuItem(path) ? Ok(Quote("executed menu: " + path)) : Error("menu item not found or failed: " + path);
        }

        // ---------------------------------------------------------------- value parse helpers

        private static float[] SplitNums(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '(' && s[s.Length - 1] == ')') s = s.Substring(1, s.Length - 2);
            string[] parts = s.Split(',');
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = float.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return result;
        }

        private static Vector2 ParseVector2(string s) { float[] n = SplitNums(s); return new Vector2(n.Length > 0 ? n[0] : 0, n.Length > 1 ? n[1] : 0); }
        private static Vector3 ParseVector3(string s) { float[] n = SplitNums(s); return new Vector3(n.Length > 0 ? n[0] : 0, n.Length > 1 ? n[1] : 0, n.Length > 2 ? n[2] : 0); }
        private static Vector4 ParseVector4(string s) { float[] n = SplitNums(s); return new Vector4(n.Length > 0 ? n[0] : 0, n.Length > 1 ? n[1] : 0, n.Length > 2 ? n[2] : 0, n.Length > 3 ? n[3] : 0); }
        private static Vector2Int ParseVector2Int(string s) { float[] n = SplitNums(s); return new Vector2Int((int)(n.Length > 0 ? n[0] : 0), (int)(n.Length > 1 ? n[1] : 0)); }
        private static Vector3Int ParseVector3Int(string s) { float[] n = SplitNums(s); return new Vector3Int((int)(n.Length > 0 ? n[0] : 0), (int)(n.Length > 1 ? n[1] : 0), (int)(n.Length > 2 ? n[2] : 0)); }

        private static Color ParseColor(string s)
        {
            if (s.StartsWith("#"))
            {
                Color html;
                if (ColorUtility.TryParseHtmlString(s, out html)) return html;
            }
            float[] n = SplitNums(s);
            return new Color(n.Length > 0 ? n[0] : 0, n.Length > 1 ? n[1] : 0, n.Length > 2 ? n[2] : 0, n.Length > 3 ? n[3] : 1);
        }

        private static float ConvF(Dictionary<string, object> d, string key)
        {
            object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToSingle(v, CultureInfo.InvariantCulture) : 0f;
        }

        private static int ConvI(Dictionary<string, object> d, string key)
        {
            object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        }

        // ---------------------------------------------------------------- minimal JSON parser

        private static object ParseJsonValue(string s)
        {
            int i = 0;
            object v = ParseJsonValueAt(s, ref i);
            SkipJsonWs(s, ref i);
            return v;
        }

        private static void SkipJsonWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static object ParseJsonValueAt(string s, ref int i)
        {
            SkipJsonWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '"') return ParseJsonString(s, ref i);
            if (c == '{') return ParseJsonObject(s, ref i);
            if (c == '[') return ParseJsonArrayAt(s, ref i);
            if (c == 't' && i + 4 <= s.Length && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (c == 'f' && i + 5 <= s.Length && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (c == 'n' && i + 4 <= s.Length && s.Substring(i, 4) == "null") { i += 4; return null; }
            return ParseJsonNumber(s, ref i);
        }

        private static string ParseJsonString(string s, ref int i)
        {
            i++; // skip opening quote
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"') { i++; return sb.ToString(); }
                if (c == '\\')
                {
                    i++;
                    if (i >= s.Length) break;
                    char e = s[i];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            if (i + 4 < s.Length) { sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; }
                            break;
                        default: sb.Append(e); break;
                    }
                    i++;
                }
                else { sb.Append(c); i++; }
            }
            return sb.ToString();
        }

        private static object[] ParseJsonArray(string s)
        {
            int i = 0;
            return ParseJsonArrayAt(s, ref i);
        }

        private static object[] ParseJsonArrayAt(string s, ref int i)
        {
            var list = new List<object>();
            i++; // skip [
            SkipJsonWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list.ToArray(); }
            while (true)
            {
                list.Add(ParseJsonValueAt(s, ref i));
                SkipJsonWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new Exception("expected ',' or ']' in JSON array");
            }
            return list.ToArray();
        }

        private static Dictionary<string, object> ParseJsonObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // skip {
            SkipJsonWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipJsonWs(s, ref i);
                string key = ParseJsonString(s, ref i);
                SkipJsonWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new Exception("expected ':' in JSON object");
                i++;
                d[key] = ParseJsonValueAt(s, ref i);
                SkipJsonWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new Exception("expected ',' or '}' in JSON object");
            }
            return d;
        }

        private static object ParseJsonNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            string num = s.Substring(start, i - start);
            if (num.IndexOf('.') >= 0 || num.IndexOf('e') >= 0 || num.IndexOf('E') >= 0)
                return double.Parse(num, CultureInfo.InvariantCulture);
            long l;
            if (long.TryParse(num, out l)) return l;
            return double.Parse(num, CultureInfo.InvariantCulture);
        }
    }
}
#endif
