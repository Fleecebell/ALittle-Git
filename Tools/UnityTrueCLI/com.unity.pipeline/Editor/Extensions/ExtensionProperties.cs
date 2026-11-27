using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Editor.Commands.GameObjects;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Extensions
{
    // Reuse the same SerializedObject/Undo implementation as the core component CLI.
    public static class ExtensionProperties
    {
        public static object Read(Component component)
        {
            var properties=new Dictionary<string,object>();
            var schema=new List<object>();
            var iterator=new SerializedObject(component).GetIterator();
            bool truncated=false;
            while(iterator.Next(true))
            {
                if(new[]{"m_Script","m_GameObject","m_CorrespondingSourceObject","m_PrefabInstance","m_PrefabAsset"}.Contains(iterator.propertyPath))continue;
                if(schema.Count==2048){truncated=true;break;}
                schema.Add(new{path=iterator.propertyPath,kind=iterator.propertyType.ToString(),iterator.editable,iterator.isArray});
                if(iterator.propertyType!=SerializedPropertyType.Generic&&!iterator.isArray)
                    properties[iterator.propertyPath]=SerializedPropertyConverter.Read(iterator.Copy());
                else if(iterator.propertyType==SerializedPropertyType.String)
                    properties[iterator.propertyPath]=iterator.stringValue;
            }
            return new {component=Unity.Pipeline.Editor.Authoring.ObjectResolver.Describe(component),properties,schema,truncated};
        }
        public static object Write(Component component, JObject properties)
        {
            if (properties == null || properties.Properties().Any(property=>new[]{"m_Script","m_GameObject","m_CorrespondingSourceObject","m_PrefabInstance","m_PrefabAsset"}.Any(identity=>property.Name==identity||property.Name.StartsWith(identity+".",StringComparison.Ordinal)))) throw new ArgumentException("A property map is required; changing script/object/prefab identity is forbidden.");
            return ComponentCommands.SetComponentProperties(new ObjectRef { InstanceId = PipelineUtils.GetObjectId(component) }, properties);
        }
    }
}
