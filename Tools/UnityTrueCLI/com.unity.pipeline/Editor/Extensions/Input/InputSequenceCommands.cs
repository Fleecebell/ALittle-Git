using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Unity.Pipeline.Input.Editor
{
    // Dedicated virtual devices keep tool-owned held state separate from the physical keyboard.
    // Events are consumed by the normal Input System update; no recursive InputSystem.Update.
    [InitializeOnLoad]
    static class InputSequenceCommands
    {
        private static readonly Dictionary<string, Sequence> Sessions = new Dictionary<string, Sequence>();
        private static Keyboard KeyboardDevice;
        private static Mouse MouseDevice;
        private static Sequence Active;
        private static string[] Held = Array.Empty<string>();
        private static bool MouseHeld;
        private static Vector2 Pointer;

        static InputSequenceCommands()
        {
            InputSystem.onBeforeUpdate += BeforeInputUpdate;
            EditorApplication.update += () =>
            {
                if (Active != null && (EditorApplication.timeSinceStartup - Active.start) * 1000 > Active.steps.Last().at_ms + 5000)
                    Finish("timed_out_waiting_for_game_updates");
            };
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) Cleanup("play_mode_ended"); };
            AssemblyReloadEvents.beforeAssemblyReload += () => Cleanup("domain_reload");
            EditorApplication.quitting += () => Cleanup("editor_exit");
        }

        [CliCommand("start_input_sequence", "Run a bounded, real-time key/pointer sequence across normal Input System updates in Play Mode. Own virtual devices; one active sequence. Steps use absolute milliseconds since start.", Tags = new[] { "input/simulation" })]
        public static object Start([CliArg("steps", "JSON steps: at_ms, keys (complete held set), optional x/y/left. Maximum 256 steps and 60 seconds.", Required = true)] InputStep[] steps)
        {
#if !ENABLE_INPUT_SYSTEM
            throw new InvalidOperationException("Input System package is installed but Active Input Handling does not enable it.");
#else
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Requires Play Mode.");
            if (Active != null) throw new InvalidOperationException("An input sequence is active; query/cancel its ID first.");
            if (steps == null || steps.Length == 0 || steps.Length > 256) throw new ArgumentException("Provide 1..256 steps.");
            int previous = -1;
            foreach (var step in steps)
            {
                if (step == null || step.at_ms < 0 || step.at_ms > 60000 || step.at_ms < previous) throw new ArgumentException("at_ms must be monotonic in 0..60000.");
                previous = step.at_ms;
                if (step.keys != null) foreach (var key in step.keys)
                    if (!Enum.TryParse<Key>(key, true, out var parsed) || parsed == Key.None || !Enum.IsDefined(typeof(Key), parsed)) throw new ArgumentException("Unknown key: " + key);
                if (step.x.HasValue != step.y.HasValue) throw new ArgumentException("Provide both x and y.");
                if (step.x.HasValue && (float.IsNaN(step.x.Value) || float.IsInfinity(step.x.Value) || float.IsNaN(step.y.Value) || float.IsInfinity(step.y.Value))) throw new ArgumentException("Pointer coordinates must be finite.");
            }
            // Keep the named device instances for the Play session so action/UI device pairing is stable.
            if (KeyboardDevice == null || !KeyboardDevice.added) KeyboardDevice = InputSystem.AddDevice<Keyboard>("TrueCliKeyboard");
            if (MouseDevice == null || !MouseDevice.added) MouseDevice = InputSystem.AddDevice<Mouse>("TrueCliMouse");
            if (Sessions.Count >= 64) Sessions.Remove(Sessions.First(pair => pair.Value != Active).Key);
            var sequence = new Sequence { id = Guid.NewGuid().ToString("N"), state = "running", steps = steps, start = EditorApplication.timeSinceStartup };
            Sessions.Add(sequence.id, sequence); Active = sequence;
            return Status(sequence.id);
#endif
        }

        [CliCommand("get_input_sequence", "Read a sequence's progress and terminal status. Queued events are not a claim that gameplay callbacks succeeded.", Tags = new[] { "input/simulation" })]
        public static object Status([CliArg("id", "Sequence ID.", Required = true)] string id)
        {
            if (!Sessions.TryGetValue(id, out var sequence)) throw new ArgumentException("Unknown input sequence ID.");
            return new { sequence.id, sequence.state, appliedSteps = sequence.index, totalSteps = sequence.steps.Length,
                sequence.lastAppliedGameFrame, keyboardDeviceId = KeyboardDevice?.deviceId, mouseDeviceId = MouseDevice?.deviceId,
                elapsedMs = (EditorApplication.timeSinceStartup - sequence.start) * 1000,
                note = "Events use owned virtual devices. PlayerInput with an explicit device restriction must pair these devices explicitly; physical input is preserved." };
        }

        [CliCommand("cancel_input_sequence", "Cancel the named sequence and release only tool-owned virtual controls.", Tags = new[] { "input/simulation" })]
        public static object Cancel([CliArg("id", "Sequence ID.", Required = true)] string id)
        {
            if (!Sessions.TryGetValue(id, out var sequence)) throw new ArgumentException("Unknown input sequence ID.");
            if (Active == sequence) Finish("cancelled");
            return Status(id);
        }

        private static void BeforeInputUpdate()
        {
            // An Editor input update must not advance gameplay steps before the game sees them.
            if (!EditorApplication.isPlaying || InputState.currentUpdateType == InputUpdateType.Editor || InputState.currentUpdateType == InputUpdateType.BeforeRender || Active == null) return;
            var elapsed = (EditorApplication.timeSinceStartup - Active.start) * 1000;
            if (Active.index == Active.steps.Length)
            {
                if (Time.frameCount != Active.lastAppliedGameFrame) Finish("completed");
                return;
            }
            var step = Active.steps[Active.index];
            if (step.at_ms > elapsed || Active.lastAppliedGameFrame == Time.frameCount) return;
            if (step.keys != null) Held = step.keys;
            if (step.x.HasValue) Pointer = new Vector2(step.x.Value, step.y.Value);
            if (step.left.HasValue) MouseHeld = step.left.Value;
            QueueState();
            Active.index++; Active.lastAppliedGameFrame = Time.frameCount;
        }

        private static void QueueState()
        {
            if (KeyboardDevice != null && KeyboardDevice.added)
                InputSystem.QueueStateEvent(KeyboardDevice, new KeyboardState(Held.Select(key => (Key)Enum.Parse(typeof(Key), key, true)).ToArray()));
            if (MouseDevice != null && MouseDevice.added)
                InputSystem.QueueStateEvent(MouseDevice, new MouseState { position = Pointer }.WithButton(MouseButton.Left, MouseHeld));
        }

        private static void Finish(string state)
        {
            if (Active == null) return;
            Active.state = state; Held = Array.Empty<string>(); MouseHeld = false;
            // A normal final up event must reach UI processing; resetting here can
            // cancel the pointer action before its release is consumed.
            if (state != "completed")
            {
                if (KeyboardDevice != null && KeyboardDevice.added) InputSystem.ResetDevice(KeyboardDevice);
                if (MouseDevice != null && MouseDevice.added) InputSystem.ResetDevice(MouseDevice);
            }
            QueueState(); Active = null;
        }

        private static void Cleanup(string reason)
        {
            Finish(reason);
            if (KeyboardDevice != null && KeyboardDevice.added) InputSystem.RemoveDevice(KeyboardDevice);
            if (MouseDevice != null && MouseDevice.added) InputSystem.RemoveDevice(MouseDevice);
            KeyboardDevice = null; MouseDevice = null;
        }

        private sealed class Sequence
        {
            public string id, state;
            public InputStep[] steps;
            public double start;
            public int index, lastAppliedGameFrame = -1;
        }
    }

    [Serializable]
    public sealed class InputStep
    {
        public int at_ms;
        public string[] keys;
        public float? x, y;
        public bool? left;
    }
}
