import { afterEach, describe, expect, it, vi } from "vitest";
import { createElement } from "react";
import { renderToString } from "react-dom/server";
import {
  scheduleDebouncedSearch,
  useDebouncedSearch,
} from "./useDebouncedSearch";

afterEach(() => {
  vi.clearAllTimers();
  vi.useRealTimers();
});

/** 时间调度只证明防抖与取消，真实读取仍由未来 feature 的授权 Query 执行。 */
describe("远程搜索准备门槛", () => {
  it("完整等待300ms才发布本轮词，不能提前请求", () => {
    vi.useFakeTimers();
    const ready = vi.fn();
    const cancel = scheduleDebouncedSearch("当前词", true, ready);
    vi.advanceTimersByTime(299);
    expect(ready).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(ready).toHaveBeenCalledExactlyOnceWith("当前词");
    cancel();
  });
  it("新输入替换取消旧等待，只发布最后一次词", () => {
    vi.useFakeTimers();
    const ready = vi.fn();
    const cancelOld = scheduleDebouncedSearch("旧词", true, ready);
    vi.advanceTimersByTime(200);
    cancelOld();
    const cancelNew = scheduleDebouncedSearch("新词", true, ready);
    vi.advanceTimersByTime(100);
    expect(ready).not.toHaveBeenCalled();
    vi.advanceTimersByTime(200);
    expect(ready).toHaveBeenCalledExactlyOnceWith("新词");
    cancelNew();
  });
  it("隐藏或撤权清理等待，未授权时不创建新timer", () => {
    vi.useFakeTimers();
    const ready = vi.fn();
    const cancel = scheduleDebouncedSearch("未提交搜索", true, ready);
    vi.advanceTimersByTime(100);
    cancel();
    const cancelDisabled = scheduleDebouncedSearch("隐藏或失权", false, ready);
    expect(vi.getTimerCount()).toBe(0);
    vi.advanceTimersByTime(500);
    expect(ready).not.toHaveBeenCalled();
    cancelDisabled();
  });
  it("SSR初次呈现未就绪，Effect不在服务器启动timer", () => {
    vi.useFakeTimers();
    /** 服务端只投影准备状态，不读取浏览器身份或发查询。 */
    function Probe() {
      const result = useDebouncedSearch("服务端初始词", true);
      return createElement(
        "span",
        { "data-ready": String(result.isReady) },
        result.value,
      );
    }
    const markup = renderToString(createElement(Probe));
    expect(markup).toContain('data-ready="false"');
    expect(markup).toContain("服务端初始词");
    expect(vi.getTimerCount()).toBe(0);
  });
});
