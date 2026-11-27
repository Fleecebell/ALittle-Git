using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using Unity.Pipeline.VisualCore;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Extensions
{
    [InitializeOnLoad]
    static class ShaderGlobalCommands
    {
        private const string HistoryKey="TrueCLI.ShaderGlobalHistory.v1";
        private static readonly Dictionary<string,GlobalShaderLease> Live=new Dictionary<string,GlobalShaderLease>();
        [Serializable] private sealed class Entry {public string id,state;public string[] keys,conflicts;}
        [Serializable] private sealed class History {public List<Entry> entries=new List<Entry>();}
        private static History history;
        static ShaderGlobalCommands()
        {
            try{history=JsonUtility.FromJson<History>(SessionState.GetString(HistoryKey,""));}catch{history=null;}
            history=history??new History();
            AssemblyReloadEvents.beforeAssemblyReload+=Cleanup;
            EditorApplication.quitting+=Cleanup;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode||state==PlayModeStateChange.ExitingEditMode)Cleanup();};
        }
        private static void Save(GlobalShaderLease lease)
        {
            history.entries.RemoveAll(e=>e.id==lease.id);
            history.entries.Add(new Entry{id=lease.id,state=lease.State,keys=lease.Keys,conflicts=lease.Conflicts});
            while(history.entries.Count>128)history.entries.RemoveAt(0);
            SessionState.SetString(HistoryKey,JsonUtility.ToJson(history));
        }
        private static void Cleanup(){foreach(var lease in Live.Values){lease.Restore();Save(lease);}Live.Clear();}

        [CliCommand("inspect_shader_global_profile", "Read an explicit persistent profile's typed values without applying globals.",Tags=new[]{"rendering/globals"})]
        public static object InspectProfile([CliArg("profile","GlobalShaderProfile asset.",Required=true)]ObjectRef profile)
        {
            var p=VisualAuthoring.Asset<GlobalShaderProfile>(profile);return new{handle=ObjectResolver.Describe(p),entries=p.entries.Select(Describe).ToArray()};
        }

        [CliCommand("get_shader_globals", "Read explicitly named, typed native Shader globals. Unity cannot enumerate all globals. This does not inspect material-local overrides.",Tags=new[]{"rendering/globals"})]
        public static object Get([CliArg("entries","Array of {name,type}; float/integer/vector/color/matrix/texture/keyword.",Required=true)]ShaderGlobalQuery[] entries)
            =>Queries(entries).Select(v=>Describe(v.Read())).ToArray();

        [CliCommand("set_shader_globals", "Apply a temporary typed global scope after validating all entries. Returns scope ID; restore/Play transition/reload returns compatible keys to baseline. Observable different external writes are preserved. Not Undo or persistent.",Tags=new[]{"rendering/globals"})]
        public static object Set([CliArg("entries","Typed entries. Exactly one scalar/integer/vector/color/matrix/texture/clear_texture/keyword field matching type.")]ShaderGlobalSetting[] entries=null,
            [CliArg("profile","Optional profile instead of entries.")]ObjectRef profile=null)
        {
            if(entries!=null&&profile!=null)throw new ArgumentException("Supply entries or profile, not both.");
            var values=profile==null?Convert(entries,false):VisualAuthoring.Asset<GlobalShaderProfile>(profile).entries;
            if(Live.Count>=32)throw new InvalidOperationException("Restore an active scope first (maximum 32).");
            var lease=new GlobalShaderLease();lease.Apply(values);Live.Add(lease.id,lease);Save(lease);
            return new{id=lease.id,state=lease.State,actual=lease.Actual.Select(Describe).ToArray()};
        }

        [CliCommand("restore_shader_globals", "End a temporary scope. Restore only keys still equal to this scope's native last write; report different external values as conflicts and preserve them. Same-value/ABA writes are not identifiable.",Tags=new[]{"rendering/globals"})]
        public static object Restore([CliArg("id","Scope ID.",Required=true)]string id)
        {
            if(Live.TryGetValue(id,out var lease)){lease.Restore();Save(lease);Live.Remove(id);}
            return Status(id);
        }

        [CliCommand("get_shader_global_scope", "Read scope terminal/active state; recent 128 records survive assembly reload in Editor SessionState.",Tags=new[]{"rendering/globals"})]
        public static object Status([CliArg("id","Scope ID.",Required=true)]string id)
        {
            if(Live.TryGetValue(id,out var live))return new{id=live.id,state=live.State,keys=live.Keys,conflicts=live.Conflicts,active=true};
            var entry=history.entries.LastOrDefault(e=>e.id==id);if(entry==null)throw new ArgumentException("Unknown or expired shader scope ID.");
            return new{entry.id,entry.state,entry.keys,entry.conflicts,active=Live.ContainsKey(id)};
        }

        [CliCommand("create_shader_global_profile", "Create a new persistent typed global profile. No overwrite, no scene save; values are applied only by an explicit scope or Play Mode driver.",Tags=new[]{"rendering/globals"})]
        public static object CreateProfile([CliArg("path","New .asset in an existing project folder.",Required=true)]string path,
            [CliArg("entries","Complete typed values.",Required=true)]ShaderGlobalSetting[] entries)
        {
            UiAuthoring.GuardEditMode();var destination=VisualAuthoring.NewPath(path,".asset");var values=Convert(entries,true);
            var profile=ScriptableObject.CreateInstance<GlobalShaderProfile>();profile.entries=values;
            try{AssetDatabase.CreateAsset(profile,destination);AssetDatabase.SaveAssetIfDirty(profile);return ObjectResolver.Describe(profile);}
            catch{if(!EditorUtility.IsPersistent(profile))UnityEngine.Object.DestroyImmediate(profile);throw;}
        }

        [CliCommand("configure_shader_global_profile", "Replace one explicit profile's complete value list and save that asset. All entries validated first; affects drivers which next acquire this profile.",Tags=new[]{"rendering/globals"})]
        public static object ConfigureProfile([CliArg("target","Writable GlobalShaderProfile asset.",Required=true)]ObjectRef target,
            [CliArg("entries","Complete typed values.",Required=true)]ShaderGlobalSetting[] entries)
        {
            UiAuthoring.GuardEditMode();var p=VisualAuthoring.Writable<GlobalShaderProfile>(target);var values=Convert(entries,true);
            p.entries=values;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);return new{profile=ObjectResolver.Describe(p),entries=p.entries.Select(Describe).ToArray()};
        }

        [CliCommand("configure_shader_global_driver", "Add/edit a scene runtime global profile driver with Undo. Applies on Play enable, returns compatible keys on disable/destroy. Does not save the scene or apply Edit Mode globals.",Tags=new[]{"rendering/globals"})]
        public static object Driver([CliArg("target","Loaded GameObject/component.",Required=true)]ObjectRef target,
            [CliArg("profile","Persistent GlobalShaderProfile.",Required=true)]ObjectRef profile,
            [CliArg("enabled","Enable driver.")]bool enabled=true)
        {
            UiAuthoring.GuardEditMode();var go=ExtensionObjects.Resolve(target);var p=VisualAuthoring.Asset<GlobalShaderProfile>(profile);
            // Validate authoring collisions before creating a component; runtime acquisition repeats this check.
            var keys=new HashSet<string>(p.entries.Select(v=>v.Key));foreach(var v in p.entries)v.Validate();
            if(enabled&&go.activeInHierarchy)
                foreach(var other in ExtensionObjects.Find<GlobalShaderDriver>())
                    if(other.gameObject!=go&&other.isActiveAndEnabled&&other.profile!=null&&other.profile.entries.Any(v=>keys.Contains(v.Key)))
                        throw new ArgumentException("Another enabled driver declares overlapping global keys: "+other.name);
            return UiAuthoring.Atomic<object>("Configure Shader Global Driver",()=>
            {
                var driver=go.GetComponent<GlobalShaderDriver>();if(driver==null)driver=Undo.AddComponent<GlobalShaderDriver>(go);else Undo.RecordObject(driver,"Configure Shader Global Driver");
                driver.profile=p;driver.enabled=enabled;ExtensionObjects.Changed(driver);return InspectDriver(target);
            });
        }

        [CliCommand("inspect_shader_global_driver", "Read persistent profile, runtime lease state and actual explicitly declared global values.",Tags=new[]{"rendering/globals"})]
        public static object InspectDriver([CliArg("target","Driver object.",Required=true)]ObjectRef target)
        {
            var driver=ExtensionObjects.Require<GlobalShaderDriver>(target);
            return new{handle=ObjectResolver.Describe(driver),profile=ObjectResolver.Describe(driver.profile),driver.enabled,driver.isActiveAndEnabled,driver.State,driver.Conflicts,
                declared=driver.profile==null?null:driver.profile.entries.Select(Describe).ToArray(),actual=driver.profile==null?null:driver.profile.entries.Select(v=>Describe(v.Read())).ToArray()};
        }

        [CliCommand("control_shader_global_driver", "Explicitly apply or restore a runtime global driver in Play Mode. Reapply requires releasing the current lease first. Not Undo.",Tags=new[]{"rendering/globals"})]
        public static object ControlDriver([CliArg("target","Driver object.",Required=true)]ObjectRef target,
            [CliArg("action","apply or restore.",Required=true)]string action)
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Driver control requires Play Mode.");
            var driver=ExtensionObjects.Require<GlobalShaderDriver>(target);
            if(action=="apply")driver.Apply();else if(action=="restore")driver.Restore();else throw new ArgumentException("action must be apply or restore.");return InspectDriver(target);
        }

        private static GlobalShaderValue[] Queries(ShaderGlobalQuery[] entries)
        {
            if(entries==null||entries.Length==0||entries.Length>128)throw new ArgumentException("Use 1..128 explicit global entries.");
            return entries.Select(e=>{if(e==null)throw new ArgumentException("Entry cannot be null.");e.ValidateKeys();return Descriptor(e.name,e.type);}).ToArray();
        }
        private static GlobalShaderValue Descriptor(string name,string type)
        {
            if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("name required.");
            var kind=VisualAuthoring.EnumValue<GlobalValueKind>(type);return new GlobalShaderValue{name=name,kind=kind};
        }
        private static GlobalShaderValue[] Convert(ShaderGlobalSetting[] entries,bool persistent)
        {
            if(entries==null||entries.Length==0||entries.Length>128)throw new ArgumentException("Use 1..128 global entries.");
            var values=entries.Select(e=>
            {
                if(e==null)throw new ArgumentException("Entry cannot be null.");e.ValidateKeys();var v=Descriptor(e.name,e.type);
                int count=(e.scalar.HasValue?1:0)+(e.integer.HasValue?1:0)+(e.vector!=null?1:0)+(e.color!=null?1:0)+(e.matrix!=null?1:0)+(e.texture!=null?1:0)+(e.clear_texture==true?1:0)+(e.keyword.HasValue?1:0);
                if(count!=1)throw new ArgumentException("Exactly one value field matching type is required.");
                switch(v.kind)
                {
                    case GlobalValueKind.Float:if(!e.scalar.HasValue)throw new ArgumentException("float requires scalar.");v.scalar=e.scalar.Value;break;
                    case GlobalValueKind.Integer:if(!e.integer.HasValue)throw new ArgumentException("integer requires integer.");v.integer=e.integer.Value;break;
                    case GlobalValueKind.Vector:case GlobalValueKind.Color:
                        var a=v.kind==GlobalValueKind.Color?e.color:e.vector;if(a==null)throw new ArgumentException("Vector/color field does not match type.");UiAuthoring.Vector(a,4,"value");v.vector=new Vector4(a[0],a[1],a[2],a[3]);break;
                    case GlobalValueKind.Matrix:
                        if(e.matrix==null)throw new ArgumentException("matrix requires 16 values in row-major order.");UiAuthoring.Vector(e.matrix,16,"matrix");
                        for(int row=0;row<4;row++)for(int col=0;col<4;col++)v.matrix[row,col]=e.matrix[row*4+col];break;
                    case GlobalValueKind.Texture:
                        if(e.texture==null&&e.clear_texture!=true)throw new ArgumentException("texture requires texture or clear_texture=true.");
                        if(e.texture!=null){if(!ObjectResolver.TryResolve(e.texture,out var t,out _)||!(t is Texture actual)||persistent&&!EditorUtility.IsPersistent(t))throw new ArgumentException("Invalid texture reference.");v.texture=actual;}break;
                    case GlobalValueKind.Keyword:if(!e.keyword.HasValue)throw new ArgumentException("keyword requires keyword bool.");v.enabled=e.keyword.Value;break;
                }
                v.Validate();return v;
            }).ToArray();
            if(values.Select(v=>v.Key).Distinct().Count()!=values.Length)throw new ArgumentException("Duplicate global property name (types share the same property namespace).");return values;
        }
        private static object Describe(GlobalShaderValue v)
        {
            object value;
            switch(v.kind)
            {
                case GlobalValueKind.Float:value=v.scalar;break;case GlobalValueKind.Integer:value=v.integer;break;
                case GlobalValueKind.Vector:case GlobalValueKind.Color:value=new[]{v.vector.x,v.vector.y,v.vector.z,v.vector.w};break;
                case GlobalValueKind.Matrix:var data=new float[16];for(int r=0;r<4;r++)for(int c=0;c<4;c++)data[r*4+c]=v.matrix[r,c];value=data;break;
                case GlobalValueKind.Texture:value=ObjectResolver.Describe(v.texture);break;default:value=v.enabled;break;
            }
            return new{v.name,type=v.kind.ToString(),value};
        }
    }
    public sealed class ShaderGlobalQuery:StrictUiSettings {public string name,type;}
    public sealed class ShaderGlobalSetting:StrictUiSettings
    {
        public string name,type;
        public float? scalar;
        public int? integer;
        public float[] vector,color,matrix;
        public ObjectRef texture;
        public bool? clear_texture,keyword;
    }
}
