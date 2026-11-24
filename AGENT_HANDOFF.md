# A-Little 项目交接文档（给接手的 AI 开发 Agent）

> 版本：2026-11-24　依据：仓库代码、场景、ProjectSettings 的实际检查（不是凭印象）
> 适用对象：第一次接触本项目的 AI 编码 Agent；人类开发者也可当速查手册
> 维护方式：改了项目结构、关卡顺序或核心数值后，请顺手更新第 4、13 节。
> **代码优先**：本文档可能过期，任何冲突以当前代码和场景为准。

> **怎么把这份文档交给下一个 Agent**：把仓库路径和本文档路径发给它，并附一句：
> "先读 `AGENT_HANDOFF.md` 的第 0、1、4、5、8、11、13 节；然后按第 12 节的工作协议开始。项目在 `E:\unity\UnityProgram\ALittle-Git`。现在没有编辑器桥：你直接读写文件，需要看运行时表现时让我进 Unity Play 并贴 Console。"

---

## 0. 30 秒速览

- 这是一个 **Unity 2D 解谜平台跳跃**游戏《A-Little》。核心不是"操作一个角色"，而是：
  **你控制 P1（本体），P2（影子）会延迟固定时间重放你的输入**。用"现在的自己"和"1 秒前的自己"配合踩按钮、拿钥匙、过机关。
- 引擎：**Unity 2022.3.55f1c1（Unity 中国版）** + URP 2D + 旧 Input Manager + DOTween + TextMeshPro。
- 入口：`Assets/Scenes/Start.unity`（菜单 / 选关 / 圆圈转场）；关卡按 Build Settings 顺序用 `buildIndex + 1` 串联。
- 已有一套**行为遥测**：Unity 客户端 → Cloudflare Worker + D1 → 网页热力图看板（记录死亡点、跳跃点、停留、通关路线）。
- **工程已安装 Unity True CLI 0.8.0**（Skill + Unity Pipeline，2026-11-24 装好，见第 14 节）。你可以通过它操作 Unity：查场景层级、改组件字段、进 Play、读 Console、截图。旧的 `EditorMcpBridge` 已删除，不要再连 `127.0.0.1:35353`。
- 现状：可玩，20 多个关卡场景，正在准备 / 刚参加展会（reflog 里有"11.20 展会前"）。但 **Build Settings 和选关面板处于合并后的混乱状态**（见 13.1），动手前必须先跟用户确认"哪一套关卡是主版本"。

**三条铁律**

1. **先读后改**：改脚本前把相关脚本读完；改场景前先用 Unity True CLI 查层级/字段；不确定就问，不要脑补。
2. **7 个文件是 GBK 编码**（见 11.1），不能用 UTF-8 工具整篇重写，否则整篇中文变乱码。
3. **能验证就不要猜**：改完必须让 Unity 编译通过，并用 CLI 读回状态；主观手感/画面再让用户确认。

---

## 1. 项目定位与现状

| 项 | 值 |
|---|---|
| 产品名 | `A-Little`（ProjectSettings 里的 productName） |
| 仓库 | `https://github.com/Fleecebell/ALittle-Git.git` |
| 当前分支 | `Fleecebell`（另有 `main`、`Nanami`、`notlie1`） |
| 类型 | 2D 解谜 / 平台跳跃，单机，PC（Windows） |
| 美术 | 2D Sprite + Tilemap + URP 2D Light + 后处理；有沙漠（Gobi）和雨林（Rainforest）两套主题 |
| 代码量 | `Assets/Scripts` 67 个 `.cs`，约 10,700 行；`Assets/Editor` 4 个，约 2,250 行 |
| 资源量 | 52 个 `.unity` 场景（含未启用）、57 个 prefab、20 个 shader |
| 完成度 | 可完整游玩；有关卡选择、计时、音量设置、死亡重生、转场、遥测 |
| 团队痕迹 | 分支名即成员名；`Assets/Scenes/Date`、`LHY`、`CY`、`notalie` 是不同人/不同阶段的关卡 |

**它不是"引擎框架型"工程**：没有 GameManager、没有依赖注入、没有 ECS。每个关卡场景是一份自包含的装配（PlayerControl + SetCanvas + LevelLoader + 机关 prefab + Tilemap 地形），跨场景状态靠 `static` 字段和 `SceneManager.sceneLoaded` 传递。理解这一点，后面所有设计都顺了。

---

## 2. 运行环境与启动

| 项 | 值 |
|---|---|
| Unity | `2022.3.55f1c1`（中国版，注意 `c1` 后缀；用国际版打开可能提示版本不一致） |
| 渲染 | URP `14.0.11`，2D Renderer，`m_CustomRenderPipeline` 已指定 |
| 输入 | **旧 Input Manager**（`activeInputHandler: 0`），代码里全是 `Input.GetKey/GetAxisRaw` |
| 序列化 | Force Text（场景/prefab 是 YAML，可 diff） |
| 分辨率 | 1920×1080，全屏窗口（`fullscreenMode: 1`） |
| 公司名 | `DefaultCompany` → 存档/日志在 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\A-Little\` |
| 主要包 | URP、TextMeshPro、Timeline、2D Feature、Visual Scripting、Test Framework、Collab Proxy |
| 第三方 | DOTween（`Assets/Plugins/Demigiant`），项目自带 2D 水面（`Assets/2DWater`） |

**启动**

1. 用 Unity Hub 添加本目录（`E:\unity\UnityProgram\ALittle-Git`），用 2022.3.55f1c1 打开。
2. 打开 `Assets/Scenes/Start.unity`，点 Play。
3. 菜单里点"开始"会弹选关（当前选关数据是过期的，见 13.2）；也可以直接在 Project 窗口双击任意关卡场景 Play。

**打包**：`File → Build Settings`，Target 为 Windows，场景列表见第 4 节；已有构建产物在 `Builds/alittle_test.exe`（`Builds/` 被 gitignore，可能不是最新）。

**环境注意**

- 本会话里 `git` 不在 PATH（`git` 命令不可用），但 `.git` 完整存在；需要提交时用 IDE 的 Git 或先确认 git 安装位置。
- 工程带 `.gitattributes`/LFS 配置；美术资源较大，不要随意 `git add -A` 大文件。
- `Library/`、`Temp/`、`Logs/` 是 Unity 生成物，不要读也不要提交。

---

## 3. 目录地图

```text
ALittle-Git/
├─ Assets/
│  ├─ Scenes/                 关卡与菜单场景
│  │  ├─ Start.unity          入口：菜单 / 选关 / 转场 / 音乐
│  │  ├─ S0.5 S1 S2 S2.5 S3 S4    "S 系列"关卡
│  │  ├─ 6th 8th ...           "第 N 关"系列（多数未启用）
│  │  └─ Date/                91xx 关卡（当前主线候选，含 9101~9115、9141~9145）
│  ├─ Scripts/                ★ 所有游戏逻辑（见附录 A）
│  │  ├─ Player/              P1/P2 移动、死亡重生、夹死、地面跟随
│  │  ├─ Gameplay/            机关：按钮、平台、钥匙锁、水、风、加速带
│  │  ├─ Flow/                关卡加载、通关门、开始界面
│  │  ├─ UI/                  HUD、计时器、选关、按钮手感
│  │  ├─ Art/                 雨、沙尘暴、风滚草、植物摆动、粒子、视差
│  │  ├─ Audio/               音乐、雨声、风声
│  │  ├─ Telemetry/           埋点客户端
│  │  └─ Config/              PlayerFeelSettings 手感资产
│  ├─ Prefabs/                ★ 关卡装配用的功能 prefab
│  │  ├─ PlayerControl.prefab 玩家（P1+P2+HUD+粒子），739 KB
│  │  ├─ SetCanvas.prefab     设置面板/计时器/音量
│  │  ├─ LevelLoader.prefab   圆圈转场
│  │  ├─ KeyAndNextLevel/     钥匙+锁+门组合
│  │  ├─ ButtonOnce/Hold/Multi、AutoPlatform、Grid_Ladder、Grid_Lava、Grid_SpeedBand …
│  ├─ Art/                    美术：tilemap、角色、装饰、材质、shader、UI
│  ├─ Editor/                 编辑器工具（批量补齐 / 选关生成 / 遥测菜单）
│  ├─ Plugins/Demigiant/      DOTween
│  ├─ 2DWater/                自带水面（当前主线未用）
│  ├─ Resources/              DOTweenSettings
│  └─ TextMesh Pro/
├─ cloudflare_backend/        遥测服务端（Worker + D1 + 看板），有独立 README
├─ ProjectSettings/           Unity 工程设置（版本、Tags、物理、Build 列表）
├─ Builds/                    Windows 构建产物（gitignore）
└─ steam_*.png / uninstall_valorant.* / move_apps.*   与游戏无关的临时文件，别引用、别打包
```

> 根目录那些 `steam_*.png`、`uninstall_valorant.ps1`、`move_apps.ps1` 是机器上的历史遗留，不属于游戏内容。

---

## 4. 场景与关卡流程

### 4.1 一次完整的关卡流转

```text
Start.unity
  └─ 点"开始" → playToChoose（弹簧面板滑入）→ 选关按钮 LevelSelectButton.OnClick
       → LevelSelectPanel.LoadLevel(index) → SceneManager.LoadScene(index)
