using System;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Extensions
{
    static class SsuRendererCommands
    {
        [CliCommand("assign_ssu_renderer_material", "Assign or clear one explicit Renderer/SpriteRenderer shared material slot with Undo. Never calls Renderer.material or clones hidden materials; preserves other slots.",Tags=new[]{"rendering/ssu"})]
        public static object Assign([CliArg("target","Renderer component or unambiguous GameObject.",Required=true)]ObjectRef target,
            [CliArg("material","SSU material asset; omit only for clear=true.")]ObjectRef material=null,
            [CliArg("slot","Existing material slot index.")]int slot=0,
            [CliArg("clear","Explicitly clear this slot.")]bool clear=false)
        {
            UiAuthoring.GuardEditMode();var renderer=Resolve(target);var array=renderer.sharedMaterials;
            if(slot<0||slot>=array.Length)throw new ArgumentException("slot outside existing material array.");
            if(clear&&material!=null||!clear&&material==null)throw new ArgumentException("Supply material or clear=true.");
            var m=clear?null:SsuMaterials.Require(material,false);
            if(m!=null&&SsuMaterials.IsGui(m.shader))throw new ArgumentException("GUI SSU materials belong to Graphic; use a sprite/lighting SSU variant here.");
            return UiAuthoring.Atomic<object>("Assign SSU Renderer Material",()=>
            {
                Undo.RecordObject(renderer,"Assign SSU Renderer Material");array[slot]=m;renderer.sharedMaterials=array;ExtensionObjects.Changed(renderer);return Inspect(target);
            });
        }

        [CliCommand("inspect_ssu_renderer", "Read actual Renderer/SpriteRenderer material slot identities and SSU keyword state without instantiating material copies. PropertyBlock presence is explicit; no claim it matches asset values.",Tags=new[]{"rendering/ssu"})]
        public static object Inspect([CliArg("target","Renderer component or unambiguous GameObject.",Required=true)]ObjectRef target)
        {
            var renderer=Resolve(target);var sprite=renderer as SpriteRenderer;
            return new{handle=ObjectResolver.Describe(renderer),renderer.enabled,hasPropertyBlock=renderer.HasPropertyBlock(),
                slots=renderer.sharedMaterials.Select((m,i)=>new{slot=i,material=ObjectResolver.Describe(m),ssu=m!=null&&SsuMaterials.IsSsu(m.shader)?SsuMaterials.Describe(m):null}).ToArray(),
                sprite=sprite==null?null:ObjectResolver.Describe(sprite.sprite),color=sprite==null?null:VisualAuthoring.ColorArray(sprite.color)};
        }
        private static Renderer Resolve(ObjectRef target)
        {
            var go=ExtensionObjects.Resolve(target);if(!ObjectResolver.TryResolve(target,out var exact,out var error))throw new ArgumentException(error);
            if(exact is Renderer renderer)return renderer;
            if(!(exact is GameObject))throw new ArgumentException("Supply an exact Renderer component or GameObject.");
            var matches=go.GetComponents<Renderer>();if(matches.Length!=1)throw new ArgumentException("Expected one Renderer; supply exact component identity.");return matches[0];
        }
    }
}
