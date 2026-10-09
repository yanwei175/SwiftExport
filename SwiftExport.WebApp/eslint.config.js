import js from "@eslint/js";
import tseslint from "typescript-eslint";
import hooks from "eslint-plugin-react-hooks";
import path from "node:path";

/** 按实际源文件位置约束单向依赖，兼容相对路径和框架别名。 */
const architectureRule = {
  meta: {
    type: "problem",
    schema: [],
    messages: {
      direction: "依赖越过架构边界：{{source}}。应用组装 → feature → shared。",
    },
  },
  create(context) {
    const filename = context.filename.replaceAll("\\", "/");
    const currentFeature = filename.match(/\/app\/features\/([^/]+)\//)?.[1];
    // 静态导入、转发导出和显式动态 import 使用同一边界，不允许经 barrel 绕行。
    const check = (node) => {
      const source = node.source?.value;
      if (typeof source !== "string") return;
      const resolved = source.startsWith("~")
        ? `/app/${source.slice(2)}`
        : source.startsWith(".")
          ? path.resolve(path.dirname(filename), source).replaceAll("\\", "/")
          : null;
      if (!resolved) return;
      const targetFeature = resolved.match(
        /\/app\/features\/([^/]+)(?:\/|$)/,
      )?.[1];
      const appAssembly =
        /\/app\/(workspace|routes|layouts|bootstrap|navigation)(?:\/|$)/.test(
          resolved,
        ) || /\/app\/(root|routes)(?:\.|$)/.test(resolved);
      if (
        (filename.includes("/app/shared/") && (targetFeature || appAssembly)) ||
        (currentFeature &&
          (appAssembly || (targetFeature && targetFeature !== currentFeature)))
      )
        context.report({ node, messageId: "direction", data: { source } });
    };
    return {
      ImportDeclaration: check,
      ExportNamedDeclaration: check,
      ExportAllDeclaration: check,
      ImportExpression: check,
    };
  },
};

export default tseslint.config(
  {
    ignores: [
      "node_modules/**",
      "build/**",
      ".react-router/**",
      "app/shared/api/generated/**",
      "test-results/**",
      "playwright-report/**",
      "openapi/**",
    ],
  },
  {
    files: ["**/*.{ts,tsx}"],
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    plugins: {
      "react-hooks": hooks,
      architecture: { rules: { direction: architectureRule } },
    },
    rules: {
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "error",
      "architecture/direction": "error",
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_" },
      ],
      "@typescript-eslint/no-explicit-any": "error",
    },
  },
);
