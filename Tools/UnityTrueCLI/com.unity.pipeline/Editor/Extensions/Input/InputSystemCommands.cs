using System;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace Unity.Pipeline.Input.Editor
{
    static class InputSystemCommands
    {
        [CliCommand("get_input_system_status", "Read actual Input System version, enabled input backend, update mode and connected devices.", Tags = new[] { "input/status" })]
        public static object Status() => new
        {
            packageVersion = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(InputSystem).Assembly)?.version,
            backendEnabled =
#if ENABLE_INPUT_SYSTEM
                true,
#else
                false,
#endif
            playMode = EditorApplication.isPlaying,
            updateMode = InputSystem.settings.updateMode.ToString(),
            devices = InputSystem.devices.Select(device => new { device.deviceId, device.name, device.layout, device.displayName, device.enabled, device.added }).ToArray(),
            players = ExtensionObjects.Find<PlayerInput>().Select(DescribePlayer).ToArray()
        };

        [CliCommand("inspect_player_input", "Read PlayerInput's actual action map, control scheme, paired devices and notification behavior.", Tags = new[] { "input/player" })]
        public static object Player([CliArg("target", "PlayerInput object.", Required = true)] ObjectRef target) => DescribePlayer(ExtensionObjects.Require<PlayerInput>(target));

        [CliCommand("pair_player_input_devices", "Explicitly pair selected connected devices to a live PlayerInput without unpairing existing devices. Use virtual device IDs returned by start_input_sequence when PlayerInput restricts devices.", Tags = new[] { "input/player" })]
        public static object Pair([CliArg("target", "PlayerInput object.", Required = true)] ObjectRef target,
            [CliArg("device_ids", "Connected device IDs to add to this user's pairing.", Required = true)] int[] deviceIds)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Requires Play Mode.");
            var player=ExtensionObjects.Require<PlayerInput>(target);
            if(!player.user.valid) throw new ArgumentException("PlayerInput has no active InputUser.");
            if(deviceIds==null || deviceIds.Length==0 || deviceIds.Length>16) throw new ArgumentException("Provide 1..16 device IDs.");
            var devices=deviceIds.Select(id=>InputSystem.GetDeviceById(id)??throw new ArgumentException("Unknown device ID: "+id)).ToArray();
            foreach(var device in devices) InputUser.PerformPairingWithDevice(device,player.user);
            return DescribePlayer(player);
        }

        private static object DescribePlayer(PlayerInput player) => new
        {
            handle = ObjectResolver.Describe(player), player.enabled, player.inputIsActive, player.playerIndex,
            actions = ObjectResolver.Describe(player.actions), currentMap = player.currentActionMap?.name,
            player.currentControlScheme, player.defaultControlScheme, player.defaultActionMap,
            notificationBehavior = player.notificationBehavior.ToString(),
            devices = player.devices.Select(device => new { device.deviceId, device.name, device.layout }).ToArray()
        };

        [CliCommand("read_input_controls", "Read controls matching an Input System path, including virtual devices. Does not inject events or enable actions.", Tags = new[] { "input/status" })]
        public static object Controls([CliArg("path", "Control path, e.g. <Keyboard>/space.", Required = true)] string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.");
            using (var controls = InputSystem.FindControls(path))
                return new { total = controls.Count, controls = controls.Take(64).Select(control => new { control.path, deviceId = control.device.deviceId,
                    layout = control.layout, value = Convert.ToString(control.ReadValueAsObject(), System.Globalization.CultureInfo.InvariantCulture) }).ToArray() };
        }

        [CliCommand("create_input_actions", "Create a NEW .inputactions asset from Input System JSON, validate through InputActionAsset, then import and read back. Never overwrites an asset. Does not change Player settings.", Tags = new[] { "input/actions" })]
        public static object Create([CliArg("asset", "New .inputactions path under the authoring root.", Required = true)] string asset,
            [CliArg("definition", "InputActionAsset JSON document.", Required = true)] string json)
        {
            var path = ProjectPaths.Resolve(asset, out var error);
            if (path == null) throw new ArgumentException(error);
            if (!path.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected .inputactions path.");
            var absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
            if (File.Exists(absolute) || AssetDatabase.LoadMainAssetAtPath(path) != null) throw new ArgumentException("Asset already exists.");
            var parsed = InputActionAsset.FromJson(json);
            try
            {
                var folder = Path.GetDirectoryName(path).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(folder)) throw new ArgumentException("Create the containing Assets folder first.");
                using (var stream = new FileStream(absolute, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) writer.Write(parsed.ToJson());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return Actions(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(parsed); }
        }

        [CliCommand("inspect_input_actions", "Read .inputactions maps, actions, binding IDs, overrides and control schemes. Does not enable an asset.", Tags = new[] { "input/actions" })]
        public static object Actions([CliArg("asset", "Project .inputactions path.", Required = true)] string asset,
            [CliArg("map", "Optional action map name or ID.")] string map = null)
        {
            var value = Load(asset);
            var maps = map == null ? value.actionMaps.ToArray() : new[] { value.FindActionMap(map, true) };
            return new { asset = ObjectResolver.Describe(value),
                maps = maps.Select(item => new { item.name, id = item.id.ToString(), item.enabled,
                    actions = item.actions.Select(action => new { action.name, id = action.id.ToString(), type = action.type.ToString(), action.expectedControlType,
                        action.enabled, phase = action.phase.ToString(), action.interactions, action.processors,
                        bindings = action.bindings.Select(binding => new { id = binding.id.ToString(), binding.name, binding.path, binding.effectivePath,
                            binding.groups, binding.interactions, binding.processors, binding.overridePath, binding.overrideInteractions, binding.overrideProcessors,
                            binding.isComposite, binding.isPartOfComposite }).ToArray() }).ToArray() }).ToArray(),
                schemes = value.controlSchemes.Select(scheme => new { scheme.name, scheme.bindingGroup,
                    devices = scheme.deviceRequirements.Select(device => new { device.controlPath, device.isOptional, device.isOR }).ToArray() }).ToArray() };
        }

        [CliCommand("configure_input_binding", "Update one binding by its stable ID, preserving the asset's other IDs/references. Requires confirm; dry_run previews. Writes .inputactions through Input System JSON and synchronously imports. Not undoable.", Tags = new[] { "input/actions" })]
        public static object Binding([CliArg("asset", "Project .inputactions path.", Required = true)] string asset,
            [CliArg("binding_id", "Binding GUID from inspect_input_actions.", Required = true)] string bindingId,
            [CliArg("path", "New control/composite path; omit to preserve.")] string path = null,
            [CliArg("groups", "New binding groups; empty clears.")] string groups = null,
            [CliArg("interactions", "New interactions; empty clears.")] string interactions = null,
            [CliArg("processors", "New processors; empty clears.")] string processors = null,
            [CliArg("confirm", "Apply the asset write.")] bool confirm = false,
            [CliArg("dry_run", "Preview without any write.")] bool dryRun = false)
        {
            var source = Load(asset);
            if (!Guid.TryParse(bindingId, out var id)) throw new ArgumentException("binding_id must be a GUID.");
            var clone = InputActionAsset.FromJson(source.ToJson());
            try
            {
                var map = clone.actionMaps.FirstOrDefault(item => item.bindings.Any(binding => binding.id == id));
                if (map == null) throw new ArgumentException("No binding with that ID.");
                int index = Array.FindIndex(map.bindings.ToArray(), binding => binding.id == id);
                var previous = map.bindings[index];
                var change = map.ChangeBinding(index);
                if (path != null) change.WithPath(path);
                if (groups != null) change.WithGroups(groups);
                if (interactions != null) change.WithInteractions(interactions);
                if (processors != null) change.WithProcessors(processors);
                var next = map.bindings[index];
                if (dryRun) return new { state = "dry_run", bindingId, before = previous, after = next };
                if (!confirm) throw new ArgumentException("confirm=true is required; use dry_run to preview.");
                var assetPath = AssetDatabase.GetAssetPath(source);
                var absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath));
                ExtensionFileWrites.ReplaceExistingText(absolute,clone.ToJson());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                return Actions(assetPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }

        [CliCommand("set_player_input_map", "Switch a live PlayerInput to an existing action map; Play Mode only. Does not change the asset or project input backend.", Tags = new[] { "input/player" })]
        public static object SetMap([CliArg("target", "PlayerInput object.", Required = true)] ObjectRef target,
            [CliArg("map", "Existing map name or ID.", Required = true)] string map)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Requires Play Mode.");
            var player = ExtensionObjects.Require<PlayerInput>(target);
            if (player.actions == null) throw new ArgumentException("PlayerInput has no actions.");
            player.actions.FindActionMap(map, true);
            player.SwitchCurrentActionMap(map);
            return DescribePlayer(player);
        }

        private static InputActionAsset Load(string path)
        {
            var resolved = ProjectPaths.Resolve(path, out var error);
            if (resolved == null) throw new ArgumentException(error);
            if (!resolved.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected .inputactions asset.");
            var value = AssetDatabase.LoadAssetAtPath<InputActionAsset>(resolved);
            if (value == null) throw new ArgumentException("InputActionAsset not found: " + resolved);
            return value;
        }
    }
}