关卡场景
  ├─ LevelLoader.Start()：如果 isTransitioning，播 CircleWipe_02 淡出
  ├─ UIManager：等玩家第一次操作才开始计时
  ├─ 玩家碰到 NextLevel（门）
  │    ├─ 无锁 → 直接通关
  │    ├─ 有锁 + Key.hasKey → 播锁动画 → 通关
  │    └─ 有锁 + 没钥匙 → 什么都发生不了
  ├─ MarkCleared()：冻结计时 → Telemetry.OnLevelClear(分数)
  └─ LevelLoader.LoadNextLevel()：CircleWipe_01 盖住屏幕 → LoadScene(buildIndex+1)
```

- `NextLevel` 用 `SceneManager.GetActiveScene().buildIndex + 1`，**没有关卡配置文件**。所以"关卡顺序 = Build Settings 顺序"是硬约束。
- 最后一关之后没有"通关画面"，只会在 Console 打 `[NextLevel] 没有更多的关卡了！`。
- 转场动画状态名写死为 `CircleWipe_01` / `CircleWipe_02`（Animator），`fadeInDuration=0.88`、`fadeOutDuration=0.889`、`transitionSpeed=0.6`。改动画机要同步改脚本里的名字和时序。

### 4.2 Build Settings 实际顺序（2026-11-24）

只列启用项；`#` 是运行时 buildIndex（禁用的场景不占索引）。

| # | 场景 | 场景里实际用到的东西（prefab 级） | 备注 |
|---|---|---|---|
| 0 | `Scenes/Start.unity` | 菜单、选关（两份 Canvas）、LevelLoader、MusicManager | 入口 |
| 1 | `Date/9101.unity` | PlayerControl、KeyAndNextLevel、ButtonOnce、WindVFX、Decorations | 主线第 1 关（三份副本之一） |
| 2 | `Date/9101 1.unity` | 同上 | 与 9101 几乎同尺寸 |
| 3 | `Date/9101 2.unity` | 同上 | 同上 |
| 4 | `S0.5.unity` | ButtonOnce×2、沙尘暴、风滚草、Wind、Light2D | 沙漠主题 |
| 5 | `Date/9102.unity` | ButtonMulti、WindVFX、后处理 | |
| 6 | `Date/9105.unity` | ButtonMulti、枯草、WindVFX | |
| 7 | `S1.unity` | ButtonHold、ButtonOnce×2、沙尘暴、风滚草、Wind | 沙漠主题 |
| 8 | `Date/9111.unity` | ButtonMulti×2、Wind | |
| 9 | `6th.unity` | ButtonOnce×2、Grid_Lava | 熔岩 |
| 10 | `Date/9103.unity` | ButtonMulti×2、Grid_Ladder×2、落叶 | |
| 11 | `Date/9113.unity` | ButtonMulti×3、Wind | |
| 12 | `S2.unity` | AutoPlatform×8、Key/Lock/NextLevel、雨、植物、ButtonHold | 雨林主题 |
| 13 | `S2.5.unity` | ButtonHold×3、ButtonOnce、雨、植物 | |
| 14 | `Date/9115.unity` | ButtonMulti×5、Grid_Ladder×2 | |
| 15 | `8th.unity` | AutoPlatform×8、Grid_Lava、ButtonOnce | |
| 16 | `Date/9104.unity` | ButtonMulti×2、Grid_Ladder | |
| 17 | `S3.unity` | Grid_Lava、钥匙/锁、雨、植物、ButtonHold | |
| 18 | `S4.unity` | ButtonMulti×2、ButtonOnce、钥匙/锁 | |
| 19 | `Date/9112.unity` | ButtonMulti×3 | |
| 20 | `Date/9114.unity` | ButtonMulti×3 | |
| 21 | `CUSoon.unity` | "Coming Soon" 页 | 收尾 |

**被禁用**（不占索引）：`S0`、`4th`、`Date/9141`~`9145`。
**完全不在 Build 列表里**的旧场景：`2nd`、`5th`、`7th`、`Start1`、`YYY`、`Fleecebell`、`notalie/notalielevel`、`CY/*`、`LHY/*`、`Date/Base` 等。

### 4.3 这里有一个必须让用户确认的问题

从文件形态看，`Date/9101`、`9101 1`、`9101 2` 大小几乎一样（约 150 KB），是**同一关的三个副本**却都被启用；同时"`S*` 系列"和"`91xx` 系列"交替排在同一份 Build 列表里。这几乎不可能是设计意图，更像是几次分支合并后把两套关卡都留下了。

**在用户明确"哪一套是主版本"之前：**

- 不要删除任何场景文件；
- 不要擅自改 Build Settings；
- 不要用当前顺序去推断"第几关应该是什么"；
- 做关卡相关任务时，先问："这次改的是 91xx 系列还是 S 系列？"

### 4.4 关卡是怎么搭出来的

一个典型关卡场景 = 下面几层，全部是"拖 prefab + 画 Tilemap"：

1. **地形**：`Grid` + 若干空名字的 Tilemap 子物体（`Tilemap`/`TilemapRenderer`/`TilemapCollider2D`），Tag 设 `Ground`。
2. **玩家**：`PlayerControl.prefab`（内含 P1、P2、各自的 ActionUI、粒子），放在出生点。
3. **终点**：`KeyAndNextLevel.prefab`（门 + 可选锁 + 钥匙 + 灵光球），或单独的 `NextLevel.prefab`。
4. **机关**：`ButtonOnce` / `ButtonHold` / `ButtonMulti` / `AutoPlatform` / `Grid_Ladder` / `Grid_Lava` / `Wind`。
5. **每个关卡必备的两个 prefab**：`LevelLoader`、`SetCanvas`（缺了就没有转场 / 没有设置面板和计时器）。
6. **装饰**：`Decorations`、`Background`、`PostProcessing`、`RainParticles` / `SandstormParticles`、植物 prefab。
7. **Camera**、`EventSystem`、`music`（AudioSource）。

`Assets/Editor/LevelLoaderBatchInstaller.cs` 就是用来批量补齐第 5 条的（菜单 `Tools/关卡工具/...`），说明"每关都要有这两个 prefab"是团队约定。

---

## 5. 核心玩法：P1 与延迟影子 P2

### 5.1 概念

- **P1**（`Player`，Tag=`Player`）：玩家实时控制。
- **P2**（`Player2`，Tag=`Player2`）：**影子**。它不接收玩家输入，而是把 P1 的输入记录延迟 `delayTime` 后逐条重放。
- 当前 prefab 里 **`delayTime = 1`（秒）**；脚本默认值是 `0.5`，以 prefab 序列化值为准。改这个值 = 改整个游戏的核心难度，**必须先问用户**。
- P2 是实体：有碰撞、能被踩、能踩按钮、能拿钥匙、会挡路、会死。两个角色**共用一套死亡/重生流程**（一个死，两个一起回出生点）。
- 两个角色头顶各有一块 `ActionUI`：**P1 头顶显示 P2 接下来要重放的动作队列**（最多 6 个，← → ↑ 《 》 ↓），**P2 头顶显示自己当前正在做的动作**。这是玩家预判"1 秒后的自己"的主要信息来源。

### 5.2 数据流

```text
MoveFirst.Update()
  └─ 读输入（水平 / 跳跃 / 冲刺 / S / E）
       ├─ 立刻作用于 P1（写 rb.velocity）
       └─ MoveLate.Update() 同帧把这一帧输入打包成 MovementRecord 入队
            （h, jump, jumpHeld, dash, dashDir, climbDown, sizeToggle, time）

MoveLate.MoveDelay() 每帧：
  while 队首记录 age >= delayTime:
      取出 → 设 P2 的移动/跳跃/爬梯/水/风/大小/下穿 → 写 rb.velocity
```

**注意**：这个 `while` 在一帧内可能连续消费多条记录（掉帧后追进度）。所以 P2 的"重放"不是严格一帧对一帧，而是"按时间戳追平"。

### 5.3 P1（`MoveFirst.cs`）行为清单

| 行为 | 实现 | 关键点 |
|---|---|---|
| 水平移动 | `Input.GetAxisRaw("Horizontal")` → `rb.velocity.x` | 默认无加速度（瞬时起停）；`WallAhead` 检测到墙时水平速度清 0，防"蹭墙跳变矮" |
| 跳跃 | 空格 / W；缓冲 0.15s + 冷却 0.25s | 起跳速度由 `jumpHeight + gravityScale` 反推，不直接填速度 |
| 着地 | `OnCollisionStay2D` 法线角 < 45°；`FixedUpdate` 每帧先置 false | 单向平台额外要求"脚底接近平台顶面" |
| 冲刺 | Shift + 左右方向；20 单位/秒 × 0.1s，冷却 1s | **当前被资产开关关闭**（`enableDash=0`） |
| 变大/变小 | E 切换到 1× 或 2×，`MoveTowards` 过渡 | **当前被资产开关关闭**（`enableSizeToggle=0`） |
| 爬梯 | 触发器 Tag=`Ladder`；按住空格/W 上，S 下 | 离梯 0.3s 宽限期，防爬过头直接掉 |
| 下穿单向平台 | 站在 `PlatformEffector2D` 上按 S | 用 `IgnoreCollision` 0.3s，不依赖 Layer |
| 水里 | `WaterPool` 接管重力；按住跳=上浮 | 见 6.4 |
| 风 | `WindArea` 每帧写 `externalWindVx/Vy` | 玩家脚本把风叠加进速度 |
| 死亡/重置 | R，或被 `Die`/`CrushCheck` 置 `MoveLate.isR` | R 会额外 `UIManager.ResetTimer()` |

