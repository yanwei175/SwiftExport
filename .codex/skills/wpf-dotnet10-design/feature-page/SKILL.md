---
name: wpf-dotnet10-feature-page
description: 新增或改变SwiftExport WPF页面组成、页面命令或Grid页面交互边界；局部模型实现不自动升级为整页设计。
---

# WPF .NET 10 Feature 页面 Skill

## 必读

公共启动沿用 `AGENTS.md`；以下仅补齐当前业务任务尚未读取的依赖，不回溯已读入口。

- 本次改变ViewModel/Model/Command时读 `../communitytoolkit-mvvm/SKILL.md`；纯视觉修正不因此重读MVVM资料
- 涉及新增、拆分或合并 View、code-behind、ViewModel、Model、Service、Component 时，读 `.codex/skills/wpf-dotnet10-design/file-organization/SKILL.md`
- 页面涉及 ViewModel/API/Feature Service 职责、权限、普通表单验证、导航参数、Document Tab identity 或 Dialog 返回协议时，读 `.codex/skills/wpf-dotnet10-design/page-architecture/SKILL.md`
- 页面涉及异步加载、搜索防抖、保存/业务命令、Busy、导航/关闭、错误反馈或资源释放时，读 `.codex/skills/wpf-dotnet10-design/runtime-safety/SKILL.md`
- 页面涉及测试证据、可访问性、本地化/文化格式、性能、日志/敏感信息、缓存或文件导入导出时，读 `.codex/skills/wpf-dotnet10-design/quality-gates/SKILL.md`
- 页面涉及新增或复用 Contracts DTO 时，读 `.codex/skills/backend/index/references/契约与DTO规定.md`
- 改变模块职责、状态/数量、页面/API/DTO契约，或有相关偏差证据时读取命中地图章节；未改变的既有结论复用
- 分层/公共依赖变化或边界不明时读工程总纲相关章节；新增或重构页面切片再按前端工作流展开设计
- 目标 Feature 真实源码

## 页面组成边界

- 本 Skill 负责页面类型、DataGrid 行为、页面命令和交互组成，不定义物理目录或文件拆分。
- View、code-behind、ViewModel、Model、Service 和 Component 的 owner、目录与拆分方式只服从 `../file-organization/SKILL.md`。
- Refit 与 Feature Service 的选择、权限、验证、导航和 Dialog 协议只服从 `../page-architecture/SKILL.md`。

## DTO 与页面模型边界

- 页面职责、禁止职责、入口关系以及 API/DTO/Model 划分必须符合目标模块能力地图。
- 页面不是 DTO 的设计或复用边界；DTO 按 API 用例和契约语义设计，一个页面可以消费多个专用 DTO。
- 页面筛选、选中、展开、脏状态、验证提示等 UI 状态放 `SearchModel`、`EditModel`、`LineModel` 或 `StateModel`，不为页面方便塞入 Contracts DTO。
- 不在 ViewModel、Model 或 code-behind 文件中顺带声明一组公开传输 DTO。
- 跨页面或跨接口复用 DTO 必须满足 Contracts DTO 复用门禁；字段相似不能作为复用理由。
- 文档差异与偏差按路由policy分类；当前事实不覆盖已确认目标，无关历史偏差不扩大任务，不从退役偏差ID恢复旧计划。

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
- 查询、初始化、写命令和延迟 UI 工作按运行时语义分类；页面离开后旧请求不得回写，进入/离开/重复进入和资源释放必须闭环。
- 确认、取消确认、拆解、转采购、过账、反过账、红冲必须是显式命令。
- 危险命令必须有确认提示、Busy 保护和执行结果反馈。

## 质量完成边界

- 页面改动按风险选择普通单元、STA/WPF、契约、构建和人工验收证据；新交互检查键盘、焦点、Automation 与高对比度。
- 文案和格式遵守资源与文化边界；UI 线程不执行同步 I/O，大集合不关闭虚拟化或缓存容器。
- 日志、缓存、导入导出遵守脱敏、隔离/失效、权限、公式注入、编码、取消和清理门禁。

## 禁止事项

- 禁止 ViewModel 直接访问 Repository、Controller、SQL。
- 禁止业务命令藏在属性 setter。
- 禁止把旧 VSTO/MVVM Light 规则带入当前客户端。
