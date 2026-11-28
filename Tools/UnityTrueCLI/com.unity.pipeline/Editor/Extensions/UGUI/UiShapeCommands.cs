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
    static class UiShapeCommands
    {
        [CliCommand("create_ui_shape", "Create a native convex MaskableGraphic or complete shape Button/Label tree. Geometry and raycasts share the same outline; text stays upright. No input-system changes.",Tags=new[]{"ui/shape"})]
        public static object Create([CliArg("name","Object name.",Required=true)]string name,
            [CliArg("parent","RectTransform parent.",Required=true)]ObjectRef parent,
            [CliArg("settings","Shape, tint/gradient/border/material/texture settings.",Required=true)]UiShapeSettings settings,
            [CliArg("rect","Optional initial RectTransform.")]UiRectSettings rect=null,
            [CliArg("button","Create a complete native Button with Label.")]bool button=false,
            [CliArg("font","Explicit project Font required for button.")]ObjectRef font=null,
            [CliArg("text","Button label (defaults to name).")]string text=null)
        {
            UiAuthoring.GuardEditMode();if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("name is required.");
            var owner=ExtensionObjects.Require<RectTransform>(parent);
            var actualFont=button?VisualAuthoring.Asset<Font>(font):null;
            if(!button&&(font!=null||text!=null))throw new ArgumentException("font/text require button=true.");
            if(rect!=null)UiAuthoring.Validate(null,rect);
            // Validate scalar/assets before any creation; final geometry is checked inside the atomic scope.
            Validate(settings);
            return UiAuthoring.Atomic<object>("Create UI Shape",()=>
            {
                var go=UiAuthoring.NewRect(name,owner);var rt=(RectTransform)go.transform;
                if(rect!=null)UiAuthoring.Apply(rt,rect);
                Undo.AddComponent<CanvasRenderer>(go);
                var graphic=Undo.AddComponent<PipelineShapeGraphic>(go);Apply(graphic,settings);
                if(settings.tint!=null&&settings.gradient_end==null)graphic.endColor=graphic.color;
                ValidateGeometry(graphic,settings);
                if(button)
                {
                    var control=Undo.AddComponent<Button>(go);control.targetGraphic=graphic;
                    var labelGo=UiAuthoring.NewRect("Label",rt);var labelRect=(RectTransform)labelGo.transform;
                    labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;
                    labelRect.offsetMin=new Vector2(12,6);labelRect.offsetMax=new Vector2(-12,-6);
                    var label=Undo.AddComponent<Text>(labelGo);label.font=actualFont;label.text=text??name;
                    label.fontSize=20;label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;
                }
                ExtensionObjects.Changed(graphic);return Inspect(new ObjectRef{InstanceId=go.GetInstanceID()});
            });
        }

        [CliCommand("configure_ui_shape", "Edit shape/gradient/border/texture/material with one Undo. Omitted settings stay unchanged. Invalid final geometry rolls the complete edit back.",Tags=new[]{"ui/shape"})]
        public static object Configure([CliArg("target","PipelineShapeGraphic object.",Required=true)]ObjectRef target,
            [CliArg("settings","Only explicit fields change.",Required=true)]UiShapeSettings settings)
        {
            UiAuthoring.GuardEditMode();var graphic=ExtensionObjects.Require<PipelineShapeGraphic>(target);Validate(settings);
            return UiAuthoring.Atomic<object>("Configure UI Shape",()=>
            {
                Undo.RecordObject(graphic,"Configure UI Shape");Apply(graphic,settings);ValidateGeometry(graphic,settings);
                graphic.SetAllDirty();ExtensionObjects.Changed(graphic);return Inspect(target);
            });
        }

        [CliCommand("inspect_ui_shape", "Read native shape state, current local outline and generated Canvas mesh counts. No layout rebuild; includes maskability, bound Button and material facts.",Tags=new[]{"ui/shape"})]
        public static object Inspect([CliArg("target","PipelineShapeGraphic object.",Required=true)]ObjectRef target)
        {
            var graphic=ExtensionObjects.Require<PipelineShapeGraphic>(target);var rt=graphic.rectTransform;
            var outline=graphic.Outline(graphic.GetPixelAdjustedRect());
            bool valid=PipelineShapeGraphic.Convex(outline,graphic.GetPixelAdjustedRect(),out var error);
            var mesh=graphic.canvasRenderer.GetMesh();
            int vertices=mesh==null?0:mesh.vertexCount,triangles=mesh==null?0:mesh.triangles.Length/3;
            return new {root=ObjectResolver.Describe(graphic.gameObject),component=ObjectResolver.Describe(graphic),shape=graphic.shape.ToString(),
                cuts=new[]{graphic.cuts.x,graphic.cuts.y,graphic.cuts.z,graphic.cuts.w},graphic.skew,insets=new[]{graphic.insets.x,graphic.insets.y},
                tint=VisualAuthoring.ColorArray(graphic.color),gradient_end=VisualAuthoring.ColorArray(graphic.endColor),graphic.gradientAngle,
                graphic.borderWidth,border_tint=VisualAuthoring.ColorArray(graphic.borderColor),graphic.raycastTarget,graphic.maskable,
                texture=ObjectResolver.Describe(graphic.texture),material=ObjectResolver.Describe(graphic.material),
                outline=outline.Select(p=>new[]{p.x,p.y}).ToArray(),valid,error,borderSaturated=valid&&graphic.borderWidth>0&&PipelineShapeGraphic.Inset(outline,graphic.borderWidth)==null,
                mesh=new{vertices,triangles},buttonTarget=ObjectResolver.Describe(graphic.GetComponent<Button>()?.targetGraphic),actual=UiAuthoring.Read(rt)};
        }

        private static void Validate(UiShapeSettings s)
        {
            if(s==null)throw new ArgumentException("settings is required.");s.ValidateKeys();
            if(s.shape!=null)Parse(s.shape);
            foreach(var c in new[]{s.tint,s.gradient_end,s.border_tint})VisualAuthoring.Color(c);
            UiAuthoring.Vector(s.cuts,4,"cuts");if(s.cuts!=null&&s.cuts.Any(x=>x<0))throw new ArgumentException("cuts must be nonnegative.");
            if(s.skew.HasValue&&Mathf.Abs(ExtensionObjects.Finite(s.skew.Value,"skew"))>=.5f)throw new ArgumentException("skew must be strictly between -.5 and .5.");
            UiAuthoring.Vector(s.insets,2,"insets");if(s.insets!=null&&s.insets.Any(x=>x<0||x>=.5f))throw new ArgumentException("insets must be in 0.. .5 exclusive.");
            if(s.gradient_angle.HasValue)ExtensionObjects.Finite(s.gradient_angle.Value,"gradient_angle");
            if(s.border_width.HasValue&&ExtensionObjects.Finite(s.border_width.Value,"border_width")<0)throw new ArgumentException("border_width cannot be negative.");
            if(s.points!=null){if(s.points.Length<3||s.points.Length>64)throw new ArgumentException("points requires 3..64 [x,y] pairs.");foreach(var p in s.points){if(p==null)throw new ArgumentException("point cannot be null.");UiAuthoring.Vector(p,2,"point");}}
            if(s.material!=null)VisualAuthoring.Asset<Material>(s.material);
            if(s.texture!=null)VisualAuthoring.Asset<Texture>(s.texture);
            if(s.clear_material==true&&s.material!=null||s.clear_texture==true&&s.texture!=null)throw new ArgumentException("Do not mix clear and assignment.");
        }
        private static void ValidateGeometry(PipelineShapeGraphic g,UiShapeSettings settings)
        {
            var r=g.rectTransform.rect;var p=g.Outline(r);
            if(settings.cuts!=null&&g.shape==PipelineShapeGraphic.ShapeKind.Chamfer&&(g.cuts.x+g.cuts.y>=r.width||g.cuts.z+g.cuts.w>=r.width||g.cuts.x+g.cuts.w>=r.height||g.cuts.y+g.cuts.z>=r.height))
                throw new ArgumentException("Adjacent cuts must fit within current width/height.");
            if(!PipelineShapeGraphic.Convex(p,r,out var error))throw new ArgumentException(error);
            if(settings.border_width.HasValue&&g.borderWidth>0&&PipelineShapeGraphic.Inset(p,g.borderWidth)==null)throw new ArgumentException("border_width leaves no positive interior at the current Rect size.");
        }
        private static void Apply(PipelineShapeGraphic g,UiShapeSettings s)
        {
            if(s.shape!=null)g.shape=Parse(s.shape);
            if(s.cuts!=null)g.cuts=new Vector4(s.cuts[0],s.cuts[1],s.cuts[2],s.cuts[3]);
            if(s.skew.HasValue)g.skew=s.skew.Value;if(s.insets!=null)g.insets=new Vector2(s.insets[0],s.insets[1]);
            if(s.points!=null)g.points=s.points.Select(p=>new Vector2(p[0],p[1])).ToArray();
            if(s.tint!=null)g.color=VisualAuthoring.ToColor(s.tint);
            if(s.gradient_end!=null)g.endColor=VisualAuthoring.ToColor(s.gradient_end);
            if(s.gradient_angle.HasValue)g.gradientAngle=s.gradient_angle.Value;
            if(s.border_width.HasValue)g.borderWidth=s.border_width.Value;
            if(s.border_tint!=null)g.borderColor=VisualAuthoring.ToColor(s.border_tint);
            if(s.material!=null)g.material=VisualAuthoring.Asset<Material>(s.material);else if(s.clear_material==true)g.material=null;
            if(s.texture!=null)g.texture=VisualAuthoring.Asset<Texture>(s.texture);else if(s.clear_texture==true)g.texture=null;
            if(s.raycast_target.HasValue)g.raycastTarget=s.raycast_target.Value;if(s.maskable.HasValue)g.maskable=s.maskable.Value;
        }
        private static PipelineShapeGraphic.ShapeKind Parse(string value)
        {
            switch(value){case "chamfer":return PipelineShapeGraphic.ShapeKind.Chamfer;case "parallelogram":return PipelineShapeGraphic.ShapeKind.Parallelogram;case "trapezoid":return PipelineShapeGraphic.ShapeKind.Trapezoid;case "polygon":return PipelineShapeGraphic.ShapeKind.Polygon;default:throw new ArgumentException("shape must be chamfer, parallelogram, trapezoid or polygon.");}
        }
    }
    public sealed class UiShapeSettings:StrictUiSettings
    {
        public string shape;
        public float[] cuts,insets,tint,gradient_end,border_tint;
        public float[][] points;
        public float? skew,gradient_angle,border_width;
        public ObjectRef material,texture;
        public bool? clear_material,clear_texture,raycast_target,maskable;
    }
}
