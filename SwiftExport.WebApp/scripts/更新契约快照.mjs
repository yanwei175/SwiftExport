import { readFile, writeFile } from "node:fs/promises";
import assert from "node:assert/strict";
import {
  generatePermissionKeys,
  projectPermissionKeys,
} from "./权限键投影.mjs";

// 仅更新已审核清单，保留真实 schema/security；变更仍须审阅实际 diff。
const flags = new Map();
for (let i = 2; i < process.argv.length; i += 2)
  flags.set(process.argv[i], process.argv[i + 1]);
assert.ok(
  flags.get("--openapi") && flags.get("--permissions"),
  "使用 --openapi <导出文件> --permissions <目录文件>",
);
const document = JSON.parse(await readFile(flags.get("--openapi"), "utf8"));
const permissions = JSON.parse(
  await readFile(flags.get("--permissions"), "utf8"),
);
projectPermissionKeys(permissions);
const selected = JSON.parse(
  await readFile(
    new URL("../openapi/已审核端点.json", import.meta.url),
    "utf8",
  ),
);
const paths = {};
for (const [path, method, operationId, idSource, responseSource] of selected) {
  const operation = document.paths?.[path]?.[method];
  assert.ok(operation, `缺少 ${method} ${path}`);
  // 未命名管理端点只补审核后的客户端方法名；已有后端 ID 不能静默覆盖。
  // 路径、DTO、响应、认证描述仍全部来自真实 Swagger，不补造后端能力。
  assert.ok(idSource === undefined || idSource === "web-alias", "未知 ID 来源");
  if (idSource === "web-alias") {
    assert.ok(!operation.operationId, `${path} 已有后端 ID，请重新审核清单`);
  } else assert.equal(operation.operationId, operationId);
  assert.ok(
    !Object.values(paths).some((methods) =>
      Object.values(methods).some((item) => item.operationId === operationId),
    ),
    `重复操作 ID：${operationId}`,
  );
  assert.ok(
    operation.responses?.[
      operationId === "logoutBrowserSession" ? "204" : "200"
    ],
    `${operationId} 缺少成功响应`,
  );
  assert.ok(
    responseSource === undefined || responseSource === "response-untyped",
    "未知响应来源",
  );
  // 明确审核无响应 schema 的现有账号端点：保留真实缺口，feature 运行时核对未知正文。
  if (responseSource === "response-untyped") {
    assert.ok(
      !operation.responses["200"].content,
      `${path} 已有响应描述，请重新审核`,
    );
  } else if (operationId !== "logoutBrowserSession")
    assert.ok(operation.responses["200"].content?.["application/json"]?.schema);
  paths[path] = {
    ...paths[path],
    [method]:
      idSource === "web-alias" ? { ...operation, operationId } : operation,
  };
}
const schemas = {};
// 引用闭包只能来自实际导出；悬空或未知引用立即停止，不能手造 DTO。
function collect(value) {
  if (value === null || typeof value !== "object") return;
  if (typeof value.$ref === "string") {
    assert.ok(
      value.$ref.startsWith("#/components/schemas/"),
      `未知引用 ${value.$ref}`,
    );
    const name = value.$ref.slice("#/components/schemas/".length);
    if (!(name in schemas)) {
      assert.ok(document.components?.schemas?.[name], `悬空引用 ${name}`);
      schemas[name] = document.components.schemas[name];
      collect(schemas[name]);
    }
  }
  for (const child of Object.values(value)) collect(child);
}
collect(paths);
const usedSecurity = new Set(
  Object.values(paths).flatMap((methods) =>
    Object.values(methods).flatMap((operation) =>
      (operation.security ?? []).flatMap(Object.keys),
    ),
  ),
);
const securitySchemes = {};
for (const name of usedSecurity) {
  assert.ok(
    document.components?.securitySchemes?.[name],
    `缺少认证描述 ${name}`,
  );
  securitySchemes[name] = document.components.securitySchemes[name];
}
await writeFile(
  new URL("../openapi/首批接口.json", import.meta.url),
  JSON.stringify(
    {
      openapi: document.openapi,
      info: document.info,
      paths,
      components: { schemas, securitySchemes },
    },
    null,
    2,
  ) + "\n",
);
await writeFile(
  new URL("../openapi/权限目录.json", import.meta.url),
  JSON.stringify({ codes: [...permissions.codes].sort() }, null, 2) + "\n",
);
await generatePermissionKeys();
process.stdout.write(
  `已从真实导出更新 ${selected.length} 个审核端点与 ${permissions.codes.length} 个权限键；请审阅差异并运行 generate/typecheck。\n`,
);