几个**反直觉但重要**的实现决定：

- 移动写在 `Update` 里直接改 `rb.velocity`，`Rigidbody2D.interpolation = None`、`sleepMode = NeverSleep`。不是常规的 `FixedUpdate + AddForce`，别"顺手重构"成物理力，会破坏手感。
- `isGrounded` 在 `FixedUpdate` 开头强制 false，再由碰撞回调置 true；不要在 `OnCollisionExit2D` 里置 false（两个角色贴在一起时会误判）。
- 跳跃是"缓冲 + 长按"双通道：按下记缓冲，按住则落地即跳（`jumpHeld`）。

### 5.4 P2（`MoveLate.cs`）的差异与坑

P2 的逻辑大部分是 P1 的"延迟镜像"，但有几处**故意不同**，改的时候要理解原因：

| 点 | P1 | P2 | 原因 |
|---|---|---|---|
| 爬梯触发 | `Input.GetKey(Space/W)`（实时） | 记录里的 `jumpHeld`（按住） | 用 `jump`（按下瞬间）会因 P2 位置滞后而错过梯子触发器 |
| 冲刺方向 | 实时 `GetAxisRaw` | 记录里的 `dashDir`（按下 Shift 时 A/D 的方向） | 必须锁定当时的意图 |
| 风（静止时） | 每帧赋值 | 没有到期记录时单独赋值 `vx = windVx`（不是叠加） | 叠加会每帧累加，把 P2 吹飞（代码里有惨痛注释） |
| 单向平台下穿 | 按键瞬间 | `climbDown` 的上升沿 | 重放的是"按住 S"的状态 |
| 死亡期间 | `Update` 直接 return | 停止录制但仍处理重置 | 避免死亡动画里继续入队 |

其他要点：

- 队列上限 1200 条（约 20 秒 @60fps）；`CheckReset` 会清空。
- `preventWhenSteppedOn`（P1 踩住 P2 时 P2 不动）字段存在但当前为 `false`，是备用机制。
- 两个角色的 `moveSpeed` 是**各自类的 static**（`MoveFirst.moveSpeed` / `MoveLate.moveSpeed`），`SpeedBand` 会直接改它们；死亡**不会**重置这个值（只有换场景会）——如果以后用加速带，要注意这个状态残留。
- `DoReset()` 只重置位置、队列、冲刺/爬梯/下穿状态；不重置 `moveSpeed`、不重置计时器（计时器由 R 分支单独处理）。

### 5.5 手感配置：`PlayerFeelSettings.asset`

**唯一调参入口**：`Assets/Scripts/Config/PlayerFeelSettings.asset`，P1 和 P2 的 prefab 都引用它。改这里会影响所有关卡里的两个角色。

当前实际值：

| 字段 | 值 | 含义 |
|---|---|---|
| `moveSpeed` | 8 | 水平速度（单位/秒），瞬时起停 |
| `useAcceleration` | false | 无加速度 |
| `jumpHeight` | 2 | 跳跃顶点高度（世界单位） |
| `gravityScale` | 6 | 重力倍率（×9.81）→ 起跳初速度约 **15.34** |
| `jumpBufferDuration` | 0.15 | 落地前按跳的缓冲 |
| `jumpLockDuration` | 0.25 | 起跳后禁止再跳的时间 |
| `dashForce / dashDuration / dashCooldown` | 20 / 0.1 / 1 | 冲刺（当前关闭） |
| `climbSpeed` | 5 | 爬梯 |
| `groundAngleThreshold` | 45 | 着地法线角 |
| `oneWayPlatformSurfaceTolerance` | 0.1 | 单向平台脚底容差 |
| `dropThroughDuration` | 0.3 | 下穿持续 |
| `bigScale / growShrinkSpeed` | 2 / 5 | 变大（当前关闭） |
| `enableSizeToggle` | **false** | E 变大/变小：**当前全局禁用** |
| `enableDash` | **false** | Shift 冲刺：**当前全局禁用** |

> 也就是说：**当前版本里 E 和 Shift 是无效的**。代码路径完整保留，关卡里也可能有需要它们的机关。不要因为"按 E 没反应"就以为是 bug；要开就和用户确认后改资产（并测试所有关卡）。

### 5.6 调手感的标准流程

1. 改 `PlayerFeelSettings.asset`（不是改 prefab 上的旧字段；旧字段只在资产为空时兜底）。
2. 进 `S1` 或 `9101`（有梯子/按钮/单向平台的关）实测：起跳高度、贴墙跳、落地连跳、平台边缘。
3. 重点看：跳跃是否够到目标平台、P2 延迟 1 秒后是否还能踩到按钮、贴墙是否"变矮"。
4. 改完同步检查 `jumpHeight` 与关卡尺度（Tilemap 一格 = 1 世界单位）。

---

## 6. 场景机关与交互物

所有机关的共同约定：

- 玩家判定用 **Tag**：`Player` / `Player2`（不是 Layer）。**门、按钮、钥匙都同时接受两个角色**。
- 地面判定用 **Tag=`Ground`**；梯子用 **Tag=`Ladder`**；风滚草用 **Tag=`Tumbleweed`**。
- 机关状态基本都在 `Start()` 记录初始值，死亡时由 `DeathRespawnVFX` 统一调 `ResetAll*()` 复原。
- 移动平台都通过 `Button_once.PlatformDeltas` 登记"本帧位移"，供 `GroundCarry` 或贴图锚定使用。

### 6.1 按钮 / 踏板（三种）

| 脚本 | 行为 | 触发方式 | 死亡复原 |
|---|---|---|---|
| `Button_once` | 把 `wall0` 从原位平滑移到 `wall1_pos` | `oneTime=true` 踩一次保持；`false` 踩住移动、离开返回 | `ResetAllButtons()` |
| `Button_Hold` | 同上，但**必须持续踩住**，离开就回弹 | `OnTriggerStay2D` / `OnTriggerExit2D` | 同上 |
| `Button_Multi` | 自身就是移动平台，由多个踏板控制 | 全部踩下（默认）或任意一个（`triggerOnSinglePedal`） | 同上 |

共同的坑：

- `Button_once` / `Button_Hold` 在 `Start()` 里 `transform.Find("big")`，**按钮物体下必须有一个叫 `big` 的子物体**，否则空引用。
- `speed` 不是"米/秒"，是**进度/秒**（`moveProgress += speed * dt`）。`speed=2` ≈ 0.5 秒走完全程，与两端距离无关。
- `Button_Multi` 的踏板会被动态挂 `PedalTrigger`；同一个踏板可被多个平台共享，别手动删这个组件。
- 平台移动用 `SmoothStep(0,1,progress)`，所以起步/停止是慢-快-慢；不要改成线性，会失去"机器感"。

### 6.2 移动平台与"贴图不滑动"

- `AutoMoving`：在 `pointA`/`pointB` 间匀速往返，`FixedUpdate` 里 `MoveTowards`；`useParentSpace` 让轨迹跟随父物体。
- 站在平台上的角色靠 `GroundCarry`（P1/P2 `Start()` 自动挂载）跟随：每帧脚底 `OverlapBox` 找 `Ground`，取最大位移叠加到 `transform.position`（不是速度）。`maxCarrySpeed` 默认 30，防止传送把角色甩飞。
- **贴图锚定**：移动平台如果用了 `PlatformDapple.shader` / `ContolPlatform.shader`，必须由移动脚本每帧 `PlatformAnchorFeeder.Feed()`；否则花纹会随平台"扫过去"。`DappleAnchorFollower` 是给 Tilemap 斑驳层用的等价组件（`_DappleAnchorPos`）。
- 平台自身的 `Rigidbody2D` 通常是 **Static/Kinematic + 冻结位置**，靠 Transform 移动；不要给平台加动力学，会把角色顶飞。

### 6.3 单向平台与下穿

- 现成 prefab：`Platform.prefab`（带 `PlatformEffector2D`）。
- 玩家按 S 时，遍历 `OnCollisionStay2D` 收集的接触体，对带 `PlatformEffector2D` 的 `IgnoreCollision(..., true)`，0.3 秒后恢复。
- 着地判定额外要求"脚底 ≥ 平台顶面 - 0.1"，防止从下方顶到平台中部就借力起跳。
- `TilemapOneWayPlatform.cs` 可以"把 Tilemap 每个瓦片变成独立单向平台"，但它要求 Layer `OneWayPlatform`——**这个 Layer 当前不存在**（Tags & Layers 里没有），所以只在 `notalielevel` 里试过，主线未使用。**别直接把它挂到主线关卡上**，先补 Layer。

