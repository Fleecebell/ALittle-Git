Shader "Unlit/WorldSpacePattern"
{
    // ======================================================================
    // WorldSpacePattern —— 装饰图案通用 Shader v3 (4 个属性轴)
    // --------------------------------------------------------------------------
    // 用途: 在平台/墙壁/背景上叠加一层几何图案, 产生类似 Photoshop 剪贴蒙版的效果
    //       —— 图案只在载体形状内显示 (最终透明度 = 载体底图的 alpha).
    //
    // 4 个属性轴 (每个轴独立, 互不依赖):
    //   轴1 平铺方式: 4 种模式 + 图案大小 + 间距XY + 拉伸XY + 参差XY + 坐标XY + 旋转
    //       间距XY: 图案平铺之间的空隙, X/Y 独立调 (0=无缝贴排, 和原版一致)
    //       拉伸XY: 贴图自身的缩放 (1=原样, 2=放大一倍). 长条纹等非正方形图
    //               用它校正显示比例; 放大超过格子时, 超出部分被格子边裁掉
    //       参差错位XY: 打破整齐的棋盘排列. X=奇数行横向错开, Y=奇数列竖向错开;
    //                   0=对齐, 0.5=砖块式交错.
    //                   !! 一次只用一个分量——两个同时填, 格子角上会缺角错乱
    //                   (几何无解). 想两个方向都有变化 → 配合随机旋转/随机大小.
    //                   错开的是"整张图"——图案不会被切开 (有错位时该轴自动
    //                   按整图居中显示, 而不是无缝相位偏移)
    //       随机旋转: 每个格子里的图案按固定随机角度转动 (填最大度数,
    //                 90=每格在±90度里随机转). 角度由格子编号决定, 播放时不闪;
    //                 转出自己格子的部分由邻格图案自动补位, 不会空缺撕裂;
    //                 适合居中留白的独立图案, 打破满铺的呆板感
    //       随机大小: 每格图案按固定随机比例缩放 (填变化幅度, 0.3=每格在
    //                 0.7~1.3倍之间). 统一缩放不改比例; 和随机旋转/参差可叠加;
    //                 由格子编号决定, 播放时不闪
    //       双向平铺: 整张图无限重复 (原版行为, 满墙/满屏底纹)
    //       竖向条带: 图案按"图案大小"自然平铺, 只显示在一条竖向窗口里
    //                (例: 墙左边来一列波浪; 宽度只裁剪范围, 不拉伸图案形状)
    //       横向条带: 竖向的镜像 (例: 地板上方横一条)
    //       单图案:   全场景只出现一次, 像贴一个独立装饰物 (自转绕图案中心)
    //       + 条带边缘过渡开关 (勾=窗口边缘透明度渐降, 不勾=干净硬边)
    //       + "跟随物体坐标"开关:
    //         不勾 = 锁世界坐标, 平台移动图案不动 / 背景视差移动时图案反向流动
    //         勾   = 跟着物体走, 坐标以物体中心为原点 (X 填 -2 = 物体左边 2 米)
    //   轴2 运动方式: 平移速度 XY + 自转速度
    //   轴3 变换参数: 呼吸 (几何颜色↔呼吸颜色 两色来回渐变) + 色相循环速度
    //   轴4 混合模式: Alpha(贴纸) / Additive(发光) + 叠加强度
    //
    // 关键点(沿用原版):
    //   1. 底色 = 载体贴图(_MainTex), 由 TilemapRenderer / SpriteRenderer 自动绑定
    //   2. 剪贴蒙版 = 最终 alpha 取自底图, 载体形状决定可见范围
    //   3. 动画 = 采样坐标叠加时间偏移, 图案缓慢漂移/自转/呼吸
    //   4. 平铺在 shader 内部自己取整, 不依赖贴图导入的 Wrap Mode;
    //      但想要格子接缝处的像素完美混合, 导入设置仍建议 Wrap Mode=Repeat
    //
    // 三种典型做法 (墙壁和背景通用):
    //   A. 满铺底纹: 平铺模式=双向, "图案大小"调尺寸, "间距XY"拉开距离,
    //      长条纹/非正方形图用"图案拉伸XY"校正显示比例.
    //   B. 局部条带: 平铺模式=竖向, "图案坐标X"=窗口中心(勾跟随物体=相对物体中心,
    //      负数在左边), "条带宽度X"=窗口宽; 想让波浪上下飘 → 平移速度调 Y.
    //      横向条带同理 (用 坐标Y + 宽度Y).
    //      边缘想要柔和渐隐: 勾"条带边缘过渡", "边缘过渡宽度"调世界单位.
    //   C. 单独图案: 平铺模式=单图案, 图案坐标=要放的位置, 图案大小XY=尺寸
    //      (非正方形图调XY比例), 平移速度=推着整个图案漂, 自转绕图案自己转.
    //      注意: Tilemap 整张图共用一个材质, 单图案全图只出现一次;
    //      要放多个不同单图案 → 用独立 Sprite 挂本材质当"覆盖层",
    //      并勾"跟随物体坐标"让图案跟着这个 Sprite 走.
    //
    // 使用流程:
    //   1. 画一张几何图 (PS/Aseprite) 作为 _PatternTex
    //   2. 选平铺模式 → 条带/单图案用"图案坐标 XY"摆位置
    //   3. 图案显示比例不对(长条纹被压成正方形等) → 调"图案拉伸 XY"
    //   4. 想运动: 轴2; 想呼吸变色: 轴3; 想发光: 勾 Additive; 浓度: 调"叠加强度"
    // ======================================================================

    Properties
    {
        [Header(Base Platform)]
        _MainTex ("平台底图 (自动来自 Tile/Sprite)", 2D) = "white" {}

        [Header(Axis 1 Tiling)]
        // 注意: Enum 选项名只能用英文 (ShaderLab 语法限制), 中文对照写在显示标签里
        [Enum(BiAxis,0,Vertical,1,Horizontal,2,Single,3)]
        _TilingMode ("平铺模式 (BiAxis=双向 Vertical=竖向 Horizontal=横向 Single=单图案)", Float) = 0
        _PatternTex ("几何图", 2D) = "white" {}
        _PatternColor ("几何颜色", Color) = (1, 1, 1, 1)
        _PatternScale ("图案大小/密度 (一个图案占的世界尺寸, 不含间距)", Float) = 2.0
        _PatternSpacing ("平铺间距 XY (图案之间的空隙, X/Y 独立, 0=无缝)", Vector) = (0, 0, 0, 0)
        _PatternStretch ("图案拉伸 XY (贴图自身缩放, 1=原样, 长条纹图用它校正比例)", Vector) = (1, 1, 0, 0)
        _PatternStagger ("参差错位 XY (X=奇数行横移, Y=奇数列竖移; 一次只用一个)", Vector) = (0, 0, 0, 0)
        _PatternJitter ("随机旋转 (每格图案随机转动, 填最大度数; 90=每格±90度; 0=关)", Float) = 0
        _PatternScaleJitter ("随机大小 (每格随机缩放, 0.3=0.7~1.3倍; 0=关)", Float) = 0
        _PatternRotation ("整体旋转角度 (度)", Float) = 0
        _PatternOffset ("图案坐标 XY (条带中心 / 单图案中心)", Vector) = (0, 0, 0, 0)
        _PatternSize ("条带宽度X/高度Y (裁剪窗口) 或 单图案大小XY (世界单位)", Vector) = (2, 2, 0, 0)
        [Toggle] _PatternEdgeFade ("条带边缘过渡 (勾=窗口边缘透明度渐降)", Float) = 0
        _PatternEdgeFadeWidth ("边缘过渡宽度 (世界单位, 越大越柔)", Float) = 0.5
        [Toggle] _PatternLocalSpace ("跟随物体坐标 (不勾=锁世界)", Float) = 0

        [Header(Axis 2 Motion)]
        _PatternScrollSpeed ("平移速度 XY (平铺=图案流动, 单图案=推着图案漂)", Vector) = (0.10, 0.05, 0, 0)
        _PatternSpinSpeed ("自转速度 (度/秒, 0=不自转)", Float) = 0

        [Header(Axis 3 Transform)]
        _PatternPulseColor ("呼吸颜色 (呼吸时渐变到的目标颜色)", Color) = (1, 1, 1, 1)
        _PatternPulseAmplitude ("呼吸幅度 (0=固定几何颜色, 1=完全渐到呼吸颜色)", Range(0, 1)) = 0
        _PatternPulseSpeed ("呼吸速度 (每秒来回几圈)", Float) = 1
        _PatternHueShiftSpeed ("色相循环速度 (0=不变, 1=每秒一圈)", Float) = 0

        [Header(Axis 4 Blend Mode)]
        [Toggle] _PatternAdditive ("发光叠加 (勾=Additive 不勾=Alpha)", Float) = 0
        _PatternIntensity ("几何强度/叠加强度 (Alpha=颜色盖上去的浓度, Additive=发光倍数)", Range(0, 2)) = 1.0
        _PatternAlpha ("几何整体透明", Range(0, 1)) = 1.0

        [Header(Stencil Clip)]
        [Enum(Keep,0,Replace,2)]
        _StencilOp ("模板写入 (墙的材质选Replace=把墙形状写进模板; 默认Keep=关)", Float) = 0
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

        // 剪贴模板: 墙材质把 _StencilOp 设为 Replace 后, 每个画出来的像素把自己的
        // 形状写进模板缓冲; 配合 StencilClipSprite (装饰精灵的材质) 实现
        // "装饰只在墙形状内显示". 默认 Keep=完全不碰模板, 背景等别的材质不受影响
        Stencil
        {
            Ref 1
            Comp Always
            Pass [_StencilOp]
        }

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

            // ---------- 模式常量 (对应 Properties 里的 Enum 值) ----------
            #define TILE_BIAXIS   0
            #define TILE_VERTICAL 1
            #define TILE_HORIZONTAL 2
            #define TILE_SINGLE   3

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
                float2 uv       : TEXCOORD0;  // 载体底图 UV
                float2 worldPos : TEXCOORD1;  // 世界坐标 (默认采样基准)
                float4 color    : TEXCOORD2;  // 顶点色透传
                float2 localPos : TEXCOORD3;  // 物体本地坐标 (跟随物体模式用)
            };

            // ---------- 贴图声明 (URP 宏) ----------
            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            TEXTURE2D(_PatternTex); SAMPLER(sampler_PatternTex);
            float4 _PatternTex_TexelSize;   // (1/w, 1/h, w, h) Unity 自动填充, 手动算 mip 用

            // ---------- 参数 ----------
            half4  _PatternColor;
            float  _TilingMode;
            float  _PatternScale;
            float4 _PatternSpacing;
            float4 _PatternStretch;
            float4 _PatternStagger;
            float  _PatternJitter;
            float  _PatternScaleJitter;
            float  _PatternRotation;
            float4 _PatternOffset;
            float4 _PatternSize;
            half   _PatternEdgeFade;
            float  _PatternEdgeFadeWidth;
            half   _PatternLocalSpace;
            float4 _PatternScrollSpeed;
            float  _PatternSpinSpeed;
            half4  _PatternPulseColor;
            half   _PatternPulseAmplitude;
            float  _PatternPulseSpeed;
            float  _PatternHueShiftSpeed;
            half   _PatternIntensity;
            half   _PatternAlpha;

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

            // ---------- 辅助: 区域边缘过渡 ----------
            // d=到窗口中心距离, halfSize=窗口半宽, feather=过渡区宽度(世界单位).
            // 窗口内部=1, 越靠边越小, 过渡区里按 smoothstep S 曲线平滑降到 0
            float EdgeFade(float d, float halfSize, float feather)
            {
                float t = saturate((halfSize - d) / max(feather, 0.0001));
                return t * t * (3.0 - 2.0 * t);
            }

            float2 EdgeFade(float2 d, float2 halfSize, float2 feather)
            {
                float2 t = saturate((halfSize - d) / max(feather, 0.0001));
                return t * t * (float2(3.0, 3.0) - 2.0 * t);
            }

            // ---------- 辅助: 带间距/拉伸/参差/随机旋转/随机大小的平铺采样 ----------
            // uv=格子坐标 (每格=1.0, 一格周期=period), size=格子基准图案大小,
            // gap=每轴空隙, stretch=贴图自身缩放XY (1=原样),
            // stagger=参差错位XY (0.5=砖块式交错),
            // rotJitter=每格随机旋转(最大度数), scaleJitter=每格随机大小(±幅度).
            //   完全无参数的轴 → 无缝连续平铺 (shader 内部 frac, 不依赖 Wrap Mode)
            //   有间距/拉伸/参差/旋转/随机大小 → 贴图按整图居中显示;
            //     错位/旋转/缩放是把"整张图"挪走/转走/放大缩小, 绝不切开
            //   有随机旋转时: 贴图转角会伸出自己的格子, 邻格的贴图也会转进来 →
            //     按"自己格子优先, 邻格按固定顺序兜底"搜 3×3 邻域, 找到能盖住
            //     当前像素的格子来画, 转角处不出现空缺/撕裂
            half4 SampleGapTiled(float2 uv, float2 period, float2 size, float2 gap,
                                 float2 stretch, float2 stagger,
                                 float rotJitter, float scaleJitter)
            {
                // ---- 变换顺序: 参差错位 → 错开后的格子编号 → 每格随机旋转/大小 ----
                // (顺序错了会冲突: 参差把格子拉过原始格线, 若随机量用错位前的
                //  编号算, 同一张可见图会拿到两种随机值, 被撕成两半)

                // 参差错位: X分量=奇数行横向错开, Y分量=奇数列竖向错开 (按用户习惯的分量式)
                // !! 已知限制: 两个分量同时非零时, 格子在角上会互相争抢,
                //    出现缺角/L形错乱 (几何上无解) —— 一次只用一个分量;
                //    想要两个方向都有变化 → 用"随机旋转/随机大小"补足
                float rowOdd = frac(floor(uv.y + 0.5) * 0.5) * 2.0;  // 行号奇数=1
                float colOdd = frac(floor(uv.x + 0.5) * 0.5) * 2.0;  // 列号奇数=1
                float2 uvS = uv + float2(stagger.x * rowOdd, stagger.y * colOdd);

                // 错开之后的格子编号 (可见格子, 所有像素在同一格里拿到同一个编号)
                float2 cellS = floor(uvS + 0.5);

                // 每屏幕像素跨多少格: 用"输入坐标 uv"在无分支的连续流里求导
                // (uvS 带参差跳变、后面还有 if/循环, 在那些地方求导会作废,
                //  让 GPU 自动选 mip 会拿到垃圾结果 → 细格纹错乱成花纹)
                float2 duv = abs(ddx(uv)) + abs(ddy(uv));

                // 基础贴图大小 = 图案大小 × 拉伸 (随机大小在每格里再乘)
                float2 texSize = max(size * max(stretch, float2(0.0001, 0.0001)), float2(0.0001, 0.0001));

                // 该轴是否启用"整图窗口": 间距/拉伸/参差/旋转/随机大小 任一非零
                // (旋转和随机大小都是两轴一起作用)
                float  jitMax  = max(rotJitter, scaleJitter);
                float2 needWin = max(max(gap, abs(stretch - float2(1.0, 1.0))),
                                     max(abs(stagger), float2(jitMax, jitMax)));
                float2 useWin = step(float2(0.001, 0.001), needWin);

                half4 p = 0;

                if (rotJitter <= 0.001)
                {
                    // ================= 无随机旋转: 只有自己格子的窗口能盖住
                    // 当前像素 (窗口半宽≤半格), 走单格逻辑; 每格可带随机大小
                    float rndS    = frac(sin(dot(cellS, float2(269.5, 183.3))) * 23371.853);
                    float cellScl = 1.0 + (rndS - 0.5) * 2.0 * scaleJitter;
                    float2 texSizeC = texSize * max(cellScl, 0.0001);

                    float2 f = uvS - cellS;

                    // 窗口坐标: 贴图中心对格子中心, 按这格的实际大小换算
                    float2 uWin = float2(0.5, 0.5) + f * period / texSizeC;

                    // 无窗口轴: frac 取整后连续平铺 (不依赖 Wrap Mode);
                    // 有窗口轴: 整图显示
                    float2 u = lerp(frac(uvS), uWin, useWin);

                    // 手动选 mip: 每像素跨多少贴图像素 (贴图小了自然糊掉,
                    // 不会出现错乱的干涉花纹)
                    float2 texPx = float2(_PatternTex_TexelSize.z, _PatternTex_TexelSize.w);
                    float2 tpp   = duv * period / texSizeC * texPx;
                    float  lod   = log2(max(max(tpp.x, tpp.y), 0.0001));
                    p = SAMPLE_TEXTURE2D_LOD(_PatternTex, sampler_PatternTex, u, lod);

                    // 这格的窗口半宽/边缘过渡宽 (格子比例)
                    float2 halfF  = min(float2(0.5, 0.5) * texSizeC / max(period, 0.0001), float2(0.5, 0.5));
                    float2 aaFrac = max(halfF * 0.02, float2(0.005, 0.005) / max(period, 0.0001));

                    // 什么时候真的需要窗口裁剪+抗锯齿:
                    //   窗口小于整格 (有间距或缩小) → 裁掉格子里的空隙区
                    //   贴图放大溢出格子 → 溢出边要软裁
                    //   有随机大小 → 缩小留空隙/放大被裁边, 都要软边
                    //   其余 (窗口正好=整格, 无间距不放大) → 不裁! 相邻格子的
                    //   贴图边靠 Repeat 采样自然衔接, 强行裁会在图案上画切缝
                    float scaleNeed = step(0.001, scaleJitter);
                    float2 needAA = max(max(step(halfF, float2(0.499, 0.499)),
                                        step(float2(1.001, 1.001), stretch)),
                                        float2(scaleNeed, scaleNeed));

                    float2 t = saturate((halfF - abs(f)) / max(aaFrac, float2(0.0001, 0.0001)));
                    float2 aa = lerp(float2(1.0, 1.0), t * t * (float2(3.0, 3.0) - 2.0 * t), useWin * needAA);
                    p.a *= aa.x * aa.y;
                }
                else
                {
                    // ================= 有随机旋转: 转角后贴图会伸出自己的格子,
                    // 邻格的贴图也会转进来盖住这里 → 搜 3×3 邻域.
                    // 优先顺序: 自己格子 → 上下左右 → 四个斜角, 找到第一个能
                    // 盖住当前像素的格子 (每格的随机角/随机大小固定, 不闪不撕)
                    int2 offs[9] = { int2(0,0),   int2(-1,0), int2(1,0),
                                     int2(0,-1), int2(0,1),
                                     int2(-1,-1), int2(-1,1), int2(1,-1), int2(1,1) };

                    [unroll]
                    for (int k = 0; k < 9; k++)
                    {
                        float2 c = cellS + offs[k];

                        // 该候选格子的固定随机角 + 随机大小 (两种不同种子, 互不相关)
                        float rndC = frac(sin(dot(c, float2(127.1, 311.7))) * 43758.5453);
                        float aC   = (rndC - 0.5) * 2.0 * rotJitter;
                        float radC = aC * 3.14159265359 / 180.0;
                        float sC   = sin(radC);
                        float cC   = cos(radC);

                        float rndS    = frac(sin(dot(c, float2(269.5, 183.3))) * 23371.853);
                        float cellScl = 1.0 + (rndS - 0.5) * 2.0 * scaleJitter;
                        float2 texSizeC = texSize * max(cellScl, 0.0001);

                        // 候选格子的局部坐标, 绕它的格心转
                        float2 fc  = uvS - c;
                        float2 frc = float2(cC * fc.x - sC * fc.y, sC * fc.x + cC * fc.y);

                        // 这格的窗口半宽/边缘过渡宽
                        float2 halfF  = min(float2(0.5, 0.5) * texSizeC / max(period, 0.0001), float2(0.5, 0.5));
                        float2 aaFrac = max(halfF * 0.02, float2(0.005, 0.005) / max(period, 0.0001));

                        // 窗口测试: 这张转过/缩放过的贴图能盖住当前像素吗?
                        float2 dd = abs(frc);
                        if (dd.x < halfF.x && dd.y < halfF.y)
                        {
                            float2 uWin = float2(0.5, 0.5) + frc * period / texSizeC;

                            // 手动选 mip (旋转不改变导数大小, duv 依然有效)
                            float2 texPx = float2(_PatternTex_TexelSize.z, _PatternTex_TexelSize.w);
                            float2 tpp   = duv * period / texSizeC * texPx;
                            float  lod   = log2(max(max(tpp.x, tpp.y), 0.0001));
                            p = SAMPLE_TEXTURE2D_LOD(_PatternTex, sampler_PatternTex, uWin, lod);

                            // 贴图边缘软裁 (转出/缩出覆盖范围的角必须裁掉)
                            float2 t = saturate((halfF - dd) / max(aaFrac, float2(0.0001, 0.0001)));
                            float2 aa = t * t * (float2(3.0, 3.0) - 2.0 * t);
                            p.a *= aa.x * aa.y;

                            // 找到能盖住的就用它; 重叠区按固定优先级, 不会闪
                            return p;
                        }
                    }
                    // 9 个格子都盖不住 (间距空隙区) → 保持透明
                }

                return p;
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
                o.localPos = v.vertex.xy;   // 物体本地坐标, 跟随物体模式以它为基准
                o.color = v.color;
                return o;
            }

            // ==================================================================
            // 片元着色器
            // ==================================================================
            half4 frag (v2f i) : SV_Target
            {
                // ---- 1. 载体底色 (被剪贴的内容) ----
                half4 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                base *= i.color;

                // ---- 2. 采样基准 P: 默认世界坐标(图案锁世界), 勾"跟随物体"用本地坐标 ----
                float2 P = lerp(i.worldPos, i.localPos, _PatternLocalSpace);

                float scale   = max(_PatternScale, 0.0001);
                float angle   = _PatternRotation + _PatternSpinSpeed * _Time.y; // 整体角度+自转合成
                float2 scroll = _PatternScrollSpeed.xy * _Time.y;

                // 平铺参数: 周期 = 图案大小 + 间距; 贴图显示大小 = 图案大小 × 拉伸
                float2 tileSize = float2(scale, scale);
                float2 gap      = max(_PatternSpacing.xy, float2(0.0, 0.0));
                float2 period   = max(tileSize + gap, float2(0.0001, 0.0001));
                float2 stretch  = max(_PatternStretch.xy, float2(0.0001, 0.0001));
                float2 stagger  = _PatternStagger.xy;
                float  rotJitter = max(_PatternJitter, 0.0);
                float  scaleJitter = max(_PatternScaleJitter, 0.0);

                // ---- 3. 按平铺模式采样几何图 ----
                half4 pattern = 0;
                if (_TilingMode == TILE_BIAXIS)
                {
                    // 双向平铺: 无限重复. 间距=0 且 拉伸=1 时与原版公式完全一致.
                    //   注意顺序: 先旋转再按周期分格 (和条带模式一致) —— 反过来的话,
                    //   间距X≠Y + 旋转角度 会让格子变成歪斜的平行四边形, 图案被拉伸
                    float2 rp = rotateUV(P, angle);
                    float2 uv = rp / period + scroll + _PatternOffset.xy / period;
                    pattern = SampleGapTiled(uv, period, tileSize, gap, stretch, stagger, rotJitter, scaleJitter);
                }
                else if (_TilingMode == TILE_VERTICAL)
                {
                    // 竖向条带 = 裁剪窗口: 图案用双向平铺同一套自然坐标 (按密度平铺,
                    //   不拉伸形状), 条带宽度X 只负责把窗口外的裁掉.
                    //   窗口中心 = 图案坐标X; 平移速度 = 图案在窗口里流动
                    float2 rp = rotateUV(P - _PatternOffset.xy, angle);
                    float2 uv = rp / period + scroll;
                    pattern = SampleGapTiled(uv, period, tileSize, gap, stretch, stagger, rotJitter, scaleJitter);
                    // 边缘: 勾"条带边缘过渡"=按设定的过渡宽度渐隐;
                    //       不勾=只留 2% 窗口宽的抗锯齿, 干净利落
                    float feather = lerp(max(_PatternSize.x * 0.02, 0.005),
                                         max(_PatternEdgeFadeWidth, 0.005),
                                         _PatternEdgeFade);
                    pattern.a *= EdgeFade(abs(rp.x), _PatternSize.x * 0.5, feather);
                }
                else if (_TilingMode == TILE_HORIZONTAL)
                {
                    // 横向条带: 竖向的镜像 (沿 X 自然平铺, Y 方向只显示窗口内)
                    float2 rp = rotateUV(P - _PatternOffset.xy, angle);
                    float2 uv = rp / period + scroll;
                    pattern = SampleGapTiled(uv, period, tileSize, gap, stretch, stagger, rotJitter, scaleJitter);
                    float feather = lerp(max(_PatternSize.y * 0.02, 0.005),
                                         max(_PatternEdgeFadeWidth, 0.005),
                                         _PatternEdgeFade);
                    pattern.a *= EdgeFade(abs(rp.y), _PatternSize.y * 0.5, feather);
                }
                else
                {
                    // 单图案: 只出现一次. 坐标XY=中心, 图案大小XY=矩形尺寸
                    // (非正方形图调 XY 比例即校正);
                    // 平移速度=推着整个图案漂, 自转=绕图案中心转 (而不是绕世界原点)
                    float2 rp = rotateUV(P - _PatternOffset.xy - scroll, angle);
                    float2 uv = rp / max(_PatternSize.xy, 0.0001) + 0.5;
                    pattern = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, uv);
                    // 出矩形范围渐隐 (留 2% 抗锯齿)
                    float2 fade = EdgeFade(abs(rp), _PatternSize.xy * 0.5, max(_PatternSize.xy * 0.02, 0.005));
                    pattern.a *= fade.x * fade.y;
                }

                // ---- 4. 几何颜色 (轴3: 呼吸=两色来回渐变 + 色相循环) ----
                // 呼吸: 0→1 来回摆动, 幅度=渐变深度; RGB 和透明度一起渐
                half pulseW = 0;
                if (_PatternPulseAmplitude > 0)
                {
                    pulseW = saturate(_PatternPulseAmplitude * (0.5 + 0.5 * cos(_Time.y * _PatternPulseSpeed * 6.283)));
                }
                half4 pColor = lerp(_PatternColor, _PatternPulseColor, pulseW);

                half3 patternColor = pColor.rgb;
                if (_PatternHueShiftSpeed > 0)
                {
                    half hue = frac(_Time.y * _PatternHueShiftSpeed);
                    patternColor = hsv2rgb(half3(hue, 1, 1)) * patternColor;
                }

                half3 patternRGB = pattern.rgb * patternColor;

                // 图案自身 alpha = 贴图alpha × 呼吸后的颜色alpha × 整体透明
                half patternA = pattern.a * pColor.a * _PatternAlpha;

                // ---- 5. 混合模式 (轴4: 叠加强度 = Alpha的浓度 / Additive的亮度倍数) ----
                half3 finalRGB;

                #if defined(_PATTERNADDITIVE_ON)
                    // Additive 发光叠加: 底色 + 几何色 × 强度 (强度>1 = 更亮)
                    finalRGB = base.rgb + patternRGB * patternA * _PatternIntensity;
                #else
                    // Alpha 贴纸叠加: 强度=贴上去的浓度 (1=完全盖住底图)
                    finalRGB = lerp(base.rgb, patternRGB, saturate(patternA * _PatternIntensity));
                #endif

                // 剪贴蒙版: 用载体 alpha 决定整体可见范围 (载体形状内才显示)
                half finalA = base.a;

                // 全透明像素直接丢弃 —— 保证写进模板的形状 = 肉眼可见的墙形状
                // (瓦片之间的透明缝隙、贴图留白不会写模板)
                clip(finalA - 0.01);

                return half4(finalRGB, finalA);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
