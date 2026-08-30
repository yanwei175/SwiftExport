---
name: wpf-dotnet10-feature-page
description: SwiftExport.Client.Wpf 当前 .NET 10 WPF Feature 页面、ViewModel、Model、DataGrid 页面和页面命令组织规则。
---

# WPF .NET 10 Feature 页面 Skill

## 必读

- `AGENTS.md`
- `.agents/frontend.toml`
- `.agents/workflows/前端NET10工作流.md`
- `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`
- 页面涉及新增或复用 Contracts DTO 时，读 `.codex/skills/aspdotnet10/contracts-dto-models/SKILL.md` 及其 `references/DTO复用与文件组织门禁.md`
- `.agents/references/工程架构总纲.md`
- 目标 Feature 真实源码

## 目录结构

业务页面放在：

```text
SwiftExport.Client.Wpf/Features/{业务域}/{功能单元}/
  Views/
  ViewModels/
  Models/
  Services/
  Components/
```

规则：

- 不建立全项目级 `Views/ViewModels/Models` 大桶。
- `Views` 放 XAML 和 code-behind。
- `ViewModels` 放页面 ViewModel、Dialog ViewModel 和有独立业务语义的子 ViewModel。
- `Models` 放 LineModel、TreeItemModel、EditModel、SearchModel、StateModel。
- `Services` 放只服务当前功能单元的 Dialog Service、Loader、Adapter、Cache。
- `Components` 只放有独立业务语义并值得复用的子 View / 子 ViewModel。

## DTO 与页面模型边界

- 页面不是 DTO 的设计或复用边界；DTO 按 API 用例和契约语义设计，一个页面可以消费多个专用 DTO。
- 页面筛选、选中、展开、脏状态、验证提示等 UI 状态放 `SearchModel`、`EditModel`、`LineModel` 或 `StateModel`，不为页面方便塞入 Contracts DTO。
- 不在 ViewModel、Model 或 code-behind 文件中顺带声明一组公开传输 DTO。
- 跨页面或跨接口复用 DTO 必须满足 Contracts DTO 复用门禁；字段相似不能作为复用理由。

## 页面类型

管理列表页：

- 筛选区。
- 工具栏。
- 主表 DataGrid。
- 分页。
- 状态/结果提示。
- 新增、删除、保存、刷新等显式命令。

头行详情页：

- 头部表单。
- 明细 DataGrid。
- 汇总区。
- 保存/取消。
- 只读状态。

工作台页：

- 筛选。
- 批量选择。
- 执行业务命令。
- 结果反馈。

## DataGrid 规则

- 可编辑 DataGrid 不直接绑定 DTO。
- 绑定 LineModel / BaseListModel。
- 删除已持久化行进入删除池。
- 删除新增行可直接移除。
- 保存成功后清理脏状态。
- 行状态、错误状态、删除软视觉必须可见。

## 页面命令

- 保存、刷新、分页、关闭前检查未保存变更。
- 确认、取消确认、拆解、转采购、过账、反过账、红冲必须是显式命令。
- 危险命令必须有确认提示、Busy 保护和执行结果反馈。

## 禁止事项

- 禁止 ViewModel 直接访问 Repository、Controller、SQL。
- 禁止业务命令藏在属性 setter。
- 禁止机械拆分 XAML 为无业务语义的小 UserControl。
- 禁止把旧 VSTO/MVVM Light 规则带入当前客户端。
