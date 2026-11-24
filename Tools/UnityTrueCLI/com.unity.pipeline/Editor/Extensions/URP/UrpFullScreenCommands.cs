using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if TRUECLI_URP_17
using NativeFullScreenPass = UnityEngine.Rendering.Universal.FullScreenPassRendererFeature;
#else
using NativeFullScreenPass = FullScreenPassRendererFeature;
#endif

namespace Unity.Pipeline.URP.Editor
{
    static class UrpFullScreenCommands
    {
        [CliCommand("configure_urp_fullscreen_pass", "Create/edit the official FullScreenPassRendererFeature on one explicit renderer asset. Keeps subasset identity/map; uses native URP14 execution or URP17 RenderGraph. Saves only this renderer; material is shared, never cloned.",Tags=new[]{"rendering/urp"})]
        public static object Configure([CliArg("renderer","Writable ScriptableRendererData asset.",Required=true)]ObjectRef renderer,
            [CliArg("settings","Material and pass settings; supplied fields only.",Required=true)]UrpFullScreenSettings s,
            [CliArg("feature","Exact existing feature ObjectRef; omit to create a new feature.")]ObjectRef feature=null,
            [CliArg("name","New feature name; required for creation.")]string name=null)
        {
            UiAuthoring.GuardEditMode();var data=VisualAuthoring.Writable<ScriptableRendererData>(renderer);if(s==null)throw new ArgumentException("settings required.");s.ValidateKeys();
            var existing=feature==null?null:VisualAuthoring.Writable<NativeFullScreenPass>(feature);
            if(existing!=null&&(!data.rendererFeatures.Contains(existing)||AssetDatabase.GetAssetPath(existing)!=AssetDatabase.GetAssetPath(data)))throw new ArgumentException("Feature does not belong to this renderer asset.");
            if(existing==null&&(string.IsNullOrWhiteSpace(name)||data.rendererFeatures.Any(f=>f!=null&&f.name==name)))throw new ArgumentException("A new unique feature name is required.");
            var material=s.material==null?existing?.passMaterial:VisualAuthoring.Asset<Material>(s.material);
            int pass=s.pass_index??(existing==null?0:existing.passIndex);if(material==null||material.shader==null||!material.shader.isSupported||ShaderUtil.ShaderHasError(material.shader)||pass<0||pass>=material.passCount)throw new ArgumentException("A supported, compiled Material and valid pass_index are required.");
            var injection=s.injection_point==null?(NativeFullScreenPass.InjectionPoint?)null:VisualAuthoring.EnumValue<NativeFullScreenPass.InjectionPoint>(s.injection_point);
            var input=ScriptableRenderPassInput.None;
            if(s.requirements!=null)foreach(var value in s.requirements)input|=VisualAuthoring.EnumValue<ScriptableRenderPassInput>(value);
            // Verify the native serialization contract before adding or changing any asset.
            var serialized=new SerializedObject(data);var map=serialized.FindProperty("m_RendererFeatureMap");if(map==null||!map.isArray)throw new InvalidOperationException("Unsupported renderer featureMap schema.");
            var f=existing;
            if(f==null)
            {
                f=ScriptableObject.CreateInstance<NativeFullScreenPass>();f.name=name;
                AssetDatabase.AddObjectToAsset(f,data);data.rendererFeatures.Add(f);
            }
            f.passMaterial=material;f.passIndex=pass;if(injection.HasValue)f.injectionPoint=injection.Value;
            if(s.fetch_color.HasValue)f.fetchColorBuffer=s.fetch_color.Value;if(s.bind_depth_stencil.HasValue)f.bindDepthStencilAttachment=s.bind_depth_stencil.Value;
            if(s.requirements!=null)f.requirements=input;if(s.active.HasValue)f.SetActive(s.active.Value);
            SyncMap(data);data.SetDirty();EditorUtility.SetDirty(f);EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);AssetDatabase.SaveAssetIfDirty(f);
            return Describe(data,f);
        }

