import { expect } from "@playwright/test";
import type { Page } from "@playwright/test";

/** 仅浏览器测试使用的 JWT 形状；服务端签名验证仍由隔离后端宿主验收。 */
export function mockJwtToken(subject = "tester", seconds = 43_200): string {
  return [
    Buffer.from('{"alg":"HS256","typ":"JWT"}').toString("base64url"),
    Buffer.from(
      JSON.stringify({
        sub: subject,
        exp: Math.floor(Date.now() / 1000) + seconds,
      }),
    ).toString("base64url"),
    "test-signature-only",
  ].join(".");
}

/** 给既有页面模拟补浏览器协议，保留其登录/me的原有身份与请求断言。 */
export async function installBrowserSessionProtocol(
  page: Page,
  token: string,
): Promise<void> {
  let restorable = false;
  page.on("request", (request) => {
    if (
      new URL(request.url()).pathname === "/api/auth/login" &&
      request.method() === "POST"
    )
      restorable = true;
  });
  await page.route("**/api/auth/browser-session/*", async (route) => {
    const path = new URL(route.request().url()).pathname;
    expect(route.request().headers().authorization).toBeUndefined();
    if (path.endsWith("/csrf"))
      await route.fulfill({ json: { requestToken: "csrf-test-only" } });
    else if (path.endsWith("/restore"))
      await route.fulfill({
        status: restorable ? 200 : 401,
        json: restorable
          ? { access_token: token }
          : { code: "UNAUTHENTICATED" },
      });
    else if (path.endsWith("/logout")) {
      restorable = false;
      await route.fulfill({ status: 204 });
    } else await route.abort("blockedbyclient");
  });
}

/** 明确认证端点白名单，其他访问仍属于业务请求，不能被测试放过。 */
export const authenticationCalls = [
  "POST /api/auth/login",
  "GET /api/auth/me",
  "GET /api/auth/browser-session/csrf",
  "GET /api/auth/browser-session/restore",
  "POST /api/auth/browser-session/logout",
];

/** 新验收使用真实浏览器 Cookie 行为，HTTP 返回仍为精确路径模拟。 */
export async function mockBrowserSession(
  page: Page,
  options: { userId?: string; userName?: string; permissions?: string[] } = {},
) {
  const fixture = {
    identity: {
      userId: options.userId ?? "user-a",
      userName: options.userName ?? "tester",
      nickName: "验收用户",
      permissions: options.permissions ?? [
        "Products.Product.View",
        "Products.Product.Edit",
      ],
    },
    token: mockJwtToken(options.userId ?? "user-a"),
    meStatus: 200,
    restoreStatus: null as number | null,
    logoutStatus: 204,
    meGate: undefined as Promise<void> | undefined,
    apiCalls: [] as string[],
    errors: [] as string[],
  };
  page.on("pageerror", (error) => fixture.errors.push(error.message));
  page.on("request", (request) => {
    const path = new URL(request.url()).pathname;
    if (path.startsWith("/api/"))
      fixture.apiCalls.push(request.method() + " " + path);
  });
  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (path === "/api/auth/me") {
        await fixture.meGate;
        expect(request.headers().authorization).toBe("Bearer " + fixture.token);
        await route.fulfill({
          status: fixture.meStatus,
          json:
            fixture.meStatus === 200
              ? fixture.identity
              : { code: "UNAUTHENTICATED" },
        });
        return;
      }
      if (!authenticationCalls.includes(request.method() + " " + path)) {
        await route.abort("blockedbyclient");
        return;
      }
      expect(request.headers().authorization).toBeUndefined();
      if (path === "/api/auth/login") {
        expect(request.headers()["x-swiftexport-web-session"]).toBe("1");
        expect(request.headers()["x-csrf-token"]).toBe("csrf-test-only");
        const originalExpiry = JSON.parse(
          Buffer.from(fixture.token.split(".")[1]!, "base64url").toString(),
        ).exp as number;
        await route.fulfill({
          json: { access_token: fixture.token },
          headers: {
            "Set-Cookie":
              "SwiftExport.Web.Restore=" +
              fixture.token +
              "; Path=/api/auth/browser-session; HttpOnly; Secure; SameSite=Strict; Expires=" +
              new Date(originalExpiry * 1000).toUTCString(),
          },
        });
      } else if (path.endsWith("/csrf"))
        await route.fulfill({ json: { requestToken: "csrf-test-only" } });
      else if (path.endsWith("/restore")) {
        const present = (await request.allHeaders()).cookie?.includes(
          "SwiftExport.Web.Restore=" + fixture.token,
        );
        const status = fixture.restoreStatus ?? (present ? 200 : 401);
        await route.fulfill({
          status,
          json:
            status === 200
              ? { access_token: fixture.token }
              : { code: "UNAUTHENTICATED" },
        });
      } else if (path.endsWith("/logout"))
        await route.fulfill({
          status: fixture.logoutStatus,
          ...(fixture.logoutStatus === 204
            ? {
                headers: {
                  "Set-Cookie":
                    "SwiftExport.Web.Restore=; Path=/api/auth/browser-session; HttpOnly; Secure; SameSite=Strict; Expires=Thu, 01 Jan 1970 00:00:00 GMT",
                },
              }
            : { json: { code: "UNEXPECTED" } }),
        });
      else await route.abort("blockedbyclient");
    },
  );
  return fixture;
}
