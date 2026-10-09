import { describe, expect, it } from "vitest";
import { readJwtExpiresAt } from "./JWT期限";

/** 形状仅供期限解析测试，固定签名不代表服务器已验签或真实登录凭据。 */
function token(claims: unknown): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
  return `test-header.${payload}.test-signature`;
}

describe("JWT期限声明", () => {
  it("按秒读取exp并返回毫秒，解析不代替服务器身份认证", () => {
    const exp = 1_792_000_000;
    expect(readJwtExpiresAt(token({ exp }))).toBe(exp * 1000);
  });

  it("过期声明仍可解析，由会话核对当前时间", () => {
    expect(readJwtExpiresAt(token({ exp: 1 }))).toBe(1000);
  });

  it.each(
    [
      {},
      { exp: "1792000000" },
      { exp: null },
      { exp: 0 },
      { exp: -1 },
      { exp: 1.5 },
      { exp: Number.MAX_VALUE },
      null,
      [],
    ].map((claims) => ({ claims })),
  )("缺失或非法声明$claims统一拒绝，不暴露原文", ({ claims }) => {
    expect(() => readJwtExpiresAt(token(claims))).toThrow(
      "缺少有效 JWT 到期时间",
    );
  });

  it.each([
    "",
    "one-part",
    "two.parts",
    "a.b.c.d",
    "a.!invalid!.b",
    "a.bm90LWpzb24.b",
  ])("结构或编码非法时拒绝(%s)", (input) => {
    expect(() => readJwtExpiresAt(input)).toThrow("缺少有效 JWT 到期时间");
  });
});
