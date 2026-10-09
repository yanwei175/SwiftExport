import { readFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { generatePermissionKeys } from "./权限键投影.mjs";

// 重新生成后逐字节比较；发现漂移立即失败，不替人工接受生成差异。
const project = fileURLToPath(new URL("../", import.meta.url));
await generatePermissionKeys(true);
const target = new URL(
  "../app/shared/api/generated/client.ts",
  import.meta.url,
);
const before = await readFile(target);
const packageRoot = new URL("../node_modules/orval/", import.meta.url);
const packageInfo = JSON.parse(
  await readFile(new URL("package.json", packageRoot), "utf8"),
);
const executable = fileURLToPath(new URL(packageInfo.bin.orval, packageRoot));
const result = spawnSync(
  process.execPath,
  [executable, "--config", "orval.config.mjs"],
  { cwd: project, stdio: "inherit" },
);
if (result.error) throw result.error;
if (result.status !== 0) process.exit(result.status ?? 1);
const after = await readFile(target);
if (!before.equals(after)) {
  console.error(
    "生成产物存在未解释差异；请审核 schema/配置变更，不能直接接受漂移。",
  );
  process.exit(1);
}
console.log(
  "生成产物一致：Orval 独立快照、统一 mutator、状态联合类型与路径编码。",
);