### 6.4 水池

- 组件：`WaterPool`（`BoxCollider2D` + `Is Trigger`）+ 可选水体 Sprite。
- 角色实现 `IWaterResponder`（P1/P2 都有），进池时 `SetWaterPool(this)`。
- 物理：进池 `gravityScale=0`；按 `scale > 1.5` 判断"大/小"；用"目标速度 + 阻尼"平滑趋近上浮/下沉，不是加速度叠加。
- 按跳 = `SwimUp`，接近水面时速度衰减，不会冲出去。
- 水位变化功能被注释掉了（作者注明"有问题，暂时关闭"）。
- **当前主线的 22 个启用场景里没有水池**，只有 `notalie/notalielevel` 用到。做水相关任务时要先确认是否要把它重新引入。

### 6.5 风、沙尘暴、风滚草

- `WindArea`：挂在空物体上，物体位置就是"风幕"分界线；`direction = Left/Right/Up/Down`；`lineHeight` 是线长。
- 判定：`OverlapBoxAll` 抓附近刚体 → 判断在分界线哪一侧、是否在风幕范围内 → 从分界线向目标发多条射线，被 Tag=`Ground` 的墙全部挡住才不吹。
- 风力**不直接改玩家速度**，而是写 `MoveFirst.externalWindVx/Vy`（或 P2 的同名字段），由玩家脚本叠加，避免被每帧覆盖。
- 沙尘暴强度来自 `SandstormController.Instance.CurrentIntensity`：没沙尘暴时玩家不受影响，风滚草只轻微滚动（`idleScrollCoeff`）。
- 风滚草：`Tumbleweed` + `TumbleweedSpawner`，设计得很轻（工具提示写 Mass=0.1），用 `AddForce`。
- 场景里调 `WindArea` 时打开 `showDebugGizmos` 能在 Scene 视图看到分界线和遮挡射线（黄=被挡，绿=能吹到）。

### 6.6 加速带

- `SpeedBand`：进入触发器后按"走过的距离"改变 `MoveFirst/MoveLate.moveSpeed`，每 `unitLength` 米 ±`speedPerUnit`。
- 当前只用于 `Grid_SpeedBand.prefab`，**主线未使用**。
- 注意：它改的是 static，死亡不重置；且玩家脚本每帧 `Clamp(moveSpeed, minMoveSpeed, maxMoveSpeed)`，上下限在玩家 prefab 上（0~50）。

### 6.7 钥匙 / 灵光球 / 锁 / 门

一条完整链路：

```text
Key（触发器，P1/P2 都能碰）
  ├─ Key.hasKey = true（全局静态）
  ├─ 激活场景里预先摆好的 KeyOrb（不是克隆）
  │    └─ 飞向 P1（弧线 DOTween）→ 变成钥匙跟随 P1
  └─ 自身：放大 → 缩小到 0 → SetActive(false)

NextLevel（门）
  ├─ 无 LockAnimator → isUnlocked = true，碰到即通关
  ├─ 有锁 + hasKey → 判定通关（立刻冻结计时）+ 播锁动画 → 切关
  └─ 有锁 + 没钥匙 → 无反应（Console 有日志）

LockAnimator（锁 + 黑暗覆盖层）
  └─ 跳起 → 下掉 → 淡出 → 禁用；同时黑暗罩淡出
```

关键约束：

- `Key.hasKey` 在**每次场景加载**时清零（`Key.OnAnySceneLoaded`），不在 `NextLevel.Start()` 里清（历史 bug：后激活的门会清掉已拿的钥匙）。
- 钥匙/锁/门都在 `Awake` 里注册进 static 列表，死亡时统一 `ResetAllKeys` / `ResetAllLocks` / `ResetAllDoors`。
- `KeyOrb` 要用**场景里预先摆好的那一个**（初始禁用）并拖给 `Key.keyOrb`，这样在 Inspector 里调它的大小/参数会直接生效；不要运行时再克隆一份（历史上克隆版出现过"球飞错地方/不跟随"）。
- 门既接受 P1 也接受 P2；`OnTriggerEnter` + `Update` 双保险（先站门里再拿钥匙也能触发）。
- `NextLevel.showDebug` 字段默认值是 `true`（prefab 实例可覆盖），正式包可能刷日志；发布前检查。

### 6.8 危险物与夹死

- `Die`：触发器（熔岩等）。碰到的角色 → `Telemetry.NoteDeath("hazard:<物体名>", actor, 位置)` → `MoveLate.isR = true`。`playerDying` 静态标志供按钮脚本提前复原。
- `CrushCheck`：P1/P2 都挂。四个方向各 3 条射线，测"两堵 Ground 墙之间的距离"，任一对小于 `crushThreshold=0.6`（玩家宽 0.97）才判死。
- 两者最终都走同一条死亡流程（见第 8 节）。
- `Fruit_Delay`、`Combatant`、`Weapon` 是旧原型残留，当前没有任何场景使用。

### 6.9 装饰与氛围（多数是"挂上就有效果"）

| 脚本 | 作用 | 备注 |
|---|---|---|
| `RainSystem` + `RainParticles.prefab` | 雨幕、地面涟漪 | 用于 S2/S2.5/S3 |
| `SandstormController` + `SandstormParticles.prefab` | 沙尘暴强度/粒子/后期 | 与 `WindArea` 联动；`S0.5/S1` |
| `Tumbleweed(Spawner)` | 风滚草 | 沙漠关 |
| `PlantSway` / `DeadGrassSway` | 植物按风摆动 | 雨林/沙漠装饰 prefab 内 |
| `ParallaxLayer` | 视差背景 | S3 等 |
| `AmbientDustController` | 环境浮尘 | |
| `PlayerVFX` | 跑动尘土、落地、死亡/重生粒子 | 在 PlayerControl prefab 内 |
| `Auto*`（Blink/Bob/Drift/Flicker/HueShift/Orbit/Parallax/Pulse/Scale/Spin/Sway/Wave） | 通用的"小动画"组件 | 挂在装饰物上，无代码依赖 |

这些脚本一般不需要动；改美术/氛围时才看。

---

## 7. UI、音频、美术管线

### 7.1 设置面板与计时器（`SetCanvas.prefab` + `UIManager`）

- `UIManager` 挂在 `SetCanvas` 上，负责：
  - 设置面板开合（`TogglePanel/OpenPanel/ClosePanel`）、返回菜单（`LoadScene(0)`）；
  - **计时器**：`startOnFirstInput=true` 时等玩家第一次按方向/跳/S/E/Shift 才开始；通关 `StopTimer()`；按 R `ResetTimer()`；
  - 自动绑定音乐/音效滑条（路径 `SetPanel/Music/Slider`、`SetPanel/SFX/Slider`）；
  - 自动绑定计时器显隐 Toggle（找不到路径时会在子树里兜底找唯一 Toggle）。
- 时间格式 `mm:ss.xx`，用"百分秒整数"再拆分，避免出现 `:60.00`。
- 计时器文本路径 `Timer/Text (TMP)`；隐藏 Timer 物体不会停止计时（只是看不见）。
- **坑**：`UIManager` 在 `Start()` 里 `AutoBind*`，如果场景里没有 `MusicManager` 会 Warning；不是致命错误。

### 7.2 按钮手感

- `BouncyButton`：多层结构（Shadow / Back / Frame / Title），悬停膨胀→压扁、按下下沉、点击冲击、光圈（用 `Mask` 裁剪在背板内）、悬停随机倾斜。**结构不符合约定会静默失效**（字段为空）。
- `UIButtonAudio`：点击音效。
- `BouncyButton` 不检查 `Button.interactable`，所以 `playToChoose` 在禁用进入按钮时会同时 `bouncy.enabled = false`——改按钮逻辑时注意这个联动。

### 7.3 开始界面（`Start.unity`）

| 对象/脚本 | 作用 |
|---|---|
| `menu`（title/play 上） | 鼠标视差：UI 元素随鼠标轻微偏移 |
| `playToChoose` | 点"开始"后设置面板用欠阻尼弹簧滑入；退出按钮滑出；`OnClick` 用代码绑定，不依赖 Inspector |
| `ObjectFloat` | 荷叶/花瓣在 Canvas 内漂浮、被点击推动、Q 弹挤压 |
| `RippleRing` | 点击波纹 |
| `LevelSelectPanel` + `LevelSelectButton` | 选关（运行时只读场景，不改 Canvas/EventSystem） |
| `LevelLoader` | 圆圈转场（同一 prefab 每关都有） |

**当前 Start 场景里有两份 `LevelSelectCanvas`**（第二份未激活），每份 28 个关卡按钮，索引 1~28；而 Build 只有 22 个场景 → 点后面的数字会提示"超出范围"。这是合并遗留，见 13.2。

### 7.4 转场（`LevelLoader`）

- 状态机：`CircleWipe_01`（盖住）→ 切场景 → `CircleWipe_02`（揭开）。
- 不按固定时长切，而是等 `normalizedTime >= 0.75`（盖满）或超时；`isTransitioning` 静态标志让新场景 `Start()` 播淡出。
- `Rebind()` 解决"覆盖动画不播"。
- **不要改动画状态名**；要改时长就同时改 `fadeInDuration/fadeOutDuration/timeout` 的逻辑。
- 已知注释过期：`UIManager.StopTimer()` 的注释写"由 LevelLoader 调用"，实际是 `NextLevel.MarkCleared()` 调用。

