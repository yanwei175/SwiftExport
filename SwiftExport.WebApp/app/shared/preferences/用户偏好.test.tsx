import { createElement } from "react";
import { renderToString } from "react-dom/server";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createUserPreferenceStore, resolveGridPreferences } from "./用户偏好";
import { UserPreferencesProvider, useUserPreferences } from "./用户偏好上下文";
import type { GridPreferences } from "./用户偏好";

/** 模拟仅本次测试的浏览器存储，不读取真实用户记录或系统凭据。 */
function browserStorage() {
  const entries = new Map<string, string>();
  const storage = {
    getItem: vi.fn((key: string) => entries.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => entries.set(key, value)),
    removeItem: vi.fn((key: string) => entries.delete(key)),
  };
  vi.stubGlobal("window", { localStorage: storage });
  return { entries, storage };
}

const columns = [
  { id: "name", defaultWidth: 180, hideable: false },
  { id: "quantity", defaultWidth: 120 },
];
const layout: GridPreferences = {
  columnOrder: ["quantity", "name"],
  hiddenColumnIds: [],
  columnWidths: { quantity: 160 },
  pageSize: 20,
};

afterEach(() => vi.unstubAllGlobals());

describe("类型明确的用户偏好", () => {
  it("创建与SSR渲染均不访问浏览器Storage，未核实身份只有默认内存值", () => {
    const { storage } = browserStorage();
    createUserPreferenceStore("user-a");
    function Probe() {
      const preferences = useUserPreferences();
      return createElement("span", null, String(preferences.sidebarCollapsed));
    }
    const html = renderToString(
      createElement(UserPreferencesProvider, {
        userId: "user-a",
        children: createElement(Probe),
      }),
    );
    expect(html).toContain("false");
    expect(storage.getItem).not.toHaveBeenCalled();
    const anonymous = createUserPreferenceStore(null);
    anonymous.load();
    anonymous.setSidebarCollapsed(true);
    expect(anonymous.getSnapshot().storageStatus).toBe("memory-only");
    expect(storage.getItem).not.toHaveBeenCalled();
    expect(storage.setItem).not.toHaveBeenCalled();
  });

  it("按稳定userId隔离、加载和恢复偏好，新身份默认值不会继承旧用户", () => {
    browserStorage();
    const first = createUserPreferenceStore("user-a");
    first.setSidebarCollapsed(true);
    first.setGridPreferences("products", layout, columns);
    const second = createUserPreferenceStore("user-b");
    expect(second.getSnapshot().sidebarCollapsed).toBe(false);
    second.load();
    expect(second.getSnapshot().grids).toEqual({});
    const restored = createUserPreferenceStore("user-a");
    expect(restored.getSnapshot().sidebarCollapsed).toBe(false);
    restored.load();
    expect(restored.getSnapshot()).toMatchObject({
      sidebarCollapsed: true,
      storageStatus: "persistent",
      grids: { products: { pageSize: 20, columnOrder: ["quantity", "name"] } },
    });
  });

  it("列布局只接受当前声明列，追加新列且不能隐藏受保护列或所有数据列", () => {
    const input = {
      ...layout,
      columnOrder: ["obsolete", "quantity", "quantity"],
      hiddenColumnIds: ["obsolete", "name", "quantity"],
      columnWidths: { obsolete: 123, name: Number.NaN, quantity: -1 },
    };
    const actual = resolveGridPreferences(input, columns);
    expect(actual).toEqual({
      columnOrder: ["quantity", "name"],
      hiddenColumnIds: ["quantity"],
      columnWidths: { name: 180, quantity: 120 },
      pageSize: 20,
    });
    expect(
      resolveGridPreferences(
        { ...layout, hiddenColumnIds: ["name", "quantity"] },
        columns.map((column) => ({ ...column, hideable: true })),
      ).hiddenColumnIds,
    ).toEqual(["name"]);
  });

  it("损坏或不兼容记录只清当前应用用户键，不清其他身份和应用", () => {
    const { entries, storage } = browserStorage();
    entries.set("swiftexport:web:prefs:v1:user-a", "broken-json");
    entries.set("swiftexport:web:prefs:v1:user-b", "other-user");
    entries.set("other-app", "other-app-value");
    const store = createUserPreferenceStore("user-a");
    store.load();
    expect(store.getSnapshot()).toMatchObject({
      sidebarCollapsed: false,
      grids: {},
      storageStatus: "persistent",
    });
    expect(storage.removeItem).toHaveBeenCalledExactlyOnceWith(
      "swiftexport:web:prefs:v1:user-a",
    );
    expect(entries.get("swiftexport:web:prefs:v1:user-b")).toBe("other-user");
    expect(entries.get("other-app")).toBe("other-app-value");
    entries.set(
      "swiftexport:web:prefs:v1:user-c",
      JSON.stringify({ schemaVersion: 2, sidebarCollapsed: true }),
    );
    const other = createUserPreferenceStore("user-c");
    other.load();
    expect(other.getSnapshot().sidebarCollapsed).toBe(false);
    expect(entries.has("swiftexport:web:prefs:v1:user-c")).toBe(false);
  });

  it("读取和写入忽略额外字段，分页非法时回退50", () => {
    const { entries } = browserStorage();
    entries.set(
      "swiftexport:web:prefs:v1:user-a",
      JSON.stringify({
        schemaVersion: 1,
        sidebarCollapsed: true,
        token: "sensitive",
        grids: {
          products: { ...layout, password: "sensitive", pageSize: 999 },
        },
      }),
    );
    const store = createUserPreferenceStore("user-a");
    store.load();
    expect(Object.keys(store.getSnapshot().grids)).toEqual(["products"]);
    expect(store.getSnapshot().grids.products!.pageSize).toBe(50);
    store.setSidebarCollapsed(false);
    expect(entries.get("swiftexport:web:prefs:v1:user-a")).not.toContain(
      "sensitive",
    );
  });

  it("Storage不可用和配额失败降级memory-only，继续保留用户当前操作", () => {
    const { storage } = browserStorage();
    storage.getItem.mockImplementation(() => {
      throw new Error("模拟禁用存储");
    });
    const store = createUserPreferenceStore("user-a");
    store.load();
    expect(store.getSnapshot().storageStatus).toBe("memory-only");
    storage.setItem.mockImplementation(() => {
      throw new Error("模拟配额不足");
    });
    store.setSidebarCollapsed(true);
    store.setGridPreferences("products", layout, columns);
    expect(store.getSnapshot()).toMatchObject({
      sidebarCollapsed: true,
      storageStatus: "memory-only",
      grids: { products: { pageSize: 20 } },
    });
  });

  it("重置只删除当前用户偏好并发布默认快照", () => {
    const { entries } = browserStorage();
    const store = createUserPreferenceStore("user-a");
    const listener = vi.fn();
    const stop = store.subscribe(listener);
    store.setSidebarCollapsed(true);
    store.setGridPreferences("products", layout, columns);
    store.resetPreferences();
    expect(entries.has("swiftexport:web:prefs:v1:user-a")).toBe(false);
    expect(store.getSnapshot()).toMatchObject({
      sidebarCollapsed: false,
      grids: {},
    });
    expect(listener).toHaveBeenCalled();
    stop();
  });
});
