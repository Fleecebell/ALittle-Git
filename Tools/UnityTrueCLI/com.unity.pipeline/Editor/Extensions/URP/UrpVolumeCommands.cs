using System;
using System.Collections.Generic;
using System.Linq;
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
    static class UrpVolumeCommands
    {
        [CliCommand("create_urp_volume_profile", "Create a new empty native VolumeProfile asset. Effects are explicit subassets; no overwrite or implicit shared profile clone.",Tags=new[]{"rendering/urp"})]
        public static object Create([CliArg("path","New .asset in an existing project folder.",Required=true)]string path)
        {
            UiAuthoring.GuardEditMode();var destination=VisualAuthoring.NewPath(path,".asset");var p=ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p,destination);AssetDatabase.SaveAssetIfDirty(p);return ObjectResolver.Describe(p);
        }

        [CliCommand("configure_urp_volume", "Add/edit a native Volume on a loaded object with an explicit shared Profile. Changes scene only, with Undo; does not call Volume.profile or save.",Tags=new[]{"rendering/urp"})]
        public static object Volume([CliArg("target","Loaded GameObject/component.",Required=true)]ObjectRef target,
            [CliArg("profile","Explicit VolumeProfile asset.",Required=true)]ObjectRef profile,
            [CliArg("global","Whether this volume affects everywhere.")]bool? global=null,
            [CliArg("weight","0..1.")]float? weight=null,
            [CliArg("priority","Finite blend priority.")]float? priority=null,
            [CliArg("blend_distance","Nonnegative local-volume blend distance.")]float? blendDistance=null)
        {
            UiAuthoring.GuardEditMode();var go=ExtensionObjects.Resolve(target);var p=VisualAuthoring.Asset<VolumeProfile>(profile);
            if(weight.HasValue&&(ExtensionObjects.Finite(weight.Value,"weight")<0||weight>1))throw new ArgumentException("weight must be 0..1.");
            if(priority.HasValue)ExtensionObjects.Finite(priority.Value,"priority");
            if(blendDistance.HasValue&&ExtensionObjects.Finite(blendDistance.Value,"blend_distance")<0)throw new ArgumentException("blend_distance must be nonnegative.");
            var old=go.GetComponent<Volume>();if(old!=null&&old.HasInstantiatedProfile())throw new ArgumentException("Volume has a private instantiated profile; this command edits explicit shared-profile volumes only.");
            bool finalGlobal=global??(old==null||old.isGlobal);
            if(!finalGlobal&&go.GetComponents<Collider>().Length==0)throw new ArgumentException("A local Volume needs an explicit Collider on this object.");
            return UiAuthoring.Atomic<object>("Configure URP Volume",()=>
            {
                var v=old??Undo.AddComponent<Volume>(go);Undo.RecordObject(v,"Configure URP Volume");v.sharedProfile=p;v.isGlobal=finalGlobal;
                if(weight.HasValue)v.weight=weight.Value;if(priority.HasValue)v.priority=priority.Value;if(blendDistance.HasValue)v.blendDistance=blendDistance.Value;
                ExtensionObjects.Changed(v);return new{handle=ObjectResolver.Describe(v),profile=ObjectResolver.Describe(v.sharedProfile),v.isGlobal,v.weight,v.priority,v.blendDistance,instantiatedProfile=v.HasInstantiatedProfile()};
            });
        }

        [CliCommand("configure_urp_effect", "Add/edit a typed native Volume override and save this explicit shared Profile. Bloom/color/vignette/tonemapping/DOF/chromatic/lens distortion/motion blur. All supplied fields validated; omitted parameters and override states preserved.",Tags=new[]{"rendering/urp"})]
        public static object Effect([CliArg("profile","Writable shared VolumeProfile asset; affects all its consumers.",Required=true)]ObjectRef profile,
            [CliArg("settings","Effect name plus typed parameters; override_state defaults true for supplied values only.",Required=true)]UrpEffectSettings s)
        {
            UiAuthoring.GuardEditMode();var p=VisualAuthoring.Writable<VolumeProfile>(profile);Validate(s);
            var type=EffectType(s.effect);var component=p.components.FirstOrDefault(c=>c!=null&&c.GetType()==type);
            bool created=component==null;
            if(created){component=p.Add(type,false);AssetDatabase.AddObjectToAsset(component,p);}
            if(s.active.HasValue)component.active=s.active.Value;
            Apply(component,s);EditorUtility.SetDirty(component);EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);AssetDatabase.SaveAssetIfDirty(component);
            return Describe(p);
        }

        [CliCommand("inspect_urp_volume_profile", "Read typed Volume values, per-parameter overrideState and component.active without instantiating Volume.profile or changing blending state.",Tags=new[]{"rendering/urp"})]
        public static object Inspect([CliArg("profile","VolumeProfile asset.",Required=true)]ObjectRef profile)=>Describe(VisualAuthoring.Asset<VolumeProfile>(profile));

        private static Type EffectType(string effect)
        {
            switch(effect){case "bloom":return typeof(Bloom);case "color":return typeof(ColorAdjustments);case "vignette":return typeof(Vignette);case "tonemapping":return typeof(Tonemapping);case "dof":return typeof(DepthOfField);case "chromatic":return typeof(ChromaticAberration);case "lens_distortion":return typeof(LensDistortion);case "motion_blur":return typeof(MotionBlur);default:throw new ArgumentException("Unknown effect. Use bloom/color/vignette/tonemapping/dof/chromatic/lens_distortion/motion_blur.");}
        }
        private static void Validate(UrpEffectSettings s)
        {
            if(s==null)throw new ArgumentException("settings required.");s.ValidateKeys();EffectType(s.effect);
            string allowed;
            switch(s.effect)
            {
                case "bloom":allowed="intensity threshold scatter clamp tint high_quality";break;
                case "color":allowed="post_exposure contrast saturation hue_shift color_filter";break;
                case "vignette":allowed="intensity smoothness center tint rounded";break;
                case "tonemapping":allowed="mode";break;
                case "dof":allowed="mode focus_distance aperture focal_length gaussian_start gaussian_end gaussian_max_radius high_quality";break;
                case "chromatic":allowed="intensity";break;
                case "lens_distortion":allowed="intensity center scale x_multiplier y_multiplier";break;
                default:allowed="intensity clamp quality";break;
            }
            var fields=new HashSet<string>((allowed+" effect active override_state").Split(' '));
            foreach(var f in typeof(UrpEffectSettings).GetFields())
                if(f.GetValue(s)!=null&&!fields.Contains(f.Name))throw new ArgumentException(f.Name+" is not supported by "+s.effect);
            foreach(var f in new[]{s.intensity,s.threshold,s.scatter,s.clamp,s.post_exposure,s.contrast,s.saturation,s.hue_shift,s.smoothness,s.focus_distance,s.aperture,s.focal_length,s.gaussian_start,s.gaussian_end,s.gaussian_max_radius,s.scale,s.x_multiplier,s.y_multiplier})
                if(f.HasValue)ExtensionObjects.Finite(f.Value,"effect parameter");
            UiAuthoring.Vector(s.tint,4,"tint");UiAuthoring.Vector(s.color_filter,4,"color_filter");UiAuthoring.Vector(s.center,2,"center");
            Range(s.scatter,0,1);Range(s.contrast,-100,100);Range(s.saturation,-100,100);Range(s.hue_shift,-180,180);Range(s.smoothness,0,1);
            Range(s.focus_distance,.1f,float.MaxValue);Range(s.aperture,1,32);Range(s.focal_length,1,300);Range(s.gaussian_start,0,float.MaxValue);Range(s.gaussian_end,0,float.MaxValue);Range(s.gaussian_max_radius,.5f,1.5f);
            Range(s.threshold,0,float.MaxValue);Range(s.scale,.01f,5);Range(s.x_multiplier,0,1);Range(s.y_multiplier,0,1);
            Range(s.intensity,s.effect=="lens_distortion"?-1:0,s.effect=="bloom"?float.MaxValue:1);Range(s.clamp,0,s.effect=="motion_blur"?.2f:float.MaxValue);
            if(s.mode!=null){if(s.effect=="dof")VisualAuthoring.EnumValue<DepthOfFieldMode>(s.mode);else VisualAuthoring.EnumValue<TonemappingMode>(s.mode);}
            if(s.quality!=null)VisualAuthoring.EnumValue<MotionBlurQuality>(s.quality);
        }
        private static void Range(float? v,float min,float max){if(v.HasValue&&(v<min||v>max))throw new ArgumentException("Effect value outside "+min+".."+max);}
        private static void Set<T>(VolumeParameter<T> p,T value,bool state){p.value=value;p.overrideState=state;}
        private static void F(VolumeParameter<float> p,float? value,bool state){if(value.HasValue)Set(p,value.Value,state);}
        private static void Apply(VolumeComponent component,UrpEffectSettings s)
        {
            bool o=s.override_state??true;
            if(component is Bloom b){F(b.intensity,s.intensity,o);F(b.threshold,s.threshold,o);F(b.scatter,s.scatter,o);F(b.clamp,s.clamp,o);if(s.tint!=null)Set(b.tint,VisualAuthoring.ToColor(s.tint),o);if(s.high_quality.HasValue)Set(b.highQualityFiltering,s.high_quality.Value,o);}
            else if(component is ColorAdjustments c){F(c.postExposure,s.post_exposure,o);F(c.contrast,s.contrast,o);F(c.saturation,s.saturation,o);F(c.hueShift,s.hue_shift,o);if(s.color_filter!=null)Set(c.colorFilter,VisualAuthoring.ToColor(s.color_filter),o);}
            else if(component is Vignette v){F(v.intensity,s.intensity,o);F(v.smoothness,s.smoothness,o);if(s.tint!=null)Set(v.color,VisualAuthoring.ToColor(s.tint),o);if(s.center!=null)Set(v.center,new Vector2(s.center[0],s.center[1]),o);if(s.rounded.HasValue)Set(v.rounded,s.rounded.Value,o);}
            else if(component is Tonemapping t){if(s.mode!=null)Set(t.mode,VisualAuthoring.EnumValue<TonemappingMode>(s.mode),o);}
            else if(component is DepthOfField d){if(s.mode!=null)Set(d.mode,VisualAuthoring.EnumValue<DepthOfFieldMode>(s.mode),o);F(d.focusDistance,s.focus_distance,o);F(d.aperture,s.aperture,o);F(d.focalLength,s.focal_length,o);F(d.gaussianStart,s.gaussian_start,o);F(d.gaussianEnd,s.gaussian_end,o);F(d.gaussianMaxRadius,s.gaussian_max_radius,o);if(s.high_quality.HasValue)Set(d.highQualitySampling,s.high_quality.Value,o);}
            else if(component is ChromaticAberration a)F(a.intensity,s.intensity,o);
            else if(component is LensDistortion l){F(l.intensity,s.intensity,o);F(l.scale,s.scale,o);F(l.xMultiplier,s.x_multiplier,o);F(l.yMultiplier,s.y_multiplier,o);if(s.center!=null)Set(l.center,new Vector2(s.center[0],s.center[1]),o);}
            else if(component is MotionBlur m){F(m.intensity,s.intensity,o);F(m.clamp,s.clamp,o);if(s.quality!=null)Set(m.quality,VisualAuthoring.EnumValue<MotionBlurQuality>(s.quality),o);}
        }
        private static object Parameter<T>(VolumeParameter<T> p)
        {
            object v=p.value;if(v is Color c)v=VisualAuthoring.ColorArray(c);else if(v is Vector2 xy)v=new[]{xy.x,xy.y};else if(v is Enum)v=v.ToString();return new{value=v,p.overrideState};
        }
        private static object Describe(VolumeProfile p)=>new{handle=ObjectResolver.Describe(p),components=p.components.Where(c=>c!=null).Select(c=>
        {
            var values=new Dictionary<string,object>();
            if(c is Bloom b){values["intensity"]=Parameter(b.intensity);values["threshold"]=Parameter(b.threshold);values["scatter"]=Parameter(b.scatter);values["clamp"]=Parameter(b.clamp);values["tint"]=Parameter(b.tint);values["high_quality"]=Parameter(b.highQualityFiltering);}
            else if(c is ColorAdjustments a){values["post_exposure"]=Parameter(a.postExposure);values["contrast"]=Parameter(a.contrast);values["saturation"]=Parameter(a.saturation);values["hue_shift"]=Parameter(a.hueShift);values["color_filter"]=Parameter(a.colorFilter);}
            else if(c is Vignette v){values["intensity"]=Parameter(v.intensity);values["smoothness"]=Parameter(v.smoothness);values["center"]=Parameter(v.center);values["tint"]=Parameter(v.color);values["rounded"]=Parameter(v.rounded);}
            else if(c is Tonemapping t)values["mode"]=Parameter(t.mode);
            else if(c is DepthOfField d){values["mode"]=Parameter(d.mode);values["focus_distance"]=Parameter(d.focusDistance);values["aperture"]=Parameter(d.aperture);values["focal_length"]=Parameter(d.focalLength);values["gaussian_start"]=Parameter(d.gaussianStart);values["gaussian_end"]=Parameter(d.gaussianEnd);values["gaussian_max_radius"]=Parameter(d.gaussianMaxRadius);values["high_quality"]=Parameter(d.highQualitySampling);}
            else if(c is ChromaticAberration ch)values["intensity"]=Parameter(ch.intensity);
            else if(c is LensDistortion l){values["intensity"]=Parameter(l.intensity);values["center"]=Parameter(l.center);values["scale"]=Parameter(l.scale);values["x_multiplier"]=Parameter(l.xMultiplier);values["y_multiplier"]=Parameter(l.yMultiplier);}
            else if(c is MotionBlur m){values["intensity"]=Parameter(m.intensity);values["clamp"]=Parameter(m.clamp);values["quality"]=Parameter(m.quality);}
            return new{handle=ObjectResolver.Describe(c),type=c.GetType().Name,c.active,parameters=values};
        }).ToArray()};
    }
    public sealed class UrpEffectSettings:StrictUiSettings
    {
        public string effect,mode,quality;
        public bool? active,override_state,high_quality,rounded;
        public float? intensity,threshold,scatter,clamp,post_exposure,contrast,saturation,hue_shift,smoothness,focus_distance,aperture,focal_length,gaussian_start,gaussian_end,gaussian_max_radius,scale,x_multiplier,y_multiplier;
        public float[] tint,color_filter,center;
    }
}
