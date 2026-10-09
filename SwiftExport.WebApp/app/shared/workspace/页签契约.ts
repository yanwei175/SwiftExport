import type { ReactNode } from "react";

/** 页签描述只携带定位身份，业务草稿由各 feature 编辑会话保留。 */
export interface TabDescriptor {
  tabId: string;
  pageKind: string;
  title: string;
  identity: string;
  params: Readonly<{ id?: string; draft?: string }>;
}

/** 汇总全部需要保护的状态；业务 clean 不代表原始输入已经处理。 */
export interface ProtectionSummary {
  dirty: boolean;
  hasUncommittedInput: boolean;
  preparing: boolean;
  saving: boolean;
  unknown: boolean;
}

/** 保存返回值由 feature 核对真实回执后决定，宿主不解析业务 DTO。 */
export type SaveDecision = "saved" | "unsaved" | "failed" | "unknown";
/** 普通放弃不能释放正在准备、提交或结果未知的会话。 */
export type DiscardDecision = "discarded" | "cancelled" | "blocked";
/** 关闭查询只读取最新摘要，不隐式执行保存。 */
export interface CloseDecision {
  allowed: boolean;
  reason?: string;
}

/** 宿主按稳定 tabId 调用同一组最新会话入口。 */
export interface TabControl {
  getSummary(): ProtectionSummary;
  canClose(): CloseDecision;
  requestSave(): Promise<SaveDecision>;
  requestDiscard(): Promise<DiscardDecision>;
}

/** 公共打开意图由应用页面目录核对参数、权限和去重身份。 */
export interface OpenIntent {
  pageKind: string;
  params?: { id?: string; draft?: string };
}

/** 应用向 feature 注入展示边界，使编辑会话位于 Activity 外。 */
export interface WorkspaceTabProps {
  descriptor: TabDescriptor;
  isActive: boolean;
  renderDisplay(children: ReactNode): ReactNode;
}

/** 每页共享的窄宿主接口；不允许 feature 操作工作区内部状态。 */
export interface TabHost {
  registerControl(control: TabControl): () => void;
  reportSummary(summary: ProtectionSummary): void;
  ensureVisible(): Promise<void>;
  open(intent: OpenIntent): void;
  /** 仅在 feature 核对新增成功回执后更新实体身份；保留 tabId，冲突保留两页。 */
  updateBusinessIdentity(uid: string): boolean;
  closeAfterManualTermination(recordId: string): boolean;
}

/** 无输入变化、业务修改或提交锁时，普通关闭才可直接放行。 */
export function isUnprotected(summary: ProtectionSummary): boolean {
  return (
    !summary.dirty &&
    !summary.hasUncommittedInput &&
    !summary.preparing &&
    !summary.saving &&
    !summary.unknown
  );
}

/** 创建独立摘要，避免多个页签通过可写对象共享状态。 */
export function emptyProtection(): ProtectionSummary {
  return {
    dirty: false,
    hasUncommittedInput: false,
    preparing: false,
    saving: false,
    unknown: false,
  };
}