### 7.5 音频

- `MusicManager`：常驻单例，跨场景不销毁；音乐/音效音量存 PlayerPrefs；`UIManager` 的滑条直接绑它的方法。
- `RainAudioController` / `SandstormAudioController`：跟随粒子/天气的循环音。
- 关卡场景里通常有一个 `music` 物体（`AudioSource`）；`Start` 场景的那个还挂了 `UIButtonAudio`。

### 7.6 渲染

- URP 2D Renderer + `Light 2D`（钥匙灵光球用 `Light2D`，在 `UnityEngine.Rendering.Universal` 命名空间）。
- 后处理：`PostProcessing.prefab`（Gobi / Rainforest 各一份），Volume 参数在 prefab 里。
- 材质里有两套"按世界坐标采样花纹"的 shader（`PlatformDapple`、`ContolPlatform`），移动平台必须喂锚点（见 6.2）。
- `Assets/2DWater` 是独立的水面渲染包（自带 URP asset / Renderer2D），当前主线未使用，别误删。

---

## 8. 死亡、重生与状态重置（最关键的不变量）

### 8.1 触发源

| 来源 | 动作 |
|---|---|
| 按 R | `MoveLate.CheckReset()`：`Telemetry.NoteDeath("manual_r")` + `UIManager.ResetTimer()` → `TriggerDeath()` |
| 碰 `Die`（熔岩等） | `NoteDeath("hazard:xxx")` + `MoveLate.isR = true` → 下一帧 `CheckReset` |
| 被夹死（`CrushCheck`） | `NoteDeath("crush")` + `isR = true` |
| 教程/传送（`tutorial.cs`、`two_to_play.cs`） | 直接置 `MoveLate.isR = true` 强制重置 |

### 8.2 时序（`DeathRespawnVFX`）

```text
TriggerDeath()
 ├─ 若 isProcessing → 忽略（防重复）
 ├─ Telemetry.OnDeath()          ← 必须在传送前，坐标才是死亡点
 ├─ isDead = true                ← 两个角色立刻禁用输入
 ├─ 所有实例 DOTween 缩小到 0（0.12s）+ 死亡粒子
 ├─ MoveLate.DoReset()：P1/P2 回出生点，清队列，清冲刺/爬梯/下穿
 ├─ Key.ResetAllKeys() + KeyOrb.DisableAllOrbs()
 ├─ LockAnimator.ResetAllLocks() + NextLevel.ResetAllDoors()
 ├─ Button_once/Hold/Multi.ResetAllButtons()
 ├─ 等一帧让物理稳定
 ├─ 所有实例放大回原尺寸（0.5s，OutElastic）+ 重生粒子
 └─ isDead = false
```

### 8.3 重置清单

| 对象 | 重置内容 | 谁负责 |
|---|---|---|
| 玩家位置 | 回 `Start()` 时记录的 `p1Pos/p2Pos` | `MoveLate.DoReset` |
| 输入队列 | 清空 | 同上 |
| 冲刺/爬梯/下穿 | 全部清状态、恢复碰撞 | 同上 |
| 钥匙 | 回原位、重新激活、`hasKey=false` | `Key.ResetAllKeys` |
| 灵光球 | 全部禁用 | `KeyOrb.DisableAllOrbs` |
| 锁/黑暗罩 | 回原位、恢复 alpha、重新激活 | `LockAnimator.ResetAllLocks` |
| 门 | 回到"有锁=锁着 / 无锁=开着" | `NextLevel.ResetAllDoors` |
| 按钮/平台 | 平滑回起点 | `Button_*.ResetAllButtons` |
| 计时器 | 仅"手动按 R"清零；被危险物杀死不清零 | `MoveLate.CheckReset` |

### 8.4 注意

- 静态列表（`allKeys`、`allDoors`、`activeInstances`…）**只增不减**（除 OnDestroy 的部分）。切场景后旧对象会被 Unity 判为 `null`，但列表元素还在；遍历时都有 `!= null` 保护，问题不大，但别在列表里假设"数量=当前场景数量"。
- 如果场景里没有任何 `DeathRespawnVFX` 实例，`TriggerDeath()` 会直接调 `DoReset()`，**不会**重置钥匙/门/按钮（降级路径，正常场景不会走到）。
- `tutorial`/`two_to_play` 直接置 `isR` 是为了"传送玩家"复用死亡流程，副作用是计时器不会清零（因为它们没走 R 分支）。改教程逻辑时留意。

---

## 9. 遥测与云端看板

### 9.1 架构

```text
Unity（Assets/Scripts/Telemetry，自动启动，零场景改动）
   │ 跳跃/驻留 → 客户端按 0.5×0.5 格聚合，60 秒或关卡结束发一批
   │ 死亡/通关/进入关卡 → 立即入队
   ▼ POST /v1/events
Cloudflare Worker（cloudflare_backend/src/index.js）→ 校验 → D1 `game_events`（只追加）
   ├─ GET /            → 看板（dashboard.html）
   └─ GET /v1/stats/*  → overview / compare / scene / recent
```

### 9.2 客户端关键点

| 文件 | 职责 |
|---|---|
| `Telemetry.cs` | 单例、队列、批量上报、失败落盘 spool、退出时同步补发、`RuntimeInitializeOnLoadMethod` 自启 |
| `TelemetryLevelStats.cs` | 单关统计：格子驻留/跳跃/死亡/时长/通关路线 |
| `TelemetryFields.cs` | 手写 JSON（不依赖 Newtonsoft） |
| `TelemetryConfig.cs` | 开关、endpoint、间隔、阈值（**只改这里**） |
| `Editor/TelemetryMenu.cs` | 菜单 `Tools/Telemetry`：开数据目录、发测试事件、清缓存 |

- 事件类型：`session_start`、`level_start`、`cell`（格子级别的 `dwell_s` / `jumps` / `jump_fails`）、`death`、`route`、`level_clear`、`level_quit`，以及测试用的 `test_ping`。**跳跃不是独立事件**，而是聚合在 `cell.jumps` 里。
- **关卡名 = 场景名**。改场景名会让历史数据断档；新关卡只要加进 Build Settings 就自动统计。
- 失败上报进 `Application.persistentDataPath/telemetry_spool.jsonl`，下次启动/30 秒后重试；永久性 4xx 会丢弃。
- 本地调试：在 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\A-Little\telemetry_endpoint.txt` 写一行 `http://127.0.0.1:8791/v1/events`，即可把数据打到本地假接收端。
- 退出时用后台线程 + `HttpWebRequest` 同步发最后一批（3.5 秒预算），因为 `UnityWebRequest` 在退出时不会完成。

### 9.3 服务端与看板

详细文档在 **`cloudflare_backend/README.md`**（写得很好，部署/维护/额度/坑都在里面）。要点：

- 目录：`src/index.js`（接收+托管看板）、`src/stats.js`（统计 SQL）、`src/dashboard.html`（看板源码）、`src/dashboard-page.js`（**生成物，别手改**）、`schema.sql`、`queries.sql`、`test/`、`tools/`。
- 部署：`npm install` → `npx wrangler login` → `npm run db:create` → 填 `wrangler.toml` 的 `database_id` → `npm run db:init` → `npm run bg:create` → `npm run deploy`。
- 部署后把地址填回 `TelemetryConfig.DefaultEndpoint`（末尾 `/v1/events`）。
- 看板 6 个标签页：热力图、横向对比、死亡、时长与通关、最近事件、数据字典；支持 P1/P2 分层、路线、背景图。
- 测试：`npm test`（README 记录为 38 个）；本地预览 `node tools/preview-dashboard.mjs`；改 `dashboard.html` 后必须 `npm run build:dashboard`。
- 安全：`INGEST_KEY` 挡伪造上报，`ADMIN_KEY` 才能清库/传背景图；不设就 403。
- **额度约束**：D1 免费每天 10 万行；`env.DB.batch()` 每批最多 50 条；`metadata` 单条 ≤4 KB；`route` ≤300 格。看板每次刷新 7 条聚合 SQL。
- **不要改 `Assets/Scripts/Flow/LevelLoader.cs`**（GBK），也不要改 `metadata` 的 JSON 结构。

---

## 10. 开发与验证方式（Unity True CLI 已就绪）

> **2026-11-24 更新**：旧的 `EditorMcpBridge`（`127.0.0.1:35353`）已删除。
> 现在统一用 **Unity True CLI 0.8.0** 操作编辑器；安装状态和入口见第 14 节。
> 如果 `Library/Pipeline/.unity-pipeline-port` 不存在，说明 Pipeline 没起——不要让用户重跑安装器，先按第 14 节排查。

### 10.1 你现在有什么

