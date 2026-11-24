using System;
using System.IO;
using System.Linq;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Extensions
{
    public static class VisualAuthoring
    {
        public static T Asset<T>(ObjectRef reference) where T:UnityEngine.Object
        {
            if(reference==null||!ObjectResolver.TryResolve(reference,out var o,out var error)||!(o is T asset)||!EditorUtility.IsPersistent(asset))
                throw new ArgumentException("Expected a persistent "+typeof(T).Name+" asset.");
            return asset;
        }
        public static T Writable<T>(ObjectRef reference) where T:UnityEngine.Object
        {
            var o=Asset<T>(reference);if(ProjectPaths.Resolve(AssetDatabase.GetAssetPath(o),out var error)==null)throw new ArgumentException(error);return o;
        }
        public static string NewPath(string value,string extension)
        {
            var path=ProjectPaths.Resolve(value,out var error);if(path==null)throw new ArgumentException(error);
            if(!path.EndsWith(extension,StringComparison.OrdinalIgnoreCase)||!AssetDatabase.IsValidFolder(Path.GetDirectoryName(path).Replace('\\','/')))
                throw new ArgumentException("Use an existing project folder and "+extension+" extension.");
            if(File.Exists(path)||AssetDatabase.LoadMainAssetAtPath(path)!=null)throw new ArgumentException("Asset already exists; no overwrite.");return path;
        }
        public static void Color(float[] value)
        {
            if(value==null)return;UiAuthoring.Vector(value,4,"color");if(value.Any(x=>x<0||x>1))throw new ArgumentException("UI colors must be RGBA in 0..1.");
        }
        public static UnityEngine.Color ToColor(float[] value)=>new UnityEngine.Color(value[0],value[1],value[2],value[3]);
        public static float[] ColorArray(UnityEngine.Color value)=>new[]{value.r,value.g,value.b,value.a};
        public static T EnumValue<T>(string value) where T:struct
        {
            if(!Enum.TryParse(value,true,out T parsed)||!Enum.IsDefined(typeof(T),parsed))throw new ArgumentException("Invalid "+typeof(T).Name+": "+value);return parsed;
        }
    }
}
