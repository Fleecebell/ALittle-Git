Shader "Unlit/WorldSpacePattern"
{
    // ======================================================================
    // WorldSpacePattern
    // 用途: 在平台(瓦片地图/Sprite)上叠加一层"不随平台移动"的圆点图案,
    //       产生类似 Photoshop 剪贴蒙版的效果——圆点只在平台形状内显示。
    //
    // 关键点:
    //   1. 底色 = 平台贴图(_MainTex), 由 TilemapRenderer / SpriteRenderer 自动绑定.
    //   2. 圆点 = 用"世界坐标"采样圆点贴图, 所以平台移动时圆点不动.
    //   3. 蒙版 = 最终 alpha 取自平台底色, 平台形状决定可见范围.
    //   4. 动画 = 世界 UV 叠加时间偏移, 圆点会缓慢漂移.
    // ======================================================================

    Properties
    {
        [Header(Base Platform)]
        _MainTex ("平台底图 (自动来自 Tile/Sprite)", 2D) = "white" {}

        [Header(Dot Pattern)]
        _PatternTex ("圆点贴图", 2D) = "white" {}
        _PatternColor ("圆点颜色", Color) = (1,1,1,1)
        _PatternScale ("圆点尺寸 (世界单位, 越大越疏)", Float) = 2.0
        _PatternScrollSpeed ("圆点移动速度 XY", Vector) = (0.10, 0.05, 0, 0)
        _PatternIntensity ("圆点强度", Range(0,1)) = 1.0
        _PatternAlpha ("圆点整体透明", Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // 标准透明混合: 支持透明底的圆点贴图与平台边缘半透明
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off   // 2D 精灵通常双面可见

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ---------- 顶点输入 ----------
            struct appdata
            {
                float4 vertex : POSITION;  // 物体空间顶点
                float2 uv     : TEXCOORD0; // 平台贴图 UV (每个 Tile 自身 UV)
                float4 color  : COLOR;     // 顶点色: Tilemap/Sprite 运行时染色用
            };

            // ---------- 顶点输出 ----------
            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0; // 平台底图 UV
                float4 worldPos : TEXCOORD1; // 世界坐标 (用于圆点图案)
                float4 color    : TEXCOORD2; // 顶点色透传
            };

            // ---------- 贴图声明 (URP 宏) ----------
            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;     // Tiling/Offset (Inspector 可调)

            TEXTURE2D(_PatternTex); SAMPLER(sampler_PatternTex);
            // 圆点用世界坐标采样, 不走 Tiling/Offset, 所以不需要 _PatternTex_ST

            // ---------- 参数 ----------
            half4  _PatternColor;        // 圆点颜色叠加
            float  _PatternScale;        // 一个圆点占多少世界单位
            float4 _PatternScrollSpeed;  // xy = 漂移速度
            half   _PatternIntensity;    // 强度
            half   _PatternAlpha;        // 整体透明

            // ==================================================================
            // 顶点着色器
            // ==================================================================
            v2f vert (appdata v)
            {
                v2f o;
                // 1) 物体顶点 -> 裁剪空间
                o.vertex = TransformObjectToHClip(v.vertex.xyz);

                // 2) 平台底图 UV (支持 Tiling/Offset)
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                // 3) 世界坐标: 圆点图案用它采样, 所以圆点不随平台移动
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);

                // 4) 顶点色透传 (Tilemap.color 运行时改色也能生效)
                o.color = v.color;
                return o;
            }

            // ==================================================================
            // 片元着色器
            // ==================================================================
            half4 frag (v2f i) : SV_Target
            {
                // ---- 1. 采样平台底色 (被蒙版的内容) ----
                // TilemapRenderer 会自动把当前 Tile 的纹理绑到 _MainTex
                half4 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                base *= i.color; // 应用顶点色

                // ---- 2. 用世界坐标采样圆点贴图 (圆点不随平台移动) ----
                // worldPos / scale: scale 越大, 一个圆点覆盖的世界范围越大 -> 圆点越疏
                float scale = max(_PatternScale, 0.0001); // 防止除 0
                float2 patternUV = i.worldPos.xy / scale;
                // 叠加时间偏移 -> 圆点缓慢移动 (动画)
                patternUV += _PatternScrollSpeed.xy * _Time.y;
                half4 pattern = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, patternUV);

                // ---- 3. 圆点颜色与可见度调制 ----
                half3 patternRGB = pattern.rgb * _PatternColor.rgb;
                // 可见度 = 圆点贴图 alpha × 颜色 alpha × 整体透明 × 强度
                half  patternA  = pattern.a * _PatternColor.a * _PatternAlpha * _PatternIntensity;

                // ---- 4. 圆点叠加到平台底色上 ----
                // 圆点处显示圆点颜色, 圆点间隙透出平台底色
                half3 finalRGB = lerp(base.rgb, patternRGB, patternA);

                // ---- 5. 剪贴蒙版: 用平台 alpha 决定整体可见范围 ----
                // 平台形状内才显示 (含圆点), 平台外完全透明
                half finalA = base.a;

                return half4(finalRGB, finalA);
            }
            ENDHLSL
        }
    }

    // 给 URP 2D Renderer 的回退 (可选)
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
