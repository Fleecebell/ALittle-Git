using System;
using System.Linq;
using System.IO;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.Pipeline.URP.Editor
{
    static class UrpCameraCommands
    {
        [CliCommand("create_urp_pipeline", "Create a new URP asset and Universal Renderer from the installed package's native renderer template. Does not select the project/Quality pipeline or overwrite assets.",Tags=new[]{"rendering/urp"})]
        public static object CreatePipeline([CliArg("path","New pipeline .asset path in an existing project folder.",Required=true)]string path,
            [CliArg("renderer_path","New renderer .asset path in an existing project folder.",Required=true)]string rendererPath)
        {
            UiAuthoring.GuardEditMode();var assetPath=VisualAuthoring.NewPath(path,".asset");var dataPath=VisualAuthoring.NewPath(rendererPath,".asset");
            if(string.Equals(assetPath,dataPath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Pipeline and renderer paths must differ.");
            var template=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRendererData.asset");
            if(template==null)throw new InvalidOperationException("Installed URP has no native Universal Renderer template.");
            var data=UnityEngine.Object.Instantiate(template);data.name=Path.GetFileNameWithoutExtension(dataPath);
            // Do not share a template feature subasset with the new renderer.
            data.rendererFeatures.Clear();var serialized=new SerializedObject(data);var map=serialized.FindProperty("m_RendererFeatureMap");
            if(map!=null){map.arraySize=0;serialized.ApplyModifiedPropertiesWithoutUndo();}
            UniversalRenderPipelineAsset asset=null;
            try
            {
                AssetDatabase.CreateAsset(data,dataPath);asset=UniversalRenderPipelineAsset.Create(data);asset.name=Path.GetFileNameWithoutExtension(assetPath);AssetDatabase.CreateAsset(asset,assetPath);
                AssetDatabase.SaveAssetIfDirty(asset);AssetDatabase.SaveAssetIfDirty(data);return new{pipeline=ObjectResolver.Describe(asset),renderer=ObjectResolver.Describe(data)};
            }
            catch
            {
                if(AssetDatabase.LoadMainAssetAtPath(assetPath)==asset&&asset!=null)AssetDatabase.DeleteAsset(assetPath);else if(asset!=null&&!EditorUtility.IsPersistent(asset))UnityEngine.Object.DestroyImmediate(asset);
                if(AssetDatabase.LoadMainAssetAtPath(dataPath)==data)AssetDatabase.DeleteAsset(dataPath);else if(!EditorUtility.IsPersistent(data))UnityEngine.Object.DestroyImmediate(data);throw;
            }
        }
        [CliCommand("inspect_urp", "Read current/default/Quality pipeline assets, configured renderer mapping and camera/Volume facts. Does not allocate renderers or Volume.profile instances; GPU use is verified separately.",Tags=new[]{"rendering/urp"})]
        public static object Inspect([CliArg("camera","Optional specific Camera; omit for current-stage cameras.")]ObjectRef camera=null)
        {
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cameras=camera==null?ExtensionObjects.Find<Camera>():new[]{ExtensionObjects.Require<Camera>(camera)};
            return new{package=UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Where(p=>p.name=="com.unity.render-pipelines.universal").Select(p=>new{p.name,p.version}).FirstOrDefault(),
                current=ObjectResolver.Describe(GraphicsSettings.currentRenderPipeline),defaultPipeline=ObjectResolver.Describe(GraphicsSettings.renderPipelineAsset),qualityOverride=ObjectResolver.Describe(QualitySettings.renderPipeline),qualityLevel=QualitySettings.GetQualityLevel(),
                renderers=pipeline==null?null:RendererList(pipeline).Select((r,i)=>new{index=i,handle=ObjectResolver.Describe(r),type=r?.GetType().FullName,
                    features=r==null?null:r.rendererFeatures.Select((f,j)=>new{index=j,handle=ObjectResolver.Describe(f),name=f?.name,type=f?.GetType().FullName,active=f!=null&&f.isActive}).ToArray()}).ToArray(),
                cameras=cameras.Select(Describe).ToArray(),volumes=ExtensionObjects.Find<Volume>().Select(v=>new{handle=ObjectResolver.Describe(v),v.isGlobal,v.priority,v.weight,v.blendDistance,layer=v.gameObject.layer,
                    sharedProfile=ObjectResolver.Describe(v.sharedProfile),instantiatedProfile=v.HasInstantiatedProfile(),v.enabled}).ToArray()};
        }

        [CliCommand("configure_urp_camera", "Add/edit UniversalAdditionalCameraData with Undo. Typed post-processing, AA, textures, volume mask/trigger and validated Base-to-Overlay stack. Does not change project pipeline.",Tags=new[]{"rendering/urp"})]
        public static object Configure([CliArg("target","Camera object.",Required=true)]ObjectRef target,
            [CliArg("settings","Only explicit fields change.",Required=true)]UrpCameraSettings s)
        {
            UiAuthoring.GuardEditMode();var camera=ExtensionObjects.Require<Camera>(target);if(s==null)throw new ArgumentException("settings required.");s.ValidateKeys();
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;if(pipeline==null)throw new ArgumentException("Current render pipeline is not URP.");
            var existing=camera.GetComponent<UniversalAdditionalCameraData>();
            var renderType=s.render_type==null?(existing==null?CameraRenderType.Base:existing.renderType):VisualAuthoring.EnumValue<CameraRenderType>(s.render_type);
            var aa=s.antialiasing==null?(AntialiasingMode?)null:VisualAuthoring.EnumValue<AntialiasingMode>(s.antialiasing);
            var quality=s.antialiasing_quality==null?(AntialiasingQuality?)null:VisualAuthoring.EnumValue<AntialiasingQuality>(s.antialiasing_quality);
            Transform trigger=s.volume_trigger==null?null:ExtensionObjects.Resolve(s.volume_trigger).transform;
            if(s.volume_trigger!=null&&s.clear_volume_trigger==true)throw new ArgumentException("Cannot assign and clear volume trigger.");
            if(s.renderer_index.HasValue)RendererAt(pipeline,s.renderer_index.Value,true);
            var baseRenderer=RendererAt(pipeline,s.renderer_index??RendererIndex(existing),false);
            Camera[] stack=null;
            if(s.stack!=null)
            {
                if(renderType!=CameraRenderType.Base)throw new ArgumentException("Only Base cameras have a stack.");
                stack=s.stack.Select(r=>ExtensionObjects.Require<Camera>(r)).ToArray();
                if(stack.Distinct().Count()!=stack.Length||stack.Contains(camera))throw new ArgumentException("Stack cannot contain duplicate/self cameras.");
            }
            if(renderType==CameraRenderType.Base)
            {
                var finalStack=stack??(existing!=null&&existing.renderType==CameraRenderType.Base?Stack(existing):Array.Empty<Camera>());
                if(finalStack.Length>0&&!(baseRenderer is UniversalRendererData))throw new ArgumentException("Stacking requires a native Universal Renderer.");
                foreach(var c in finalStack)
                {
                    if(c==null)throw new ArgumentException("Stack contains a missing camera; supply a valid stack explicitly.");
                    var data=c.GetComponent<UniversalAdditionalCameraData>();
                    if(data==null||data.renderType!=CameraRenderType.Overlay)throw new ArgumentException("Stack members must already be Overlay cameras.");
                    var renderer=RendererAt(pipeline,RendererIndex(data),false);
                    if(baseRenderer==null||renderer==null||baseRenderer.GetType()!=renderer.GetType())throw new ArgumentException("Stack renderer types must match.");
                }
            }
            else
                foreach(var other in ExtensionObjects.Find<UniversalAdditionalCameraData>())
                    if(other!=existing&&other.renderType==CameraRenderType.Base&&Stack(other).Contains(camera))
                    {
                        var parentRenderer=RendererAt(pipeline,RendererIndex(other),false);
                        if(baseRenderer==null||parentRenderer==null||baseRenderer.GetType()!=parentRenderer.GetType())throw new ArgumentException("Overlay renderer would be incompatible with a Base stack consumer.");
                    }
            if(renderType==CameraRenderType.Overlay&&existing!=null&&existing.renderType==CameraRenderType.Base&&Stack(existing).Length!=0)
                throw new ArgumentException("Clear the Base camera stack before converting to Overlay.");
            if(renderType==CameraRenderType.Base)
                foreach(var other in ExtensionObjects.Find<UniversalAdditionalCameraData>())
                    if(other!=existing&&other.renderType==CameraRenderType.Base&&Stack(other).Contains(camera))throw new ArgumentException("Remove this camera from its parent stack before converting to Base.");
            return UiAuthoring.Atomic<object>("Configure URP Camera",()=>
            {
                var data=existing??Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);Undo.RecordObject(data,"Configure URP Camera");
                data.renderType=renderType;if(s.renderer_index.HasValue)data.SetRenderer(s.renderer_index.Value);
                if(s.post_processing.HasValue)data.renderPostProcessing=s.post_processing.Value;if(aa.HasValue)data.antialiasing=aa.Value;if(quality.HasValue)data.antialiasingQuality=quality.Value;
                if(s.depth_texture.HasValue)data.requiresDepthTexture=s.depth_texture.Value;if(s.color_texture.HasValue)data.requiresColorTexture=s.color_texture.Value;
                if(s.shadows.HasValue)data.renderShadows=s.shadows.Value;if(s.dithering.HasValue)data.dithering=s.dithering.Value;if(s.stop_nan.HasValue)data.stopNaN=s.stop_nan.Value;
                if(s.volume_mask.HasValue)data.volumeLayerMask=s.volume_mask.Value;
                if(trigger!=null)data.volumeTrigger=trigger;else if(s.clear_volume_trigger==true)data.volumeTrigger=null;
                if(stack!=null)
                {
                    var serialized=new SerializedObject(data);var array=serialized.FindProperty("m_Cameras");array.arraySize=stack.Length;
                    for(int i=0;i<stack.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=stack[i];serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                ExtensionObjects.Changed(data);return Describe(camera);
            });
        }

        [CliCommand("configure_urp_pipeline", "Edit one explicit URP asset's public HDR/depth/opaque texture/MSAA/render scale/shadow-distance settings and save that asset. Does not install/select a pipeline.",Tags=new[]{"rendering/urp"})]
        public static object Pipeline([CliArg("target","Writable URP asset.",Required=true)]ObjectRef target,
            [CliArg("settings","Only explicit fields change.",Required=true)]UrpPipelineSettings s)
        {
            UiAuthoring.GuardEditMode();var asset=VisualAuthoring.Writable<UniversalRenderPipelineAsset>(target);if(s==null)throw new ArgumentException("settings required.");s.ValidateKeys();
            if(s.render_scale.HasValue&&(ExtensionObjects.Finite(s.render_scale.Value,"render_scale")<.1f||s.render_scale>2))throw new ArgumentException("render_scale must be .1..2.");
            if(s.shadow_distance.HasValue&&ExtensionObjects.Finite(s.shadow_distance.Value,"shadow_distance")<0)throw new ArgumentException("shadow_distance must be nonnegative.");
            if(s.msaa.HasValue&&!new[]{1,2,4,8}.Contains(s.msaa.Value))throw new ArgumentException("msaa must be 1,2,4,8.");
            if(s.hdr.HasValue)asset.supportsHDR=s.hdr.Value;if(s.depth_texture.HasValue)asset.supportsCameraDepthTexture=s.depth_texture.Value;if(s.opaque_texture.HasValue)asset.supportsCameraOpaqueTexture=s.opaque_texture.Value;
            if(s.msaa.HasValue)asset.msaaSampleCount=s.msaa.Value;if(s.render_scale.HasValue)asset.renderScale=s.render_scale.Value;if(s.shadow_distance.HasValue)asset.shadowDistance=s.shadow_distance.Value;
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
            return new{handle=ObjectResolver.Describe(asset),asset.supportsHDR,asset.supportsCameraDepthTexture,asset.supportsCameraOpaqueTexture,asset.msaaSampleCount,asset.renderScale,asset.shadowDistance};
        }
        internal static ScriptableRendererData[] RendererList(UniversalRenderPipelineAsset pipeline)
        {
            var field=new SerializedObject(pipeline).FindProperty("m_RendererDataList");if(field==null||!field.isArray)throw new InvalidOperationException("Unsupported URP renderer serialization schema.");
            var data=new ScriptableRendererData[field.arraySize];for(int i=0;i<data.Length;i++)data[i]=field.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;return data;
        }
        private static int RendererIndex(UniversalAdditionalCameraData data)=>data==null?-1:new SerializedObject(data).FindProperty("m_RendererIndex").intValue;
        private static ScriptableRendererData RendererAt(UniversalRenderPipelineAsset pipeline,int index,bool strict)
        {
            var list=RendererList(pipeline);int defaultIndex=new SerializedObject(pipeline).FindProperty("m_DefaultRendererIndex").intValue;
            if(index==-1)index=defaultIndex;
            if(index<0||index>=list.Length||list[index]==null){if(strict)throw new ArgumentException("renderer_index must identify an existing renderer or -1 for default.");index=defaultIndex;}
            return index>=0&&index<list.Length?list[index]:null;
        }
        private static object Describe(Camera camera)
        {
            var data=camera.GetComponent<UniversalAdditionalCameraData>();var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(data==null)return new{camera=ObjectResolver.Describe(camera),configured=false};
            return new{camera=ObjectResolver.Describe(camera),component=ObjectResolver.Describe(data),renderType=data.renderType.ToString(),rendererIndex=RendererIndex(data),
                configuredRenderer=pipeline==null?null:ObjectResolver.Describe(RendererAt(pipeline,RendererIndex(data),false)),
                data.renderPostProcessing,antialiasing=data.antialiasing.ToString(),antialiasingQuality=data.antialiasingQuality.ToString(),data.requiresDepthTexture,data.requiresColorTexture,
                data.renderShadows,data.dithering,data.stopNaN,volumeMask=(int)data.volumeLayerMask,volumeTrigger=ObjectResolver.Describe(data.volumeTrigger),
                stack=data.renderType==CameraRenderType.Base?Stack(data).Select(c=>ObjectResolver.Describe(c)).ToArray():null};
        }
        private static Camera[] Stack(UniversalAdditionalCameraData data)
        {
            var array=new SerializedObject(data).FindProperty("m_Cameras");if(array==null||!array.isArray)throw new InvalidOperationException("Unsupported URP camera stack schema.");
            var values=new Camera[array.arraySize];for(int i=0;i<values.Length;i++)values[i]=array.GetArrayElementAtIndex(i).objectReferenceValue as Camera;return values;
        }
    }
    public sealed class UrpCameraSettings:StrictUiSettings
    {
        public string render_type,antialiasing,antialiasing_quality;
        public bool? post_processing,depth_texture,color_texture,shadows,dithering,stop_nan,clear_volume_trigger;
        public int? renderer_index,volume_mask;
        public ObjectRef volume_trigger;
        public ObjectRef[] stack;
    }
    public sealed class UrpPipelineSettings:StrictUiSettings
    {
        public bool? hdr,depth_texture,opaque_texture;
        public int? msaa;
        public float? render_scale,shadow_distance;
    }
}
