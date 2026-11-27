using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Pipeline.Editor.Extensions
{
    public static class UiAuthoring
    {
        public static void GuardEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("UI authoring requires stable Edit Mode.");
        }

        public static T Atomic<T>(string name, Func<T> action)
        {
            GuardEditMode();
            using (var scope = new AuthoringUndoScope(name))
            {
                int group = Undo.GetCurrentGroup();
                try { return action(); }
                catch
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.RevertAllDownToGroup(group);
                    scope.Cancel();
                    throw;
                }
            }
        }

        public static void Vector(float[] value, int length, string name)
        {
            if (value == null) return;
            if (value.Length != length) throw new ArgumentException(name + " requires " + length + " finite numbers.");
            foreach (var item in value) ExtensionObjects.Finite(item, name);
        }

        public static void Validate(RectTransform rect, UiRectSettings settings)
        {
            if (settings == null) throw new ArgumentException("settings is required.");
            settings.ValidateKeys();
            Vector(settings.anchor_min, 2, "anchor_min"); Vector(settings.anchor_max, 2, "anchor_max");
            Vector(settings.pivot, 2, "pivot"); Vector(settings.size_delta, 2, "size_delta");
            Vector(settings.offset_min, 2, "offset_min"); Vector(settings.offset_max, 2, "offset_max");
            Vector(settings.anchored_position, 3, "anchored_position"); Vector(settings.local_position, 3, "local_position");
            Vector(settings.rotation, 3, "rotation"); Vector(settings.scale, 3, "scale");
            bool offsets = settings.offset_min != null || settings.offset_max != null;
            if (offsets && (settings.size_delta != null || settings.anchored_position != null || settings.local_position != null))
                throw new ArgumentException("offsets cannot be mixed with size_delta or position in one call.");
            if (settings.anchored_position != null && settings.local_position != null)
                throw new ArgumentException("anchored_position and local_position are mutually exclusive.");
            var min = settings.anchor_min == null ? (rect == null ? new Vector2(.5f,.5f) : rect.anchorMin) : V2(settings.anchor_min);
            var max = settings.anchor_max == null ? (rect == null ? new Vector2(.5f,.5f) : rect.anchorMax) : V2(settings.anchor_max);
            if (min.x > max.x || min.y > max.y) throw new ArgumentException("anchor_min must not exceed anchor_max.");
        }

        public static void Apply(RectTransform rect, UiRectSettings settings)
        {
            if (settings.anchor_min != null) rect.anchorMin = V2(settings.anchor_min);
            if (settings.anchor_max != null) rect.anchorMax = V2(settings.anchor_max);
            if (settings.pivot != null) rect.pivot = V2(settings.pivot);
            if (settings.size_delta != null) rect.sizeDelta = V2(settings.size_delta);
            if (settings.offset_min != null) rect.offsetMin = V2(settings.offset_min);
            if (settings.offset_max != null) rect.offsetMax = V2(settings.offset_max);
            if (settings.anchored_position != null) rect.anchoredPosition3D = V3(settings.anchored_position);
            if (settings.local_position != null) rect.localPosition = V3(settings.local_position);
            if (settings.rotation != null) rect.localEulerAngles = V3(settings.rotation);
            if (settings.scale != null) rect.localScale = V3(settings.scale);
        }

        public static GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create UI");
            StageUtility.PlaceGameObjectInCurrentStage(go);
            if (parent != null)
            {
                if (go.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
                go.transform.SetParent(parent, false);
            }
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = new Vector2(160,80);
            return go;
        }

        public static object Read(RectTransform rect) => new
        {
            handle = ObjectResolver.Describe(rect.gameObject),
            anchorMin = A(rect.anchorMin), anchorMax = A(rect.anchorMax), pivot = A(rect.pivot),
            sizeDelta = A(rect.sizeDelta), offsetMin = A(rect.offsetMin), offsetMax = A(rect.offsetMax),
            anchoredPosition3D = A(rect.anchoredPosition3D), localPosition = A(rect.localPosition),
            localEulerAngles = A(rect.localEulerAngles), localScale = A(rect.localScale), lossyScale = A(rect.lossyScale),
            rect = new[] { rect.rect.x,rect.rect.y,rect.rect.width,rect.rect.height },
            drivenBy = ObjectResolver.Describe(rect.drivenByObject),
            note = "Immediate state; parent layout may change driven properties on its next normal layout pass."
        };
        private static Vector2 V2(float[] value) => new Vector2(value[0],value[1]);
        private static Vector3 V3(float[] value) => new Vector3(value[0],value[1],value[2]);
        private static float[] A(Vector2 value) => new[] {value.x,value.y};
        private static float[] A(Vector3 value) => new[] {value.x,value.y,value.z};
    }

    static class UiStructureCommands
    {
        [CliCommand("configure_ui_canvas_group", "Add/edit native CanvasGroup opacity, interaction and raycast propagation with Undo. No input backend or scene save.",Tags=new[]{"ui/authoring"})]
        public static object Group([CliArg("target","Loaded UI object.",Required=true)]ObjectRef target,
            [CliArg("alpha","Opacity 0..1.")]float? alpha=null,
            [CliArg("interactable","Enable child Selectable interaction.")]bool? interactable=null,
            [CliArg("blocks_raycasts","Whether children receive graphic raycasts.")]bool? blocksRaycasts=null,
            [CliArg("ignore_parent_groups","Ignore ancestor CanvasGroup restrictions.")]bool? ignoreParentGroups=null)
        {
            UiAuthoring.GuardEditMode();var rect=ExtensionObjects.Require<RectTransform>(target);
            if(alpha.HasValue&&(ExtensionObjects.Finite(alpha.Value,"alpha")<0||alpha>1))throw new ArgumentException("alpha must be 0..1.");
            return UiAuthoring.Atomic<object>("Configure Canvas Group",()=>
            {
                var g=rect.GetComponent<CanvasGroup>();if(g==null)g=Undo.AddComponent<CanvasGroup>(rect.gameObject);else Undo.RecordObject(g,"Configure Canvas Group");
                if(alpha.HasValue)g.alpha=alpha.Value;if(interactable.HasValue)g.interactable=interactable.Value;if(blocksRaycasts.HasValue)g.blocksRaycasts=blocksRaycasts.Value;
                if(ignoreParentGroups.HasValue)g.ignoreParentGroups=ignoreParentGroups.Value;ExtensionObjects.Changed(g);
                return new{handle=ObjectResolver.Describe(g),g.alpha,g.interactable,g.blocksRaycasts,g.ignoreParentGroups};
            });
        }
        [CliCommand("configure_ui_rect", "Edit RectTransform anchors, offsets and 3D local pose atomically with Undo. Do not mix offsets with size/position, or local with anchored position. No layout rebuild.", Tags=new[]{"ui/authoring"})]
        public static object Rect([CliArg("target","Loaded RectTransform object.",Required=true)] ObjectRef target,
            [CliArg("settings","Optional fields: anchor_min/max, pivot, size_delta, offset_min/max (2 floats), anchored_position/local_position, rotation, scale (3 floats).",Required=true)] UiRectSettings settings)
        {
            UiAuthoring.GuardEditMode();
            var rect = ExtensionObjects.Require<RectTransform>(target); UiAuthoring.Validate(rect,settings);
            return UiAuthoring.Atomic("Configure UI Rect", () =>
            {
                Undo.RecordObject(rect,"Configure UI Rect"); UiAuthoring.Apply(rect,settings); ExtensionObjects.Changed(rect);
                return new { requested=settings, actual=UiAuthoring.Read(rect) };
            });
        }

        [CliCommand("create_ui_canvas", "Create a Canvas/RectTransform in the current stage. Camera and WorldSpace modes require an explicit camera. Does not add or change EventSystem, scaler or raycaster.", Tags=new[]{"ui/authoring"})]
        public static object Create([CliArg("name","New object name.",Required=true)] string name,
            [CliArg("mode","overlay, camera or world.")] string mode="overlay",
            [CliArg("camera","Required Camera handle for camera/world modes.")] ObjectRef camera=null,
            [CliArg("parent","Optional existing parent in the current stage.")] ObjectRef parent=null,
            [CliArg("rect","Optional initial RectTransform settings.")] UiRectSettings rect=null)
        {
            UiAuthoring.GuardEditMode();
            if(string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required.");
            var renderMode = ParseMode(mode); var cam=ResolveCamera(renderMode,camera,null);
            float initialDistance=renderMode==RenderMode.ScreenSpaceCamera?InteriorDistance(cam):0;
            var parentTransform=parent==null?null:ExtensionObjects.Resolve(parent).transform;
            if(rect!=null) UiAuthoring.Validate(null,rect);
            return UiAuthoring.Atomic<object>("Create UI Canvas",()=>
            {
                var go=UiAuthoring.NewRect(name,parentTransform); var canvas=Undo.AddComponent<Canvas>(go);
                canvas.renderMode=renderMode; canvas.worldCamera=cam;
                if(renderMode==RenderMode.ScreenSpaceCamera) canvas.planeDistance=initialDistance;
                if(rect!=null)UiAuthoring.Apply((RectTransform)go.transform,rect);
                ExtensionObjects.Changed(canvas);return Describe(canvas);
            });
        }

        [CliCommand("configure_ui_canvas", "Configure a Canvas mode, explicit camera, plane distance and sorting atomically with Undo. Omitted values stay unchanged. No scene save or input-system mutation.",Tags=new[]{"ui/authoring"})]
        public static object CanvasSettings([CliArg("target","Canvas object.",Required=true)] ObjectRef target,
            [CliArg("mode","Optional overlay/camera/world.")] string mode=null,
            [CliArg("camera","Explicit Camera; required when switching from Overlay to Camera/WorldSpace.")] ObjectRef camera=null,
            [CliArg("plane_distance","Distance from camera (Camera mode only). Must lie inside clipping planes.")] float? planeDistance=null,
            [CliArg("sorting_order","Canvas order -32768..32767.")] int? sortingOrder=null,
            [CliArg("override_sorting","Override parent Canvas order.")] bool? overrideSorting=null)
        {
            UiAuthoring.GuardEditMode();var canvas=ExtensionObjects.Require<Canvas>(target);
            var renderMode=mode==null?canvas.renderMode:ParseMode(mode);
            bool enteringOverlay=renderMode==RenderMode.ScreenSpaceOverlay&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay;
            var cam=ResolveCamera(renderMode,camera,enteringOverlay?null:canvas.worldCamera);
            if(sortingOrder.HasValue&&(sortingOrder<short.MinValue||sortingOrder>short.MaxValue))throw new ArgumentException("sorting_order exceeds Canvas range.");
            if(planeDistance.HasValue)
            {
                ExtensionObjects.Finite(planeDistance.Value,"plane_distance");
                if(renderMode!=RenderMode.ScreenSpaceCamera||planeDistance<=cam.nearClipPlane||planeDistance>=cam.farClipPlane)throw new ArgumentException("plane_distance requires Camera mode and must lie within camera clipping planes.");
            }
            float finalDistance=planeDistance??canvas.planeDistance;
            if(renderMode==RenderMode.ScreenSpaceCamera)ExtensionObjects.Finite(finalDistance,"plane_distance");
            if(renderMode==RenderMode.ScreenSpaceCamera && (finalDistance<=cam.nearClipPlane||finalDistance>=cam.farClipPlane))
                throw new ArgumentException("Existing plane_distance lies outside the selected camera clipping planes; supply a valid plane_distance explicitly.");
            return UiAuthoring.Atomic<object>("Configure UI Canvas",()=>
            {
                // A mode/camera/distance change can drive the Canvas RectTransform on a later layout pass.
                Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject,"Configure UI Canvas");
                Undo.RecordObject(canvas,"Configure UI Canvas");
                if(canvas.renderMode!=renderMode)canvas.renderMode=renderMode;
                if(camera!=null||enteringOverlay)canvas.worldCamera=cam;
                if(planeDistance.HasValue)canvas.planeDistance=planeDistance.Value;
                if(sortingOrder.HasValue)canvas.sortingOrder=sortingOrder.Value;
                if(overrideSorting.HasValue)canvas.overrideSorting=overrideSorting.Value;
                ExtensionObjects.Changed(canvas);return Describe(canvas);
            });
        }

        private static RenderMode ParseMode(string mode)
        {
            switch(mode){case "overlay":return RenderMode.ScreenSpaceOverlay;case "camera":return RenderMode.ScreenSpaceCamera;case "world":return RenderMode.WorldSpace;default:throw new ArgumentException("mode must be overlay, camera or world.");}
        }
        private static Camera ResolveCamera(RenderMode mode,ObjectRef reference,Camera current)
        {
            if(mode==RenderMode.ScreenSpaceOverlay){if(reference!=null)throw new ArgumentException("Overlay has no event camera; omit camera.");return current;}
            var camera=reference==null?current:ExtensionObjects.Require<Camera>(reference);
            if(camera==null)throw new ArgumentException("Camera/WorldSpace requires an explicit camera.");
            ExtensionObjects.Resolve(new ObjectRef{InstanceId=PipelineUtils.GetObjectId(camera)});
            return camera;
        }
        private static float InteriorDistance(Camera camera)
        {
            float near=camera.nearClipPlane,far=camera.farClipPlane;
            ExtensionObjects.Finite(near,"nearClipPlane");ExtensionObjects.Finite(far,"farClipPlane");
            float value=near+(far-near)*.5f;
            if(!(value>near&&value<far))throw new ArgumentException("Camera clipping planes have no representable interior distance.");
            return value;
        }
        private static object Describe(Canvas canvas)=>new {handle=ObjectResolver.Describe(canvas.gameObject),mode=canvas.renderMode.ToString(),camera=ObjectResolver.Describe(canvas.worldCamera),canvas.sortingOrder,canvas.overrideSorting,canvas.planeDistance,actual=UiAuthoring.Read((RectTransform)canvas.transform)};
    }

    [Serializable]
    public abstract class StrictUiSettings : IStructuredCommandInput
    {
        [JsonExtensionData] private IDictionary<string,JToken> unknownFields;
        public void ValidateKeys()
        {
            if(unknownFields!=null&&unknownFields.Count!=0)throw new ArgumentException("Unknown UI settings field: "+string.Join(", ",unknownFields.Keys));
        }
    }
    public sealed class UiRectSettings : StrictUiSettings
    {
        public float[] anchor_min,anchor_max,pivot,size_delta,offset_min,offset_max;
        public float[] anchored_position,local_position,rotation,scale;
    }
}