        [CliCommand("remove_urp_fullscreen_pass", "Remove one exact official FullScreenPass feature and its own renderer subasset. Preserve other features and shared pass material; saves renderer. Not Undo.",Tags=new[]{"rendering/urp"})]
        public static object Remove([CliArg("renderer","Writable renderer asset.",Required=true)]ObjectRef renderer,
            [CliArg("feature","Exact feature subasset.",Required=true)]ObjectRef feature,
            [CliArg("confirm","Required for irreversible subasset deletion.")]bool confirm=false,
            [CliArg("dry_run","Validate and preview without changing assets.")]bool dryRun=false)
        {
            UiAuthoring.GuardEditMode();var data=VisualAuthoring.Writable<ScriptableRendererData>(renderer);var f=VisualAuthoring.Writable<NativeFullScreenPass>(feature);
            if(!data.rendererFeatures.Contains(f)||AssetDatabase.GetAssetPath(f)!=AssetDatabase.GetAssetPath(data))throw new ArgumentException("Feature is not this renderer's subasset.");
            if(dryRun)return new{wouldRemove=ObjectResolver.Describe(f),renderer=ObjectResolver.Describe(data),preservedMaterial=ObjectResolver.Describe(f.passMaterial)};
            if(!confirm)throw new ArgumentException("confirm=true is required to delete this non-Undo subasset.");
            var material=ObjectResolver.Describe(f.passMaterial);data.rendererFeatures.Remove(f);SyncMap(data);
            AssetDatabase.RemoveObjectFromAsset(f);UnityEngine.Object.DestroyImmediate(f);data.SetDirty();EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);
            return new{renderer=ObjectResolver.Describe(data),preservedMaterial=material,remaining=data.rendererFeatures.Select(x=>ObjectResolver.Describe(x)).ToArray()};
        }

        [CliCommand("inspect_urp_fullscreen_pass", "Read exact native feature material/pass/injection/requirements, renderer list index and persisted feature localId. No renderer creation.",Tags=new[]{"rendering/urp"})]
        public static object Inspect([CliArg("renderer","Renderer asset.",Required=true)]ObjectRef renderer,
            [CliArg("feature","Exact FullScreenPass subasset.",Required=true)]ObjectRef feature)
        {
            var data=VisualAuthoring.Asset<ScriptableRendererData>(renderer);var f=VisualAuthoring.Asset<NativeFullScreenPass>(feature);if(!data.rendererFeatures.Contains(f))throw new ArgumentException("Feature not in renderer.");return Describe(data,f);
        }

        private static void SyncMap(ScriptableRendererData data)
        {
            var serialized=new SerializedObject(data);serialized.Update();var list=serialized.FindProperty("m_RendererFeatures");var map=serialized.FindProperty("m_RendererFeatureMap");
            if(list==null||map==null)throw new InvalidOperationException("Unsupported renderer feature serialization schema.");
            map.arraySize=data.rendererFeatures.Count;
            for(int i=0;i<data.rendererFeatures.Count;i++)
            {
                long id=0;if(data.rendererFeatures[i]!=null&&!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i],out string guid,out id))throw new InvalidOperationException("Feature has no persistent localId.");
                map.GetArrayElementAtIndex(i).longValue=id;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static object Describe(ScriptableRendererData data,NativeFullScreenPass f)
        {
            int index=data.rendererFeatures.IndexOf(f);var map=new SerializedObject(data).FindProperty("m_RendererFeatureMap");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f,out string guid,out long id);
            return new{renderer=ObjectResolver.Describe(data),feature=ObjectResolver.Describe(f),f.name,index,localId=id,
                mappedLocalId=map!=null&&index>=0&&index<map.arraySize?map.GetArrayElementAtIndex(index).longValue:0,
                active=f.isActive,material=ObjectResolver.Describe(f.passMaterial),passIndex=f.passIndex,injectionPoint=f.injectionPoint.ToString(),f.fetchColorBuffer,requirements=f.requirements.ToString(),f.bindDepthStencilAttachment};
        }

        [CliCommand("create_urp_fullscreen_shader", "Write/import a new editable URP Full Screen Pass shader template. Includes global tint/intensity and invert keyword, consumes official _BlitTexture. No overwrite; returns actual compiler messages.",Tags=new[]{"rendering/urp"})]
        public static object ShaderTemplate([CliArg("path","New .shader path in an existing project folder.",Required=true)]string path,
            [CliArg("shader_name","Unique shader name; no quotes/newlines.",Required=true)]string shaderName)
        {
            UiAuthoring.GuardEditMode();var destination=VisualAuthoring.NewPath(path,".shader");
            if(string.IsNullOrWhiteSpace(shaderName)||shaderName.IndexOfAny(new[]{'"','\n','\r','\\'})>=0)throw new ArgumentException("Invalid shader_name.");
            if(Shader.Find(shaderName)!=null)throw new ArgumentException("Shader name already exists.");
            var source=Template.Replace("__SHADER_NAME__",shaderName);File.WriteAllText(destination,source,new UTF8Encoding(false));AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
            return ShaderDiagnostics(new ObjectRef{Path=destination});
        }

        [CliCommand("inspect_shader_compilation", "Read actual ShaderUtil compile errors/warnings and support state for an explicit Shader asset. Does not treat missing Shader Graph compiler state as success.",Tags=new[]{"rendering/urp"})]
        public static object ShaderDiagnostics([CliArg("shader","Shader asset.",Required=true)]ObjectRef shader)
        {
            var s=VisualAuthoring.Asset<Shader>(shader);return new{handle=ObjectResolver.Describe(s),s.name,s.isSupported,hasErrors=ShaderUtil.ShaderHasError(s),messages=ShaderUtil.GetShaderMessages(s).Select(m=>new{m.message,m.line,m.file,severity=m.severity.ToString(),platform=m.platform.ToString()}).ToArray()};
        }
        private const string Template=@"Shader ""__SHADER_NAME__""
{
 Properties { _Strength (""Strength"", Range(0,1)) = 1 }
 SubShader {
  Tags { ""RenderPipeline""=""UniversalPipeline"" }
  Pass {
   Name ""FullScreen""
   ZTest Always ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _TRUECLI_INVERT
   #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
   #include ""Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl""
   CBUFFER_START(UnityPerMaterial)
    float _Strength;
   CBUFFER_END
   float4 _TrueCliGlobalTint;
   float _TrueCliGlobalIntensity;
   half4 Frag(Varyings input) : SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
    float amount = saturate(_TrueCliGlobalIntensity * _Strength);
    c.rgb = lerp(c.rgb, c.rgb * _TrueCliGlobalTint.rgb, amount);
    #if defined(_TRUECLI_INVERT)
     c.rgb = lerp(c.rgb, 1.0 - c.rgb, amount);
    #endif
    return c;
   }
   ENDHLSL
  }
 }
}";
    }
    public sealed class UrpFullScreenSettings:StrictUiSettings
    {
        public ObjectRef material;
        public int? pass_index;
        public string injection_point;
        public string[] requirements;
        public bool? fetch_color,bind_depth_stencil,active;
    }
}
