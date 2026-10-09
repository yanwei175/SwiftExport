import { ESLint } from "eslint";
import { expect, it } from "vitest";

/** 使用真实ESLint配置检测禁止导入；依赖冷加载给15秒预算，所有违例断言保持。 */
it("shared反向引用与feature跨模块/组装引用确实报错", async () => {
  const eslint = new ESLint();
  for (const [filePath, source] of [
    ["app/shared/违规.ts", "../features/auth/登录表单"],
    ["app/features/a/违规.ts", "../b/internal"],
    ["app/features/a/违规.ts", "../../workspace/工作区状态"],
  ]) {
    const results = await eslint.lintText(`import '${source}';`, { filePath });
    expect(
      results[0]?.messages.some(
        (message) => message.ruleId === "architecture/direction",
      ),
    ).toBe(true);
  }
}, 15000);

it("转发导出和动态导入不能绕过同一依赖边界，正常shared引用允许", async () => {
  const eslint = new ESLint();
  for (const code of [
    "export * from '~/features/auth/登录表单';",
    "export { LoginForm } from '~/features/auth/登录表单';",
    "void import('~/features/auth/登录表单');",
  ]) {
    const result = await eslint.lintText(code, {
      filePath: "app/shared/违规.ts",
    });
    expect(
      result[0]?.messages.some(
        (message) => message.ruleId === "architecture/direction",
      ),
    ).toBe(true);
  }
  const allowed = await eslint.lintText(
    "import '~/shared/workspace/页签契约';",
    {
      filePath: "app/features/auth/正常.ts",
    },
  );
  expect(
    allowed[0]?.messages.some(
      (message) => message.ruleId === "architecture/direction",
    ),
  ).toBe(false);
});
