using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.Pipeline.Editor.Extensions
{
    // Original adapter. Discovers the user's installed shaders; no vendor source or Assembly-CSharp reference.
    public static class SsuMaterials
    {
        public const string Prefix="Sprite Shaders Ultimate/";
        public static bool IsSsu(Shader shader)=>shader!=null&&shader.name.StartsWith(Prefix,StringComparison.Ordinal);
        public static bool IsGui(Shader shader)=>IsSsu(shader)&&(shader.name.EndsWith("/GUI SSU",StringComparison.Ordinal)||shader.name.EndsWith("/Additive GUI SSU",StringComparison.Ordinal));
        public static Material Require(ObjectRef reference,bool writable)
        {
            var m=writable?VisualAuthoring.Writable<Material>(reference):VisualAuthoring.Asset<Material>(reference);
            if(!IsSsu(m.shader))throw new ArgumentException("Material must use an installed Sprite Shaders Ultimate shader.");return m;
        }
        public static Shader Shader(string name)
        {
            var matches=AssetDatabase.FindAssets("t:Shader").Select(g=>AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g))).Where(s=>IsSsu(s)&&s.name==name).Distinct().ToArray();
            if(matches.Length==0)throw new ArgumentException("SSU shader not installed: "+name+". Install your licensed local SSU in this project first.");
            if(matches.Length>1)throw new ArgumentException("Ambiguous SSU shader name; supply shader_ref. Matching assets: "+string.Join(", ",matches.Select(AssetDatabase.GetAssetPath)));
            return matches[0];
        }
        public static Shader ExactShader(ObjectRef reference)
        {var shader=VisualAuthoring.Asset<Shader>(reference);if(!IsSsu(shader))throw new ArgumentException("shader_ref must identify an installed SSU Shader.");return shader;}
        public static int Property(Shader shader,string name)
        {
            if(string.IsNullOrEmpty(name))throw new ArgumentException("Property name required.");int i=shader.FindPropertyIndex(name);
            if(i<0)throw new ArgumentException("Unknown SSU property: "+name);return i;
        }
        public static string[] Attributes(Shader s,int index)=>s.GetPropertyAttributes(index);
        public static string[] EnumChoices(Shader s,int index)
        {
            var attribute=Attributes(s,index).FirstOrDefault(a=>a.StartsWith("KeywordEnum(",StringComparison.OrdinalIgnoreCase));
            return attribute==null?null:attribute.Substring(attribute.IndexOf('(')+1).TrimEnd(')').Split(',').Select(v=>v.Trim()).ToArray();
        }
        public static string ToggleKeyword(Shader s,int index,out bool toggle,out bool inverse)
        {
            toggle=false;inverse=false;
            foreach(var a in Attributes(s,index))
            {
                var match=Regex.Match(a,@"^(Toggle|ToggleOff)(?:\(([^)]*)\))?$",RegexOptions.IgnoreCase);
                if(!match.Success)continue;
                toggle=true;inverse=match.Groups[1].Value.Equals("ToggleOff",StringComparison.OrdinalIgnoreCase);
                string explicitName=match.Groups[2].Value.Trim();string keyword=explicitName.Length>0?explicitName:(s.GetPropertyName(index)+(inverse?"_OFF":"_ON")).ToUpperInvariant();
                if(s.keywordSpace.FindKeyword(keyword).isValid)return keyword;
                if(explicitName.Length>0)throw new ArgumentException("Declared Toggle keyword is missing from actual shader.keywordSpace: "+keyword);
                return null; // A dynamic bool UI attribute is not proof that a static keyword exists.
            }
            return null;
        }
        public static string EnumKeyword(Shader s,string property,string choice)
        {
            string name=(property+"_"+Regex.Replace(choice,@"\s+","_")).ToUpperInvariant();
            if(!s.keywordSpace.FindKeyword(name).isValid)throw new ArgumentException("KeywordEnum member is not compiled in the installed shader: "+name);return name;
        }
        public static string EffectOwner(Shader shader,int propertyIndex)
        {
            for(int i=propertyIndex-1;i>=0;i--)
            {
                string name=shader.GetPropertyName(i);
                if(name.StartsWith("_Enable",StringComparison.Ordinal))return name;
            }
            return null;
        }
        public static List<Action<Material>> Plan(Shader shader,SsuPropertyValue[] values)
        {
            var actions=new List<Action<Material>>();if(values==null)return actions;
            if(values.Length>512)throw new ArgumentException("At most 512 property edits.");var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var value in values)
            {
                if(value==null)throw new ArgumentException("Property entry cannot be null.");value.ValidateKeys();int index=Property(shader,value.name);
                if(!seen.Add(value.name))throw new ArgumentException("Duplicate property edit: "+value.name);
                int count=(value.number.HasValue?1:0)+(value.integer.HasValue?1:0)+(value.color!=null?1:0)+(value.vector!=null?1:0)+(value.texture!=null?1:0)+(value.clear_texture==true?1:0)+(value.choice!=null?1:0);
                if(count!=1)throw new ArgumentException("One typed value is required for "+value.name);
                var choices=EnumChoices(shader,index);string name=value.name;var kind=shader.GetPropertyType(index);
                if(choices!=null)
                {
                    int selected;
                    if(value.choice!=null)selected=Array.FindIndex(choices,c=>c.Equals(value.choice,StringComparison.Ordinal));
                    else if(value.number.HasValue&&value.number.Value==Mathf.Floor(value.number.Value))selected=(int)value.number.Value;
                    else throw new ArgumentException("KeywordEnum requires a declared choice or integer number: "+name);
                    if(selected<0||selected>=choices.Length)throw new ArgumentException("Unknown KeywordEnum choice: "+name);
                    var keywords=choices.Select(c=>EnumKeyword(shader,name,c)).ToArray();
                    actions.Add(m=>{m.SetFloat(name,selected);for(int k=0;k<keywords.Length;k++)Keyword(m,keywords[k],k==selected);});continue;
                }
                string toggleKeyword=ToggleKeyword(shader,index,out bool toggle,out bool inverse);
                if(toggle)
                {
                    if(!value.number.HasValue)throw new ArgumentException("Toggle requires number: "+name);
                    bool on=ExtensionObjects.Finite(value.number.Value,name)>.5f;
                    actions.Add(m=>{m.SetFloat(name,on?1:0);if(toggleKeyword!=null)Keyword(m,toggleKeyword,inverse?!on:on);});continue;
                }
                switch(kind)
                {
                    case ShaderPropertyType.Float:case ShaderPropertyType.Range:
                        if(!value.number.HasValue)throw new ArgumentException(name+" requires number.");float n=ExtensionObjects.Finite(value.number.Value,name);
                        if(kind==ShaderPropertyType.Range){var range=shader.GetPropertyRangeLimits(index);if(n<range.x||n>range.y)throw new ArgumentException(name+" outside declared range.");}
                        actions.Add(m=>m.SetFloat(name,n));break;
                    case ShaderPropertyType.Int:
                        if(!value.integer.HasValue)throw new ArgumentException(name+" requires integer.");int integer=value.integer.Value;actions.Add(m=>m.SetInteger(name,integer));break;
                    case ShaderPropertyType.Color:
                        if(value.color==null)throw new ArgumentException(name+" requires color[4].");UiAuthoring.Vector(value.color,4,name);var c=VisualAuthoring.ToColor(value.color);actions.Add(m=>m.SetColor(name,c));break;
                    case ShaderPropertyType.Vector:
                        if(value.vector==null)throw new ArgumentException(name+" requires vector[4].");UiAuthoring.Vector(value.vector,4,name);var v=new Vector4(value.vector[0],value.vector[1],value.vector[2],value.vector[3]);actions.Add(m=>m.SetVector(name,v));break;
                    case ShaderPropertyType.Texture:
                        if(value.texture==null&&value.clear_texture!=true)throw new ArgumentException(name+" requires texture or clear_texture.");
                        var texture=value.texture==null?null:VisualAuthoring.Asset<Texture>(value.texture);
                        var dimension=shader.GetPropertyTextureDimension(index);if(texture!=null&&dimension!=TextureDimension.Any&&texture.dimension!=dimension)throw new ArgumentException("Texture dimension mismatch for "+name);
                        actions.Add(m=>m.SetTexture(name,texture));break;
                    default:throw new ArgumentException("Unsupported SSU property type: "+kind);
                }
            }
            return actions;
        }
        public static void Keyword(Material m,string name,bool enabled)
        {
            var keyword=m.shader.keywordSpace.FindKeyword(name);if(!keyword.isValid)throw new ArgumentException("Unknown shader keyword: "+name);
            if(enabled)m.EnableKeyword(keyword);else m.DisableKeyword(keyword);
        }
        public static object ReadProperty(Material material,int index)
        {
            var shader=material.shader;string name=shader.GetPropertyName(index);object value;
            switch(shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color:value=VisualAuthoring.ColorArray(material.GetColor(name));break;
                case ShaderPropertyType.Vector:var v=material.GetVector(name);value=new[]{v.x,v.y,v.z,v.w};break;
                case ShaderPropertyType.Int:value=material.GetInteger(name);break;
                case ShaderPropertyType.Texture:value=ObjectResolver.Describe(material.GetTexture(name));break;
                default:value=material.GetFloat(name);break;
            }
            return new{name,type=shader.GetPropertyType(index).ToString(),value};
        }
        public static object Describe(Material m)=>new{handle=ObjectResolver.Describe(m),shader=ObjectResolver.Describe(m.shader),shaderName=m.shader.name,gui=IsGui(m.shader),
            enabledKeywords=m.enabledKeywords.Select(k=>k.name).ToArray(),effects=Enumerable.Range(0,m.shader.GetPropertyCount()).Where(i=>m.shader.GetPropertyName(i).StartsWith("_Enable",StringComparison.Ordinal)).Select(i=>
            {
                string keyword=ToggleKeyword(m.shader,i,out bool toggle,out bool inverse);float number=m.GetFloat(m.shader.GetPropertyName(i));bool on=number>.5f;bool? native=keyword==null?(bool?)null:m.IsKeywordEnabled(m.shader.keywordSpace.FindKeyword(keyword));
                return new{property=m.shader.GetPropertyName(i),effect=m.shader.GetPropertyName(i).Substring(7),number,enabled=on,keyword,keywordEnabled=native,mismatch=native.HasValue&&native.Value!=(inverse?!on:on)};
            }).ToArray()};
    }

    public static class SsuMaterialCommands
    {
        [CliCommand("get_ssu_catalog", "Discover installed SSU shader variants and actual effects/properties/keywords. Does not ship/install vendor assets or guess SSU version from ASE. Missing SSU returns installed=false.",Tags=new[]{"rendering/ssu"})]
        public static object Catalog([CliArg("shader","Optional exact SSU shader name; omit for available variants.")]string shaderName=null,
            [CliArg("filter","Optional property-name/description filter for one shader.")]string filter=null,
            [CliArg("shader_ref","Exact Shader identity when multiple assets share a name.")]ObjectRef shaderRef=null)
        {
            if(shaderName!=null&&shaderRef!=null)throw new ArgumentException("Supply shader or shader_ref.");
            if(shaderName==null&&shaderRef==null)
            {
                var shaders=AssetDatabase.FindAssets("t:Shader").Select(g=>AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g))).Where(SsuMaterials.IsSsu).Distinct().OrderBy(s=>s.name).ToArray();
                return new{installed=shaders.Length>0,version="unknown; use source fingerprints",variants=shaders.Select(s=>new{handle=ObjectResolver.Describe(s),s.name,gui=SsuMaterials.IsGui(s),s.isSupported,hasErrors=ShaderUtil.ShaderHasError(s),fingerprint=Fingerprint(s)}).ToArray(),source="User-installed Sprite Shaders Ultimate; vendor files are not in this CLI bundle."};
            }
            var shader=shaderRef==null?SsuMaterials.Shader(shaderName):SsuMaterials.ExactShader(shaderRef);
            return new{handle=ObjectResolver.Describe(shader),shader.name,fingerprint=Fingerprint(shader),gui=SsuMaterials.IsGui(shader),shader.isSupported,
                properties=Enumerable.Range(0,shader.GetPropertyCount()).Where(i=>filter==null||shader.GetPropertyName(i).IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0||shader.GetPropertyDescription(i).IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0).Select(i=>
                {
                    string keyword=SsuMaterials.ToggleKeyword(shader,i,out bool toggle,out bool inverse);var choices=SsuMaterials.EnumChoices(shader,i);
                    return new{name=shader.GetPropertyName(i),description=shader.GetPropertyDescription(i),type=shader.GetPropertyType(i).ToString(),attributes=shader.GetPropertyAttributes(i),range=shader.GetPropertyType(i)==ShaderPropertyType.Range?new[]{shader.GetPropertyRangeLimits(i).x,shader.GetPropertyRangeLimits(i).y}:null,
                        effect=shader.GetPropertyName(i).StartsWith("_Enable",StringComparison.Ordinal),toggle,keyword,inverse,choices};
                }).ToArray(),keywords=shader.keywordSpace.keywords.Select(k=>new{k.name,k.isOverridable}).ToArray()};
        }

        [CliCommand("create_ssu_material", "Create a new material using one explicitly installed SSU shader, with fully validated typed properties and native Toggle/KeywordEnum sync. Edit Mode, no overwrite.",Tags=new[]{"rendering/ssu"})]
        public static object Create([CliArg("path","New .mat path in an existing folder.",Required=true)]string path,
            [CliArg("shader","Unique installed shader name; use this or shader_ref.")]string shaderName=null,
            [CliArg("properties","Optional typed property entries.")]SsuPropertyValue[] properties=null,
            [CliArg("shader_ref","Exact installed Shader asset identity.")]ObjectRef shaderRef=null)
        {
            UiAuthoring.GuardEditMode();var destination=VisualAuthoring.NewPath(path,".mat");
            if((shaderName==null)==(shaderRef==null))throw new ArgumentException("Supply exactly one shader or shader_ref.");
            var shader=shaderRef==null?SsuMaterials.Shader(shaderName):SsuMaterials.ExactShader(shaderRef);var plan=SsuMaterials.Plan(shader,properties);
            var material=new Material(shader);
            try{foreach(var apply in plan)apply(material);AssetDatabase.CreateAsset(material,destination);AssetDatabase.SaveAssetIfDirty(material);return SsuMaterials.Describe(material);}
            catch{if(!EditorUtility.IsPersistent(material))UnityEngine.Object.DestroyImmediate(material);throw;}
        }

        [CliCommand("configure_ssu_material", "Edit explicit SSU material asset properties atomically after complete typed validation, synchronizing declared Toggle/KeywordEnum local keywords. Saves asset, Edit Mode only; no partial unknown-member acceptance.",Tags=new[]{"rendering/ssu"})]
        public static object Configure([CliArg("material","Writable SSU material asset.",Required=true)]ObjectRef reference,
            [CliArg("properties","Typed property entries; exactly one value each.",Required=true)]SsuPropertyValue[] properties)
        {
            UiAuthoring.GuardEditMode();var m=SsuMaterials.Require(reference,true);if(properties==null||properties.Length==0)throw new ArgumentException("properties required.");
            return Commit(m,SsuMaterials.Plan(m.shader,properties));
        }

        [CliCommand("configure_ssu_effects", "Configure installed SSU effects by exact _Enable property or suffix and parameters. All edits validate before mutation; edits both Toggle float and actual shader keyword. Editor asset authoring only.",Tags=new[]{"rendering/ssu"})]
        public static object Effects([CliArg("material","Writable SSU material asset.",Required=true)]ObjectRef reference,
            [CliArg("effects","[{effect,enabled?,parameters?}]. Parameters must belong to that effect.",Required=true)]SsuEffectEdit[] effects)
        {
            UiAuthoring.GuardEditMode();var m=SsuMaterials.Require(reference,true);if(effects==null||effects.Length==0||effects.Length>128)throw new ArgumentException("Provide 1..128 effects.");var edits=new List<SsuPropertyValue>();
            foreach(var effect in effects)
            {
                if(effect==null||string.IsNullOrWhiteSpace(effect.effect))throw new ArgumentException("effect required.");effect.ValidateKeys();string toggle=effect.effect.StartsWith("_Enable",StringComparison.Ordinal)?effect.effect:"_Enable"+effect.effect;
                int index=SsuMaterials.Property(m.shader,toggle);SsuMaterials.ToggleKeyword(m.shader,index,out bool isToggle,out _);if(!isToggle)throw new ArgumentException("Not an effect Toggle: "+toggle);
                if(effect.enabled.HasValue)edits.Add(new SsuPropertyValue{name=toggle,number=effect.enabled.Value?1:0});
                if(effect.parameters!=null)foreach(var entry in effect.parameters)
                {
                    if(entry==null||entry.name==null||SsuMaterials.EffectOwner(m.shader,SsuMaterials.Property(m.shader,entry.name))!=toggle
                        ||!entry.name.StartsWith("_"+toggle.Substring(7),StringComparison.Ordinal))throw new ArgumentException("Parameter does not belong to effect "+toggle);edits.Add(entry);
                }
            }
            return Commit(m,SsuMaterials.Plan(m.shader,edits.ToArray()));
        }

        [CliCommand("inspect_ssu_material", "Read actual SSU asset effect Toggle floats separately from enabled local keywords, including mismatches. Optional property names return actual typed values.",Tags=new[]{"rendering/ssu"})]
        public static object Inspect([CliArg("material","SSU material asset.",Required=true)]ObjectRef reference,
            [CliArg("properties","Optional exact property names.")]string[] properties=null)
        {
            var m=SsuMaterials.Require(reference,false);var indices=properties==null?Array.Empty<int>():properties.Select(p=>SsuMaterials.Property(m.shader,p)).ToArray();
            return new{material=SsuMaterials.Describe(m),properties=indices.Select(i=>SsuMaterials.ReadProperty(m,i)).ToArray()};
        }
        private static object Commit(Material m,List<Action<Material>> plan)
        {
            // Native writes below are prevalidated. Keep a complete snapshot for exceptions, not a false Undo/file transaction.
            var backup=new Material(m);
            try{foreach(var apply in plan)apply(m);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);RefreshMaterials(m);return SsuMaterials.Describe(m);}
            catch{m.CopyPropertiesFromMaterial(backup);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);RefreshMaterials(m);throw;}
            finally{UnityEngine.Object.DestroyImmediate(backup);}
        }
        // uGUI is optional, so its adapter subscribes without a core->uGUI assembly dependency.
        public static event Action<Material> MaterialChanged;
        public static void RefreshMaterials(Material m)=>MaterialChanged?.Invoke(m);
        private static string Fingerprint(Shader shader)
        {
            var path=AssetDatabase.GetAssetPath(shader);if(!File.Exists(path))return "unavailable";
            using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
    public sealed class SsuPropertyValue:StrictUiSettings
    {
        public string name,choice;
        public float? number;
        public int? integer;
        public float[] vector,color;
        public ObjectRef texture;
        public bool? clear_texture;
    }
    public sealed class SsuEffectEdit:StrictUiSettings {public string effect;public bool? enabled;public SsuPropertyValue[] parameters;}
}
