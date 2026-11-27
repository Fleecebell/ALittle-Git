using System;
using System.Linq;
using Cinemachine;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Cinemachine2.Editor
{
    static class CinemachineCommands
    {
        [CliCommand("inspect_cinemachine", "Read Cinemachine 2.x cameras, Follow/LookAt, priorities, lens and actual Brain live/blend state in the current stage.", Tags = new[] { "cinemachine/status" })]
        public static object Inspect() => new
        {
            packageVersion = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CinemachineBrain).Assembly)?.version,
            cameras = ExtensionObjects.Find<CinemachineVirtualCameraBase>().Select(camera => new
            {
                handle = ObjectResolver.Describe(camera), camera.name, camera.enabled,
                priority = camera.Priority, follow = ObjectResolver.Describe(camera.Follow), lookAt = ObjectResolver.Describe(camera.LookAt),
                lens = new { camera.State.Lens.FieldOfView, camera.State.Lens.OrthographicSize, camera.State.Lens.NearClipPlane, camera.State.Lens.FarClipPlane },
                pipeline = camera.GetComponents<CinemachineComponentBase>().Select(component => component.GetType().FullName).ToArray()
            }).ToArray(),
            brains = ExtensionObjects.Find<CinemachineBrain>().Select(Brain).ToArray()
        };

        [CliCommand("inspect_camera_blend", "Read one Cinemachine Brain's actual output, live camera and blend state. No priority-based guesses.", Tags = new[] { "cinemachine/status" })]
        public static object Blend([CliArg("target", "Brain GameObject/component.", Required = true)] ObjectRef target) => Brain(ExtensionObjects.Require<CinemachineBrain>(target));

        [CliCommand("configure_cinemachine_brain", "Set the existing Brain's default blend duration/style with Undo. Existing custom blend rules may override the default.", Tags = new[] { "cinemachine/authoring" })]
        public static object ConfigureBrain([CliArg("target", "Brain object.", Required = true)] ObjectRef target,
            [CliArg("duration", "Default blend seconds, 0..60.")] float? duration = null,
            [CliArg("style", "Cinemachine blend style name, e.g. Cut, EaseInOut, Linear.")] string style = null)
        {
            var brain = ExtensionObjects.Require<CinemachineBrain>(target);
            var definition = brain.m_DefaultBlend;
            if (duration.HasValue && (ExtensionObjects.Finite(duration.Value,"duration") < 0 || duration.Value > 60)) throw new ArgumentException("duration must be 0..60.");
            if (style != null)
            {
                if (!Enum.TryParse<CinemachineBlendDefinition.Style>(style, true, out var parsed) || !Enum.IsDefined(typeof(CinemachineBlendDefinition.Style), parsed)) throw new ArgumentException("Invalid blend style.");
                definition.m_Style = parsed;
            }
            if (duration.HasValue) definition.m_Time = duration.Value;
            using (new AuthoringUndoScope("Configure Cinemachine Brain"))
            { Undo.RecordObject(brain,"Configure Cinemachine Brain"); brain.m_DefaultBlend = definition; ExtensionObjects.Changed(brain); }
            return Brain(brain);
        }

        private static object Brain(CinemachineBrain brain)
        {
            var blend = brain.ActiveBlend;
            return new { handle = ObjectResolver.Describe(brain), brain.enabled,
                output = ObjectResolver.Describe(brain.OutputCamera), live = ObjectResolver.Describe(brain.ActiveVirtualCamera as Component), liveName = brain.ActiveVirtualCamera?.Name,
                brain.IsBlending, blend = blend == null ? null : new { from = ObjectResolver.Describe(blend.CamA as Component), fromName = blend.CamA?.Name,
                    to = ObjectResolver.Describe(blend.CamB as Component), toName = blend.CamB?.Name, blend.TimeInBlend, blend.Duration, blend.BlendWeight } };
        }

        [CliCommand("configure_cinemachine_camera", "Configure a Cinemachine 2.x basic camera with Undo. Follow/LookAt can be explicitly cleared. Does not replace existing pipeline components or save scenes.", Tags = new[] { "cinemachine/authoring" })]
        public static object Configure([CliArg("target", "Basic camera object.", Required = true)] ObjectRef target,
            [CliArg("follow", "Optional Follow object.")] ObjectRef follow = null,
            [CliArg("look_at", "Optional LookAt object.")] ObjectRef lookAt = null,
            [CliArg("priority", "New camera priority.")] int? priority = null,
            [CliArg("field_of_view", "Perspective FOV in (0,179). Does not change lens projection mode.")] float? fieldOfView = null,
            [CliArg("orthographic_size", "Positive orthographic size.")] float? orthographicSize = null,
            [CliArg("follow_offset", "Optional [x,y,z]. Requires the existing basic follow component; no other body is replaced.")] float[] followOffset = null,
            [CliArg("clear_follow", "Explicitly clear Follow; conflicts with follow.")] bool clearFollow = false,
            [CliArg("clear_look_at", "Explicitly clear LookAt; conflicts with look_at.")] bool clearLookAt = false)
        {
            var camera = ExtensionObjects.Require<CinemachineVirtualCamera>(target);
            if ((clearFollow && follow != null) || (clearLookAt && lookAt != null)) throw new ArgumentException("Clear and target options conflict.");
            var followTransform = follow == null ? null : ExtensionObjects.Resolve(follow).transform;
            var lookTransform = lookAt == null ? null : ExtensionObjects.Resolve(lookAt).transform;
            if (fieldOfView.HasValue && (ExtensionObjects.Finite(fieldOfView.Value, "field_of_view") <= 0 || fieldOfView.Value >= 179)) throw new ArgumentException("Invalid FOV.");
            if (orthographicSize.HasValue && ExtensionObjects.Finite(orthographicSize.Value, "orthographic_size") <= 0) throw new ArgumentException("Invalid size.");
            var body = camera.GetCinemachineComponent<CinemachineTransposer>();
            if (followOffset != null)
            {
                if (followOffset.Length != 3) throw new ArgumentException("follow_offset must have three values.");
                foreach (var value in followOffset) ExtensionObjects.Finite(value, "follow_offset");
                if (body == null) throw new ArgumentException("Camera has no CinemachineTransposer; add the intended body explicitly before configuring its offset.");
            }
            using (new AuthoringUndoScope("Configure Cinemachine"))
            {
                Undo.RecordObject(camera, "Configure Cinemachine");
                if (follow != null || clearFollow) camera.Follow = followTransform;
                if (lookAt != null || clearLookAt) camera.LookAt = lookTransform;
                if (priority.HasValue) camera.Priority = priority.Value;
                var lens = camera.m_Lens;
                if (fieldOfView.HasValue) lens.FieldOfView = fieldOfView.Value;
                if (orthographicSize.HasValue) lens.OrthographicSize = orthographicSize.Value;
                camera.m_Lens = lens;
                if (followOffset != null) { Undo.RecordObject(body, "Configure Cinemachine Follow"); body.m_FollowOffset = new Vector3(followOffset[0], followOffset[1], followOffset[2]); ExtensionObjects.Changed(body); }
                ExtensionObjects.Changed(camera);
            }
            return Inspect();
        }

        [CliCommand("create_cinemachine_camera", "Create a basic Cinemachine 2.x follow/look camera with Undo. Explicitly adds the standard follow and look-at pipeline. Does not create a Brain or save.", Tags = new[] { "cinemachine/authoring" })]
        public static object Create([CliArg("name", "Camera object name.", Required = true)] string name,
            [CliArg("follow", "Optional Follow target.")] ObjectRef follow = null,
            [CliArg("look_at", "Optional LookAt target.")] ObjectRef lookAt = null,
            [CliArg("priority", "Camera priority.")] int priority = 10)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required.");
            var f = follow == null ? null : ExtensionObjects.Resolve(follow).transform;
            var l = lookAt == null ? null : ExtensionObjects.Resolve(lookAt).transform;
            using (new AuthoringUndoScope("Create Cinemachine Camera"))
            {
                var go = new GameObject(name);
                UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);
                Undo.RegisterCreatedObjectUndo(go, "Create Cinemachine Camera");
                var camera = Undo.AddComponent<CinemachineVirtualCamera>(go);
                camera.Follow = f; camera.LookAt = l; camera.Priority = priority;
                camera.AddCinemachineComponent<CinemachineTransposer>(); camera.AddCinemachineComponent<CinemachineComposer>();
                ExtensionObjects.Changed(camera);
                return ObjectResolver.Describe(camera);
            }
        }
    }
}
