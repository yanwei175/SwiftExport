import { renderToPipeableStream } from "react-dom/server";
import { PassThrough, Readable } from "node:stream";
import { ServerRouter } from "react-router";
import type { EntryContext } from "react-router";

/** 与框架默认入口一致的序列化期限；不用于页面展示握手。 */
export const streamTimeout = 5000;

/** 仅供框架开发与 SPA 构建渲染根文档，发布产物无需运行此入口。 */
export default function handleRequest(
  request: Request,
  status: number,
  headers: Headers,
  context: EntryContext,
): Promise<Response> {
  if (request.method === "HEAD")
    return Promise.resolve(new Response(null, { status, headers }));
  return new Promise((resolve, reject) => {
    let timeout: ReturnType<typeof setTimeout> | undefined;
    const cleanup = () => {
      clearTimeout(timeout);
      timeout = undefined;
      request.signal.removeEventListener("abort", abort);
    };
    const { pipe, abort } = renderToPipeableStream(
      <ServerRouter context={context} url={request.url} />,
      {
        // SPA 静态渲染等全部根内容就绪，避免提前输出不完整的序列化流。
        onAllReady() {
          const body = new PassThrough();
          body.once("finish", cleanup);
          body.once("close", cleanup);
          headers.set("Content-Type", "text/html; charset=utf-8");
          resolve(
            new Response(Readable.toWeb(body) as ReadableStream<Uint8Array>, {
              status,
              headers,
            }),
          );
          pipe(body);
        },
        onShellError(error) {
          cleanup();
          reject(error);
        },
        onError() {
          status = 500;
        },
      },
    );
    // 按官方入口给序列化超时留一秒清理窗口；异常不能永久占用构建请求。
    timeout = setTimeout(abort, streamTimeout + 1000);
    request.signal.addEventListener("abort", abort, { once: true });
  });
}
