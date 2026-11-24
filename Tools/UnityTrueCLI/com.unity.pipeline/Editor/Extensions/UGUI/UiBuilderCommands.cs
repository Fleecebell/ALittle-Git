using System;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.Pipeline.UGUI.Editor
{
    static class UiBuilderCommands
    {
        [CliCommand("create_ui_element", "Build empty/panel/image/button under an explicit RectTransform. Button creates its actual label child and requires an explicit Font. All creation is one Undo step; no EventSystem mutation.",Tags=new[]{"ui/authoring"})]
        public static object Create([CliArg("name","Object name.",Required=true)]string name,
            [CliArg("parent","Existing RectTransform parent.",Required=true)]ObjectRef parent,
            [CliArg("kind","empty, panel, image or button.")]string kind="panel",
            [CliArg("rect","Optional initial anchors, size and 3D pose.")]UiRectSettings rect=null,
            [CliArg("font","Required project Font for button label.")]ObjectRef font=null,
            [CliArg("text","Button label; ignored kinds are rejected.")]string text=null,
            [CliArg("tint","Optional image RGBA in 0..1.")]float[] color=null,
            [CliArg("sprite","Optional Sprite for image/panel/button.")]ObjectRef sprite=null)
        {
            UiAuthoring.GuardEditMode();
            if(string.IsNullOrWhiteSpace(name)||!new[]{"empty","panel","image","button"}.Contains(kind))throw new ArgumentException("Valid name and kind required.");
            var owner=ExtensionObjects.Require<RectTransform>(parent);Font actualFont=null;Sprite actualSprite=null;
            if(kind=="button")actualFont=Asset<Font>(font,"button font");
            else if(font!=null||text!=null)throw new ArgumentException("font/text are only for button.");
            if(kind=="empty"&&(color!=null||sprite!=null))throw new ArgumentException("empty element has no graphic.");
            if(sprite!=null)actualSprite=Asset<Sprite>(sprite,"sprite");
            UiAuthoring.Vector(color,4,"color");if(color!=null&&color.Any(value=>value<0||value>1))throw new ArgumentException("color must be in 0..1.");
            if(rect!=null)UiAuthoring.Validate(null,rect);
            return UiAuthoring.Atomic<object>("Create UI Element",()=>
            {
                var go=UiAuthoring.NewRect(name,owner);var transform=(RectTransform)go.transform;
                if(kind!="empty")
                {
                    var image=Undo.AddComponent<Image>(go);image.sprite=actualSprite;
                    image.color=color==null?(kind=="panel"?new Color(.12f,.16f,.24f,1):Color.white):new Color(color[0],color[1],color[2],color[3]);
                    if(kind=="button")
                    {
                        var button=Undo.AddComponent<Button>(go);button.targetGraphic=image;
                        var child=UiAuthoring.NewRect("Label",transform);var childRect=(RectTransform)child.transform;
                        childRect.anchorMin=Vector2.zero;childRect.anchorMax=Vector2.one;childRect.offsetMin=new Vector2(6,6);childRect.offsetMax=new Vector2(-6,-6);
                        var label=Undo.AddComponent<Text>(child);label.font=actualFont;label.text=text??name;label.fontSize=20;
                        label.alignment=TextAnchor.MiddleCenter;label.color=Color.black;label.raycastTarget=false;
                    }
                }
                if(rect!=null)UiAuthoring.Apply(transform,rect);
                ExtensionObjects.Changed(transform);
                return new {root=ObjectResolver.Describe(go),created=go.GetComponentsInChildren<Transform>(true).Select(item=>ObjectResolver.Describe(item.gameObject)).ToArray(),actual=UiAuthoring.Read(transform)};
            });
        }

        private static T Asset<T>(ObjectRef handle,string name) where T:UnityEngine.Object
        {
            if(handle==null||!ObjectResolver.TryResolve(handle,out var value,out _)||!(value is T typed)||!EditorUtility.IsPersistent(value))throw new ArgumentException(name+" requires an explicit persistent "+typeof(T).Name+" asset.");
            return typed;
        }

        [CliCommand("configure_ui_layout", "Add/edit native horizontal, vertical or grid layout with Undo. Same type preserves unspecified settings; changing type requires replace=true. Does not force layout rebuild.",Tags=new[]{"ui/authoring"})]
        public static object Layout([CliArg("target","RectTransform container.",Required=true)]ObjectRef target,
            [CliArg("kind","horizontal, vertical or grid.",Required=true)]string kind,
            [CliArg("settings","Optional alignment, padding[4], spacing, cell_size[2], grid_spacing[2], columns, control_width/height, expand_width/height, horizontal_fit/vertical_fit.")]UiLayoutSettings settings=null,
            [CliArg("replace","Permit replacing a different existing LayoutGroup.")]bool replace=false)
        {
            UiAuthoring.GuardEditMode();var rect=ExtensionObjects.Require<RectTransform>(target);settings=settings??new UiLayoutSettings();
            settings.ValidateKeys();
            Type type=kind=="horizontal"?typeof(HorizontalLayoutGroup):kind=="vertical"?typeof(VerticalLayoutGroup):kind=="grid"?typeof(GridLayoutGroup):null;
            if(type==null)throw new ArgumentException("Unknown layout kind.");
            var existing=rect.GetComponent<LayoutGroup>();if(existing!=null&&existing.GetType()!=type&&!replace)throw new ArgumentException("Different LayoutGroup exists; replace=true is required.");
            if(settings.padding!=null&&(settings.padding.Length!=4||settings.padding.Any(value=>value<0)))throw new ArgumentException("padding requires nonnegative [left,right,top,bottom].");
            var alignment=TextAnchor.UpperLeft;
            if(settings.alignment!=null&&(!Enum.TryParse(settings.alignment,true,out alignment)||!Enum.IsDefined(typeof(TextAnchor),alignment)))throw new ArgumentException("Invalid alignment.");
            var horizontalFit=Fit(settings.horizontal_fit);var verticalFit=Fit(settings.vertical_fit);
            if(kind=="grid")
            {
                if(settings.spacing.HasValue||settings.control_width.HasValue||settings.control_height.HasValue||settings.expand_width.HasValue||settings.expand_height.HasValue)throw new ArgumentException("Linear layout fields are not valid for grid.");
                UiAuthoring.Vector(settings.cell_size,2,"cell_size");UiAuthoring.Vector(settings.grid_spacing,2,"grid_spacing");
                if(settings.cell_size!=null&&settings.cell_size.Any(value=>value<=0))throw new ArgumentException("cell_size must be positive.");
                if(settings.columns.HasValue&&settings.columns<1)throw new ArgumentException("columns must be positive.");
            }
            else
            {
                if(settings.cell_size!=null||settings.grid_spacing!=null||settings.columns.HasValue)throw new ArgumentException("Grid fields require grid kind.");
                if(settings.spacing.HasValue)ExtensionObjects.Finite(settings.spacing.Value,"spacing");
            }
            return UiAuthoring.Atomic<object>("Configure UI Layout",()=>
            {
                Undo.RegisterFullObjectHierarchyUndo(rect.gameObject,"Configure UI Layout");
                var group=existing;
                if(group!=null&&group.GetType()!=type){Undo.DestroyObjectImmediate(group);group=null;}
                if(group==null)group=(LayoutGroup)Undo.AddComponent(rect.gameObject,type);else Undo.RecordObject(group,"Configure UI Layout");
                if(settings.padding!=null)group.padding=new RectOffset(settings.padding[0],settings.padding[1],settings.padding[2],settings.padding[3]);
                if(settings.alignment!=null)group.childAlignment=alignment;
                if(group is HorizontalOrVerticalLayoutGroup linear)
                {
                    if(settings.spacing.HasValue)linear.spacing=settings.spacing.Value;
                    if(settings.control_width.HasValue)linear.childControlWidth=settings.control_width.Value;
                    if(settings.control_height.HasValue)linear.childControlHeight=settings.control_height.Value;
                    if(settings.expand_width.HasValue)linear.childForceExpandWidth=settings.expand_width.Value;
                    if(settings.expand_height.HasValue)linear.childForceExpandHeight=settings.expand_height.Value;
                }
                if(group is GridLayoutGroup grid)
                {
                    if(settings.cell_size!=null)grid.cellSize=new Vector2(settings.cell_size[0],settings.cell_size[1]);
                    if(settings.grid_spacing!=null)grid.spacing=new Vector2(settings.grid_spacing[0],settings.grid_spacing[1]);
                    if(settings.columns.HasValue){grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=settings.columns.Value;}
                }
                if(horizontalFit.HasValue||verticalFit.HasValue)
                {
                    var fitter=rect.GetComponent<ContentSizeFitter>();if(fitter==null)fitter=Undo.AddComponent<ContentSizeFitter>(rect.gameObject);else Undo.RecordObject(fitter,"Configure UI Fitter");
                    if(horizontalFit.HasValue)fitter.horizontalFit=horizontalFit.Value;if(verticalFit.HasValue)fitter.verticalFit=verticalFit.Value;ExtensionObjects.Changed(fitter);
                }
                ExtensionObjects.Changed(group);return UiComponentCommands.Inspect(target,false);
            });
        }

        private static ContentSizeFitter.FitMode? Fit(string value)
        {
            if(value==null)return null;
            if(!Enum.TryParse<ContentSizeFitter.FitMode>(value,true,out var result)||!Enum.IsDefined(typeof(ContentSizeFitter.FitMode),result))throw new ArgumentException("Invalid fit mode.");return result;
        }

        [CliCommand("configure_ui_layout_element", "Add/edit LayoutElement size preferences and flexible weights with Undo. Omitted values stay unchanged; -1 means unspecified. Parent layout determines final geometry.",Tags=new[]{"ui/authoring"})]
        public static object Element([CliArg("target","RectTransform child.",Required=true)]ObjectRef target,
            [CliArg("settings","min_size, preferred_size, flexible_size [2]; ignore_layout bool; priority int.",Required=true)]UiLayoutElementSettings settings)
        {
            UiAuthoring.GuardEditMode();var rect=ExtensionObjects.Require<RectTransform>(target);if(settings==null)throw new ArgumentException("settings is required.");
            settings.ValidateKeys();
            foreach(var value in new[]{settings.min_size,settings.preferred_size,settings.flexible_size}){UiAuthoring.Vector(value,2,"size");if(value!=null&&value.Any(item=>item<0&&item!=-1))throw new ArgumentException("Layout size values must be nonnegative or -1.");}
            return UiAuthoring.Atomic<object>("Configure Layout Element",()=>
            {
                var element=rect.GetComponent<LayoutElement>();if(element==null)element=Undo.AddComponent<LayoutElement>(rect.gameObject);else Undo.RecordObject(element,"Configure Layout Element");
                if(settings.min_size!=null){element.minWidth=settings.min_size[0];element.minHeight=settings.min_size[1];}
                if(settings.preferred_size!=null){element.preferredWidth=settings.preferred_size[0];element.preferredHeight=settings.preferred_size[1];}
                if(settings.flexible_size!=null){element.flexibleWidth=settings.flexible_size[0];element.flexibleHeight=settings.flexible_size[1];}
                if(settings.ignore_layout.HasValue)element.ignoreLayout=settings.ignore_layout.Value;if(settings.priority.HasValue)element.layoutPriority=settings.priority.Value;
                ExtensionObjects.Changed(element);return UiComponentCommands.Inspect(target,false);
            });
        }
    }
    [Serializable] public sealed class UiLayoutSettings : StrictUiSettings
    {
        public int[] padding;
        public string alignment,horizontal_fit,vertical_fit;
        public float? spacing;
        public float[] cell_size,grid_spacing;
        public int? columns;
        public bool? control_width,control_height,expand_width,expand_height;
    }
    [Serializable] public sealed class UiLayoutElementSettings : StrictUiSettings
    {
        public float[] min_size,preferred_size,flexible_size;
        public bool? ignore_layout;
        public int? priority;
    }
}
