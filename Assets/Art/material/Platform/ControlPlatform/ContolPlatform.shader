Shader "Custom/ContolPlatform"
{
    Properties
    {
        // 平台本身的 Sprite 贴图，SpriteRenderer / Tilemap 会自动写入这个属性
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}

        // 底板颜色，也可以用 SpriteRenderer 的 Color 或 Tile 颜色叠加
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)

        // 纹理贴图（木纹 / 石纹 / 岩石纹等）：RGB 灰度作为明暗细节，Alpha 作为覆盖范围
        _PatternTex ("Pattern Texture", 2D) = "white" {}

        // 给纹理灰度上色，纹理的明暗变化会保留
        _PatternColor ("Pattern Color", Color) = (1,1,1,1)

        // 纹理强度：0 = 完全看不到纹理，1 = 完全显示纹理颜色
        _PatternStrength ("Pattern Strength", Range(0, 1)) = 1

        // 纹理缩放（重复频率），1 = 每 1 个世界单位重复一次；值越大纹理越密集
        _PatternScale ("Pattern Scale", Float) = 1

        // 纹理偏移（世界单位）
        _PatternOffset ("Pattern Offset", Vector) = (0, 0, 0, 0)

        // 纹理锚定到平台：1 = 用平台相对坐标（移动的 Tilemap 平台必须开，否则纹理随平台滑动）
        //                    0 = 直接用顶点坐标（普通 SpriteRenderer 的顶点本来就是局部的，用 0）
        _PatternAnchor ("Pattern Anchor To Platform (1=tilemap 用, 0=sprite 用)", Range(0, 1)) = 1

        // 平台世界位置，由 DappleAnchorFollower 脚本每帧写入（不用手动填）
        _PlatformAnchorPos ("Platform Anchor XY (written by DappleAnchorFollower)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 positionOS : TEXCOORD1;
                float4 color      : COLOR;
            };

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_PatternTex); SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PatternColor;
                float  _PatternStrength;
                float  _PatternScale;
                float4 _PatternOffset;
                float  _PatternAnchor;
                float4 _PlatformAnchorPos;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.positionOS = IN.positionOS.xy;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // 1. 取 Sprite 自身的 Alpha，用来保留平台原本的透明轮廓
                half spriteAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;

                // 2. 底板颜色 = 材质颜色 * 顶点颜色
                //    顶点颜色来自 SpriteRenderer 的 Color 或 Tilemap 中每个 Tile 的颜色
                half4 baseColor = _BaseColor * IN.color;

                // 3. 纹理坐标：用平台自身坐标，而不是 Sprite UV
                //    Tilemap / Sprite 图集的 UV 只是整张图集里的一小块，
                //    用它采样会导致只看到纹理的一个小裁切区域。
                //    !! Tilemap 送进来的顶点 positionOS 其实是"世界坐标"（平台一动它跟着变），
                //       直接用它采样纹理会随平台平移而从表面扫过去。
                //       减掉 DappleAnchorFollower 每帧写进来的平台位置 → 平台相对坐标，
                //       纹理就钉在平台表面上了（_PatternAnchor=0 时退化为原行为）。
                //    frac 保证无论纹理 Wrap Mode 是什么都能平铺。
                float2 patternPos = IN.positionOS - _PlatformAnchorPos.xy * _PatternAnchor;
                float2 patternUV = frac(patternPos * _PatternScale + _PatternOffset.xy);

                // 4. 保留贴图自带的灰度明暗，只用 Pattern Color 给它上色
                half4 patternTex = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, patternUV);
                half  patternLum = dot(patternTex.rgb, half3(0.299, 0.587, 0.114));
                half3 tintedPattern = _PatternColor.rgb * patternLum;

                // 5. 用贴图 Alpha 决定覆盖范围，强度控制整体浓度
                half patternMask = saturate(patternTex.a * _PatternStrength);

                // 6. 底板颜色 → 上色后的纹理
                half3 finalColor = lerp(baseColor.rgb, tintedPattern, patternMask);
                half  finalAlpha = baseColor.a * spriteAlpha;

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
}
