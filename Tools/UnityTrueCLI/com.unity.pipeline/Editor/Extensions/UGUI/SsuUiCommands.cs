using System;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using Unity.Pipeline.VisualUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.Pipeline.UGUI.Editor
{
    [InitializeOnLoad]
    static class SsuUiCommands
    {
        static SsuUiCommands(){SsuMaterialCommands.MaterialChanged+=Refresh;}
        private static void Refresh(Material m)
        {
            foreach(var graphic in ExtensionObjects.Find<Graphic>().Where(g=>g.material==m))
            {if(graphic is MaskableGraphic maskable)maskable.RecalculateMasking();graphic.SetMaterialDirty();}
        }

        [CliCommand("assign_ssu_ui_material", "Assign or clear a GUI SSU material asset on an exact Graphic with Undo. Optional original SSU size bridge owns only a derived runtime material; labels/children and source assets stay unchanged.",Tags=new[]{"rendering/ssu","ui/ssu"})]
        public static object Assign([CliArg("target","Graphic object/component.",Required=true)]ObjectRef target,
            [CliArg("material","GUI SSU material asset; omit only for clear.")]ObjectRef material=null,
            [CliArg("clear","Clear assigned material and disable the size bridge.")]bool clear=false,
            [CliArg("bridge","Attach/enable the original actual-Rect material bridge.")]bool bridge=true,
            [CliArg("sprite_sheet","Synchronize rectangular, non-rotated Image sprite atlas UV bounds.")]bool spriteSheet=false)
        {
            UiAuthoring.GuardEditMode();var graphic=Resolve(target);
            if(clear&&material!=null||!clear&&material==null)throw new ArgumentException("Supply material or clear=true.");
            var m=clear?null:SsuMaterials.Require(material,false);if(m!=null&&!SsuMaterials.IsGui(m.shader))throw new ArgumentException("UI requires GUI SSU or Additive GUI SSU for native stencil/clipping.");
            if(!clear&&bridge)ValidateBridge(graphic,spriteSheet);
            return UiAuthoring.Atomic<object>("Assign SSU UI Material",()=>
            {
                Undo.RecordObject(graphic,"Assign SSU UI Material");graphic.material=m;
                var b=graphic.GetComponent<SsuGraphicBridge>();
                if(clear||!bridge){if(b!=null){Undo.RecordObject(b,"Disable SSU UI Bridge");b.enabled=false;ExtensionObjects.Changed(b);}}
                else{if(b==null)b=Undo.AddComponent<SsuGraphicBridge>(graphic.gameObject);else Undo.RecordObject(b,"Configure SSU UI Bridge");b.syncRect=true;b.syncSpriteSheet=spriteSheet;b.enabled=true;ExtensionObjects.Changed(b);}
                if(graphic is MaskableGraphic maskable)maskable.RecalculateMasking();graphic.SetMaterialDirty();ExtensionObjects.Changed(graphic);return Inspect(target);
            });
        }

        [CliCommand("configure_ssu_ui_bridge", "Configure original SSU Graphic bridge enable/Rect/atlas synchronization with Undo. Native material-modifier order and vendor-owner conflicts are validated before enabling.",Tags=new[]{"rendering/ssu","ui/ssu"})]
        public static object Configure([CliArg("target","Graphic object.",Required=true)]ObjectRef target,
            [CliArg("enabled","Enable bridge; disable returns to native material chain.")]bool enabled=true,
            [CliArg("sync_rect","Use actual Rect width/height, including stretched/layout-driven sizes.")]bool syncRect=true,
            [CliArg("sprite_sheet","Synchronize a rectangular Image sprite region.")]bool spriteSheet=false)
        {
            UiAuthoring.GuardEditMode();var graphic=Resolve(target);
            if(enabled){if(!SsuMaterials.IsGui(graphic.material?.shader))throw new ArgumentException("Assign a GUI SSU material first.");ValidateBridge(graphic,spriteSheet);}
            return UiAuthoring.Atomic<object>("Configure SSU UI Bridge",()=>
            {
                var b=graphic.GetComponent<SsuGraphicBridge>();if(b==null)b=Undo.AddComponent<SsuGraphicBridge>(graphic.gameObject);else Undo.RecordObject(b,"Configure SSU UI Bridge");
                b.syncRect=syncRect;b.syncSpriteSheet=spriteSheet;b.enabled=enabled;graphic.SetMaterialDirty();ExtensionObjects.Changed(b);return Inspect(target);
            });
        }

        [CliCommand("inspect_ssu_ui", "Read source/direct material, already submitted Canvas material and this bridge's owned/borrowed instances separately. Does not invoke materialForRendering or allocate stencil materials.",Tags=new[]{"rendering/ssu","ui/ssu"})]
        public static object Inspect([CliArg("target","Graphic object/component.",Required=true)]ObjectRef target)
        {
            var graphic=Resolve(target);var bridge=graphic.GetComponent<SsuGraphicBridge>();var rect=graphic.rectTransform.rect;
            var rendered=graphic.canvasRenderer.materialCount>0?graphic.canvasRenderer.GetMaterial(0):null;
            var owned=bridge==null?null:bridge.OwnedMaterial;
            return new{handle=ObjectResolver.Describe(graphic),directMaterial=ObjectResolver.Describe(graphic.material),sourcePersistent=EditorUtility.IsPersistent(graphic.material),
                submittedMaterial=ObjectResolver.Describe(rendered),bridge=ObjectResolver.Describe(bridge),ownedMaterial=ObjectResolver.Describe(owned),borrowedMaterial=ObjectResolver.Describe(bridge?.BorrowedMaterial),
                state=bridge?.State,error=bridge?.Error,enabled=bridge!=null&&bridge.enabled,syncRect=bridge!=null&&bridge.syncRect,syncSpriteSheet=bridge!=null&&bridge.syncSpriteSheet,
                actualRect=new[]{rect.width,rect.height},ownedRect=owned!=null&&owned.HasProperty("_RectWidth")&&owned.HasProperty("_RectHeight")?new[]{owned.GetFloat("_RectWidth"),owned.GetFloat("_RectHeight")}:null,
                ownedAtlas=owned!=null&&owned.HasProperty("_SpriteSheetRect")?Vector(owned.GetVector("_SpriteSheetRect")):null,
                nativeStencil=rendered!=null&&rendered.HasProperty("_Stencil")?new[]{rendered.GetFloat("_Stencil"),rendered.GetFloat("_StencilComp"),rendered.GetFloat("_StencilOp")}:null,
                source=graphic.material!=null&&SsuMaterials.IsSsu(graphic.material.shader)?SsuMaterials.Describe(graphic.material):null,
                note="GPU alpha/UV/vertex effects do not automatically change CPU raycast geometry. Text/TMP variants require explicit shader options and separate validation."};
        }

        [CliCommand("set_ssu_ui_parameters", "Set temporary typed parameters on an active bridge-owned material in Play Mode, preserving them across Rect/mask rebuilds. Does not edit assets or feature keywords; no save/Undo.",Tags=new[]{"rendering/ssu","ui/ssu"})]
        public static object Runtime([CliArg("target","Graphic with active rendered SSU bridge.",Required=true)]ObjectRef target,
            [CliArg("properties","Typed parameter entries; feature Toggle/KeywordEnum changes are Edit Mode only.",Required=true)]SsuPropertyValue[] properties,
            [CliArg("reset","Clear this bridge's temporary parameter overrides before applying supplied values.")]bool reset=false)
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Requires Play Mode.");var g=Resolve(target);var b=g.GetComponent<SsuGraphicBridge>();
            if(b==null||!b.isActiveAndEnabled||b.OwnedMaterial==null)throw new ArgumentException("An active, normally rendered SSU bridge is required.");
            if(properties==null)throw new ArgumentException("properties required (may be empty with reset=true).");
            foreach(var p in properties)
            {
                if(p==null)throw new ArgumentException("Entry cannot be null.");int i=SsuMaterials.Property(b.OwnedMaterial.shader,p.name);SsuMaterials.ToggleKeyword(b.OwnedMaterial.shader,i,out bool toggle,out _);
                if(toggle||SsuMaterials.EnumChoices(b.OwnedMaterial.shader,i)!=null)throw new ArgumentException("Prepare Toggle/KeywordEnum variants in Edit Mode; Play edits only parameters.");
                if(p.name=="_RectWidth"||p.name=="_RectHeight"||p.name=="_SpriteSheetRect")throw new ArgumentException("Geometry fields belong to the bridge, not parameter overrides.");
            }
            var plan=SsuMaterials.Plan(b.OwnedMaterial.shader,properties);
            if(reset)b.ResetRuntimeOverrides();foreach(var apply in plan)apply(b.OwnedMaterial);b.CaptureRuntimeOverrides(properties.Select(p=>p.name).ToArray());g.SetMaterialDirty();return Inspect(target);
        }
        private static float[] Vector(Vector4 v)=>new[]{v.x,v.y,v.z,v.w};
        private static Graphic Resolve(ObjectRef target)
        {
            var go=ExtensionObjects.Resolve(target);if(!ObjectResolver.TryResolve(target,out var exact,out var error))throw new ArgumentException(error);
            if(exact is Graphic g)return g;if(!(exact is GameObject))throw new ArgumentException("Supply exact Graphic or unambiguous GameObject.");
            var graphics=go.GetComponents<Graphic>();if(graphics.Length!=1)throw new ArgumentException("Expected one Graphic; supply an exact component.");return graphics[0];
        }
        private static void ValidateBridge(Graphic graphic,bool atlas)
        {
            var bridge=graphic.GetComponent<SsuGraphicBridge>();var conflict=SsuGraphicBridge.Conflict(graphic,bridge);if(conflict!=null)throw new ArgumentException(conflict);
            if(atlas)
            {
                if(!(graphic is Image image))throw new ArgumentException("Sprite atlas sync currently supports Image only.");
                if(!SsuGraphicBridge.Atlas(image.overrideSprite??image.sprite,out _,out var error))throw new ArgumentException(error);
            }
        }
    }
}
