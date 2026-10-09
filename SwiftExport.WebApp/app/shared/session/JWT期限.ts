/** 只读取已由服务器核实的 JWT 到期声明用于界面锁定；不替代服务器签名验证。 */
export function readJwtExpiresAt(token: string): number {
  try {
    const parts = token.split(".");
    if (parts.length !== 3) throw new Error("JWT 结构错误");
    const encoded = parts[1]!.replace(/-/g, "+").replace(/_/g, "/");
    const claims: unknown = JSON.parse(
      atob(encoded.padEnd(Math.ceil(encoded.length / 4) * 4, "=")),
    );
    const exp = (claims as { exp?: unknown }).exp;
    if (typeof exp !== "number" || !Number.isSafeInteger(exp) || exp <= 0)
      throw new Error("JWT exp 无效");
    return exp * 1000;
  } catch {
    throw new Error("登录响应缺少有效 JWT 到期时间，不能开放工作区。");
  }
}