| 手段 | 说明 |
|---|---|
| `unity-true-cli`（首选） | 查场景/对象/组件、改字段、进 Play、读 Console、截图、跑命令 |
| CLI 文件工具 | 读写 `.cs`、读 YAML、grep guid；适合改代码和查历史 |
| 用户 + Unity 编辑器 | 主观手感、画面确认、需要人眼判断的部分 |
| Unity 日志 | 编译错误、异常堆栈（见 10.2） |
| Git | 改坏了回滚 |

### 10.2 编译与日志

- 改完 `.cs` 先触发 Unity 刷新/编译。优先用 CLI 查当前 Pipeline 的 `recompile` 命令并执行；没有就请用户切回 Unity 按 `Ctrl+R`。
- 用 `editor_status` 看 `compiling` / `domainReloadInProgress`，等它们都变 `false` 再操作。
- 编译错误：CLI 查 Console；或读 `%LOCALAPPDATA%\Unity\Editor\Editor.log`，搜 `error CS` / `Compilation failed`。
- 刚删除/重命名脚本时，第一次编译可能报一次 `error CS2001`（旧文件还在编译列表里），再刷新一次即可。
- 不要在 Unity 已打开该工程时用 `-batchmode` 跑同一个工程：工程锁会失败。

### 10.3 运行时验证

- 用 CLI 进 Play、读 Console、截图；**但不要替用户判断手感**——操作感、节奏、视觉是否舒服，让用户看。
- 一次只验证一个点，给出明确步骤和预期结果。
- 需要临时日志时加 `Debug.Log("[Tag] ...")`，验证完删掉。
- 验证完把 Play 停掉，不要留着 Play 模式离开。

### 10.4 场景 / prefab

- 用 CLI 的命令（先 `Start-UnityTask.ps1` 查 schema）读写场景、GameObject、组件、prefab；**不要手改 `.unity` / `.prefab` YAML**。
- 读 YAML 只用于理解引用和 diff，不用于写入。
- CLI 操作前确认当前场景没有未保存改动；**不要自动保存、不要自动关闭 Editor、不要自动停 Play**，这些先问用户。
- 场景 YAML 里很多 Tilemap 子物体的 `m_Name` 是空的，不代表文件损坏。

### 10.5 推荐的"改-验"流程

1. 读脚本/文档，确认改哪里。
2. 用 CLI 查当前能力与目标对象（只读）。
3. 改代码或调用写命令。
4. 等编译/域重载结束，读回对象状态或 Console。
5. 需要主观判断时，请用户看画面/手感。
6. 交付前跑第 12.5 的清单。

---

## 11. 代码约定与工程坑

### 11.1 编码（最容易踩的坑）

工程里绝大多数 `.cs` 是 **UTF-8**，但有 **7 个是 GBK**：

```text
Assets/2DWater/WaterShapeController.cs
Assets/2DWater/WaterSpring.cs
Assets/Scripts/Flow/Start/menu.cs
Assets/Scripts/Flow/LevelLoader.cs
Assets/Scripts/Gameplay/Mechanism/Fruit_Delay.cs
Assets/Scripts/Other/Combatant.cs
Assets/Scripts/Other/Weapon.cs
```

- 用 UTF-8 工具读这 7 个文件会报 "invalid UTF-8"；用 UTF-8 写回会把中文变乱码。
- 改它们的正确姿势：用能指定编码的编辑器/脚本（GBK/GB18030 读写），或者只做**纯 ASCII 的局部替换**并先备份。
- **不要**用 PowerShell 的 `Get-Content -Raw` + `Set-Content` 批量改任何含中文的源码：Windows PowerShell 默认按 ANSI(GBK) 解码，会把 UTF-8 的中文整篇毁掉。用 `edit` 工具或显式指定编码。
- 其他文件（UTF-8）用 `edit` 工具做局部替换是安全的。

### 11.2 代码风格

- **中文注释，解释"为什么"而不是"做什么"**。很多注释记录了踩坑历史（例如"不能直接改 rb.velocity"、"不能在这里设 isGrounded=false"）。修改代码时**保留这些注释**，新增逻辑也按同样风格写。
- 字段/方法命名：公开字段用 camelCase（`delayTime`、`moveSpeed`），私有字段也多为 camelCase；类型 PascalCase。没有统一 namespace（全局命名空间）。
- 大量 `public` 字段以便 Inspector 拖拽；`[Header]`/`[Tooltip]` 写得很全，新增字段请补 Tooltip。
- 防御式编程：`if (x == null) return;`、`try/catch` 包住非关键路径、兜底默认值。
- 不要引入新框架/DI/第三方包来解决局部问题。

### 11.3 物理与生命周期

- `Physics2D.autoSyncTransforms = false`：改 `Rigidbody2D.position` 不会立刻同步 `Transform`；`GroundCarry` 因此**双写** `transform.position` 和 `rb.position`。
- 移动写 `Update`，物理步在 `FixedUpdate`；`interpolation = None`。
- 大量 `static` 状态跨场景存续。**切场景不会自动清 static**，新增静态字段时要想清楚"什么时候归零"。
- 协程在对象被禁用/销毁时会停；DOTween 动画要 `DOKill()`（死亡/重置代码里都有）。
- `Time.timeScale` 目前没有暂停逻辑；设置面板是直接 `SetActive`，游戏仍在后台跑（如果以后要做真暂停，注意 `WaitForSeconds` 和 DOTween 的 `SetUpdate(true)`）。

### 11.4 动画与美术约定

- 玩家 Animator 参数：`p1j` / `p2j`（bool，是否在空中）。改动画机要同步这两个名字。
- 转场动画状态名：`CircleWipe_01` / `CircleWipe_02`。
- 移动平台的贴图锚点属性：`_DappleAnchorPos` / `_PlatformAnchorPos`。
- 场景的 Sorting Layer 只有 `Default` 和 `Background`；层级主要靠 `Order in Layer`。

### 11.5 场景 / prefab 操作

- 序列化是 Force Text，场景 YAML 可读；但**不要手改 YAML**（fileID 引用极易断），用 Unity 编辑器改。
- `PlayerControl.prefab` 739 KB、引用很多美术/粒子，**不要在 prefab 模式里大改结构**；优先改脚本或加子物体。
- 关卡里的 PlayerControl 是 prefab 实例；改 prefab 会影响所有关卡，改实例只影响当前关。想清楚再动手。
- 每关必须有 `LevelLoader` + `SetCanvas`（否则无转场/无 HUD），可用 `Tools/关卡工具/批量补齐…` 检查。
- 改 Build Settings 会改变所有关卡的 `buildIndex`，**同时影响 `NextLevel` 的下一关和选关按钮的索引**。

### 11.6 标签 / 层 / 物理设置（现状）

- Tags：`Ground`、`Ladder`、`Player`、`Player2`、`Button`、`Tumbleweed`；此外还有一个小写 `player2` tag（与 `Player2` 不是同一个，极易写错，代码里用的是 `Player2`）。
- Layers：4=`Water`、5=`UI`、6=`Player`、7=`player2`；没有 `OneWayPlatform`。
- 碰撞矩阵全开（`ffff…`），没有做层过滤；性能敏感时不要指望 Layer 帮忙。
- `Physics2D.gravity = (0, -9.81)`，`queriesHitTriggers = true`（所以射线会打到触发器）。

### 11.7 性能注意

- `WindArea.Update` 每帧 `OverlapBoxAll` + 对每个目标多条 `RaycastAll`；同场景别叠太多风区。
- `UIManager.FindSingleToggle`、`FindObjectOfType` 在 `Start` 里用没问题，不要每帧调。
- `TilemapOneWayPlatform` 会为每个瓦片建一个 GameObject，大图慎用。
- 粒子系统较多（雨/沙尘/风滚草），低端机注意 `maxParticles`。

---

## 12. 接手后的工作协议

### 12.1 第一次对话就这么做

1. 读本文档第 0、1、4、5、8、10、11、13、14 节。
2. 确认 Unity True CLI 就绪：`Library/Pipeline/.unity-pipeline-port` 存在、`/api/status` 为 `ready`、`editor_status` 返回 `success:true`；再问清当前打开的是哪个场景。
3. 问用户 2~3 个关键问题（见 13.4），**不要一次问十个**。
4. 在没搞清楚"主版本关卡"之前，只做读操作和小范围脚本修改。

### 12.2 每个任务开始前

- [ ] 这个需求属于哪一层：玩法 / 关卡 / UI / 美术 / 遥测 / 工具？
- [ ] 涉及哪些脚本？先 `read` 完整文件（不要只看片段）。
- [ ] 涉及场景/prefab 吗？先用 CLI 查层级/组件/字段，别直接读 YAML 猜。
- [ ] 会不会动到 static 状态、Build Settings、Tag/Layer、动画状态名？
- [ ] 改完怎么验证？（CLI 读回 / 编译状态 / 进 Play / Console / 遥测本地接收端）

### 12.3 改代码

- 小步修改，一次只解决一个问题；不要顺手重构。
- 保持中文注释风格，解释"为什么"。
- 新增 `[Tooltip]`，尽量给默认值，避免场景里忘配就空引用。
- 不要在 `Update` 里新加 `FindObjectOfType` / `GetComponent` 链。
- 涉及 GBK 文件时，用编码安全的方式，并检查 diff 没有整篇变红。
- 编译错误先看 CLI 的 Console 查询或 `Editor.log`，Unity 的报错行号比 IDE 准。
- 触发编译用 CLI 的 `recompile`（先查 schema）；没有就请用户按 `Ctrl+R`。等 `editor_status.compiling=false` 再读回。

