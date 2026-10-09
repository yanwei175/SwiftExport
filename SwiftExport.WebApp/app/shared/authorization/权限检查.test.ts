import { describe, expect, it } from "vitest";
import { hasPermission } from "./权限检查";

describe("当前用户的精确权限投影", () => {
  it("权限精确匹配，查看不推导编辑或删除", () => {
    const user = { userId: "user", permissions: ["Products.Product.View"] };
    expect(hasPermission(user, "Products.Product.View")).toBe(true);
    expect(hasPermission(user, "products.product.view")).toBe(false);
    expect(hasPermission(user, "Products.Product.Edit")).toBe(false);
    expect(hasPermission(user, "Products.Product.Delete")).toBe(false);
  });

  it("角色名称及管理员标志不代替服务端已知权限集合", () => {
    expect(
      hasPermission(
        { isAdmin: true, roles: ["Admin"], permissions: [] },
        "System.Users.View",
      ),
    ).toBe(false);
    expect(
      hasPermission({ roleCodes: ["SYSTEM_ADMIN"] }, "Products.Product.View"),
    ).toBe(false);
  });

  it("匿名、空集合和空键均不放行", () => {
    expect(hasPermission(null, "Products.Product.View")).toBe(false);
    expect(hasPermission({ permissions: null }, "Products.Product.View")).toBe(
      false,
    );
    expect(hasPermission({ permissions: [""] }, "")).toBe(false);
    expect(
      hasPermission(
        { permissions: ["Unknown.Module.View"] },
        "Unknown.Module.View",
      ),
    ).toBe(false);
  });
});
