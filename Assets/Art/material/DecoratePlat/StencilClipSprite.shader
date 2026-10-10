Shader "Unlit/StencilClipSprite"
{
    // ======================================================================
    // StencilClipSprite —— 装饰精灵专用: 只在"墙模板"范围内显示 (剪贴蒙版)
    // --------------------------------------------------------------------------
    // 配合 WorldSpacePattern 的"模板写入=Replace"使用:
    //   1. 墙的 TilemapRenderer 材质用 WorldSpacePattern 材质,
    //      并把它的 "模板写入" 设为 Replace —— 每个瓦片把自己的可见形状
    //      写进模板缓冲 (全透明像素不写, 所以模板=墙的形状)
    //   2. 装饰物体 (SpriteRenderer) 的材质用本 shader:
    //      只在模板=1 (墙的像素) 的地方绘制, 转到墙外自动被剪掉
    //   3. 每关的墙换形状也不用改任何东西 —— 模板是运行时由实际瓦片写的
    //
    // 用法: Project 里建一个 Material 选本 shader, 挂到装饰物的
    //       SpriteRenderer → Materials → Element 0 (贴图会自动绑定)
    //       装饰物照常挂 AutoSpin / AutoOrbit 旋转公转
    //
    // 排序要求: 墙必须先画、装饰后画 (模板先写后读).
    //   装饰物的 Sorting Layer / Order in Layer 要排在墙的后面 (数值更大)
    // ======================================================================

    Properties
    {
        _MainTex ("Sprite (自动绑定)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        // 只在模板=1 (墙上) 的像素绘制; 墙外模板测试失败 → 像素直接丢弃
        Stencil
        {
            Ref 1
            Comp Equal
            Pass Keep
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ---------- 顶点输入/输出 ----------
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;   // SpriteRenderer 的 Color 通过顶点色进来
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                c *= i.color;
                return c;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
