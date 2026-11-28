using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.Pipeline.VisualUI
{
    // Independent of the optional runtime CLI protocol: authored scenes keep this component in release builds.
    [AddComponentMenu("UI/Pipeline Shape")]
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class PipelineShapeGraphic : MaskableGraphic, ICanvasRaycastFilter
    {
        public enum ShapeKind { Chamfer, Parallelogram, Trapezoid, Polygon }
        public ShapeKind shape = ShapeKind.Chamfer;
        public Vector4 cuts = new Vector4(16,16,16,16); // TL, TR, BR, BL in local units
        [Range(-.49f,.49f)] public float skew = .15f;
        public Vector2 insets = new Vector2(.15f,0); // top/bottom fraction of width
        public Vector2[] points;
        public Color endColor = Color.white;
        public float gradientAngle;
        public float borderWidth;
        public Color borderColor = Color.white;
        public Texture texture;
        public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;

        public Vector2[] Outline(Rect r)
        {
            var v = new List<Vector2>();
            if (shape == ShapeKind.Chamfer)
            {
                float tl=cuts.x,tr=cuts.y,br=cuts.z,bl=cuts.w;
                // Layout can shrink this Rect after authoring. Preserve the shape, scaling all cuts together.
                float factor=1;
                foreach(var pair in new[]{new Vector2(tl+tr,r.width),new Vector2(bl+br,r.width),new Vector2(tl+bl,r.height),new Vector2(tr+br,r.height)})
                    if(pair.x>0)factor=Mathf.Min(factor,Mathf.Max(0,pair.y*.999f)/pair.x);
                tl*=factor;tr*=factor;br*=factor;bl*=factor;
                v.Add(new Vector2(r.xMin+bl,r.yMin)); v.Add(new Vector2(r.xMax-br,r.yMin));
                if(br>0)v.Add(new Vector2(r.xMax,r.yMin+br));
                v.Add(new Vector2(r.xMax,r.yMax-tr)); if(tr>0)v.Add(new Vector2(r.xMax-tr,r.yMax));
                v.Add(new Vector2(r.xMin+tl,r.yMax)); if(tl>0)v.Add(new Vector2(r.xMin,r.yMax-tl));
                v.Add(new Vector2(r.xMin,r.yMin+bl));
            }
            else
            {
                Vector2[] uv;
                if(shape==ShapeKind.Parallelogram)
                {
                    float s=Mathf.Abs(skew);
                    uv=skew>=0 ? new[]{new Vector2(0,0),new Vector2(1-s,0),new Vector2(1,1),new Vector2(s,1)}
                        : new[]{new Vector2(s,0),new Vector2(1,0),new Vector2(1-s,1),new Vector2(0,1)};
                }
                else if(shape==ShapeKind.Trapezoid)
                    uv=new[]{new Vector2(insets.y,0),new Vector2(1-insets.y,0),new Vector2(1-insets.x,1),new Vector2(insets.x,1)};
                else uv=points;
                if(uv==null)return Array.Empty<Vector2>();
                foreach(var p in uv)v.Add(new Vector2(r.xMin+p.x*r.width,r.yMin+p.y*r.height));
            }
            if(v.Count>1&&(v[0]-v[v.Count-1]).sqrMagnitude<1e-12f)v.RemoveAt(v.Count-1);
            return v.ToArray();
        }

        public static bool Convex(Vector2[] v, Rect bounds, out string error)
        {
            error=null;
            if(v==null||v.Length<3||v.Length>64){error="A convex shape requires 3..64 vertices.";return false;}
            float tolerance=Mathf.Max(bounds.width,bounds.height)*1e-6f;
            if(bounds.width<=0||bounds.height<=0){error="Shape Rect must have positive area.";return false;}
            for(int i=0;i<v.Length;i++)
            {
                var p=v[i];
                if(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.x)||float.IsInfinity(p.y)
                    ||p.x<bounds.xMin-tolerance||p.x>bounds.xMax+tolerance||p.y<bounds.yMin-tolerance||p.y>bounds.yMax+tolerance)
                {error="Vertices must be finite and inside the Rect.";return false;}
                var edge=v[(i+1)%v.Length]-p;
                if(edge.sqrMagnitude<=tolerance*tolerance){error="Repeated or degenerate vertices.";return false;}
                // Every other vertex must lie strictly on the interior side of every edge.
                // This also rejects star/self-intersecting order, rather than trusting local turns.
                for(int j=0;j<v.Length;j++)
                    if(j!=i&&j!=(i+1)%v.Length&&Cross(edge,v[j]-p)<=tolerance*edge.magnitude)
                    {error="Use a strictly convex, counter-clockwise, non-self-intersecting outline.";return false;}
            }
            return true;
        }

        public static Vector2[] Inset(Vector2[] v,float width)
        {
            var inner=new Vector2[v.Length];
            for(int i=0;i<v.Length;i++)
            {
                var a=(v[i]-v[(i+v.Length-1)%v.Length]).normalized;
                var b=(v[(i+1)%v.Length]-v[i]).normalized;
                var na=new Vector2(-a.y,a.x);var nb=new Vector2(-b.y,b.x);
                float denominator=1+Vector2.Dot(na,nb);
                if(denominator<1e-6f)return null;
                inner[i]=v[i]+(na+nb)*(width/denominator);
            }
            // An inset beyond the inradius may turn inside-out yet still have positive signed area.
            foreach(var p in inner)
                for(int i=0;i<v.Length;i++)
                {
                    var e=v[(i+1)%v.Length]-v[i];
                    float epsilon=Mathf.Max(1e-5f,Mathf.Max(width,e.magnitude)*1e-6f);
                    if(Cross(e,p-v[i])/e.magnitude<width-epsilon)return null;
                }
            return Convex(inner,Bounds(v),out _)?inner:null;
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();var r=GetPixelAdjustedRect();var outer=Outline(r);
            if(!Convex(outer,r,out _))return;
            var inner=borderWidth>0?Inset(outer,borderWidth):outer;
            if(inner==null){Fan(helper,outer,r,true);return;} // Stable saturation after a driven resize.
            if(borderWidth>0)
            {
                for(int i=0;i<outer.Length;i++)
                {
                    int n=(i+1)%outer.Length,k=helper.currentVertCount;
                    Vertex(helper,outer[i],r,true);Vertex(helper,outer[n],r,true);
                    Vertex(helper,inner[n],r,true);Vertex(helper,inner[i],r,true);
                    helper.AddTriangle(k,k+1,k+2);helper.AddTriangle(k,k+2,k+3);
                }
            }
            Fan(helper,inner,r,false);
        }
        private void Fan(VertexHelper helper,Vector2[] v,Rect r,bool border)
        {
            var center=Vector2.zero;foreach(var p in v)center+=p;center/=v.Length;
            int k=helper.currentVertCount;Vertex(helper,center,r,border);
            foreach(var p in v)Vertex(helper,p,r,border);
            for(int i=0;i<v.Length;i++)helper.AddTriangle(k,k+1+i,k+1+(i+1)%v.Length);
        }
        private void Vertex(VertexHelper helper,Vector2 p,Rect r,bool border)
        {
            var uv=new Vector2((p.x-r.xMin)/r.width,(p.y-r.yMin)/r.height);
            var d=new Vector2(Mathf.Cos(gradientAngle*Mathf.Deg2Rad),Mathf.Sin(gradientAngle*Mathf.Deg2Rad));
            float t=.5f+Vector2.Dot(uv-new Vector2(.5f,.5f),d)/(Mathf.Abs(d.x)+Mathf.Abs(d.y));
            Color tint=border?borderColor:Color.Lerp(color,endColor,t);
            if(border)tint.a*=color.a;
            helper.AddVert(new Vector3(p.x,p.y,0),tint,uv);
        }
        public bool IsRaycastLocationValid(Vector2 screenPoint,Camera eventCamera)
        {
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screenPoint,eventCamera,out var p))return false;
            var r=GetPixelAdjustedRect();var v=Outline(r);if(!Convex(v,r,out _))return false;
            for(int i=0;i<v.Length;i++)if(Cross(v[(i+1)%v.Length]-v[i],p-v[i])<0)return false;
            return true;
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static Rect Bounds(Vector2[] v)
        {
            var min=v[0];var max=v[0];foreach(var p in v){min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
    }
}
