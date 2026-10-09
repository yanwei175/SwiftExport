import { readFile, writeFile, mkdir } from "node:fs/promises";
import assert from "node:assert/strict";
import { format } from "prettier";

// 权限目录只消费后端权威投影；本脚本不推测角色，也不授予任何权限。
export function projectPermissionKeys(snapshot) {
  assert.ok(Array.isArray(snapshot.codes), "权限快照缺少 codes");
  assert.ok(snapshot.codes.length > 0, "权限目录不能为空");
  assert.ok(
    snapshot.codes.every(
      (code) =>
        typeof code === "string" &&
        /^[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)+$/.test(code),
    ),
    "权限键格式错误",
  );
  assert.equal(
    new Set(snapshot.codes).size,
    snapshot.codes.length,
    "权限目录存在重复键",
  );
  const codes = [...snapshot.codes].sort();
  return (
    "/** 后端 PermissionCatalog.All 的机器投影；由契约管线生成，不手工维护。 */\nexport const permissionKeys = " +
    JSON.stringify(codes, null, 2) +
    " as const;\n\n/** Web 可声明的真实权限键；权限授予仍来自 me。 */\nexport type PermissionKey = (typeof permissionKeys)[number];\n"
  );
}

// 生成或核对同一份 TS 常量；Web 自带快照，独立构建不需要 .NET 工程。
export async function generatePermissionKeys(check = false) {
  const snapshot = JSON.parse(
    await readFile(
      new URL("../openapi/权限目录.json", import.meta.url),
      "utf8",
    ),
  );
  const target = new URL(
    "../app/shared/authorization/generated/权限键.ts",
    import.meta.url,
  );
  const expected = await format(projectPermissionKeys(snapshot), {
    parser: "typescript",
  });
  if (check)
    assert.equal(
      await readFile(target, "utf8"),
      expected,
      "权限键生成产物漂移",
    );
  else {
    await mkdir(
      new URL("../app/shared/authorization/generated/", import.meta.url),
      { recursive: true },
    );
    await writeFile(target, expected, "utf8");
  }
}
