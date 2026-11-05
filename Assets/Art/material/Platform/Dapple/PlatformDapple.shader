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
    //     → 门槛裁剪 _DappleThreshold (灰度 < 门槛 一律没斑驳) → 剩下的重映射回 0..1
    //     → × (1 + 平台相对坐标 Noise 扰动)   ← _DappleNoiseWeight 控制, 只在 Mask 内起伏
    //       (世界坐标 - 平台世界位置 → 平台移动时斑驳跟随平台, 不会滑动)
    //     → Contrast 值域整形 → Softness 过渡带宽
    //     × _DappleStrength (整体强度)
    //     → 染上 _DappleColor → 输出彩色斑点
    //
    // 斑驳厚度 _DappleThreshold (按黑白灰度裁, 门槛以上的灰度才有斑驳):
    //   数据图是"上白下黑"的竖直渐变时, 门槛 = 斑驳从最白处往下能蔓延多深:
    //     门槛 0    → 整条渐变都出斑驳 (最厚, 和以前一样)
    //     门槛 0.5  → 只有灰度 ≥ 0.5 的偏白区域出斑驳 (厚度约剩一半)
    //     门槛 0.9  → 只有接近最白的一条出斑驳 (最薄)
    //   门槛以上的灰度会被重新拉伸回 0..1, 所以"淡→浓"的渐隐过渡依然平滑,
    //   只是整段变窄/变薄, 不会因为裁掉一半就只剩一层弱斑驳。
    //
    //   注意: 门槛作用在"原始灰度"上, 且在 Noise 扰动之前 ——
    //   门槛以下的灰度永远是 0, 噪声再怎么起伏也绝不会在下面无中生有出斑驳。
    //
    // !! 数据 / 颜色 严格分离 (形状位置绝不受颜色影响) !!
    //   形状与位置 = 只由"数据贴图"决定, 读的是贴图画好的灰度 (r × a):
    //       瓦片模式   → _MainTex 当前瓦片的灰度
    //       整图模式   → _MaskTex 在对应世界坐标的灰度
    //     参与形状的只有这些标量开关: 门槛 / Mask强度 / 噪声 / 对比 / 柔和 / 总强度。
    //     _DappleColor 和顶点色 (瓦片色、TilemapRenderer 的 Color) 一个都不进 Mask,
    //     所以改斑驳颜色永远不会挪动、缩放、加厚、削薄斑驳。
    //   颜色 = _DappleColor × 顶点色, 只在最后一步染色:
    //     - 想统一改色 → 改材质上的 _DappleColor
    //     - 想分区域改色 → 给瓦片/渲染器上色 (只染色, 不动形状)
    //   一句话: 数据图管"长在哪、长多少", 颜色管"什么颜色"。
    //
    // 移动平台 (自动移动平台上斑驳不能滑动) —— 重要:
    //   实测结论: TilemapRenderer 送进来的顶点 v.vertex 已经是"世界坐标"
    //   (平台一动它跟着变), 而对象矩阵的平移列是 0 —— 也就是说 shader 内部
    //   **拿不到平台自己的位置**。所以必须由脚本喂进来:
    //
    //     Assets/Scripts/Gameplay/Platform/DappleAnchorFollower.cs
    //       挂到移动平台的斑驳层上, 每帧把平台世界位置写进 _DappleAnchorPos
    //
    //   然后把材质的 噪声锚定(_DappleAnchorMode) 设成 5:
    //       噪声坐标 = v.vertex - _DappleAnchorPos  → 平台相对坐标
    //     → 平台平移时坐标恒定, 斑驳钉在平台上; 且是世界单位,
    //       _DappleScale / 噪声扰动 / 对比 / 柔和度 全部照常可调, 没有瓦片重复。
    //
    //   静态平台不挂脚本也行: 它不动, v.vertex-0 就是固定值, 结果一样稳。
    //   锚定模式 0~4 是排查用的对照 (0/1/3/4 会滑, 2 是瓦片UV只能停住但图案重复)。
    //   只处理平移; 如果以后有会自转的平台, 告诉我, 我再把平台轴向一起传进来。
    //   注意: 整图 Mask 模式 (勾"使用整图 Mask") 是按世界坐标采样的, 本来就
    //   不跟平台走; 移动平台请用瓦片灰度模式。
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
        _DappleColor("斑驳颜色 (纯染色: 改它不影响形状/位置)", Color) = (0.45, 0.6, 0.35, 1)
        _DappleStrength("斑驳强度 (整体开关, 0=关)", Range(0, 1)) = 1
        _DappleThreshold("斑驳门槛 (灰度 ≥ 此值才出斑驳: 越大斑驳越薄, 0=整条渐变都出)", Range(0, 1)) = 0
        _DappleColorStrength("斑驳不透明度 (只改浓淡, 不改形状/位置)", Range(0, 1)) = 0.8
        _DappleNoiseWeight("噪声扰动 (0=完全按Mask, 1=强烈扰动)", Range(0, 1)) = 0.3
        _DappleScale("扰动粗细 (世界单位, 越小豁口越碎)", Float) = 0.8
        _DappleContrast("对比整形 (~1=按Mask原样, <1 灰值两极化(斑块更利落), >1 向0.5收拢(更平缓))", Range(0.25, 4)) = 1
        _DappleSoftness("柔和度 (0=按Mask原值透出, 1=S曲线渐变)", Range(0, 1)) = 0.25

        [Header(Debug)]
        _DappleDebug("调试视图 (0=正常, 1=显示噪声坐标, 2=品红标记)", Range(0, 2)) = 0
        _DappleAnchorMode("噪声锚定 (0顶点 1世界 2瓦片UV 3顶点-矩阵原点 4世界-矩阵原点 5顶点-脚本锚点)", Range(0, 5)) = 0
        _DappleAnchorPos("噪声锚点 XY (DappleAnchorFollower 每帧写入, 一般不用手填)", Vector) = (0, 0, 0, 0)

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
            // 禁止动态合批: 动态合批会把顶点烘焙到世界空间、
            // 对象矩阵变成单位矩阵, 导致 shader 里拿不到平台自身的位置。
            "DisableBatching" = "True"
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
                float2 worldPos : TEXCOORD1;   // 世界坐标 → 整图 Mask 用
                float4 color    : TEXCOORD2;   // 顶点色 (Tile 的 Color) 透传
                float2 dapplePos : TEXCOORD3;  // 物体空间顶点位置 → 噪声用, 天生钉在平台上
                float2 dapplePosMinus : TEXCOORD4;  // 顶点位置 - 平台世界原点
                float2 worldPosMinus  : TEXCOORD5;  // 世界坐标 - 平台世界原点
            };

            // ---------- 贴图声明 ----------
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            // ---------- 参数 ----------
            // 必须放在 UnityPerMaterial 里: 否则 shader 不兼容 SRP Batcher,
            // 会被动态合批, 顶点被烘焙到世界空间 → 拿不到平台自身世界位置。
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4  _DappleColor;
            half   _DappleStrength;
            half   _DappleThreshold;
            half   _DappleColorStrength;
            half   _DappleNoiseWeight;
            float  _DappleScale;
            half   _DappleContrast;
            half   _DappleSoftness;
            half   _DappleDebug;
            half   _DappleAnchorMode;
            float4 _DappleAnchorPos;
            half   _MaskStrength;
            float  _UseMaskTex;
            float4 _MaskWorldOrigin;
            float4 _MaskWorldSize;
            CBUFFER_END

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

                // 世界坐标 (只给整图 Mask 用; 那张图本身就是世界坐标数据)
                o.worldPos = TransformObjectToWorld(v.vertex.xyz).xy;

                // ---------- 平台锚点候选 ----------
                // 实测: 这个 tilemap 的顶点不是物体空间坐标(平台一动它也跟着变),
                // 所以除了直接用顶点/世界坐标, 再准备两种"减掉平台世界原点"的锚定方式,
                // 由 _DappleAnchorMode 在片元里挑。平台世界原点 = 对象矩阵的平移列。
                float2 platformOrigin = GetObjectToWorldMatrix()._m03_m13;
                o.dapplePos      = v.vertex.xy;                 // 0: 顶点位置
                o.dapplePosMinus = v.vertex.xy - platformOrigin;  // 3: 顶点 - 平台原点
                o.worldPosMinus  = o.worldPos   - platformOrigin; // 4: 世界 - 平台原点

                o.color = v.color;
                return o;
            }

            // ---------- 片元着色器 ----------
            half4 frag (v2f i) : SV_Target
            {
                // ---- 0. 噪声坐标: 多种锚定方式 (用来找"哪个坐标钉得住") ----
                // 0 = 顶点位置          1 = 世界坐标(对照, 会滑)
                // 2 = 瓦片UV (绝对不动, 但图案按瓦片重复、形状不好调)
                // 3 = 顶点位置 - 平台原点   4 = 世界坐标 - 平台原点   ← 这两个如果稳, 就最理想
                // 5 = 顶点位置 - _DappleAnchorPos   ← 脚本喂平台位置, 推荐的正式方案
                float2 np;
                if (_DappleAnchorMode < 0.5)      np = i.dapplePos / max(_DappleScale, 0.0001);
                else if (_DappleAnchorMode < 1.5) np = i.worldPos / max(_DappleScale, 0.0001);
                else if (_DappleAnchorMode < 2.5) np = i.uv * 32.0 / max(_DappleScale, 0.05);
                else if (_DappleAnchorMode < 3.5) np = i.dapplePosMinus / max(_DappleScale, 0.0001);
                else if (_DappleAnchorMode < 4.5) np = i.worldPosMinus / max(_DappleScale, 0.0001);
                else                              np = (i.dapplePos - _DappleAnchorPos.xy) / max(_DappleScale, 0.0001);

                // ---- 0.5. 调试视图 (默认 0 = 不生效, 不影响正常画面) ----
                // 1 = 把当前噪声坐标画成颜色: 平台移动时颜色钉在表面上不动 = 坐标稳;
                //     颜色在表面上滑动 = 这个网格的顶点/UV 不在平台空间里
                // 2 = 整层变品红 → 确认这版 shader 真的编译生效了
                if (_DappleDebug > 0.5)
                {
                    if (_DappleDebug < 1.5)
                    {
                        float2 dfr = frac(np);
                        return half4(dfr.x, dfr.y, 0.5, 1);
                    }
                    return half4(1, 0, 1, 1);   // 品红
                }

                // ---- 1. Mask: 只当数据用, 绝不上画面 (两种来源二选一) ----
                float mask;
                if (_UseMaskTex > 0.5)
                {
                    // 整图模式: 世界坐标 → 整图 UV (图左下角对齐 _MaskWorldOrigin)
                    // 覆盖整个连续平台的渐变 Mask 用这种, 一张图管整个平台区域
                    // (故意用世界坐标: 一张图可以横跨多块平台。所以它在移动平台上会跟着
                    //  世界走、不跟平台走 —— 移动平台请用瓦片灰度模式)
                    float2 muv = (i.worldPos - _MaskWorldOrigin.xy) / max(_MaskWorldSize.xy, float2(0.0001, 0.0001));
                    if (muv.x < 0.0 || muv.x > 1.0 || muv.y < 0.0 || muv.y > 1.0)
                    {
                        mask = 0.0;   // 图覆盖范围之外 → 无斑驳
                    }
                    else
                    {
                        half4 m4 = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, muv);
                        // 乘 alpha: 透明像素视作纯黑 (PNG 透明区 RGB 常为白, 只读 r 会误判)
                        mask = saturate(m4.r * m4.a);
                    }
                }
                else
                {
                    // 瓦片模式: 当前瓦片的灰度 = 该格强度
                    // (适合半砖/异形数据瓦片; 乘 alpha → 透明部分视作纯黑=无斑驳,
                    //  PNG 透明区 RGB 常为白, 只读 r 会把透明误判成强斑驳)
                    // !! 只读贴图灰度, 绝不乘顶点色 / 瓦片色 ——
                    //    给瓦片或 TilemapRenderer 调颜色, 是纯染色, 不会挪动斑驳的形状位置。
                    half4 maskTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    mask = saturate(maskTex.r * maskTex.a);
                }

                // ---- 1.5. 斑驳门槛: 按黑白灰度裁出"够白才算斑驳"的范围 ----
                // 数据图是"上白下黑"的竖直渐变时, 门槛直接就是斑驳从最白处往下的厚度:
                //   门槛 0   → 整条渐变都出斑驳 (最厚)
                //   门槛 0.5 → 只有偏白的一半区域出斑驳 (厚度减半)
                //   门槛 1   → 只有最白处出斑驳 (最薄)
                // 门槛以上的灰度重新拉伸回 0..1 → 渐隐过渡的柔和程度不变, 只是整段变薄;
                // 门槛以下恒为 0, 后面的 Noise 也不会让它在下面冒出斑驳。
                // (先按原始灰度裁, 再乘 _MaskStrength, 保证门槛读的是贴图真实灰度值)
                float threshold = clamp(_DappleThreshold, 0.0, 0.999);
                mask = saturate((mask - threshold) / (1.0 - threshold)) * _MaskStrength;

                // ---- 2. Noise: 只扰动 Mask, 不生成形状 ----
                // np 已在上面按 _DappleAnchorMode 算好 (0=物体空间顶点, 1=世界, 2=瓦片UV)
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
                // 1=原样; <1 中间值向 0/1 两极分化 (斑块边缘利落, 对比更强);
                // >1 向 0.5 收拢 (整体更平缓更弱)
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

                // ---- 7. 输出彩色斑驳: 颜色只在这一步进场, 绝不回流进形状 ----
                // 绝不输出黑白 Mask; 覆盖层 Alpha 只承载斑驳强弱 (平台 Alpha 不受影响)
                // 顶点色 (瓦片色 / TilemapRenderer 的 Color) 只当纯颜色乘上来:
                //   改它只改颜色, 上面算好的形状/位置一个像素都不会动。
                // 完全不想要瓦片染色时把 i.color 去掉即可 (只留 _DappleColor)。
                half3 tint = _DappleColor.rgb * i.color.rgb;
                return half4(tint, saturate(amount * _DappleColorStrength));
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
