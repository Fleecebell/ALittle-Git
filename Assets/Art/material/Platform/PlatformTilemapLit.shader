Shader "Custom/PlatformTilemapLit"
{
    // ======================================================================
    // PlatformTilemapLit —— 平台 Tilemap 专用 2D 光照 Sprite Shader
    // --------------------------------------------------------------------------
    // 用途: 给 URP 2D 的"平台 Tilemap"上色, 同时完整保留 Unity 2D Light 光照.
    //       基于 URP 14.0.11 官方 Sprite-Lit-Default 移植, 光照部分逐行一致.
    //
    // 颜色管线 (Shader 只负责颜色, 不替代 2D Light):
    //   灰度 Tile 贴图 × 顶点色
    //     → × 平台颜色场 (BaseColor → 横向渐变 + 纵向渐变 加权合成)
    //     → × Unity 2D Light (Shape Light 逐像素光照, 效果与原来完全一样)
    //     → 最终颜色
    //
    // 核心设计:
    //   1. 跨 Tile 连续: 渐变全部用"世界坐标"采样, 不用单 Tile 自己的 UV,
    //      所以 Tile A→B→C 颜色平滑衔接, 不会每块瓦片各自从头渐一遍
    //   2. 横向/纵向不打架: 两个渐变按各自 Strength 加权平均合成一张二维
    //      颜色场; 单边 Strength=0 时该渐变完全消失, 两边都 0 → 退化为 BaseColor
    //   3. Alpha 全程不动: 只乘 RGB, Sprite 透明区不会被填色
    //   4. 无模糊/辉光/后处理, 只改颜色不改轮廓; 与 Tilemap Collider 无关
    //   (噪声/斑驳功能已拆分到独立的 Custom/PlatformDapple 覆盖层 Shader)
    //
    // 使用: 建材质 (右键 Create → Material, Shader 选 Custom/PlatformTilemapLit)
    //       → 拖到平台 Tilemap 的 TilemapRenderer → Materials[0].
    //       墙壁 Tilemap 不换材质就完全不受影响.
    // ======================================================================

    Properties
    {
        _MainTex("Tile 贴图 (TilemapRenderer 自动绑定)", 2D) = "white" {}
        [HideInInspector] _MaskTex("Mask", 2D) = "white" {}
        [HideInInspector] _NormalMap("Normal Map", 2D) = "bump" {}

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0

        [Header(Base)]
        _BaseColor("基础颜色 (渐变全关时的平台颜色)", Color) = (1, 1, 1, 1)
        _OverallStrength("整体效果强度 (渐变效果的总开关)", Range(0, 1)) = 1

        [Header(Horizontal)]
        _HColorA("颜色 A (左端)", Color) = (1, 1, 1, 1)
        _HColorB("颜色 B (右端)", Color) = (1, 1, 1, 1)
        _HStrength("横向强度", Range(0, 1)) = 0
        _HScale("横向跨度 (A→B 经过的世界距离)", Float) = 32
        _HOffset("横向起点 X (世界坐标, 渐变从这里开始; 想反向=交换 A/B)", Float) = 0

        [Header(Vertical)]
        _VColorTop("顶部颜色 (上端)", Color) = (1, 1, 1, 1)
        _VColorBottom("底部颜色 (下端)", Color) = (1, 1, 1, 1)
        _VStrength("纵向强度", Range(0, 1)) = 0
        _VScale("纵向跨度 (底→顶 经过的世界距离)", Float) = 4
        _VOffset("纵向起点 Y (世界坐标, 渐变从这里开始)", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        // ======================= Pass 1: 平台颜色 + 2D 光照 =======================
        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma vertex CombinedShapeLightVertex
            #pragma fragment CombinedShapeLightFragment

            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2  uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS  : SV_POSITION;
                half4   color       : COLOR;
                float2  uv          : TEXCOORD0;
                half2   lightingUV  : TEXCOORD1;
                float2  positionWS  : TEXCOORD2;   // 世界坐标, 渐变的采样基准
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            half4 _MainTex_ST;
            float4 _Color;
            half4 _RendererColor;

            // ---------- 平台颜色参数 ----------
            half4  _BaseColor;
            half   _OverallStrength;
            half4  _HColorA;
            half4  _HColorB;
            half   _HStrength;
            float  _HScale;
            float  _HOffset;
            half4  _VColorTop;
            half4  _VColorBottom;
            half   _VStrength;
            float  _VScale;
            float  _VOffset;

            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif

            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif

            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif

            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif

            Varyings CombinedShapeLightVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionWS = TransformObjectToWorld(v.positionOS).xy;   // 新增: 世界坐标
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);

                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            // ---------- 平台颜色场: 世界坐标 → 颜色 (只算 RGB, 不碰 Alpha) ----------
            half3 PlatformTint(float2 worldPos)
            {
                // ---- 横向渐变: 世界 X 连续, 跨 Tile 不重新开始 ----
                // t=0 在世界 X=_HOffset 处, t=1 在 X=_HOffset+_HScale 处
                float tH = saturate((worldPos.x - _HOffset) / max(_HScale, 0.0001));
                half3 colH = lerp(_HColorA.rgb, _HColorB.rgb, tH);

                // ---- 纵向渐变: 世界 Y 连续 (底部颜色在下, 顶部颜色在上) ----
                float tV = saturate((worldPos.y - _VOffset) / max(_VScale, 0.0001));
                half3 colV = lerp(_VColorBottom.rgb, _VColorTop.rgb, tV);

                // ---- 合成二维颜色场: 按 Strength 加权平均 ----
                // 不相乘 → 两个渐变不会"打架"压暗彼此; 权重归一化后各按话语权混合.
                // 某个 Strength=0 → 权重 0, 该渐变完全消失; 两个都 0 → 退化为 BaseColor
                half wH = _HStrength;
                half wV = _VStrength;
                half wSum = wH + wV;
                half3 field = (wSum > 0.001) ? (colH * wH + colV * wV) / wSum : _BaseColor.rgb;

                // ---- 整体效果强度: 纯 BaseColor ←→ 完整颜色场 ----
                return lerp(_BaseColor.rgb, field, _OverallStrength);
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                half4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);

                // ==== 平台颜色: 只乘 RGB, Alpha 原样保留 (透明区不会被填色) ====
                main.rgb *= PlatformTint(i.positionWS);

                SurfaceData2D surfaceData;
                InputData2D inputData;

                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(i.uv, i.lightingUV, inputData);

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        // ============ Pass 2: 法线渲染 (2D 法线光照需要, 与官方一致) ============
        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                float4 tangent      : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS      : SV_POSITION;
                half4   color           : COLOR;
                float2  uv              : TEXCOORD0;
                half3   normalWS        : TEXCOORD1;
                half3   tangentWS       : TEXCOORD2;
                half3   bitangentWS     : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            half4 _NormalMap_ST;

            Varyings NormalsRenderingVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                o.uv = TRANSFORM_TEX(attributes.uv, _NormalMap);
                o.color = attributes.color;
                o.normalWS = -GetViewForwardDir();
                o.tangentWS = TransformObjectToWorldDir(attributes.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * attributes.tangent.w;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            half4 NormalsRenderingFragment(Varyings i) : SV_Target
            {
                const half4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv));

                return NormalsRenderingShared(mainTex, normalTS, i.tangentWS.xyz, i.bitangentWS.xyz, i.normalWS.xyz);
            }
            ENDHLSL
        }

        // ============ Pass 3: 无光照兜底 (与官方一致) ============
        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS      : SV_POSITION;
                float4  color           : COLOR;
                float2  uv              : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            half4 _RendererColor;

            Varyings UnlitVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                o.uv = TRANSFORM_TEX(attributes.uv, _MainTex);
                o.color = attributes.color * _Color * _RendererColor;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                float4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                return mainTex;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
