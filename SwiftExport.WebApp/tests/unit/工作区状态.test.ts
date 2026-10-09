import { describe, expect, it } from "vitest";
import { workspaceReducer } from "../../app/workspace/工作区状态";
import {
  emptyProtection,
  isUnprotected,
} from "../../app/shared/workspace/页签契约";
import type { TabDescriptor } from "../../app/shared/workspace/页签契约";

const tab = (id: string, identity = id): TabDescriptor => ({
  tabId: id,
  identity,
  pageKind: "test",
  title: id,
  params: {},
});
describe("稳定工作区身份与完整输入保护", () => {
  it("新增成功只转换业务定位，原tabId和活动实例保持", () => {
    const draft = {
      ...tab("draft-tab", "test:draft-a"),
      params: { draft: "draft-a" },
    };
    const state = workspaceReducer(
      { tabs: [draft], activeId: draft.tabId },
      { type: "identity", id: draft.tabId, uid: "entity-a" },
    );
    expect(state.activeId).toBe("draft-tab");
    expect(state.tabs[0]).toEqual({
      ...draft,
      identity: "test:entity-a",
      params: { id: "entity-a" },
    });
    expect(
      workspaceReducer(state, {
        type: "open",
        tab: tab("new-instance", "test:entity-a"),
      }).tabs,
    ).toEqual(state.tabs);
  });
  it("实体身份冲突保留两页，不覆盖已有草稿或移除新单会话", () => {
    const state = {
      tabs: [
        tab("draft-tab", "test:draft-a"),
        tab("existing", "test:entity-a"),
      ],
      activeId: "draft-tab",
    };
    expect(
      workspaceReducer(state, {
        type: "identity",
        id: "draft-tab",
        uid: "entity-a",
      }),
    ).toBe(state);
    expect(
      workspaceReducer(state, {
        type: "identity",
        id: "draft-tab",
        uid: "invalid/id",
      }),
    ).toBe(state);
  });
  it("重复业务身份激活原实例，不替换tabId或参数", () => {
    const state = { tabs: [tab("a", "same")], activeId: null };
    expect(
      workspaceReducer(state, { type: "open", tab: tab("b", "same") }),
    ).toEqual({ tabs: state.tabs, activeId: "a" });
  });
  it("关闭活动页选邻近实例并允许工作区为空", () => {
    const state = workspaceReducer(
      { tabs: [tab("a"), tab("b")], activeId: "b" },
      { type: "remove", id: "b" },
    );
    expect(state.activeId).toBe("a");
    expect(
      workspaceReducer(state, { type: "remove", id: "a" }).activeId,
    ).toBeNull();
  });
  it.each([
    "dirty",
    "hasUncommittedInput",
    "preparing",
    "saving",
    "unknown",
  ] as const)("%s单独存在就阻止普通关闭", (key) => {
    expect(isUnprotected({ ...emptyProtection(), [key]: true })).toBe(false);
  });
});
