import { chromium, expect, test } from "@playwright/test";
import type { BrowserContext, Page, Route } from "@playwright/test";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import {
  authenticationCalls,
  mockBrowserSession,
  mockJwtToken,
} from "./会话模拟";

type SessionFixture = Awaited<ReturnType<typeof mockBrowserSession>>;
const observations = new WeakMap<BrowserContext, SessionFixture[]>();
const restorePath = "/api/auth/browser-session/restore";
const restoreCookieName = "SwiftExport.Web.Restore";

/** 仅使用精确认证 HTTP 模拟及真实 Chrome Cookie；不证明服务器签名或数据库行为。 */
async function initialize(page: Page): Promise<SessionFixture> {
  const fixture = await mockBrowserSession(page);
  const previous = observations.get(page.context()) ?? [];
  observations.set(page.context(), [...previous, fixture]);
  return fixture;
}

/** 通过实际 Ant Form 完成模拟登录，等待 me 核实后才观察工作区。 */
async function login(page: Page, userName = "tester"): Promise<void> {
  await page.getByLabel("用户名", { exact: true }).fill(userName);
  await page
    .getByLabel("密码", { exact: true })
    .fill("browser-session-test-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
}

/** 使用首期 HTTPS 地址进入匿名页面再登录；不调用真实账号或业务端点。 */
async function authenticated(page: Page): Promise<SessionFixture> {
  const fixture = await initialize(page);
  await page.goto("/");
  await login(page);
  return fixture;
}

/** 只解析合成测试 JWT 的 exp 用于核对原期限；此解析不是服务器身份或签名验证。 */
function tokenExpiry(token: string): number {
  const payload = JSON.parse(
    Buffer.from(token.split(".")[1]!, "base64url").toString("utf8"),
  ) as { exp: number };
  expect(Number.isSafeInteger(payload.exp)).toBe(true);
  return payload.exp;
}

/** 从当前测试 Cookie 容器读取元数据，不读取用户浏览器或持久化文件。 */
async function recoveryCookie(context: BrowserContext) {
  const cookie = (
    await context.cookies("https://localhost:4173" + restorePath)
  ).find((item) => item.name === restoreCookieName);
  if (!cookie) throw new Error("当前测试上下文缺少恢复 Cookie。");
  return cookie;
}

/** 只清理本次创建的临时 Chrome profile，递归删除前核对绝对路径和目录归属。 */
async function removeTemporaryProfile(
  profile: string,
  temporaryRoot: string,
  profilePrefix: string,
): Promise<void> {
  const absoluteProfile = path.resolve(profile);
  if (
    absoluteProfile === temporaryRoot ||
    path.dirname(absoluteProfile) !== temporaryRoot ||
    !path.basename(absoluteProfile).startsWith(profilePrefix)
  ) {
    throw new Error("拒绝清理不属于本次测试的 Chrome profile。");
  }
  await rm(absoluteProfile, { recursive: true, force: true });
}

/** 显式门闩观察请求阶段，避免靠 sleep 猜测迟到回执。 */
function gate() {
  let resolve!: () => void;
  const promise = new Promise<void>((done) => {
    resolve = done;
  });
  return { promise, release: () => resolve() };
}

/** 核对已到达浏览器的模拟响应；HTTP 成功本身不等于身份已接纳。 */
async function fulfillLate(
  route: Route,
  json: unknown,
  status = 200,
): Promise<void> {
  // 真实 fetch 被退出取消时，浏览器请求已经失败，不再给已取消传输伪造响应。
  if (route.request().failure()) return;
  await route.fulfill({ status, json });
}

test.afterEach(async ({ context }) => {
  for (const fixture of observations.get(context) ?? []) {
    expect(fixture.errors).toEqual([]);
    expect(
      fixture.apiCalls.filter((call) => !authenticationCalls.includes(call)),
    ).toEqual([]);
  }
});

test.describe("原 JWT 浏览器恢复与退出（HTTP 模拟）", () => {
  test("刷新使用同一 Cookie 和原 Token，不重复密码登录或延长 exp", async ({
    page,
  }) => {
    const fixture = await authenticated(page);
    const before = await recoveryCookie(page.context());
    const loginCount = fixture.apiCalls.filter(
      (call) => call === "POST /api/auth/login",
    ).length;
    const restoreResponse = page.waitForResponse(
      (response) => new URL(response.url()).pathname === restorePath,
    );
    await page.reload();
    const response = await restoreResponse;
    expect(await response.json()).toEqual({ access_token: fixture.token });
    expect((await response.allHeaders())["set-cookie"]).toBeUndefined();
    const headers = await response.request().allHeaders();
    expect(headers.authorization).toBeUndefined();
    expect(headers.cookie).toContain(restoreCookieName + "=" + fixture.token);
    await expect(
      page.getByRole("button", { name: "退出登录", exact: true }),
    ).toBeVisible();
    expect(
      fixture.apiCalls.filter((call) => call === "POST /api/auth/login"),
    ).toHaveLength(loginCount);
    const after = await recoveryCookie(page.context());
    expect(after.value).toBe(before.value);
    expect(after.expires).toBe(tokenExpiry(fixture.token));
    expect(after.expires).toBe(before.expires);
  });

  test("关闭 page 后同一浏览器上下文重开，从 Cookie 恢复原 Token", async ({
    page,
    context,
  }) => {
    const first = await authenticated(page);
    const before = await recoveryCookie(context);
    await page.close();
    const reopened = await context.newPage();
    const next = await initialize(reopened);
    next.token = first.token;
    await reopened.goto("/workspace");
    await expect(
      reopened.getByRole("button", { name: "退出登录", exact: true }),
    ).toBeVisible();
    expect(next.apiCalls).toEqual(["GET " + restorePath, "GET /api/auth/me"]);
    const after = await recoveryCookie(context);
    expect(after.value).toBe(first.token);
    expect(after.expires).toBe(before.expires);
  });

  test("关闭整个 Chrome 进程后使用同一临时 profile 重开，恢复原 Token 和期限", async () => {
    const temporaryRoot = path.resolve(tmpdir());
    const profilePrefix = "swiftexport-browser-session-";
    const profile = await mkdtemp(path.join(temporaryRoot, profilePrefix));
    let activeContext: BrowserContext | undefined;
    try {
      // profile 仅属于此测试；真实 Chrome 自行保留 Cookie，应用没有新增磁盘存储机制。
      const launchOptions = {
        channel: "chrome",
        headless: true,
        baseURL: "https://localhost:4173",
        ignoreHTTPSErrors: false,
      };
      activeContext = await chromium.launchPersistentContext(
        profile,
        launchOptions,
      );
      const firstPage =
        activeContext.pages()[0] ?? (await activeContext.newPage());
      const first = await initialize(firstPage);
      await firstPage.goto("/");
      await login(firstPage);
      const originalToken = first.token;
      const originalIdentity = {
        ...first.identity,
        permissions: [...first.identity.permissions],
      };
      const before = await recoveryCookie(activeContext);
      expect(before.expires).toBe(tokenExpiry(originalToken));
      expect(first.errors).toEqual([]);
      expect(
        first.apiCalls.filter((call) => !authenticationCalls.includes(call)),
      ).toEqual([]);
      // 关闭持久上下文会关闭其整个浏览器；下一次启动不共享内存上下文或 JS 会话。
      await activeContext.close();
      activeContext = undefined;

      activeContext = await chromium.launchPersistentContext(
        profile,
        launchOptions,
      );
      const reopened =
        activeContext.pages()[0] ?? (await activeContext.newPage());
      const next = await initialize(reopened);
      next.token = originalToken;
      next.identity = originalIdentity;
      const restoreResponse = reopened.waitForResponse(
        (response) => new URL(response.url()).pathname === restorePath,
      );
      await reopened.goto("/workspace");
      const response = await restoreResponse;
      expect(await response.json()).toEqual({ access_token: originalToken });
      expect((await response.allHeaders())["set-cookie"]).toBeUndefined();
      const headers = await response.request().allHeaders();
      expect(headers.authorization).toBeUndefined();
      expect(headers.cookie).toContain(restoreCookieName + "=" + originalToken);
      await expect(
        reopened.getByRole("button", { name: "退出登录", exact: true }),
      ).toBeVisible();
      expect(next.apiCalls).toEqual(["GET " + restorePath, "GET /api/auth/me"]);
      expect(await recoveryCookie(activeContext)).toEqual(before);
      expect(next.errors).toEqual([]);
      // HTTP 仍由精确路由模拟，不将浏览器持久 Cookie 证据升级为真实服务器认证验收。
    } finally {
      await activeContext?.close();
      await removeTemporaryProfile(profile, temporaryRoot, profilePrefix);
    }
  });

  test("Cookie 保持 Secure/HttpOnly/Strict/host-only 和原 exp，JavaScript 不可读取", async ({
    page,
    context,
  }) => {
    const fixture = await authenticated(page);
    const cookie = await recoveryCookie(context);
    expect(cookie).toMatchObject({
      domain: "localhost",
      path: "/api/auth/browser-session",
      secure: true,
      httpOnly: true,
      sameSite: "Strict",
      value: fixture.token,
      expires: tokenExpiry(fixture.token),
    });
    expect(await page.evaluate(() => window.isSecureContext)).toBe(true);
    // 在 Cookie 作用路径内也不可读，避免仅凭 /workspace 的路径不匹配假证 HttpOnly。
    await page.goto(restorePath);
    expect(await page.evaluate(() => document.cookie)).not.toContain(
      restoreCookieName,
    );
  });

  test("退出按 csrf→204→restore401 核实，刷新后仍需密码登录", async ({
    page,
    context,
  }) => {
    const fixture = await authenticated(page);
    const before = fixture.apiCalls.length;
    await page.getByRole("button", { name: "退出登录", exact: true }).click();
    await expect(
      page.getByRole("button", { name: "登录", exact: true }),
    ).toBeVisible();
    expect(fixture.apiCalls.slice(before)).toEqual([
      "GET /api/auth/browser-session/csrf",
      "POST /api/auth/browser-session/logout",
      "GET " + restorePath,
    ]);
    expect(
      (await context.cookies()).some(
        (cookie) => cookie.name === restoreCookieName,
      ),
    ).toBe(false);
    await page.reload();
    await expect(
      page.getByRole("button", { name: "登录", exact: true }),
    ).toBeVisible();
    expect(
      fixture.apiCalls.filter((call) => call === "GET /api/auth/me"),
    ).toHaveLength(1);
    // 此证据仅为当前 Cookie 容器和模拟恢复不可用，不声称文件删除或服务器 JWT 撤销。
  });

  test("退出未收到204保持未确认，不允许换用户，主动重试可完成", async ({
    page,
  }) => {
    const fixture = await authenticated(page);
    fixture.logoutStatus = 503;
    await page.getByRole("button", { name: "退出登录", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "确认退出", exact: true });
    await expect(
      dialog.getByRole("button", { name: "重试退出", exact: true }),
    ).toBeVisible();
    await expect(page.getByLabel("用户名", { exact: true })).toHaveCount(0);
    expect((await recoveryCookie(page.context())).value).toBe(fixture.token);
    fixture.logoutStatus = 204;
    await dialog.getByRole("button", { name: "重试退出", exact: true }).click();
    await expect(
      page.getByRole("button", { name: "登录", exact: true }),
    ).toBeVisible();
  });

  test("退出响应丢失保留 Cookie 和未确认状态，不自动重发", async ({ page }) => {
    const fixture = await authenticated(page);
    let fail = true;
    await page.route("**/api/auth/browser-session/logout", (route) =>
      fail ? route.abort("failed") : route.fallback(),
    );
    await page.getByRole("button", { name: "退出登录", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "确认退出", exact: true });
    await expect(
      dialog.getByRole("button", { name: "重试退出", exact: true }),
    ).toBeVisible();
    expect(
      fixture.apiCalls.filter(
        (call) => call === "POST /api/auth/browser-session/logout",
      ),
    ).toHaveLength(1);
    expect((await recoveryCookie(page.context())).value).toBe(fixture.token);
    fail = false;
    await dialog.getByRole("button", { name: "重试退出", exact: true }).click();
    await expect(
      page.getByRole("button", { name: "登录", exact: true }),
    ).toBeVisible();
  });

  for (const outcome of ["200", "503", "network"] as const) {
    test(`退出204后核实${outcome}仍未确认且不接纳身份`, async ({ page }) => {
      const fixture = await authenticated(page);
      if (outcome !== "network") fixture.restoreStatus = Number(outcome);
      let fail = outcome === "network";
      await page.route("**/api/auth/browser-session/restore", (route) =>
        fail ? route.abort("failed") : route.fallback(),
      );
      await page.getByRole("button", { name: "退出登录", exact: true }).click();
      await expect(
        page.getByRole("button", { name: "重试退出", exact: true }),
      ).toBeVisible();
      await expect(
        page.getByRole("button", { name: "登录", exact: true }),
      ).toHaveCount(0);
      await expect(page.getByText("验收用户", { exact: true })).toHaveCount(0);
      expect(
        fixture.apiCalls.filter((call) => call === "GET /api/auth/me"),
      ).toHaveLength(1);
      fail = false;
      fixture.restoreStatus = null;
      await page.getByRole("button", { name: "重试退出", exact: true }).click();
      await expect(
        page.getByRole("button", { name: "登录", exact: true }),
      ).toBeVisible();
    });
  }

  test("退出204后的迟到恢复200只用于核实，不能恢复旧身份", async ({ page }) => {
    const fixture = await authenticated(page);
    const entered = gate();
    const release = gate();
    const completed = gate();
    await page.route("**/api/auth/browser-session/restore", async (route) => {
      expect(
        (await route.request().allHeaders()).authorization,
      ).toBeUndefined();
      entered.release();
      await release.promise;
      try {
        await fulfillLate(route, { access_token: fixture.token });
      } finally {
        completed.release();
      }
    });
    try {
      await page.getByRole("button", { name: "退出登录", exact: true }).click();
      await entered.promise;
      await expect(
        page.getByText("正在确认退出…", { exact: true }),
      ).toBeVisible();
      await expect(page.getByText("验收用户", { exact: true })).toHaveCount(0);
      release.release();
      await completed.promise;
      await expect(
        page.getByRole("button", { name: "重试退出", exact: true }),
      ).toBeVisible();
      expect(
        fixture.apiCalls.filter((call) => call === "GET /api/auth/me"),
      ).toHaveLength(1);
      await expect(
        page.getByRole("button", { name: "退出登录", exact: true }),
      ).toHaveCount(0);
    } finally {
      release.release();
    }
  });

  test("迟到退出失败保持未确认，等待期间及失败后均不能换用户", async ({
    page,
  }) => {
    const fixture = await authenticated(page);
    const entered = gate();
    const release = gate();
    await page.route("**/api/auth/browser-session/logout", async (route) => {
      entered.release();
      await release.promise;
      await fulfillLate(route, { code: "UNEXPECTED" }, 503);
    });
    try {
      await page.getByRole("button", { name: "退出登录", exact: true }).click();
      await entered.promise;
      await expect(
        page.getByRole("dialog", { name: "确认退出", exact: true }),
      ).toBeVisible();
      await expect(page.getByLabel("用户名", { exact: true })).toHaveCount(0);
      release.release();
      await expect(
        page.getByRole("button", { name: "重试退出", exact: true }),
      ).toBeVisible();
      await expect(page.getByLabel("用户名", { exact: true })).toHaveCount(0);
      expect((await recoveryCookie(page.context())).value).toBe(fixture.token);
      expect(
        fixture.apiCalls.filter((call) => call === "GET /api/auth/me"),
      ).toHaveLength(1);
    } finally {
      release.release();
    }
  });

  test("退出后登录新用户，退出前的迟到me不能恢复旧身份", async ({ page }) => {
    const fixture = await authenticated(page);
    const originalToken = fixture.token;
    const originalIdentity = {
      ...fixture.identity,
      permissions: [...fixture.identity.permissions],
    };
    const entered = gate();
    const release = gate();
    const completed = gate();
    let first = true;
    await page.route("**/api/auth/me", async (route) => {
      if (!first) {
        await route.fallback();
        return;
      }
      first = false;
      expect((await route.request().allHeaders()).authorization).toBe(
        "Bearer " + originalToken,
      );
      entered.release();
      await release.promise;
      try {
        await fulfillLate(route, originalIdentity);
      } finally {
        completed.release();
      }
    });
    try {
      await page.getByRole("button", { name: "刷新权限", exact: true }).click();
      await entered.promise;
      await page.getByRole("button", { name: "退出登录", exact: true }).click();
      await expect(
        page.getByRole("button", { name: "登录", exact: true }),
      ).toBeVisible();
      fixture.token = mockJwtToken("user-b");
      fixture.identity = {
        ...fixture.identity,
        userId: "user-b",
        userName: "second-user",
        nickName: "新用户乙",
      };
      await login(page, "second-user");
      await expect(page.getByText("新用户乙", { exact: true })).toBeVisible();
      release.release();
      await completed.promise;
      await expect(page.getByText("新用户乙", { exact: true })).toBeVisible();
      await expect(page.getByText("验收用户", { exact: true })).toHaveCount(0);
      await expect(
        page.getByRole("dialog", { name: "重新认证", exact: true }),
      ).toHaveCount(0);
    } finally {
      release.release();
    }
  });

  test("模拟恢复返回已过exp的Token也不能发me或开放工作区", async ({ page }) => {
    const fixture = await initialize(page);
    fixture.token = mockJwtToken("user-a", -1);
    fixture.restoreStatus = 200;
    await page.goto("/workspace");
    await expect(
      page.getByRole("button", { name: "登录", exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "重试恢复", exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "退出登录", exact: true }),
    ).toHaveCount(0);
    expect(fixture.apiCalls).toEqual(["GET " + restorePath]);
  });

  test("原exp到期进入重认证，不自动续签或再次密码登录", async ({ page }) => {
    await page.clock.install({ time: new Date() });
    const fixture = await initialize(page);
    fixture.token = mockJwtToken("user-a", 30);
    await page.goto("/");
    await login(page);
    await page.clock.fastForward(31_000);
    await expect(
      page.getByRole("dialog", { name: "重新认证", exact: true }),
    ).toBeVisible();
    expect(
      fixture.apiCalls.filter((call) => call === "POST /api/auth/login"),
    ).toHaveLength(1);
    expect(
      fixture.apiCalls.filter((call) => call === "GET /api/auth/me"),
    ).toHaveLength(1);
    expect(
      fixture.apiCalls.filter(
        (call) => call === "POST /api/auth/browser-session/logout",
      ),
    ).toHaveLength(0);
  });
});
