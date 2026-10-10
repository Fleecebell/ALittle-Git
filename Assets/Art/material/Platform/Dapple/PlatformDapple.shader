Shader "Custom/PlatformDapple"
{
    // ======================================================================
    // PlatformDapple —— 平台斑驳覆盖层 Shader (独立使用, 不改平台本体 Shader)
    // --------------------------------------------------------------------------
    // 用法: 给单独的 Platform_Dapple Tilemap 用 (无 Collider, 排序在
    //       Platform_Body 之上)。Mask 数据有两种传入方式 (Inspector 二选一):
    //
    //       方式1 瓦片灰度 (默认, 不勾"使用整图 Mask"):
    //         瓦片贴图的灰度 = 该格斑驳的基础强度
    //         黑 = 不出斑驳   灰 = 弱斑驳   白 = 强斑驳
    //         → 适合纯色数据瓦片 (一块瓦片一个强度值)
    //       方式2 整图 Mask (勾选"使用整图 Mask" + 拖入 _MaskTex):
    //         一张覆盖整片平台的渐变图, 按世界坐标采样
    //         图左下角 = _MaskWorldOrigin, 覆盖范围 = _MaskWorldSize
    //         → 适合"顶部强→往下渐弱"这类整图数据 (瓦片只当覆盖范围载体,
    //           刷纯白瓦片即可, 灰度全部来自整图)
    //
    //       两种模式都: Shader 只把灰度当数据读, 绝不把黑白画到画面上。
    //       !! 数据贴图导入规范: 关闭 sRGB (Color Texture) + Compression=None —
    //       本项目是 Linear 色彩空间, sRGB 贴图采样时灰度会被转换压暗
    //       (50% 灰 → 约 0.21), 导致"灰和白看起来一样"、渐变层次失真。
    //
    // 颜色管线 (Mask 是主形状, Noise 只做扰动):
    //   Mask 灰度 (美术形状: 黑=无 灰=弱 白=强)
    //     → × (1 + 世界坐标 Noise 扰动)   ← _DappleNoiseWeight 控制, 只在 Mask 内起伏
    //     → Contrast 值域整形 → Softness 过渡带宽
    //     × _DappleStrength (整体强度)
    //     → 染上 _DappleColor → 输出彩色斑点
    //
    // Alpha 说明:
    //   Platform_Body (平台本体) 的颜色/Alpha 完全不受影响;
    //   本覆盖层自身的 Alpha 只用来承载斑驳强弱 ——
    //   黑 Mask 处 amount=0 直接 discard, 画面上什么都看不到 (不会留灰膜);
    //   灰 Mask 处是"更淡的彩色斑点", 不是半透明的灰色。
    //
    // 简单版: 无 2D Light / 无渐变 / 无顶部检测 / 无动态效果, 按需再加。
    // ======================================================================

    Properties
    {
        // 灰度 Mask 数据瓦片的贴图 (TilemapRenderer 自动绑定, 不用手动填)
        _MainTex("Mask 数据瓦片 (TilemapRenderer 自动绑定)", 2D) = "white" {}

        [Header(Dapple)]
        _DappleColor("斑驳颜色", Color) = (0.45, 0.6, 0.35, 1)
        _DappleStrength("斑驳强度 (整体开关, 0=关)", Range(0, 1)) = 1
        _DappleColorStrength("颜色混入 (斑块最深时的不透明度)", Range(0, 1)) = 0.8
        _DappleNoiseWeight("噪声扰动 (0=完全按Mask, 1=强烈扰动)", Range(0, 1)) = 0.3
        _DappleScale("扰动粗细 (世界单位, 越小豁口越碎)", Float) = 0.8
        _DappleContrast("对比整形 (>1 灰值两极化, 1=按Mask原样不变, <1 平缓)", Range(0.25, 4)) = 1
        _DappleSoftness("柔和度 (0=按Mask原值透出, 1=S曲线渐变)", Range(0, 1)) = 0.25

        [Header(Mask)]
        _MaskStrength("Mask 强度 (整体压低 Mask 的影响)", Range(0, 1)) = 1

        [Header(Whole Image Mask)]
        [Toggle] _UseMaskTex("使用整图 Mask (勾=用下方贴图数据, 不勾=用瓦片灰度)", Float) = 0
        _MaskTex("整图 Mask 数据 (白=强 灰=弱 黑=无, 建议 Compression=None)", 2D) = "white" {}
        _MaskWorldOrigin("整图原点 XY (图左下角对应的世界坐标)", Vector) = (0, 0, 0, 0)
        _MaskWorldSize("整图覆盖 XY (图覆盖的世界宽高)", Vector) = (32, 8, 0, 0)
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
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;   // 瓦片自身 UV → 只用来读 Mask 灰度数据
                float2 worldPos : TEXCOORD1;   // 世界坐标 → 用来生成跨 Tile 连续的 Noise
                float4 color    : TEXCOORD2;   // 顶点色 (Tile 的 Color) 透传
            };

            // ---------- 贴图声明 ----------
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            // ---------- 参数 ----------
            half4  _DappleColor;
            half   _DappleStrength;
            half   _DappleColorStrength;
            half   _DappleNoiseWeight;
            float  _DappleScale;
            half   _DappleContrast;
            half   _DappleSoftness;
            half   _MaskStrength;
            float  _UseMaskTex;
            float4 _MaskWorldOrigin;
            float4 _MaskWorldSize;

            // ---------- 程序化噪声: 世界坐标采样 → 跨 Tile 连续 ----------
            // (内部用 float 计算, 哈希在 half 精度下会坏)
            float DappleHash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float DappleValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);   // smoothstep 插值, 保证值连续
                float a = DappleHash(i);
                float b = DappleHash(i + float2(1, 0));
                float c = DappleHash(i + float2(0, 1));
                float d = DappleHash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // ---------- 顶点着色器 ----------
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xy;   // 世界坐标
                o.color = v.color;
                return o;
            }

            // ---------- 片元着色器 ----------
            half4 frag (v2f i) : SV_Target
            {
                // ---- 1. Mask: 只当数据用, 绝不上画面 (两种来源二选一) ----
                float mask;
                if (_UseMaskTex > 0.5)
                {
                    // 整图模式: 世界坐标 → 整图 UV (图左下角对齐 _MaskWorldOrigin)
                    // 覆盖整个连续平台的渐变 Mask 用这种, 一张图管整个平台区域
                    float2 muv = (i.worldPos - _MaskWorldOrigin.xy) / max(_MaskWorldSize.xy, float2(0.0001, 0.0001));
                    if (muv.x < 0.0 || muv.x > 1.0 || muv.y < 0.0 || muv.y > 1.0)
                    {
                        mask = 0.0;   // 图覆盖范围之外 → 无斑驳
                    }
                    else
                    {
                        half4 m4 = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, muv);
                        // 乘 alpha: 透明像素视作纯黑 (PNG 透明区 RGB 常为白, 只读 r 会误判)
                        mask = saturate(m4.r * m4.a * _MaskStrength);
                    }
                }
                else
                {
                    // 瓦片模式: 当前瓦片的灰度 = 该格强度
                    // (适合半砖/异形数据瓦片; 乘 alpha → 透明部分视作纯黑=无斑驳,
                    //  PNG 透明区 RGB 常为白, 只读 r 会把透明误判成强斑驳)
                    half4 maskTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    float tileTint = dot(i.color.rgb, half3(0.299, 0.587, 0.114));
                    mask = saturate(maskTex.r * maskTex.a * tileTint * _MaskStrength);
                }

                // ---- 2. 世界坐标 Noise: 只扰动 Mask, 不生成形状 ----
                // 连续跨 Tile, 绝不用单 Tile UV; Scale = 扰动的空间频率 (豁口/起伏的粗细)
                float2 np = i.worldPos / max(_DappleScale, 0.0001);
                // 轻度域扭曲: 先让噪声场自己变成波浪形有机纹路 (去掉 value noise 的规则格子感)
                np += (float2(DappleValueNoise(np + float2(17.3, 51.7)),
                              DappleValueNoise(np + float2(93.7, 11.3))) - 0.5) * 0.8;
                // 双倍频叠加: 粗起伏 70% + 细碎变化 30%, 更自然不呆板
                float n01 = DappleValueNoise(np) * 0.7 + DappleValueNoise(np * 2.6 + 7.7) * 0.3;
                float n = n01 * 2.0 - 1.0;                             // -1..1, 均值 0

                // ---- 3. 扰动: Noise 轻推 Mask 的值 ----
                // 乘法式 → 黑色处永远为 0, 斑驳绝不逃出 Mask 画好的形状;
                // weight=0 → 完全等于 Mask; 调高 → 白/灰区的值随机起伏:
                //   边缘被啃出随机豁口, 内部出现深浅变化 (自然风化感)
                float disturbed = saturate(mask * (1.0 + n * _DappleNoiseWeight));

                // ---- 4. Contrast: 整形扰动后的 Mask 值域 (不重新生成形状) ----
                // 1=原样; >1 中间值向 0/1 两极分化 (豁口边缘利落, 灰区对比强);
                // <1 向 0.5 收拢 (整体更平缓更弱)
                float c = disturbed * 2.0 - 1.0;
                c = sign(c) * pow(abs(c), _DappleContrast);
                disturbed = saturate(c * 0.5 + 0.5);

                // ---- 5. Softness: 柔和度曲线 (0 = 按 Mask 原值透出, 1 = S 曲线渐变) ----
                // 不再有固定 0.5 中心的阈值带宽, 贴图里的灰度渐变不会被压成两极
                float dapple = lerp(disturbed, smoothstep(0.0, 1.0, disturbed), _DappleSoftness);

                // ---- 6. 强度合成: Mask 形状 → 扰动 → 整形 → 总强度 ----
                float amount = dapple * _DappleStrength;

                // 无斑驳处直接丢弃 → 不留任何半透明灰膜, 也省填充开销
                clip(amount - 0.001);

                // ---- 5. 输出彩色斑驳 ----
                // 绝不输出黑白 Mask; 覆盖层 Alpha 只承载斑驳强弱 (平台 Alpha 不受影响)
                return half4(_DappleColor.rgb, saturate(amount * _DappleColorStrength));
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
