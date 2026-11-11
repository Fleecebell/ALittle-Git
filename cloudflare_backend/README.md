# ALittle 遥测后端（Cloudflare Worker + D1 + 网页看板）

记录玩家 **在哪儿死、在哪儿跳、在哪儿待着不走、每关花多久**，只存库、不统计。
统计在 Worker 里做（`/v1/stats/*`），浏览器打开就是一个热力图看板。

```text
Unity 客户端 (Assets/Scripts/Telemetry)
   │  高频的跳跃/驻留 → 客户端按 1×1 格子聚合，60 秒或关卡结束发一次增量
   │  离散事件（死亡/通关/开一关） → 立刻入队
   ▼  POST /v1/events  {"game":"alittle","v":1,"events":[...]}
Cloudflare Worker (src/index.js)     ← 校验、CORS、批量写
   ├─ env.DB.batch()  一次最多 50 条
   ▼
D1 (game_events 表)                  ← 只追加，不更新
   │
   ├─ GET /            → 看板（src/dashboard.html，热力图 / 死亡热点 / 通关率…）
   └─ GET /v1/stats/*  → 看板用的只读统计接口
```

## 目录

```
cloudflare_backend/
├─ wrangler.toml          Worker 配置（含 D1 绑定 DB、兼容日期、开关变量）
├─ src/index.js           接收端：CORS + 校验 + batch 写入；顺带托管看板
├─ src/stats.js           只读统计接口（overview / scene / recent）
├─ src/http.js            json/html/CORS 响应小工具
├─ src/dashboard.html     看板页面源码（改完要重新生成）
├─ src/dashboard-page.js  由 dashboard.html 生成的模块（别手改）
├─ schema.sql             建表 + 3 个索引
├─ queries.sql            13 条统计 SQL（热力图/死亡热点/停留/通关率/通关路线…）
├─ test/                  35 个测试（上报校验 + 真 SQLite 跑 schema/queries/stats）
├─ tools/mock-receiver.mjs    本地假接收端，用来看客户端到底发了什么
├─ tools/preview-dashboard.mjs 本地看板预览（假数据，跑真 Worker 代码）
├─ tools/build-dashboard.mjs   把 dashboard.html 打包成 dashboard-page.js
└─ .dev.vars.example      本地敏感变量模板
```

## 一、第一次部署（照着敲）

前提：Node 18+（本机 24.x 可以），一个 Cloudflare 账号（免费版就够）。

```powershell
cd E:\unity\UnityProgram\ALittle-Git\cloudflare_backend
npm install                      # 装 wrangler（devDependency）
npx wrangler login               # 浏览器授权一次
npm run db:create                # 建 D1 库，输出里有 database_id
```

把输出的 `database_id` 填进 `wrangler.toml` 的 `REPLACE_WITH_YOUR_DATABASE_ID`，然后：

```powershell
npm run db:init                  # 把 schema.sql 应用到线上库（建表 + 索引）
npm run bg:create                # 建 KV 命名空间（背景图用），把输出的 id 填进 wrangler.toml 的 [[kv_namespaces]]
npm run deploy                   # 发布 Worker，输出形如
                                 #   https://alittle-telemetry.xxx.workers.dev
```

最后把地址填回 Unity：`Assets/Scripts/Telemetry/TelemetryConfig.cs` 的 `DefaultEndpoint`，
末尾要带 `/v1/events`：

```csharp
public const string DefaultEndpoint = "https://alittle-telemetry.xxx.workers.dev/v1/events";
```

想加一道钥匙（防止别人往你的库灌垃圾）：

```powershell
npx wrangler secret put INGEST_KEY       # 随便一串随机字符
```

再把同一串填到 `TelemetryConfig.IngestKey`。留空则服务端不校验，测试期可以偷懒。

## 二、Unity 侧

