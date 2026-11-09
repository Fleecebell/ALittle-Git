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
├─ queries.sql            9 条统计 SQL（热力图/死亡热点/停留/通关率…）
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
| `Assets/Scripts/Telemetry/TelemetryLevelStats.cs` | 单关统计：格子驻留 / 跳跃 / 死亡 / 时长 |
| `Assets/Scripts/Telemetry/TelemetryFields.cs` | 手写 JSON 构造（不依赖 Newtonsoft） |
| `Assets/Editor/TelemetryMenu.cs` | 菜单 `Tools/Telemetry`：开数据目录、发测试事件、清缓存 |

启动方式：`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 自动建常驻单例，
**没有改任何场景 / Prefab / ProjectSettings**，以后加新关卡不用管它。

埋点位置（一共 6 处，都是加调用，没有改原有逻辑）：

| 位置 | 事件 |
| --- | --- |
| `MoveFirst.cs` 跳跃执行块 | `OnJump` / `OnJumpFail`（按了没跳成也算一次，用来看判定苛刻的地方） |
| `DeathRespawnVFX.TriggerDeath()` | `OnDeath` → `death`（唯一死亡入口，此时还没被传送走，坐标是死亡点） |
| `Die.cs` 碰到危险物 | `NoteDeath("hazard:<物体名>")` 交代死因 |
| `CrushCheck.cs` 夹死 | `NoteDeath("crush")` |
| `MoveLate.cs` 手动按 R | `NoteDeath("manual_r")` |
| `NextLevel.GoToNextLevel()` | `OnLevelClear` → `level_clear` |

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
| 热力图 | 按 1×1 世界坐标格子铺色，六个指标可切：死亡 / 跳跃 / 跳跃失败 / 停留 / 发呆 / 跳跃失败率。鼠标悬停看该格全部数值；可叠一张关卡截图当背景（本地选图，不上传）；下方是“最热的 15 个格子”和本关概况 |
| 关卡对比 | 所有关卡按「每次进入的平均死亡」排序（比裸死亡数公平，排除“进得多所以死得多”），带通关率和跳跃失败率 |
| 死亡 | 死因分布、死亡最集中的格子、死亡流水（位置 / 活了多久 / 死前跳了几次 / 本关第几次死） |
| 时长与通关 | 单次游玩时长直方图 + 每次游玩明细 |
| 最近事件 | 最新 80 条原始事件，用来确认数据真的在进来 |
| 数据字典 | 每种事件的每个字段是什么意思（也就是“到底采集了哪些数据”） |

地址栏参数可以定位视图，方便把链接发给别人（比如“9101 的跳跃热力图”）：

```
/?scene=9101&tab=heatmap&metric=jumps&range=7d
```

只读接口（都是 `GET`）：

```
/v1/stats/overview?since=<毫秒时间戳>
/v1/stats/scene?scene=9101&since=<毫秒时间戳>
/v1/stats/recent?limit=50
```

`since` 就是看板右上角那个时间范围下拉框。

**给看板加个只读密码**（默认公开在公网，谁拿到 URL 都能看）：

```powershell
npx wrangler secret put READ_KEY
```

设了之后打开 `https://<你的域名>/?key=你设的密码`，密码会存进浏览器 localStorage，之后不用再输。
同样的方式设 `INGEST_KEY` 就能挡住伪造上报。

也可以绕过看板直接跑 SQL（`queries.sql` 里 9 条，都有中文注释）：

```powershell
npx wrangler d1 execute alittle-telemetry --remote --command="SELECT * FROM game_events LIMIT 5"
npx wrangler d1 execute alittle-telemetry --remote --file=./queries.sql   # 会按顺序全部输出
```

几条最常用的：

- **哪关死得多**：`GROUP BY scene, COUNT(*)` —— 一眼看出难度墙在哪关
- **死在哪**：`cell_x / cell_y` 分组，按格子画热力图；`cause` 分布告诉你是不是某个机关太狠
- **空格按得最多**：`cell` 事件里 `SUM(jumps)`，再算 `jump_fails / jumps` 得到“按了没跳成”的比例
- **在哪待最久**：`SUM(dwell_s)`，`idle_ratio` 区分“卡住发呆”和“慢慢摸索”
- **每关时长 / 通关率**：`level_start` 与 `level_clear` 对数

## 四、本地自测（不花额度）

```powershell
npm test                          # 35 个测试：上报校验 + 真 SQLite 跑 schema/queries + 统计接口
node tools/mock-receiver.mjs      # 本地假接收端，监听 127.0.0.1:8791
node tools/preview-dashboard.mjs  # 本地看板预览：假数据 2000+ 条，开 http://127.0.0.1:8792/
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

## 六、坑

- `Assets/Scripts/Flow/LevelLoader.cs` 是 **GBK 编码**，别用 UTF-8 工具直接改，会乱码。
- Worker 里 `env.DB.batch()` 一次最多 50 条（代码里 `BATCH_CHUNK`），别改成几千。
- 客户端失败会整批进 spool 并 30 秒后重试；服务端 DB 异常返回 503 而不是 200，就是为了让客户端重试。
- 事件表只追加不更新，重复上报同一条会多一行——客户端同一批失败重试时可能重复，
  统计时用 `COUNT(DISTINCT ...)` 或按 `session_id` 去重即可。
- 看板默认没有密码。给测试者发链接前建议设 `READ_KEY`，否则链接被转发就等于数据公开。