### 12.4 改场景 / prefab

- 用 CLI 做改动，读回确认后再 Play 验证；不要手改 `.unity` / `.prefab` YAML。
- 保存前确认场景里被标脏（dirty）的东西都是你有意改的；**不要自动保存**，保存前问用户。
- 改 prefab 前问自己：所有关卡都会变吗？要不要用实例覆盖？
- 不要把实验对象（`__Probe__` 之类）留在场景里。
- 不要删场景、不要改场景文件名（遥测按场景名统计）。

### 12.5 验证清单（每次交付前）

- [ ] Unity 无编译错误、无新增红色报错（CLI `editor_status` + Console）。
- [ ] 用 CLI 进 Play：P1 能移动/跳/（如启用）冲刺/爬梯/下穿；P2 延迟重放正确。
- [ ] 死亡一次：两个角色都回出生点，钥匙/锁/按钮/门复原，计时器行为正确。
- [ ] 通关一次：转场正常，下一关加载正确。
- [ ] 改动涉及遥测时：本地 mock receiver 能收到合法 JSON。
- [ ] Console 没有新的 `NullReferenceException` / `MissingReferenceException`。

### 12.6 沟通方式

- 用户用中文，偏好**直接、具体、有依据**的回答；不要空泛地说"已优化"。
- 报告时给：改了什么、为什么、怎么验证的、还有什么风险。
- 不确定就明确说"不确定/需要确认"，不要编造 Unity 行为。
- 用户很在意**手感**和**细节**（代码里大量"为什么这么写"的注释就是证据）；改数值要给对比。

### 12.7 Git

- 远程：`https://github.com/Fleecebell/ALittle-Git.git`，当前分支 `Fleecebell`。
- **未得到用户明确指示，不要 commit / push / 切分支 / 合并**。
- 提交前先 `git status` 看清改动；不要提交 `Library/`、`Builds/`、`*.csproj`、`*.sln`、`.env`。
- 如果 git 命令不可用，先问用户用哪种方式（IDE 或安装 Git）。

---

## 13. 已知问题与建议待办

### 13.1 【高】Build Settings 关卡重复 / 顺序混乱

- 证据：`9101`、`9101 1`、`9101 2` 三个近乎同尺寸场景都被启用；`S*` 与 `91xx` 交替；`S0`、`4th` 被禁用；`CUSoon` 收尾。
- 影响：正常游玩会把同一关连玩三遍；`NextLevel` 的"下一关"不可预测；做关卡任务容易改错版本。
- 建议：先让用户确认主版本（很可能是 `91xx` 系列，因为遥测看板示例用的是 `scene=9101`），再整理 Build 列表；**整理前不要改**。

### 13.2 【高】选关面板过期 + 重复

- 证据：`Start.unity` 有两份 `LevelSelectCanvas`（第二份未激活），每份 28 个 `LevelSelectButton`，`levelIndex` 最大 28；Build 实际只有 22 个场景。
- 影响：点按钮报"索引超出范围"；两份面板互相挡点击（`LevelSelectPanel` 自己会打警告）。
- 建议：只保留一份，用 `Tools/关卡工具/生成选关面板` 重新生成（或手动删多余按钮）；确认 Build 列表后再生成。

### 13.3 【中】遗留代码 / 未使用系统

| 项 | 状态 | 建议 |
|---|---|---|
| `tutorial.cs`、`one_to_two.cs`、`two_to_play.cs` | 只存在于未启用的 `Start1.unity` | 确认是否还要教学关，不要就归档 |
| `choose.cs` | 只用于 `CUSoon.unity` 的调试跳关 | 保留或删，随用户 |
| `TilemapOneWayPlatform.cs` | 需要不存在的 Layer `OneWayPlatform` | 要用先补 Layer，否则只警告、下穿失效 |
| `WaterPool` / `2DWater` | 只在 `notalielevel` | 确认是否回归主线 |
| `SpeedBand` / `Grid_SpeedBand.prefab` | 主线未使用 | 用之前注意 static `moveSpeed` 残留 |
| `Fruit_Delay` / `Combatant` / `Weapon` | 无任何场景使用 | 可删，但先问 |
| `LevelLoaderBatchInstaller` / `LevelSelectPanelGenerator` | 编辑器工具，可用 | 执行前先 commit（工具说明里也这么写） |

### 13.4 【中】需要用户确认的问题

1. **哪一套关卡是主版本？**（`91xx` 还是 `S*`；`9101/9101 1/9101 2` 是不是三个副本）
2. **当前交付目标是什么？**（展会 demo / 完整流程 / 只改某一关 / 加新机制）
3. **E 变大、Shift 冲刺是否要重新启用？**（现在资产里是关闭的）
4. **P2 延迟 1 秒是最终值吗？**（脚本默认 0.5，prefab 是 1）
5. 是否要清理根目录的 `steam_*.png` / `uninstall_valorant.*` 等无关文件？

### 13.5 【低】其他

- `NextLevel.showDebug` 字段默认 `true`（实例可覆盖），正式包可能刷 Console。
- `UIManager.StopTimer` 注释过期（写的是 LevelLoader 调用）。
- `TilemapOneWayPlatform` 里 `DebugDrawPlatform` 是空实现。
- 工程没有 Unity 侧自动化测试；只有后端有 `npm test`。
- `Assets/Scenes` 里还有大量历史场景，容易误开；建议按第 4 节表格核对。

---

## 14. 工具链：Unity True CLI（已安装）

> 2026-11-24：原 `EditorMcpBridge` 已删除；现改用 **Unity True CLI 0.8.0**（第三方兼容发行包：Skill + Unity Pipeline + 官方签名 CLI 二进制）。
> 安装包位置：`E:\unity\UnityPlugins\CliPro-0.8.0-windows-x64`（入口 `Install-UnityCli.ps1`）；细节见包内 `开始使用.md`、`SKILL.md`。

### 14.1 当前安装状态（2026-11-24 22:20 独立核实）

| 项 | 值 |
|---|---|
| Skill | `%USERPROFILE%\.codex\skills\unity-true-cli`，0.8.0，624 个文件 |
| 项目 Pipeline | `Tools/UnityTrueCLI/com.unity.pipeline`，`0.8.0-exp.1+truecli.9`（593 个文件） |
| 服务 | `Library/Pipeline/.unity-pipeline-port`；`/api/status` = `ready`（本次端口 7800，每次启动可能不同，以文件为准） |
| 冒烟 | `editor_status` 返回 `success:true`、`compiling:false`、`playMode:"stopped"` |
| 输入后端 | `activeInputHandler: 0`（旧 Input 保留） |
| Input System | 间接依赖解析为 `1.11.2`（`packages-lock.json`，depth 1） |
| 安装回执 | `Temp/UnityTrueCLI/bootstrap-result-dbf5b8af90cc416a83ecfb2a366cf961.json` |

### 14.2 怎么用

```powershell
$skill = "$env:USERPROFILE\.codex\skills\unity-true-cli"
& "$skill\scripts\Start-UnityTask.ps1" -ProjectPath "E:\unity\UnityProgram\ALittle-Git" -Task general
& "$skill\scripts\Invoke-Unity.ps1" -ProjectPath "E:\unity\UnityProgram\ALittle-Git" -UnityArgs @('command','editor_status','--json')
```

- 先只读：`editor_status`、当前场景、场景层级、Console。
- 再查 schema：`Start-UnityTask.ps1 -Task camera/ui/...` 或 `-Command <name>`，**不要假定命令名**。
- 写操作后用读回确认（再查一次对象/字段），不要只看命令返回的 success。
- 保存场景、关闭 Editor、停 Play 之前先问用户。
- 命令索引在 `references/command-index.md`；恢复/配方在 `references/ai-workflow.md`。

### 14.3 判断"能不能开始改工程"

四项全过才可以：

1. `Library/Pipeline/.unity-pipeline-port` 存在，`/api/status` 返回 `ready`；
2. `editor_status` 返回 `success:true`，`compiling` 与 `domainReloadInProgress` 都是 `false`；
3. `activeInputHandler` 仍是 `0`；
4. Unity Console 没有 `error CS`。

如果 port 文件不存在：**不要重跑 `Install-UnityCli.ps1`**。先看 `Temp/UnityTrueCLI` 里的 request/result，八成是 Unity 没刷新（让用户按 `Ctrl+R`），然后用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\.codex\skills\unity-true-cli\scripts\Install-Pipeline.ps1" `
  -ProjectPath "E:\unity\UnityProgram\ALittle-Git" -RetryFailedInstall -TimeoutSeconds 1800
