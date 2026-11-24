using System;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Pipeline.Editor.Extensions
{
    static class RenderCameraCommands
    {
        [CliCommand("create_render_camera", "Create a native Camera with pose/lens/output settings in one Undo step. Does not add a Cinemachine Brain, choose an input backend or change render pipeline.",Tags=new[]{"rendering/camera"})]
        public static object Create([CliArg("name","New camera name.",Required=true)]string name,
            [CliArg("settings","Optional camera lens/pose/output fields.")]RenderCameraSettings settings=null,
            [CliArg("parent","Optional loaded parent.")]ObjectRef parent=null)
        {
            UiAuthoring.GuardEditMode();if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("name required.");
            settings=settings??new RenderCameraSettings();var owner=parent==null?null:ExtensionObjects.Resolve(parent).transform;
            Validate(settings,null,owner==null?Vector3.zero:owner.position);
            return UiAuthoring.Atomic<object>("Create Render Camera",()=>
            {
                var go=new GameObject(name,typeof(Camera));Undo.RegisterCreatedObjectUndo(go,"Create Render Camera");StageUtility.PlaceGameObjectInCurrentStage(go);
                if(owner!=null){if(go.scene!=owner.gameObject.scene)SceneManager.MoveGameObjectToScene(go,owner.gameObject.scene);go.transform.SetParent(owner,false);}
                var camera=go.GetComponent<Camera>();Apply(camera,settings);ExtensionObjects.Changed(camera);return Describe(camera);
            });
        }

        [CliCommand("configure_render_camera", "Edit native Camera pose, physical/projection lens, clipping, viewport and output with one Undo. Omitted fields stay unchanged. Pipeline-specific settings use their adapter.",Tags=new[]{"rendering/camera"})]
        public static object Configure([CliArg("target","Camera object.",Required=true)]ObjectRef target,
            [CliArg("settings","Explicit settings to edit.",Required=true)]RenderCameraSettings settings)
        {
            UiAuthoring.GuardEditMode();var camera=ExtensionObjects.Require<Camera>(target);Validate(settings,camera);
            return UiAuthoring.Atomic<object>("Configure Render Camera",()=>
            {
                Undo.RecordObjects(new UnityEngine.Object[]{camera,camera.transform},"Configure Render Camera");Apply(camera,settings);ExtensionObjects.Changed(camera);ExtensionObjects.Changed(camera.transform);return Describe(camera);
            });
        }

        [CliCommand("inspect_render_camera", "Read actual native Camera pose/lens/clip/output state and current render pipeline. Does not infer Cinemachine or URP settings from priorities.",Tags=new[]{"rendering/camera"})]
        public static object Inspect([CliArg("target","Camera object.",Required=true)]ObjectRef target)=>Describe(ExtensionObjects.Require<Camera>(target));

        private static void Validate(RenderCameraSettings s,Camera camera,Vector3? initialPosition=null)
        {
            if(s==null)throw new ArgumentException("settings required.");s.ValidateKeys();
            UiAuthoring.Vector(s.position,3,"position");UiAuthoring.Vector(s.rotation,3,"rotation");UiAuthoring.Vector(s.sensor_size,2,"sensor_size");UiAuthoring.Vector(s.lens_shift,2,"lens_shift");UiAuthoring.Vector(s.viewport,4,"viewport");UiAuthoring.Vector(s.background,4,"background");
            foreach(var f in new[]{s.near,s.far,s.field_of_view,s.focal_length,s.orthographic_size,s.depth})if(f.HasValue)ExtensionObjects.Finite(f.Value,"camera setting");
            float near=s.near??(camera==null ? .3f : camera.nearClipPlane),far=s.far??(camera==null?1000:camera.farClipPlane);
            ExtensionObjects.Finite(near,"near");ExtensionObjects.Finite(far,"far");
            if(near<=0||far<=near)throw new ArgumentException("Require 0 < near < far.");
            if(s.field_of_view.HasValue&&(s.field_of_view<=0||s.field_of_view>=180))throw new ArgumentException("field_of_view must be within 0..180 exclusive.");
            if(s.field_of_view.HasValue&&s.focal_length.HasValue)throw new ArgumentException("Choose field_of_view or focal_length.");
            if(s.focal_length<=0||s.orthographic_size<=0||s.sensor_size!=null&&s.sensor_size.Any(x=>x<=0))throw new ArgumentException("Lens sizes must be positive.");
            if(s.focal_length.HasValue&&!(s.physical??(camera!=null&&camera.usePhysicalProperties)))throw new ArgumentException("focal_length requires final physical=true.");
            if(s.viewport!=null&&(s.viewport[2]<=0||s.viewport[3]<=0))throw new ArgumentException("Viewport width/height must be positive.");
            if(s.clear_flags!=null)VisualAuthoring.EnumValue<CameraClearFlags>(s.clear_flags);
            if(s.gate_fit!=null)VisualAuthoring.EnumValue<Camera.GateFitMode>(s.gate_fit);
            if(s.target_texture!=null)VisualAuthoring.Asset<RenderTexture>(s.target_texture);
            if(s.target_texture!=null&&s.clear_target_texture==true)throw new ArgumentException("Cannot assign and clear target texture.");
            if(s.look_at!=null)
            {
                if(s.rotation!=null)throw new ArgumentException("Use look_at or rotation.");
                var target=ExtensionObjects.Resolve(s.look_at).transform;
                var position=s.position==null?(camera==null?initialPosition??Vector3.zero:camera.transform.position):V3(s.position);
                if((target.position-position).sqrMagnitude<1e-10f)throw new ArgumentException("look_at target must differ from camera position.");
            }
        }
        private static void Apply(Camera c,RenderCameraSettings s)
        {
            if(s.position!=null)c.transform.position=V3(s.position);if(s.rotation!=null)c.transform.eulerAngles=V3(s.rotation);
            if(s.look_at!=null)c.transform.LookAt(ExtensionObjects.Resolve(s.look_at).transform,Vector3.up);
            if(s.physical.HasValue)c.usePhysicalProperties=s.physical.Value;
            if(s.sensor_size!=null)c.sensorSize=new Vector2(s.sensor_size[0],s.sensor_size[1]);if(s.lens_shift!=null)c.lensShift=new Vector2(s.lens_shift[0],s.lens_shift[1]);
            if(s.gate_fit!=null)c.gateFit=VisualAuthoring.EnumValue<Camera.GateFitMode>(s.gate_fit);
            if(s.focal_length.HasValue)c.focalLength=s.focal_length.Value;if(s.field_of_view.HasValue)c.fieldOfView=s.field_of_view.Value;
            if(s.orthographic.HasValue)c.orthographic=s.orthographic.Value;if(s.orthographic_size.HasValue)c.orthographicSize=s.orthographic_size.Value;
            if(s.near.HasValue)c.nearClipPlane=s.near.Value;if(s.far.HasValue)c.farClipPlane=s.far.Value;
            if(s.hdr.HasValue)c.allowHDR=s.hdr.Value;if(s.msaa.HasValue)c.allowMSAA=s.msaa.Value;if(s.depth.HasValue)c.depth=s.depth.Value;
            if(s.clear_flags!=null)c.clearFlags=VisualAuthoring.EnumValue<CameraClearFlags>(s.clear_flags);if(s.background!=null)c.backgroundColor=VisualAuthoring.ToColor(s.background);
            if(s.culling_mask.HasValue)c.cullingMask=s.culling_mask.Value;if(s.enabled.HasValue)c.enabled=s.enabled.Value;
            if(s.viewport!=null)c.rect=new Rect(s.viewport[0],s.viewport[1],s.viewport[2],s.viewport[3]);
            if(s.target_texture!=null)c.targetTexture=VisualAuthoring.Asset<RenderTexture>(s.target_texture);else if(s.clear_target_texture==true)c.targetTexture=null;
        }
        private static Vector3 V3(float[] v)=>new Vector3(v[0],v[1],v[2]);
        private static object Describe(Camera c)=>new{handle=ObjectResolver.Describe(c.gameObject),component=ObjectResolver.Describe(c),c.enabled,
            position=new[]{c.transform.position.x,c.transform.position.y,c.transform.position.z},rotation=new[]{c.transform.eulerAngles.x,c.transform.eulerAngles.y,c.transform.eulerAngles.z},
            c.fieldOfView,c.focalLength,c.usePhysicalProperties,sensorSize=new[]{c.sensorSize.x,c.sensorSize.y},lensShift=new[]{c.lensShift.x,c.lensShift.y},gateFit=c.gateFit.ToString(),
            c.nearClipPlane,c.farClipPlane,c.orthographic,c.orthographicSize,c.allowHDR,c.allowMSAA,c.depth,c.cullingMask,
            clearFlags=c.clearFlags.ToString(),background=VisualAuthoring.ColorArray(c.backgroundColor),viewport=new[]{c.rect.x,c.rect.y,c.rect.width,c.rect.height},
            targetTexture=ObjectResolver.Describe(c.targetTexture),pipeline=ObjectResolver.Describe(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline)};
    }
    public sealed class RenderCameraSettings:StrictUiSettings
    {
        public float[] position,rotation,sensor_size,lens_shift,viewport,background;
        public ObjectRef look_at,target_texture;
        public bool? physical,orthographic,hdr,msaa,enabled,clear_target_texture;
        public float? near,far,field_of_view,focal_length,orthographic_size,depth;
        public string clear_flags,gate_fit;
        public int? culling_mask;
    }
}
