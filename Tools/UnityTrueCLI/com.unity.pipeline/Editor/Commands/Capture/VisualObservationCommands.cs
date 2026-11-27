using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.Capture
{
    [InitializeOnLoad]
    static class VisualObservationCommands
    {
        private static Observation Active;
        private static Camera ExplicitCamera;
        private static double NextCapture;
        private static readonly string Root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp/UnityTrueCLI/observations");
        private const long ByteLimit = 64 * 1024 * 1024;

        static VisualObservationCommands()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += () => Finish("domain_reload");
            EditorApplication.quitting += () => Finish("editor_exit");
            // SessionState survives domain reloads but not Editor process restarts.
            var interrupted = SessionState.GetString("TrueCli.Observation", "");
            if (interrupted.Length != 0)
            {
                try { var previous = Load(interrupted); if (previous.state == "running") { previous.state = "interrupted"; Save(previous); } }
                catch (Exception exception) { Debug.LogWarning("Could not recover observation: " + exception.Message); }
                SessionState.EraseString("TrueCli.Observation");
            }
        }

        [CliCommand("start_visual_observation", "Start continuous bounded PNG capture without blocking the Editor. Default screen source includes Overlay UI. Poll read_visual_frames during operations; starting capture alone does not start a model.", Tags = new[] { "capture/observation" })]
        public static Observation Start(
            [CliArg("fps", "Requested sample frequency, 0.2..10.")] float fps = 2,
            [CliArg("width", "Capture width, 64..1920.")] int width = 960,
            [CliArg("height", "Capture height, 64..1080.")] int height = 540,
            [CliArg("capacity", "Frames retained in ring, 2..120; also capped at 64 MiB.")] int capacity = 32,
            [CliArg("duration", "Maximum seconds, 1..3600, unscaled editor time.")] float duration = 120,
            [CliArg("source", "screen or camera. camera requires a unique object handle and omits Overlay UI.")] string source = "screen",
            [CliArg("camera", "Required for source=camera; forbidden for screen.")] ObjectRef camera = null)
        {
            if (Active != null) throw new InvalidOperationException("An observation is active; query/stop its ID first.");
            ExtensionObjects.Finite(fps, "fps"); ExtensionObjects.Finite(duration, "duration");
            if (fps < 0.2f || fps > 10 || width < 64 || width > 1920 || height < 64 || height > 1080 || capacity < 2 || capacity > 120 || duration < 1 || duration > 3600)
                throw new ArgumentException("Capture options exceed their documented bounds.");
            if (source != "screen" && source != "camera") throw new ArgumentException("source must be screen or camera.");
            if ((source == "camera") != (camera != null)) throw new ArgumentException("Supply camera exactly when source=camera.");
            var selectedCamera = camera == null ? null : ExtensionObjects.Require<Camera>(camera);
            // Validate capture before publishing an active session or creating its directory.
            var first = CaptureCommands.CaptureObservationFrame(width, height, selectedCamera);
            var session = new Observation
            {
                id = Guid.NewGuid().ToString("N"), state = "running", source = source,
                camera = ObjectResolver.Describe(selectedCamera), width = width, height = height, fps = fps,
                capacity = capacity, duration = duration, startedUtc = DateTime.UtcNow,
                startedEditorTime = EditorApplication.timeSinceStartup
            };
            Directory.CreateDirectory(Path.Combine(Root, session.id));
            try { Append(session, first); }
            catch { session.state = "failed"; throw; }
            Active = session; ExplicitCamera = selectedCamera;
            NextCapture = EditorApplication.timeSinceStartup + 1.0 / fps;
            SessionState.SetString("TrueCli.Observation", session.id);
            Save(session);
            return Snapshot(session);
        }

        [CliCommand("get_visual_observation_status", "Read persisted observation status, measured capture cost, sequence and ring eviction counters.", Tags = new[] { "capture/observation" })]
        public static Observation Status([CliArg("id", "Observation ID.", Required = true)] string id) => Snapshot(Get(id));

        [CliCommand("read_visual_frames", "Incrementally read bounded frame paths after a sequence. latest=true skips backlog explicitly. Frames remain in the ring and may later expire. No base64 payload flood.", Tags = new[] { "capture/observation" })]
        public static object Read([CliArg("id", "Observation ID.", Required = true)] string id,
            [CliArg("after", "Last consumed sequence, initially 0.")] long after = 0,
            [CliArg("limit", "Maximum returned frames, 1..12.")] int limit = 4,
            [CliArg("latest", "Return the newest limit frames; report skipped backlog.")] bool latest = true)
        {
            if (after < 0 || limit < 1 || limit > 12) throw new ArgumentException("after must be >=0 and limit 1..12.");
            var session = Get(id);
            if (after > session.sequence) throw new ArgumentException("Cursor is ahead of this session.");
            var available = session.frames.Where(frame => frame.sequence > after).ToArray();
            var selected = latest ? available.Skip(Math.Max(0, available.Length - limit)).ToArray() : available.Take(limit).ToArray();
            long skipped = selected.Length == 0 ? 0 : selected[0].sequence - after - 1;
            return new { session.id, session.state, session.source, frames = selected,
                cursor = selected.Length == 0 ? after : selected.Last().sequence, skippedFrames = skipped,
                session.evictedFrames, session.lastError,
                note = "View the returned local images and correlate their time/frame/marker while continuing operations. This command does not itself perform model inference." };
        }

        [CliCommand("mark_visual_observation", "Associate the next captures with a caller operation label without changing the game.", Tags = new[] { "capture/observation" })]
        public static Observation Mark([CliArg("id", "Active observation ID.", Required = true)] string id,
            [CliArg("label", "Operation label, at most 200 characters.", Required = true)] string label)
        {
            if (Active == null || Active.id != id) throw new ArgumentException("That observation is not active.");
            if (label == null || label.Length > 200) throw new ArgumentException("label must contain at most 200 characters.");
            Active.marker = label; Save(Active); return Snapshot(Active);
        }

        [CliCommand("stop_visual_observation", "Stop the named observation; keep its bounded frame evidence. Does not stop Play Mode or alter scenes.", Tags = new[] { "capture/observation" })]
        public static Observation Stop([CliArg("id", "Observation ID.", Required = true)] string id)
        {
            var session = Get(id);
            if (Active != null && Active.id == id) Finish("stopped");
            return Snapshot(Get(id));
        }

        private static Observation Get(string id) => Active != null && Active.id == id ? Active : Load(id);
        private static Observation Snapshot(Observation session) => JsonConvert.DeserializeObject<Observation>(JsonConvert.SerializeObject(session));

        private static Observation Load(string id)
        {
            ValidateId(id);
            var directory = Path.Combine(Root, id);
            if (!Directory.Exists(directory)) throw new ArgumentException("Unknown observation ID.");
            var path = Directory.GetFiles(directory,"state-*.json").OrderByDescending(file=>file,StringComparer.Ordinal).FirstOrDefault();
            if (path == null) throw new ArgumentException("Unknown observation ID.");
            var result = JsonConvert.DeserializeObject<Observation>(File.ReadAllText(path));
            if (result == null || result.id != id) throw new InvalidOperationException("Observation identity mismatch.");
            return result;
        }

        private static void ValidateId(string id)
        {
            if (id == null || id.Length != 32 || !Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid observation ID.");
        }

        private static void Update()
        {
            if (Active == null) return;
            if (EditorApplication.timeSinceStartup - Active.startedEditorTime >= Active.duration) { Finish("completed"); return; }
            if (EditorApplication.timeSinceStartup < NextCapture || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            NextCapture = EditorApplication.timeSinceStartup + 1.0 / Active.fps;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (Active.source == "camera" && ExplicitCamera == null) throw new InvalidOperationException("Captured camera was destroyed; no substitute selected.");
                Append(Active, CaptureCommands.CaptureObservationFrame(Active.width, Active.height, ExplicitCamera));
                Active.lastCaptureMs = watch.Elapsed.TotalMilliseconds;
                Active.totalCaptureMs += Active.lastCaptureMs;
                Save(Active);
            }
            catch (Exception exception) { Active.lastError = exception.Message; Finish("failed"); }
        }

        private static void Append(Observation session, byte[] png)
        {
            if (png.Length > ByteLimit) throw new InvalidOperationException("Single frame exceeds observation byte limit.");
            session.sequence++;
            string digest;
            using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
            var frame = new VisualFrame
            {
                sequence = session.sequence, utc = DateTime.UtcNow, editorTime = EditorApplication.timeSinceStartup,
                gameFrame = Time.frameCount, renderedFrame = Time.renderedFrameCount,
                marker = session.marker, bytes = png.Length, sha256 = digest,
                identicalPixels = session.frames.Count != 0 && session.frames.Last().sha256 == digest,
                path = Path.Combine(Root, session.id, session.sequence.ToString("D8") + ".png")
            };
            File.WriteAllBytes(frame.path, png);
            session.frames.Add(frame); session.bufferBytes += png.Length;
            session.measuredSampleFps = (session.sequence - 1) / Math.Max(0.001, frame.editorTime - session.startedEditorTime);
            while (session.frames.Count > session.capacity || session.bufferBytes > ByteLimit)
            {
                var oldest = session.frames[0]; File.Delete(oldest.path);
                session.frames.RemoveAt(0); session.bufferBytes -= oldest.bytes; session.evictedFrames++;
            }
        }

        private static void Finish(string reason)
        {
            if (Active == null) return;
            Active.state = reason; Active.endedUtc = DateTime.UtcNow;
            try { Save(Active); SessionState.EraseString("TrueCli.Observation"); }
            finally { Active = null; ExplicitCamera = null; }
        }

        private static void Save(Observation session)
        {
            // Publish a new immutable state file. Avoid Mono/Windows File.Replace
            // contention with readers/scanners; failed publication leaves the old state intact.
            session.revision++;
            var directory = Path.Combine(Root,session.id);
            var path = Path.Combine(directory, "state-" + session.revision.ToString("D12") + ".json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(session, Formatting.Indented));
            File.Move(temporary, path);
            foreach(var stale in Directory.GetFiles(directory,"state-*.json").OrderByDescending(file=>file,StringComparer.Ordinal).Skip(2))
            {
                try { File.Delete(stale); }
                catch(IOException) { /* A concurrent local reader may still hold the old snapshot. */ }
                catch(UnauthorizedAccessException) { /* Retry pruning on the next publication. */ }
            }
        }
    }

    [Serializable]
    public sealed class Observation
    {
        public string id, state, source, marker, lastError;
        public AuthoringResult camera;
        public int width, height, capacity;
        public float fps, duration;
        public DateTime startedUtc;
        public DateTime? endedUtc;
        public double startedEditorTime, lastCaptureMs, totalCaptureMs, measuredSampleFps;
        public long revision, sequence, bufferBytes, evictedFrames;
        public List<VisualFrame> frames = new List<VisualFrame>();
    }

    [Serializable]
    public sealed class VisualFrame
    {
        public long sequence;
        public DateTime utc;
        public double editorTime;
        public int gameFrame, renderedFrame, bytes;
        public string path, marker, sha256;
        public bool identicalPixels;
    }
}
