using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Unity.Pipeline.VisualUI
{
    [ExecuteAlways,DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    [AddComponentMenu("UI/Pipeline SSU Graphic Bridge")]
    public sealed class SsuGraphicBridge:UIBehaviour,IMaterialModifier
    {
        public bool syncRect=true;
        public bool syncSpriteSheet;
        private Graphic graphic;
        private Material owned,borrowed,source;
        private int sourceCrc;
        private Vector2 lastSize;
        private Sprite lastSprite;
        private readonly List<Parameter> overrides=new List<Parameter>();
        public Material OwnedMaterial=>owned;
        public Material BorrowedMaterial=>borrowed;
        public string State {get;private set;}="idle";
        public string Error {get;private set;}

        [Serializable] private sealed class Parameter
        {
            public string name;public ShaderPropertyType type;public float number;public int integer;public Vector4 vector;public Texture texture;
            public void Apply(Material m)
            {
                switch(type){case ShaderPropertyType.Color:m.SetColor(name,(Color)vector);break;case ShaderPropertyType.Vector:m.SetVector(name,vector);break;case ShaderPropertyType.Texture:m.SetTexture(name,texture);break;case ShaderPropertyType.Int:m.SetInteger(name,integer);break;default:m.SetFloat(name,number);break;}
            }
        }
        protected override void OnEnable(){base.OnEnable();graphic=GetComponent<Graphic>();source=null;Dirty();}
        protected override void OnDisable(){Release();overrides.Clear();Dirty();base.OnDisable();}
        protected override void OnDestroy(){Release();base.OnDestroy();}
        protected override void OnRectTransformDimensionsChange(){base.OnRectTransformDimensionsChange();Dirty();}
#if UNITY_EDITOR
        protected override void OnValidate(){base.OnValidate();Dirty();}
#endif
        private void Dirty(){if(graphic==null)graphic=GetComponent<Graphic>();if(graphic!=null)graphic.SetMaterialDirty();}

        public static string Conflict(Graphic target,SsuGraphicBridge self)
        {
            var modifiers=target.GetComponents<IMaterialModifier>();bool sawSelf=false;
            foreach(var modifier in modifiers)
            {
                if(ReferenceEquals(modifier,self)){sawSelf=true;continue;}
                if(sawSelf)return "SSU bridge must follow the native Graphic/Mask material modifiers; move/re-add it last.";
                if(!ReferenceEquals(modifier,target)&&modifier.GetType()!=typeof(Mask))return "This SSU bridge supports the native Graphic/Mask material chain; another custom material modifier requires an explicit adapter.";
            }
            foreach(var component in target.GetComponentsInParent<MonoBehaviour>(true))
            {
                if(component==null||component==self)continue;var type=component.GetType();
                for(var t=type;t!=null;t=t.BaseType)
                    if(t.FullName=="SpriteShadersUltimate.InstancerSSU"&&component.gameObject==target.gameObject)return "Vendor Instancer already owns this Graphic's material.";
                if(type.FullName=="SpriteShadersUltimate.SpriteSheetSSU"&&component.gameObject==target.gameObject)return "Vendor SpriteSheetSSU already writes this Graphic's material.";
            }
            // SSU ships in Assembly-CSharp without an asmdef. Read its public ownership fields at this
            // optional integration boundary; do not reference Assembly-CSharp or drive vendor methods.
            foreach(var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if(component==null||!component.gameObject.scene.IsValid()||!component.gameObject.scene.isLoaded)continue;
                var type=component.GetType();bool fader=false;
                for(var t=type;t!=null;t=t.BaseType)if(t.FullName=="SpriteShadersUltimate.ShaderFaderSSU"){fader=true;break;}
                if(!fader)continue;
                var childField=type.GetField("getChildObjects");var graphicsField=type.GetField("graphics");
                if(childField==null||childField.FieldType!=typeof(bool)||graphicsField==null)return "Installed ShaderFader has an unknown ownership schema; inspect it before enabling this bridge.";
                bool children=(bool)childField.GetValue(component);
                if(children&&(target.transform==component.transform||target.transform.IsChildOf(component.transform)))return "A vendor ShaderFader owns this Graphic through its child scope.";
                if(!children&&graphicsField.GetValue(component) is IEnumerable graphics)
                    foreach(var candidate in graphics)if(ReferenceEquals(candidate,target))return "A vendor ShaderFader explicitly owns this Graphic.";
            }
            return null;
        }
        public static bool Atlas(Sprite sprite,out Vector4 uv,out string error)
        {
            uv=new Vector4(0,0,1,1);error=null;if(sprite==null)return true;
            if(sprite.packed&&(sprite.packingMode==SpritePackingMode.Tight||sprite.packingRotation!=SpritePackingRotation.None))
            {error="Tight/rotated SpriteAtlas entries are unsupported by SSU's rectangular _SpriteSheetRect.";return false;}
            try{var r=sprite.textureRect;uv=new Vector4(r.xMin/sprite.texture.width,r.yMin/sprite.texture.height,r.xMax/sprite.texture.width,r.yMax/sprite.texture.height);return true;}
            catch(Exception e){error="Sprite has no rectangular atlas region: "+e.Message;return false;}
        }
        private Sprite CurrentSprite(){var image=graphic as Image;return image==null?null:image.overrideSprite??image.sprite;}

        private void LateUpdate()
        {
            if(!isActiveAndEnabled||graphic==null)return;var current=graphic.material;var size=graphic.rectTransform.rect.size;var sprite=CurrentSprite();
            // Rebuild native stencil derivatives when the SAME source is edited in place.
            // No per-frame material clones, source writes or borrowed stencil-cache destruction.
            int crc=current==null?0:current.ComputeCRC();
            bool changed=current!=source||crc!=sourceCrc;
            if(changed)
            {
                source=current;sourceCrc=crc;
                if(graphic is MaskableGraphic maskable)maskable.RecalculateMasking();
            }
            if(changed||size!=lastSize||sprite!=lastSprite){lastSize=size;lastSprite=sprite;Dirty();}
        }
        public Material GetModifiedMaterial(Material baseMaterial)
        {
            if(!isActiveAndEnabled)return baseMaterial;
            if(graphic==null)graphic=GetComponent<Graphic>();
            var actualSource=graphic.material;
            var error=Conflict(graphic,this);
            if(error==null&&(actualSource==null||actualSource.shader==null||baseMaterial==null))error="Assigned material is not available.";
            if(error==null&&!(actualSource.shader.name.EndsWith("/GUI SSU",StringComparison.Ordinal)||actualSource.shader.name.EndsWith("/Additive GUI SSU",StringComparison.Ordinal))
                ||error==null&&!actualSource.shader.name.StartsWith("Sprite Shaders Ultimate/",StringComparison.Ordinal))error="Graphic bridge requires a GUI SSU variant.";
            Vector4 atlas=new Vector4(0,0,1,1);
            if(error==null&&syncSpriteSheet&&!Atlas(CurrentSprite(),out atlas,out error)){}
            if(error!=null){Release();Error=error;State="conflict";return baseMaterial;}
            if(owned==null||owned.shader!=actualSource.shader)
            {
                Release();owned=new Material(actualSource){name="TrueCLI.SSU.Bridge."+GetInstanceID(),hideFlags=HideFlags.HideAndDontSave};
            }
            borrowed=baseMaterial;
            owned.CopyPropertiesFromMaterial(actualSource);
            // Native stencil materials are reference-counted caches: another unbridged Graphic may
            // keep an older cached copy alive after the shared source changes. Read SSU effect data
            // from the actual source while retaining native stencil/clip state from the borrowed input.
            if(baseMaterial!=null)
            {
                var shader=owned.shader;
                for(int i=0;i<shader.GetPropertyCount();i++)
                {
                    string name=shader.GetPropertyName(i);
                    if(!NativeUiProperty(name)||!baseMaterial.HasProperty(name))continue;
                    switch(shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color:owned.SetColor(name,baseMaterial.GetColor(name));break;
                        case ShaderPropertyType.Vector:owned.SetVector(name,baseMaterial.GetVector(name));break;
                        case ShaderPropertyType.Texture:owned.SetTexture(name,baseMaterial.GetTexture(name));owned.SetTextureOffset(name,baseMaterial.GetTextureOffset(name));owned.SetTextureScale(name,baseMaterial.GetTextureScale(name));break;
                        case ShaderPropertyType.Int:owned.SetInteger(name,baseMaterial.GetInteger(name));break;
                        default:owned.SetFloat(name,baseMaterial.GetFloat(name));break;
                    }
                }
                foreach(var keyword in shader.keywordSpace.keywords)
                    if(keyword.name=="UNITY_UI_CLIP_RECT"||keyword.name=="UNITY_UI_ALPHACLIP")owned.SetKeyword(keyword,baseMaterial.IsKeywordEnabled(keyword.name));
            }
            foreach(var value in overrides)if(owned.HasProperty(value.name))value.Apply(owned);
            if(syncRect)
            {
                var r=graphic.rectTransform.rect;if(owned.HasProperty("_RectWidth"))owned.SetFloat("_RectWidth",r.width);if(owned.HasProperty("_RectHeight"))owned.SetFloat("_RectHeight",r.height);
            }
            if(syncSpriteSheet&&owned.HasProperty("_SpriteSheetRect"))owned.SetVector("_SpriteSheetRect",atlas);
            Error=null;State="ready";return owned;
        }
        public void CaptureRuntimeOverrides(string[] names)
        {
            if(!Application.isPlaying||!isActiveAndEnabled||owned==null)throw new InvalidOperationException("An active, rendered bridge is required.");
            foreach(var name in names)
            {
                int i=owned.shader.FindPropertyIndex(name);if(i<0)throw new ArgumentException("Unknown runtime property: "+name);
                var p=new Parameter{name=name,type=owned.shader.GetPropertyType(i)};
                switch(p.type){case ShaderPropertyType.Color:p.vector=owned.GetColor(name);break;case ShaderPropertyType.Vector:p.vector=owned.GetVector(name);break;case ShaderPropertyType.Texture:p.texture=owned.GetTexture(name);break;case ShaderPropertyType.Int:p.integer=owned.GetInteger(name);break;default:p.number=owned.GetFloat(name);break;}
                overrides.RemoveAll(v=>v.name==name);overrides.Add(p);
            }
        }
        public void ResetRuntimeOverrides(){overrides.Clear();Dirty();}
        private static bool NativeUiProperty(string name)=>name=="_MainTex"||name.StartsWith("_Stencil",StringComparison.Ordinal)||name=="_ColorMask"||name=="_UseUIAlphaClip"||name=="_ClipRect"||name.StartsWith("_UIMaskSoftness",StringComparison.Ordinal);
        private void Release()
        {
            var material=owned;owned=null;borrowed=null;
            if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}State="idle";
        }
    }
}
