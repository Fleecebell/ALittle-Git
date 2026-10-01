Shader "Unlit/WorldSpacePattern"
{
    // ======================================================================
    // WorldSpacePattern —— 装饰图案通用 Shader (基于原版扩展, 5 个属性轴)
    // --------------------------------------------------------------------------
    // 用途: 在平台/墙壁/背景上叠加一层"不随平台移动"的几何图案,
    //       产生类似 Photoshop 剪贴蒙版的效果——图案只在平台形状内显示.
    //
    // 5 个属性轴 (每个轴独立, 互不依赖):
    //   轴1 平铺方式: 几何图密度 + 整体旋转角度
    //   轴2 运动方式: 平移速度 XY + 自转速度
    //   轴3 变换参数: 呼吸幅度/速度, 色相循环速度
    //   轴4 混合模式: Alpha(透明叠加) / Additive(发光叠加)
    //   轴5 Mask 遮罩: 哪里有效果 (白色=显示 透明=不显示)
    //
    // 关键点(沿用原版):
    //   1. 底色 = 平台贴图(_MainTex), 由 TilemapRenderer / SpriteRenderer 自动绑定
    //   2. 几何 = 用"世界坐标"采样, 所以平台移动时几何不动 (剪贴蒙版)
    //   3. 蒙版 = 最终 alpha 取自平台底色, 平台形状决定可见范围
    //   4. 动画 = 世界 UV 叠加时间偏移, 几何缓慢漂移/自转/呼吸
    //
    // 使用流程:
    //   1. 你画一张白色几何图 (PS/Aseprite, 白色不透明背景透明) 作为 _PatternTex
    //   2. 调 _PatternColor 给几何染色 (白×色=染色结果)
    //   3. 调 _PatternScale 控制密度 (2.0 = 2 米一个)
    //   4. 想运动: 调 _PatternScrollSpeed / _PatternSpinSpeed
    //   5. 想发光: 勾 _PatternAdditive
    //   6. 想限定区域: 画一张 Mask 图 (白色=显示) 挂到 _MaskTex
    // ======================================================================

    Properties
    {
        [Header(Base Platform)]
        _MainTex ("平台底图 (自动来自 Tile/Sprite)", 2D) = "white" {}

        [Header(Axis 1 Tiling)]
        _PatternTex ("几何图", 2D) = "white" {}
        _PatternColor ("几何颜色", Color) = (1, 1, 1, 1)
        _PatternScale ("平铺密度 (世界单位, 越大越疏)", Float) = 2.0
        _PatternRotation ("整体旋转角度 (度)", Float) = 0

        [Header(Axis 2 Motion)]
        _PatternScrollSpeed ("平移速度 XY", Vector) = (0.10, 0.05, 0, 0)
        _PatternSpinSpeed ("自转速度 (度/秒, 0=不自转)", Float) = 0

        [Header(Axis 3 Transform)]
        _PatternPulseAmplitude ("呼吸幅度 (0=不呼吸)", Range(0, 1)) = 0
        _PatternPulseSpeed ("呼吸速度", Float) = 1
        _PatternHueShiftSpeed ("色相循环速度 (0=不变, 1=每秒一圈)", Float) = 0

        [Header(Axis 4 Blend Mode)]
        [Toggle] _PatternAdditive ("发光叠加 (勾=Additive 不勾=Alpha)", Float) = 0
        _PatternIntensity ("几何强度", Range(0, 1)) = 1.0
        _PatternAlpha ("几何整体透明", Range(0, 1)) = 1.0

        [Header(Axis 5 Mask)]
        _MaskTex ("条纹遮罩 (白色=显示 透明=不显示)", 2D) = "white" {}
        _MaskScale ("遮罩密度 (世界单位)", Float) = 1
        _MaskScrollSpeed ("遮罩移动速度 XY", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // 标准 Alpha 混合; Additive 模式在 Pass 内用 #pragma 切换
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            // _PATTERNADDITIVE_ON 是 [Toggle] _PatternAdditive 自动生成的宏
            // multi_compile 让 Unity 编译两个版本 (Alpha / Additive), Inspector 勾选时切换
            #pragma multi_compile _ _PATTERNADDITIVE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ---------- 顶点输入 ----------
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            // ---------- 顶点输出 ----------
            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;  // 平台底图 UV
                float2 worldPos : TEXCOORD1;  // 世界坐标 (几何/Mask 用它采样)
                float4 color    : TEXCOORD2;  // 顶点色透传
            };

            // ---------- 贴图声明 (URP 宏) ----------
            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            TEXTURE2D(_PatternTex); SAMPLER(sampler_PatternTex);

            TEXTURE2D(_MaskTex);    SAMPLER(sampler_MaskTex);

            // ---------- 参数 ----------
            half4  _PatternColor;
            float  _PatternScale;
            float  _PatternRotation;
            float4 _PatternScrollSpeed;
            float  _PatternSpinSpeed;
            half   _PatternPulseAmplitude;
            float  _PatternPulseSpeed;
            float  _PatternHueShiftSpeed;
            half   _PatternIntensity;
            half   _PatternAlpha;

            float  _MaskScale;
            float4 _MaskScrollSpeed;

            // ---------- 辅助: HSV 转 RGB ----------
            // hue 0~1 = 红→黄→绿→青→蓝→紫→红 一圈
            half3 hsv2rgb(half3 c)
            {
                half3 rgb = clamp(abs(fmod(c.x * 6.0 + half3(0, 4, 2), 6.0) - 3.0) - 1.0, 0, 1);
                return c.z * lerp(half3(1, 1, 1), rgb, c.y);
            }

            // ---------- 辅助: 2D UV 旋转 ----------
            float2 rotateUV(float2 uv, float angleDeg)
            {
                float rad = angleDeg * 3.14159265359 / 180.0;
                float s = sin(rad);
                float c = cos(rad);
                return float2(c * uv.x - s * uv.y, s * uv.x + c * uv.y);
            }

            // ==================================================================
            // 顶点着色器
            // ==================================================================
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xy;
                o.color = v.color;
                return o;
            }

            // ==================================================================
            // 片元着色器
            // ==================================================================
            half4 frag (v2f i) : SV_Target
            {
                // ---- 1. 平台底色 (蒙版内容) ----
                half4 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                base *= i.color;

                // ---- 2. 几何图采样 (轴1+轴2: 平铺/旋转/平移/自转) ----
                float scale = max(_PatternScale, 0.0001);
                float2 patternUV = i.worldPos / scale;
                patternUV += _PatternScrollSpeed.xy * _Time.y;
                patternUV = rotateUV(patternUV, _PatternRotation);
                patternUV = rotateUV(patternUV, _PatternSpinSpeed * _Time.y);
                half4 pattern = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, patternUV);

                // ---- 3. 几何颜色 (轴3: 色相循环 + 呼吸亮度) ----
                half3 patternColor = _PatternColor.rgb;
                if (_PatternHueShiftSpeed > 0)
                {
                    half hue = frac(_Time.y * _PatternHueShiftSpeed);
                    patternColor = hsv2rgb(half3(hue, 1, 1)) * _PatternColor.rgb;
                }

                half pulse = 1;
                if (_PatternPulseAmplitude > 0)
                {
                    pulse = 1 + _PatternPulseAmplitude * 0.5 * cos(_Time.y * _PatternPulseSpeed * 6.283);
                }

                half3 patternRGB = pattern.rgb * patternColor * pulse;
                half  patternA  = pattern.a * _PatternColor.a * _PatternAlpha * _PatternIntensity;

                // ---- 4. Mask 遮罩 (轴5: 哪里有效果) ----
                float2 maskUV = i.worldPos / max(_MaskScale, 0.0001);
                maskUV += _MaskScrollSpeed.xy * _Time.y;
                half mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUV).a;
                patternA *= mask;

                // ---- 5. 混合模式 (轴4: Alpha vs Additive) ----
                half3 finalRGB;
                half  finalA;

                #if defined(_PATTERNADDITIVE_ON)
                    // Additive 发光叠加: 底色 + 几何色 × 几何A
                    finalRGB = base.rgb + patternRGB * patternA;
                #else
                    // Alpha 透明叠加: 几何盖在底图上 (像贴纸)
                    finalRGB = lerp(base.rgb, patternRGB, patternA);
                #endif

                // 剪贴蒙版: 用平台 alpha 决定整体可见范围 (平台形状内才显示)
                finalA = base.a;

                return half4(finalRGB, finalA);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
