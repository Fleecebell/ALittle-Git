// 把 src/dashboard.html 内联成 src/dashboard-page.js（一个导出字符串的模块）。
// 这样做的好处：Worker 里 import 即可返回页面，不需要额外的静态资源绑定，
// 也不用往 wrangler.toml 加 rules，部署配置保持最小。
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const srcPath = join(root, "src", "dashboard.html");
const outPath = join(root, "src", "dashboard-page.js");

const html = readFileSync(srcPath, "utf8");
const lines = html.split("\n").map((line) => "  " + JSON.stringify(line));

const banner =
  "// 自动生成，不要手改。源码是 src/dashboard.html，改完运行 `npm run build:dashboard`。\n";
const body = "export default [\n" + lines.join(",\n") + '\n].join("\\n");\n';

writeFileSync(outPath, banner + body, "utf8");
console.log(
  `dashboard-page.js 已生成：${html.length} 字符 / ${lines.length} 行`
);
