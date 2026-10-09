---
name: react-web-workspace
description: SwiftExport Web 多标签工作区。用于 pageKind/tabId/业务身份、Ant Tabs 与 Activity、完整输入保护、保存关闭协调、会话恢复和未知记录导出；不创建通用窗口引擎或控制器继承框架。
---

# Web 多标签工作区

## 输入与范围

输入是 [多标签设计](../../../../docs/Web多标签工作区设计.md)、实际 Workspace/page registry/TabFrame、feature 注册及会话调用链。写入限授权 app/workspace、shared/workspace、session 接线、feature 入口及相关测试；不操作业务 DTO 或后端事务。

## 实施与复用入口

1. 搜索现有描述/页面目录/open/activate/close 注册，保持 pageKind、tabId、business identity 分离；URL 仅活动页定位，所有 feature 使用自身 descriptor。
2. 按 WEB-TAB-01 放置控制器、保存及保护注册的生命周期；Activity 仅包展示，隐藏不能卸载会话、清 raw buffer 或注销保护。外部查询受 isActive 与权限控制。
3. 用 [公共契约](../../../../docs/Web多标签工作区设计.md#workspace-contract) 的最新完整摘要实现 canClose/requestSave/requestDiscard；关闭当前/其他/全部及数据替换/离开/主动登出同用此语义。saved/discarded 后再次判定，不能以 dirty=false 或 callback 已结束移除实例。
4. 隐藏目标保存先激活，等待显式展示就绪握手再走 feature 唯一保存入口；无效输入定位原页，失败/取消/未知停止批量关闭。回执只更新发起会话，不写活动标签。
5. 401/到期保留同用户会话并遮蔽，按 [会话规则](../../../../docs/Web多标签工作区设计.md#workspace-session) 恢复原 JWT 后重读 me；权限撤销影响已打开页，换用户先通过完整离开与退出确认再隔离缓存/草稿。恢复失败、退出未确认提供重试，Cookie 恢复不等于草稿持久化；不再另选 token 存储方案。
6. 未知结果按 [WEB-TAB-04](../../../../docs/Web多标签工作区设计.md#workspace-recovery) 的真实后端能力恢复；无查询协议不伪造。授权主动导出核实记录，严格遵守文件/recordId、脱敏、当前权限和两步确认；失败/取消/未确认不返回 manualTerminated，批量关闭不触发人工终止。

## 禁止事项

不把 Workspace 变成 DTO 草稿仓库、不直接解析 Product BatchSave、不使 feature 调内部 reducer、不用关闭或 abort 表示服务器回滚。beforeunload 仅有限提醒，不能作为可靠关闭阻止或持久化。人工终止不转换 saved/clean，不自动 localStorage 或新增服务器恢复存储。

## 验收

对实际组件执行多标签文档第 9 节中本次适用矩阵，重点检查首改未失焦、隐藏关闭、全部关闭取消、最新摘要复核、回执归属、Activity 清理和权限变化；恢复功能再覆盖导出失败/取消/无权限/未确认及正常人工终止。静态引用或两个伪页面通过不能冒充真实业务恢复已验收。
