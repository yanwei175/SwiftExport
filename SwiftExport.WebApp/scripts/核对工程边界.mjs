import fs from "node:fs/promises";
import path from "node:path";
// 检查大小写与UTF-8路径可移植性，避免Windows成功掩盖Linux导入失败。
const root = path.resolve("app");
const files = [];
async function walk(directory) {
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    const target = path.join(directory, entry.name);
    if (entry.isDirectory()) await walk(target);
    else files.push(target);
  }
}
await walk(root);
const known = new Set(files.map((file) => file.replaceAll("\\", "/")));
const folded = new Set();
for (const file of files) {
  const normalized = file.replaceAll("\\", "/");
  if (folded.has(normalized.toLowerCase()))
    throw new Error(`文件仅大小写不同：${normalized}`);
  folded.add(normalized.toLowerCase());
  if (!/\.(ts|tsx)$/.test(file)) continue;
  const source = new TextDecoder("utf-8", { fatal: true }).decode(
    await fs.readFile(file),
  );
  for (const match of source.matchAll(
    /(?:from\s*|import\s*\(\s*)["'](\.[^"']+)["']/g,
  )) {
    const target = path
      .resolve(path.dirname(file), match[1])
      .replaceAll("\\", "/");
    if (
      ![
        target,
        `${target}.ts`,
        `${target}.tsx`,
        `${target}.css`,
        `${target}/index.ts`,
      ].some((candidate) => known.has(candidate)) &&
      !match[1].includes("+types")
    )
      throw new Error(`导入路径大小写或目标不符：${file} → ${match[1]}`);
  }
}
process.stdout.write(
  `已核对 ${files.length} 个源码文件的 UTF-8 与路径大小写。\n`,
);