```

### 14.4 本项目特有的风险

1. **Input System**：Pipeline 依赖 `com.unity.inputsystem`（本次解析 1.11.2）。项目是旧 Input Manager（`activeInputHandler: 0`）；装完必须确认它仍是 `0`。如果 Unity 弹窗问是否启用新输入后端，选"保留旧 Input / No"。否则 `Input.GetKey` 会抛异常，游戏直接不能玩。
2. **GBK 脚本**：7 个 GBK 文件（见 11.1）不要让 CLI 做"读-改-写回"，否则中文会乱码；要改用编码安全的方式。
3. **版本**：项目 `2022.3.55f1c1`，发行包验证的是 `2022.3.45f1`（同系列，未逐补丁验证）。装完先跑 `editor_status` 和一条只读命令，再上大任务。
4. **manifest 是绝对路径**：`"com.unity.pipeline": "file:E:/unity/UnityProgram/ALittle-Git/Tools/UnityTrueCLI/com.unity.pipeline"`。换机器/移动工程会失效，需要重装或改成相对路径（改前先问用户）。
5. **本地控制通道**：Pipeline 只监听本机，token 在 `Library/Pipeline/.unity-pipeline-port`；`eval` 能执行 C#。不要把端口/token 暴露出去，也不要运行来源不明的 eval。

### 14.5 安装改了什么 / 提交什么

| 路径 | 说明 |
|---|---|
| `Packages/manifest.json` / `packages-lock.json` | 增加 `com.unity.pipeline`（file: 本地包）及依赖（含 Input System） |
| `Tools/UnityTrueCLI/com.unity.pipeline/` | Pipeline 包本体（约 40 MB、593 个文件） |
| `Assets/Editor/UnityTrueCli/PackageBootstrap.cs` | 安装引导脚本（含 .meta） |
| `Temp/UnityTrueCLI/`、`Library/Pipeline/` | 生成物/回执，**不要提交** |

> Git：如果要保留"clone 即用"，提交 `Tools/UnityTrueCLI`、`Assets/Editor/UnityTrueCli`、`manifest.json`、`packages-lock.json`；代价是仓库多约 40 MB。不提交则每台机器都要重装，而且 manifest 的 `file:` 引用会指向不存在的目录。二选一，写进 README。

---

## 附录 A：脚本速查表

### Player

| 文件 | 一句话 |
|---|---|
| `Player/MoveFirst.cs` | P1 全部输入与移动、跳跃、冲刺、爬梯、下穿、水、风、大小 |
| `Player/MoveLate.cs` | P2 延迟重放、队列、死亡重置入口 `DoReset` |
| `Player/DeathRespawnVFX.cs` | 死亡/重生动画与全局重置编排 |
| `Player/CrushCheck.cs` | 四向夹缝死亡检测 |
| `Player/GroundCarry.cs` | 站在移动平台上跟随平台位移 |

### Gameplay

| 文件 | 一句话 |
|---|---|
| `Gameplay/Key/Key.cs` | 钥匙拾取、`hasKey`、重生复原 |
| `Gameplay/Key/KeyOrb.cs` | 灵光球飞向 P1、变钥匙、跟随 |
| `Gameplay/Key/LockAnimator.cs` | 锁 + 黑暗罩的解锁动画与复原 |
| `Gameplay/Mechanism/Die.cs` | 危险物致死 |
| `Gameplay/Mechanism/WaterPool.cs` | 水池浮力/游泳 |
| `Gameplay/Mechanism/WindArea.cs` | 风幕、遮挡检测、风力写入 |
| `Gameplay/Mechanism/SpeedBand.cs` | 按距离加减速 |
| `Gameplay/Mechanism/TilemapOneWayPlatform.cs` | Tilemap 每格生成单向平台（需 Layer） |
| `Gameplay/Mechanism/Fruit_Delay.cs` | 旧原型，未使用 |
| `Gameplay/Platform/Button_once.cs` | 一次性/踩住按钮 + 平台 |
| `Gameplay/Platform/Button_Hold.cs` | 持续踩住按钮 |
| `Gameplay/Platform/Button_Multi.cs` | 多踏板按钮（AND/OR） |
| `Gameplay/Platform/AutoMoving.cs` | 两点往返平台 |
| `Gameplay/Platform/PlatformAnchorFeeder.cs` | 平台位置喂 shader（防贴图滑动） |
| `Gameplay/Platform/DappleAnchorFollower.cs` | Tilemap 斑驳层锚定 |

### Flow / UI / Art / Audio

| 文件 | 一句话 |
|---|---|
| `Flow/LevelLoader.cs`（GBK） | 圆圈转场、加载下一关 |
| `Flow/NextLevel.cs` | 门/锁/通关判定、冻结计时、上报 |
| `Flow/Start/playToChoose.cs` | 开始面板弹簧滑入 |
| `Flow/Start/menu.cs`（GBK） | 鼠标视差 |
| `Flow/Start/ObjectFloat.cs` | 荷叶漂浮 |
| `Flow/Start/tutorial.cs` | 旧教学关逻辑（Start1） |
| `UI/UIManager.cs` | 设置面板、计时器、音量滑条绑定 |
| `UI/BouncyButton.cs` | Q 弹按钮 |
| `UI/LevelSelectPanel.cs` | 选关面板运行逻辑 + 只读诊断 |
| `UI/LevelSelectButton.cs` | 单个关卡按钮 |
| `UI/ActionUI.cs` | 两个角色头顶的动作显示（P1 看队列，P2 看当前） |
| `UI/UIButtonAudio.cs` | 按钮音效 |
| `Audio/MusicManager.cs` | 跨场景音乐/音量 |
| `Art/PlayerVFX.cs` | 角色粒子 |
| `Art/RainSystem.cs` / `SandstormController.cs` | 天气 |
| `Art/Tumbleweed*.cs` / `PlantSway.cs` / `ParallaxLayer.cs` | 装饰 |

### Editor

| 文件 | 一句话 |
|---|---|
| `Editor/LevelSelectPanelGenerator.cs` | 生成选关面板（菜单 Tools/关卡工具） |
| `Editor/LevelLoaderBatchInstaller.cs` | 给每个关卡补齐 LevelLoader / SetCanvas |
| `Editor/TelemetryMenu.cs` | Tools/Telemetry 菜单 |

---

## 附录 B：输入、标签、层

### 默认键位

| 操作 | 键 |
|---|---|
| 左右移动 | A / D 或 ← / →（`Horizontal` 轴） |
| 跳跃 | 空格 或 W |
| 冲刺 | Shift（+ 左右；当前被资产关闭） |
| 变大/变小 | E（当前被资产关闭） |
| 爬梯 | 空格/W 上，S 下 |
| 下穿单向平台 | S（站在平台上） |
| 重置本关 | R |
| 选关面板开关 | Tab（`LevelSelectPanel.toggleKey`，仅在该脚本启用的场景） |
| 设置面板 | 点界面上的设置按钮（`UIManager`） |

### Tags

`Ground`、`Ladder`、`Player`、`Player2`、`Button`、`Tumbleweed`。

### Layers

| 索引 | 名称 |
|---|---|
| 0 | Default |
| 1 | TransparentFX |
| 2 | Ignore Raycast |
| 4 | Water |
| 5 | UI |
| 6 | Player |
| 7 | player2 |

> 注意：`Player` / `player2` 是 **Layer**；代码里判断玩家用的是 **Tag** `Player` / `Player2`（大小写敏感）。

---

## 附录 C：无桥环境的 CLI 速查

```powershell
# Unity 日志（编译错误 / 运行时异常）。Unity 每次启动会覆盖 Editor.log，旧的叫 Editor-prev.log
Get-Content "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Tail 100

# 只看错误
Select-String -Path "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Pattern 'error CS|Exception' | Select-Object -Last 20

# 找脚本 guid（在场景里搜引用时用）
Select-String -Path Assets\Scripts\Player\MoveLate.cs.meta -Pattern 'guid:'

# 找某个 guid 出现在哪些场景 / prefab（把 <guid> 换成实际的 32 位 guid）
Get-ChildItem Assets -Recurse -Include *.unity,*.prefab | Select-String -Pattern '<guid>' -List

# 找场景里的对象名
Select-String -Path Assets\Scenes\S1.unity -Pattern 'm_Name: Player'
```

> Unity 打开工程时**不要**跑 `-batchmode`（工程锁会失败）。真要批处理，先让用户关掉编辑器，并确认 Unity 安装路径与许可。

---

## 附录 D：术语表

| 术语 | 含义 |
|---|---|
| P1 / Player | 玩家本体，`MoveFirst` |
| P2 / Player2 / 影子 | 延迟重放的角色，`MoveLate` |
| delayTime | P2 落后 P1 的时间（prefab 当前 1 秒） |
| feelSettings | `PlayerFeelSettings.asset`，手感唯一配置 |
| 风幕 | `WindArea` 的分界线，一侧被吹 |
| 锚点 | shader 用的平台世界位置，防贴图滑动 |
| 转场 | `CircleWipe_01/02` 圆圈动画 |
| spool | 遥测失败时的本地缓存文件 |
| buildIndex | Build Settings 里启用场景的顺序索引 |

---

**最后**：这份文档的目标是让你"少踩坑、快上手"，不是替代读代码。真正动手前，至少把 `MoveFirst.cs`、`MoveLate.cs`、`DeathRespawnVFX.cs`、`NextLevel.cs`、`PlayerFeelSettings.cs` 这五个文件完整读一遍——它们是整个游戏的骨架。
