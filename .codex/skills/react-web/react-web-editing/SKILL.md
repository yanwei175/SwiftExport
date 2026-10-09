---
name: react-web-editing
description: SwiftExport Web 编辑与批量保存。用于 Form/Grid 当前值、baseline、dirty、未提交单元格缓冲、冻结请求和原子或行级回执接纳；不负责 Workspace 内部 reducer 或后端事务。
---

# Web 编辑与批量保存

## 输入与范围

读取当前字段契约、真实保存端点/回执、feature 编辑会话、Form/Grid/集合实现及相关测试。写入限授权 feature、shared/editing、shared/ui 的必需编辑能力及测试，公共文件归属按工作流明确；不能因修前端保存改后端语义。

正文规则：[WEB-EDIT-01/02](../../../../docs/Web客户端架构与脚手架决策.md#web-field-owner)、[完整保护摘要](../../../../docs/Web多标签工作区设计.md#workspace-contract)、[保存与结果分类](../../../../docs/Web多标签工作区设计.md#workspace-save)。集合算法细则参考已同步的 [编辑基础设施](../../../../docs/web-frontend-architecture.md)。

## 实施与复用入口

1. 搜索集合 reducer/hook、Grid 编辑事件与 feature builder；为表单、批量列表、头行分别标明唯一当前值 owner。baseline 与当前值分开，增改删从同一状态推导，避免多份双向同步。
2. 逐个检查尚未失焦、无效/未完成输入的 raw buffer、提交/取消和保护注册。把控件缓冲变化报告给该 tabId 的摘要，保留隐藏输入；业务 dirty 不替代输入保护。恢复原值/取消只能解除相应保护。
3. 保存调用同一 feature 入口，复用 commit-active-editor、字段验证和 freeze；失败不发请求并保留输入。准备至接纳按契约锁编辑，其他页可操作；保存前重新核操作权限。
4. 构建请求用 baseline Uid/Version、稳定 ClientKey 和可写字段投影；RowIndex 仅定位。冻结范围与回执逐项关联，完整核对 operation/身份/版本/计数/遗漏重复，再按实际端点契约接纳。
5. 按 A/B/C 处理锁及草稿；已确认部分成功只接纳对应行，未确认行仍受限制。Product 真实写入先满足 [独立后端门槛](../../../../docs/Web首批接口契约验收.md#product-recovery)，不把普通 BatchSave 自动升级为 CommandId 命令。
6. 复用 shared/format 的数字/日期转换、shared/editing 的字段错误定位、SwiftDataGrid custom editor 与候选控件；远程读取由 feature Query 管理。头 Form + 行集合先参考现有头行验收会话的统一校验/冻结，不能复制一套长期头字段当前值。公共设施边界只引用 [架构基线](../../../../docs/Web客户端架构与脚手架决策.md#web-common-services)。

## 禁止事项

Grid 不调 API/拼 BatchSave/提高 Version；不把缺失页数据当删除、不由树渲染猜级联、不一次 accept 全部再补回失败行、不在结果未知时 reject/reset。普通放弃只经 Workspace 公共契约；人工终止交 [workspace](../react-web-workspace/SKILL.md)。

## 验收

按 [架构规则矩阵](../../../../docs/Web客户端架构与脚手架决策.md#web-rule-checks) 选择纯状态与浏览器场景：修改再还原、新增再删、删除恢复、baseline 无别名、ClientKey/Uid/NewVersion 对齐；首改未失焦关闭、无效输入无请求、隐藏后缓冲保护、准备/保存只读及三类结果。记录真实证据，不能只测试 reducer dirty 后就声称缓冲关闭保护通过。
