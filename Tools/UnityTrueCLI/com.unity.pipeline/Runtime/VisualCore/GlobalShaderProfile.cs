using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.Pipeline.VisualCore
{
    public enum GlobalValueKind { Float, Integer, Vector, Color, Matrix, Texture, Keyword }
    [Serializable]
    public sealed class GlobalShaderValue
    {
        public string name;
        public GlobalValueKind kind;
        public float scalar;
        public int integer;
        public Vector4 vector;
        public Matrix4x4 matrix = Matrix4x4.identity;
        public Texture texture;
        public bool enabled;
        public string Key => (kind==GlobalValueKind.Keyword?"K:":"P:")+name;
        public GlobalShaderValue Clone() => (GlobalShaderValue)MemberwiseClone();
        public GlobalShaderValue Read()
        {
            var v=Clone();
            switch(kind)
            {
                case GlobalValueKind.Float:v.scalar=Shader.GetGlobalFloat(name);break;
                case GlobalValueKind.Integer:v.integer=Shader.GetGlobalInteger(name);break;
                case GlobalValueKind.Vector:v.vector=Shader.GetGlobalVector(name);break;
                case GlobalValueKind.Color:v.vector=Shader.GetGlobalColor(name);break;
                case GlobalValueKind.Matrix:v.matrix=Shader.GetGlobalMatrix(name);break;
                case GlobalValueKind.Texture:v.texture=Shader.GetGlobalTexture(name);break;
                case GlobalValueKind.Keyword:v.enabled=Shader.IsKeywordEnabled(name);break;
            }
            return v;
        }
        public void Write()
        {
            switch(kind)
            {
                case GlobalValueKind.Float:Shader.SetGlobalFloat(name,scalar);break;
                case GlobalValueKind.Integer:Shader.SetGlobalInteger(name,integer);break;
                case GlobalValueKind.Vector:Shader.SetGlobalVector(name,vector);break;
                case GlobalValueKind.Color:Shader.SetGlobalColor(name,(Color)vector);break;
                case GlobalValueKind.Matrix:Shader.SetGlobalMatrix(name,matrix);break;
                case GlobalValueKind.Texture:Shader.SetGlobalTexture(name,texture);break;
                case GlobalValueKind.Keyword:if(enabled)Shader.EnableKeyword(name);else Shader.DisableKeyword(name);break;
            }
        }
        public bool Same(GlobalShaderValue v)
        {
            switch(kind)
            {
                case GlobalValueKind.Float:return scalar.Equals(v.scalar);
                case GlobalValueKind.Integer:return integer==v.integer;
                case GlobalValueKind.Vector:case GlobalValueKind.Color:return vector.Equals(v.vector);
                case GlobalValueKind.Matrix:return matrix.Equals(v.matrix);
                case GlobalValueKind.Texture:return texture==v.texture;
                case GlobalValueKind.Keyword:return enabled==v.enabled;
                default:return false;
            }
        }
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(name)||!Enum.IsDefined(typeof(GlobalValueKind),kind))throw new ArgumentException("Invalid global name/type.");
            if(kind==GlobalValueKind.Float&&!Finite(scalar))throw new ArgumentException("Global float must be finite.");
            if(kind==GlobalValueKind.Vector||kind==GlobalValueKind.Color)
                for(int i=0;i<4;i++)if(!Finite(vector[i]))throw new ArgumentException("Global vector/color must be finite.");
            if(kind==GlobalValueKind.Matrix)for(int i=0;i<16;i++)if(!Finite(matrix[i]))throw new ArgumentException("Global matrix must be finite.");
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    }

    [CreateAssetMenu(menuName="Pipeline/Shader Global Profile")]
    public sealed class GlobalShaderProfile:ScriptableObject
    {
        public GlobalShaderValue[] entries=Array.Empty<GlobalShaderValue>();
    }

    // This is ownership within this tool, not a claim to detect arbitrary native writers.
    // Only an observable different value can be distinguished from our last write (same-value/ABA cannot).
    public sealed class GlobalShaderLease
    {
        private static readonly Dictionary<string,string> Owners=new Dictionary<string,string>(StringComparer.Ordinal);
        private GlobalShaderValue[] before,last;
        public readonly string id=Guid.NewGuid().ToString("N");
        public bool Active {get;private set;}
        public string State {get;private set;}="new";
        public string[] Conflicts {get;private set;}=Array.Empty<string>();
        public string[] Keys => last==null?Array.Empty<string>():last.Select(v=>v.Key).ToArray();
        public GlobalShaderValue[] Actual => last==null?Array.Empty<GlobalShaderValue>():last.Select(v=>v.Read()).ToArray();

        public void Apply(GlobalShaderValue[] values)
        {
            if(Active)throw new InvalidOperationException("Restore the existing lease before applying a new one; its first baseline must not be replaced.");
            if(values==null||values.Length==0||values.Length>128)throw new ArgumentException("Use 1..128 global values.");
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var v in values)
            {
                if(v==null)throw new ArgumentException("Global entry cannot be null.");v.Validate();
                if(!keys.Add(v.Key)||Owners.ContainsKey(v.Key))throw new InvalidOperationException("Global already owned or duplicated: "+v.name);
            }
            before=values.Select(v=>v.Read()).ToArray();last=values.Select(v=>v.Clone()).ToArray();
            foreach(var key in keys)Owners.Add(key,id);
            int written=0;
            try
            {
                for(int i=0;i<last.Length;i++){last[i].Write();written++;last[i]=last[i].Read();}
                Active=true;State="applied";
            }
            catch
            {
                for(int i=0;i<written;i++)before[i].Write();foreach(var key in keys)Owners.Remove(key);
                State="failed";throw;
            }
        }
        public void Restore()
        {
            if(!Active)return;
            var conflicts=new List<string>();
            for(int i=0;i<last.Length;i++)
            {
                if(Owners.TryGetValue(last[i].Key,out var owner)&&owner==id)
                {
                    if(last[i].Same(last[i].Read()))before[i].Write();else conflicts.Add(last[i].name);
                    Owners.Remove(last[i].Key);
                }
            }
            Active=false;Conflicts=conflicts.ToArray();State=conflicts.Count==0?"restored":"conflicted";
        }
    }
}
