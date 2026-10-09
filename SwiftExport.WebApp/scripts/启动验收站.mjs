import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import path from "node:path";
const require = createRequire(import.meta.url);
// 调用真实框架开发服务器，不依赖 PowerShell/cmd 或平台专属命令。
const packagePath = require.resolve("@react-router/dev/package.json");
const cli = path.join(
  path.dirname(packagePath),
  require(packagePath).bin["react-router"],
);
const child = spawn(
  process.execPath,
  [cli, "dev", "--host", "localhost", "--port", "4173", "--strictPort"],
  {
    stdio: "inherit",
    env: { ...process.env, VITE_ENABLE_WORKSPACE_PROBES: "true" },
  },
);
for (const signal of ["SIGINT", "SIGTERM"])
  process.on(signal, () => child.kill(signal));
child.on("exit", (code) => process.exit(code ?? 1));
