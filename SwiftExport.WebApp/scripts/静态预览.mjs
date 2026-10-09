import http from "node:http";
import fs from "node:fs/promises";
import path from "node:path";

// 本地验收静态发布目录；API/文件和缺失资源始终保持404，不落入SPA首页。
const root = path.resolve("build/client");
const mime = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".json": "application/json",
};
const server = http.createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(
      new URL(request.url, "http://localhost").pathname,
    );
    if (/^\/(api|files)(\/|$)/.test(pathname)) {
      response.writeHead(404);
      response.end();
      return;
    }
    let target = path.resolve(root, `.${pathname}`);
    if (!target.startsWith(`${root}${path.sep}`) && target !== root) {
      response.writeHead(403);
      response.end();
      return;
    }
    if (!path.extname(target)) target = path.join(root, "index.html");
    const bytes = await fs.readFile(target);
    response.writeHead(200, {
      "Content-Type": mime[path.extname(target)] ?? "application/octet-stream",
      "Cache-Control": target.endsWith("index.html")
        ? "no-cache"
        : "public, max-age=31536000, immutable",
    });
    response.end(bytes);
  } catch {
    response.writeHead(404);
    response.end();
  }
});
server.listen(Number(process.env.PORT ?? 4174), "127.0.0.1", () =>
  process.stdout.write("静态预览已启动\n"),
);