| 文件 | 作用 |
| --- | --- |
| `Assets/Scripts/Telemetry/TelemetryConfig.cs` | 开关、地址、各种间隔与阈值（只改这里） |
| `Assets/Scripts/Telemetry/Telemetry.cs` | 单例、队列、上报、离线补发、静态埋点 API |
| `Assets/Scripts/Telemetry/TelemetryLevelStats.cs` | 单关统计：格子驻留 / 跳跃 / 死亡 / 时长 / 通关路线 |
| `Assets/Scripts/Telemetry/TelemetryFields.cs` | 手写 JSON 构造（不依赖 Newtonsoft） |
| `Assets/Editor/TelemetryMenu.cs` | 菜单 `Tools/Telemetry`：开数据目录、发测试事件、清缓存 |

启动方式：`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 自动建常驻单例，
**没有改任何场景 / Prefab / ProjectSettings**，以后加新关卡不用管它。

埋点位置（一共 6 处，都是加调用，没有改原有逻辑）：

| 位置 | 事件 |
| --- | --- |
| `MoveFirst.cs` 跳跃执行块 | `OnJump("player1", ...)`（P1 起跳；热力图上红色） |
| `MoveLate.cs` P2 起跳块 | `OnJump("player2", ...)`（P2 延迟模仿后起跳；热力图上蓝色） |
| `DeathRespawnVFX.TriggerDeath()` | `OnDeath` → `death`（唯一死亡入口，此时还没被传送走，坐标是死亡点） |
| `Die.cs` 碰到危险物 | `NoteDeath("hazard:<物体名>")` 交代死因 |
| `CrushCheck.cs` 夹死 | `NoteDeath("crush")` |
| `MoveLate.cs` 手动按 R | `NoteDeath("manual_r")` |
| `NextLevel.GoToNextLevel()` | `OnLevelClear` → `level_clear` |

`cell` 事件带 `actor`（`player1` / `player2`），`level_start` 与 `level_clear/quit` 带 `has_p2`，
所以看板能把两个角色分开画。跳跃失败（按了空格但被地面判定挡下）仍然采集在 `jump_fails` 里，
只是界面上不再展示。

格子边长由 `TelemetryConfig.CellSize` 控制，现在是 **0.5 世界单位**（比 1.0 密一倍，横竖各 2 格）。
`cell` 事件里带 `cell_size` 字段，看板按它换算，所以老数据（没有这个字段，按 1.0 算）不会错位。

**每趟尝试结束都会发一条 `route` 事件**（P1、P2 各一条），内容是这一趟走过的格子序列
（`cells: "20,0;21,0;21,1;…"`，最多 300 格，超了截断）。`outcome` 说明这趟是怎么结束的：

| `outcome` | 什么时候发 | 看板里 |
| --- | --- | --- |
| `clear` | 通关 | 「只看通关」勾着时画 |
| `death` | 这一趟死了（只发死者的路线，且少于 10 格不发，避免刚进关就摔死刷数据） | 勾掉「只看通关」才画，能看出死在哪 |
| `quit` | 中途退出、切场景、退出游戏 | 同上 |

看板用它在热力图上叠出带高亮描边的红 / 蓝色块当**路线**，下拉可切「最近一趟」或「全部叠加」。

退出/暂停：`OnApplicationQuit` 会同步把队列刷完（最多等 2 秒），剩下的写本地
`telemetry_spool.jsonl`，下次启动补发。手机端被系统杀进程也不会丢太多。

打包前想临时关掉：`TelemetryConfig.enabled = false`。

## 三、看数据（网页看板）

浏览器直接开：

```
https://alittle-telemetry.lhy060303.workers.dev/
```

看板是 Worker 自己托管的（`GET /` 直接返回 HTML，不引任何 CDN），页面同源调 `/v1/stats/*`，
所以不用另建静态站点、也不会踩 CORS。

六个标签页：

| 标签 | 能看什么 |
| --- | --- |
| 热力图 | 按 0.5×0.5 世界坐标格子铺色，三个指标可切：死亡 / 跳跃 / 停留（停留只记超过 1 秒的格子，颜色每 1 秒一档）。**红色是 P1，蓝色是 P2**，两层各自按自己的最大值归一化后叠在一起（同格重叠会偏紫）；P1 / P2 / 网格 / 路线四个开关可单独关。带高亮描边的色块是**路线**，默认只画通关的（勾掉「只看通关」能看到死在哪儿、半路退出走到哪儿）。鼠标悬停看该格两人的数值和死因。可以给每关铺一张关卡截图当背景，见下面「背景图」 |
| 横向对比 | 每一列是一关，12 行指标（进入次数、通关率、死亡总数、死亡·P1、死亡·P2、每次进入死亡、平均时长、跳跃总数、跳跃·P1、跳跃·P2、停留时长、格子数）。每行按自己的最大值缩放条形，绿色=这一行最好、红色=最差，可换排序依据 |
| 死亡 | 死因分布（带 P1/P2 拆分）、死亡最集中的格子、死亡流水（位置 / 活了多久 / 死前跳了几次 / 本关第几次死） |
| 时长与通关 | 单次游玩时长直方图 + 每次游玩明细 |
| 最近事件 | 最新 80 条原始事件，用来确认数据真的在进来 |
| 数据字典 | 每种事件的每个字段是什么意思（也就是“到底采集了哪些数据”） |

右上角 ⚙ 设置里有 **清空本关** 和 **清空全部** 两个按钮（要先把管理密钥填进去，见下面「清数据」）。
清空是不可撤销的删除，会先弹一次确认框。

地址栏参数可以定位视图，方便把链接发给别人（比如“9101 的跳跃热力图”）：

```
/?scene=9101&tab=heatmap&metric=jumps&range=7d
```

只读接口（都是 `GET`）：

```
/v1/stats/overview?since=<毫秒时间戳>      # 总量 + 每关汇总 + 死因 + P1/P2 拆分
/v1/stats/compare?since=<毫秒时间戳>       # 横向对比用：每关指标 + 每行的最大值
/v1/stats/scene?scene=9101&since=<毫秒时间戳>   # 格子 + 边界 + 统计 + 死亡流水 + 通关路线（routes）
/v1/stats/recent?limit=50
```

`since` 就是看板右上角那个时间范围下拉框。`/v1/stats/scene` 还会返回 `cell_size`
（这一关的格子边长，0.5 或 1.0）和 `routes`（按 outcome 各取最近 20 条，`cells` 已解析成 `[[x,y],…]`，`outcome` 为 `clear`/`death`/`quit`）。

### 清数据（要管理密钥）

删除是不可撤销的，所以默认关闭：**不设 `ADMIN_KEY` 时清库接口一律返回 403**。

```powershell
npx wrangler secret put ADMIN_KEY     # 自己定一串随机字符
```

然后打开看板的 ⚙ 设置，把同一串填进「管理密钥」保存，两个按钮就能用了：

| 按钮 | 实际请求 | 删什么 |
| --- | --- | --- |
| 清空本关 | `POST /v1/admin/clear` `{"scene":"9101"}` | 该关所有事件 |
| 清空全部 | `POST /v1/admin/clear` `{"all":true}` | 整张表 |

命令行等价写法（`--remote` 直接改线上库，别手滑）：

```powershell
npx wrangler d1 execute alittle-telemetry --remote --command="DELETE FROM game_events WHERE json_extract(metadata,'$.scene')='9101'"
npx wrangler d1 execute alittle-telemetry --remote --command="DELETE FROM game_events"
```

### 背景图（每关一张，可以只自己看，也可以传上去共用）

热力图下方「背景图」区域：

- **选择图片**：把该关的游戏内截图铺到网格上，先按关卡名存进浏览器 IndexedDB（库 `alittle-bg`），此时**只有你自己能看到**
- **调整位置大小**：勾上后在地图上拖拽移动、滚轮以中心缩放；也可以直接填「左 / 下 / 宽 / 高」四个数字（世界单位），或点「对齐到格子」按整格吸附
- **锁宽高比**：缩放时保持原图比例，方便对着网格拉伸
- **铺满网格** / **居中** / **还原宽高比**：三个一键摆位
- **透明度**滑块：背景太亮看不见热力格子时往低调
- **删除本机背景**：只删浏览器里那张图（服务器上的不动）

#### 上传到服务器（给一起开发的人看）

对好位置之后点 **「上传到服务器（共用）」**：

1. 浏览器把图压到**长边最多 1600px**（优先 WebP，质量 0.85，超 2.5 MB 自动降质量），一般 80~250 KB
2. `PUT /v1/backgrounds/<关卡名>`，除了图片本体还会把**摆位（x/y/宽/高/透明度）**一起存进 KV 的 metadata
3. 之后任何人打开这一关，本机没有草稿就会自动从服务器拉这张图，并按存下来的摆位显示 —— 不用再手动对格子

需要 `ADMIN_KEY`（和清库是同一把，见上）。没配就返回 403。

| 按钮 | 请求 | 说明 |
| --- | --- | --- |
| 上传到服务器（共用） | `PUT /v1/backgrounds/<scene>` | 覆盖同名关卡，同时更新摆位 |
| 删除服务器副本 | `DELETE /v1/backgrounds/<scene>` | 本机那份不受影响 |
| 从服务器重新加载 | `GET /v1/backgrounds/<scene>` | 丢掉本机草稿，看别人改过的版本 |

背景图存 **Workers KV**（不是 D1）：D1 单条 SQL 上限 100 KB 装不下截图，而且 500 MB 的免费库塞二进制会拖慢统计查询。KV 免费额度 1 GB / 10 万读每天 / 1000 写每天，一张图一次写，够用很久。

> 想换成 R2（10 GB、写入更多）也行：加 `[[r2_buckets]] binding = "BG"`，`src/backgrounds.js` 里把 `env.BG.put/getWithMetadata/delete/list` 换成 R2 的 API 即可。

背景只影响显示，「还原宽高比」不会去裁剪原图。


**给看板加个只读密码**（默认公开在公网，谁拿到 URL 都能看）：

```powershell
npx wrangler secret put READ_KEY
```

设了之后打开 `https://<你的域名>/?key=你设的密码`，密码会存进浏览器 localStorage，之后不用再输。
同样的方式设 `INGEST_KEY` 就能挡住伪造上报。

也可以绕过看板直接跑 SQL（`queries.sql` 里 13 条，都有中文注释）：

```powershell
npx wrangler d1 execute alittle-telemetry --remote --command="SELECT * FROM game_events LIMIT 5"
npx wrangler d1 execute alittle-telemetry --remote --file=./queries.sql   # 会按顺序全部输出
```

几条最常用的：

- **哪关死得多**：`GROUP BY scene, COUNT(*)` —— 一眼看出难度墙在哪关
- **死在哪**：`cell_x / cell_y` 分组，按格子画热力图；`cause` 分布告诉你是不是某个机关太狠
- **空格按得最多**：`cell` 事件里 `SUM(jumps)`，按 `actor` 分组就是 P1 / P2 各自的
- **在哪待最久**：`SUM(dwell_s)`（客户端只上报停留超过 1 秒的格子，路过的格子不占行）
- **每关时长 / 通关率**：`level_start` 与 `level_clear` 对数
- **P2 是不是拖后腿**：`death` / `cell` 事件里的 `actor` 字段，P1 是 `player1`、P2 是 `player2`

## 四、本地自测（不花额度）

```powershell
npm test                          # 38 个测试：上报校验 + 真 SQLite 跑 schema/queries + 统计/清库接口
node tools/mock-receiver.mjs      # 本地假接收端，监听 127.0.0.1:8791
node tools/preview-dashboard.mjs  # 本地看板预览：假数据 2000+ 条，开 http://127.0.0.1:8792/
$env:ADMIN_KEY="devkey"; node tools/preview-dashboard.mjs   # 想顺手测清库按钮就带上这个
```

`preview-dashboard.mjs` 跑的是**真的 Worker 代码**（只把 D1 换成内存 SQLite 并灌假数据），
所以预览看到的就是线上会看到的样子。改完 HTML 先在这里看一眼再部署。

让 Unity 打到本地：在

```
C:\Users\<你>\AppData\LocalLow\DefaultCompany\A-Little\telemetry_endpoint.txt
```

写一行 `http://127.0.0.1:8791/v1/events`（这个文件会覆盖 `DefaultEndpoint`，**不进版本库、不打包**），
然后进 Play 跑一会儿。收到的原始 JSON 会逐行追加到 `tools/received.jsonl`，直接看就知道字段对不对。
测完记得删掉这个文件，否则游戏一直往本地发。

**改了 `src/dashboard.html` 一定要重新生成**：`npm run build:dashboard`（`npm run deploy` 会自动先跑它）。
测试里有一条专门检查 `dashboard-page.js` 与 `dashboard.html` 是否一致，忘了生成会直接红。

从某些宿主环境里跑（比如被 Electron 以 node 模式启动的终端）`npx wrangler` 会报
`Unknown arguments: ...\wrangler-dist\cli.js` —— 那是 argv 错位，不是项目问题。
换普通 PowerShell 窗口，或者直接用真 node 跑：

```powershell
node node_modules\wrangler\bin\wrangler.js deploy
```

## 五、额度与量级

D1 免费版每天 10 万行写入、5 GB 存储。按现在的设计，一关大约产生：

- 1 条 `level_start` + 1 条 `level_clear`/`level_quit`
- 每 60 秒最多 300 条 `cell`（实际一关几十条）
- 每次死亡 1 条 `death`

一局 200 关的完整通关大约几千行，**几十万次游玩才会碰上限**。真要省，把
`TelemetryConfig.CellFlushIntervalSeconds` 调大、`MinDwellToReport` 调大即可。

看板的统计查询是每次刷新跑 7 条聚合 SQL，同样在免费额度内（每天 10 万次读）。

### 免费额度到底是多少（2026 年官网口径）

| 项目 | 免费额度 | 上限 / 说明 |
| --- | --- | --- |
| Workers 请求 | 10 万次/天 | 单次 CPU 10 ms |
| D1 行写入 | 10 万行/天 | |
| D1 行读取 | 500 万行/天 | |
| D1 存储 | 5 GB/账号，单库 500 MB | 单行/单 BLOB ≤ 2 MB；单条 SQL ≤ 100 KB；单次调用 50 条查询 |
| KV 存储 | 1 GB | 单个 value ≤ 25 MB，写 1000 次/天、读 10 万次/天 |
| R2（可选） | 10 GB 存储 | 写 100 万次/月，出站流量免费 |

一局 200 关的游戏数据大约几千行，**几十万次游玩才会碰到 D1 写入上限**；背景图按每关 200 KB 算，29 关不到 6 MB，1 GB 的 KV 能放几千张。

### 能同时多少人玩

先纠正一个概念：这套东西**没有"在线人数"上限**。游戏是单机，服务器只收上报；Workers 是无状态边缘节点，
100 个人同时在线和 1 个人同时在线对它的区别只是"每秒几个请求"。真正的天花板是**每天能写多少行**。

实测每条事件的行写入成本（D1 的 `meta.rows_written`）：

| 表结构 | 每条事件写入行数 | 免费额度（10 万行/天）能存多少事件 |
| --- | --- | --- |
| 3 个索引 + `AUTOINCREMENT`（最初的版本） | 5 | 20,000 |
| 1 个索引 + `AUTOINCREMENT`（第一轮优化） | 3 | 33,000 |
| 1 个索引、无 `AUTOINCREMENT`（**现在**） | **2** | **50,000** |

> 两个反直觉的点：**索引也算写入行数**（多一个索引 = 每次 INSERT 多写一行）；
> **`AUTOINCREMENT` 也要多写一行**（维护 `sqlite_sequence`）。两项加起来让写入成本翻了 2.5 倍。

按实际数据，一次关卡尝试大约产生 10 条事件（1 条 `level_start` + 1 条结束 + 死亡 + 格子增量 + 路线），
即 **20 行写入/尝试**。所以现在的天花板大致是：

| 场景 | 每天可承载 |
| --- | --- |
| 免费档现状 | **2,000~5,000 次关卡尝试/天**，约 60~125 个游玩小时 |
| 晚高峰集中在 4 小时 | 同时约 **15~30 人** |
| 全天均匀铺开 | 平均 2~5 人同时 |
| 把格子按关卡打包（每条尝试 1 行） | 约 2.5 倍，晚高峰 40~75 人 |
| Workers Paid（$5/月，5,000 万行/月） | 再 ×16，晚高峰数百人 |

（次数波动很大，取决于玩家死多少次、在多少格子里停留超过 1 秒。）

请求数不是瓶颈：客户端 8 秒刷一次队列，每小时约 450 个请求，10 万/天够 220 个游玩小时。

**想继续提高上限，按性价比排序：**

1. （已做）砍掉没人用的 `session_id` / `player_id` 索引。
2. （已做）去掉 `AUTOINCREMENT`，见 `migrations/0002_drop_autoincrement.sql`。
3. **把一关的格子聚合成一行**：`cell` 事件占事件总数的 60~80%，改成每关结束时把整张热力图打包进
   一条事件的 `metadata`（JSON 数组，按 4 KB 分片），写入量能再降 2~3 倍。
   需要同时改 `TelemetryLevelStats`（攒着不发）和 `stats.js`（用 `json_each` 拆开聚合）。
4. 客户端降采样：`CellFlushIntervalSeconds` 60 → 120、`MinDwellToReport` 1 → 2，事件数减半。
5. 只采"死亡点 + 跳跃点"，不要全图驻留（最省，但热力图会变糙）。
6. 上 Workers Paid（$5/月），或把原始事件改写到 R2（1 GB 免费、每月 100 万次写入，
   一次请求写一个对象，跟事件条数无关），再用定时 Worker 汇总进 D1。

另外注意**读**也会随数据量涨：看板一次刷新会扫当天的相关行，5,000,000 行/天。
数据攒到百万行以后，建议看板默认时间范围用 7 天，或定期删旧数据（`wrangler d1 execute ... --command="DELETE FROM game_events WHERE client_ts < ?"`）。

## 六、以后怎么改（维护手册）

这一套刻意做成"加东西不用动老代码"的结构，四层各管一件事：

```
Unity 采集（TelemetryLevelStats 拼 JSON）
   → Worker 原样入库（不多解析，metadata 是一个 JSON 文本列）
      → stats.js 用 json_extract 聚合（所有口径都在这里，改口径只改这里）
         → 看板只消费 /v1/stats/* 接口（不直接连数据库）
```

**关键点：事件字段在 `metadata` 里，加字段不需要改数据库、不需要迁移、不需要重新建表。**

### 新关卡：零改动

关卡名就是场景名（`SceneManager.GetActiveScene().name`），进场景自动 `level_start`、
结束自动 `level_clear`/`level_quit`，新关卡只要加进 Build Settings 就会自动被统计，
**不需要在关卡里放任何脚本、不需要改代码**。

### 加一种新事件（例：踩陷阱次数）

1. `Assets/Scripts/Telemetry/Telemetry.cs` 加一个静态方法，内部调 `stats.XXX(...)`；
2. `TelemetryLevelStats.cs` 里 `owner.Enqueue("trap_hit", new TelemetryFields().Str("scene", scene)...Build(), Telemetry.NowMs())`；
3. 游戏脚本里一处单行调用 `Telemetry.OnTrapHit(transform.position);`；
4. 测试：`test/stats.test.mjs` 插一条 fixture，断言接口里的数；
5. 看板：需要展示就加一列 / 一张卡，不需要就到此为止（数据已经进库了）。

`event_type` 只允许小写字母 + 数字 + 下划线（服务端 `TYPE_RE` 会校验），`metadata` 单条不超过 4 KB。

### 加一个指标

口径在 `src/stats.js`：SQL 放在文件上半部分的 `Q_*` 常量里，`mergeScenes()` 负责把行拼成每关一行，
路由在 `handleStats()`。看板在 `src/dashboard.html` 的 `COMPARE_ROWS`（对比表）或 KPI 区加一项，
然后**必须** `npm run build:dashboard`（`dashboard-page.js` 是生成物，改 HTML 不重新生成等于没改）。

### 性能与额度闸门（别随手拆掉）

| 闸门 | 值 | 为什么 |
| --- | --- | --- |
| 格子增量上报 | 每 60 秒一批、单批 ≤ 300 格 | 高频数据在客户端聚合，D1 写入量降到 1/600 |
| `route` 单条 | ≤ 300 格 | 一条 metadata 超 4 KB 服务端会整条拒收 |
| 死亡路线 | 少于 10 格不发 | 刚进关就摔死不值得占一行 |
| `/v1/stats/scene` 的 routes | 每个 outcome 各 20 条 | 一关死 47 次也不会把通关路线挤出去 |
| Worker 入库 | `env.DB.batch()` 每批 50 条 | D1 单次调用最多 50 条查询 |
| 一次看板刷新 | 7 条聚合 SQL | 免费额度每天 10 万次读 |

### 不要做的事

- 不要改 `Assets/Scripts/Flow/LevelLoader.cs`（GBK 编码，UTF-8 工具一写就乱）。
- 不要用 PowerShell 的 `Get-Content -Raw` + `Set-Content` 批量改这些含中文的源码（会按 GBK 解码、整篇乱码）。
- 不要把 `metadata` 拆成一个个数据库列——现在加字段零成本，拆了就得迁移。
- 不要在 Worker 里做重计算（免费版单次 CPU 10 ms），聚合交给 SQL。

### 出问题按这四层查

1. Unity Console 搜 `[Telemetry]`（`TelemetryConfig.verbose = true` 会打印每次上报结果）；
2. `curl https://<你的域名>/health`；
3. `curl https://<你的域名>/v1/stats/overview?since=0` 看有没有数；
4. 看板 ⚙ 里确认 API 地址和 `READ_KEY`，以及浏览器 F12 的网络面板。

## 七、坑

- `Assets/Scripts/Flow/LevelLoader.cs` 是 **GBK 编码**，别用 UTF-8 工具直接改，会乱码。
- Worker 里 `env.DB.batch()` 一次最多 50 条（代码里 `BATCH_CHUNK`），别改成几千。
- 客户端失败会整批进 spool 并 30 秒后重试；服务端 DB 异常返回 503 而不是 200，就是为了让客户端重试。
- 事件表只追加不更新，重复上报同一条会多一行——客户端同一批失败重试时可能重复，
  统计时用 `COUNT(DISTINCT ...)` 或按 `session_id` 去重即可。
- 看板默认没有密码。给测试者发链接前建议设 `READ_KEY`，否则链接被转发就等于数据公开。
- 清库要 `ADMIN_KEY`，没设就 403 —— 这是故意留的安全默认，别为了图方便把它写成空字符串放源码里。
- 背景图默认只在本机 IndexedDB，换浏览器/清站点数据就没了；点「上传到服务器」才会进 KV，那份是所有人的。
- `src/dashboard.html` 里有大量中文，**不要用 PowerShell 的 `Get-Content -Raw` + `Set-Content` 整篇替换**
  （会按 GBK 解码 UTF-8 再写回，整篇变乱码）。用编辑器的查找替换或 `edit` 工具改。
- 改了 `src/dashboard.html` 后必须 `npm run build:dashboard`，否则线上还是旧页面。
