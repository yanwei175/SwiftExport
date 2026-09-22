---
name: wpf-dotnet10-feature-page
description: SwiftExport.Client.Wpf 当前 .NET 10 WPF Feature 页面、ViewModel、Model、DataGrid 页面和页面命令组织规则。
---

# WPF .NET 10 Feature 页面 Skill

## 必读

公共启动沿用 `AGENTS.md`；以下仅补齐当前业务任务尚未读取的依赖，不回溯已读入口。

- `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`
- 页面涉及 ViewModel/API/Feature Service 职责、权限、普通表单验证、导航参数、Document Tab identity 或 Dialog 返回协议时，读 `.codex/skills/wpf-dotnet10-design/page-architecture/SKILL.md`
- 页面涉及异步加载、搜索防抖、保存/业务命令、Busy、导航/关闭、错误反馈或资源释放时，读 `.codex/skills/wpf-dotnet10-design/runtime-safety/SKILL.md`
- 页面涉及测试证据、可访问性、本地化/文化格式、性能、日志/敏感信息、缓存或文件导入导出时，读 `.codex/skills/wpf-dotnet10-design/quality-gates/SKILL.md`
- 页面涉及新增或复用 Contracts DTO 时，读 `.codex/skills/aspdotnet10/contracts-dto-models/SKILL.md` 及 `.codex/skills/aspdotnet10/contracts-dto-models/references/DTO复用与文件组织门禁.md`
- 目标业务模块已有能力地图时必须读取；采购页面读 `.agents/references/模块能力地图/采购模块能力地图.md`
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
- 多接口编排、跨调用协调或结果归一化的 Feature Service 放当前功能单元 `Services`；简单窄 API 不为形式统一强制包装。
- 页面打开参数、Dialog 输入和返回结果等仅属于客户端协作的窄类型放当前 Feature 的 `Models`，一个公开顶级类型一个文件；只有跨进程传输契约才放 Contracts。
- `Components` 只放有独立业务语义并值得复用的子 View / 子 ViewModel。

## DTO 与页面模型边界

- 页面职责、禁止职责、入口关系以及 API/DTO/Model 划分必须符合目标模块能力地图。
- 页面不是 DTO 的设计或复用边界；DTO 按 API 用例和契约语义设计，一个页面可以消费多个专用 DTO。
- 页面筛选、选中、展开、脏状态、验证提示等 UI 状态放 `SearchModel`、`EditModel`、`LineModel` 或 `StateModel`，不为页面方便塞入 Contracts DTO。
- 不在 ViewModel、Model 或 code-behind 文件中顺带声明一组公开传输 DTO。
- 跨页面或跨接口复用 DTO 必须满足 Contracts DTO 复用门禁；字段相似不能作为复用理由。
- 若本次页面改动会触碰、复制或扩大能力地图记录的已知偏差，必须先输出“门禁偏差提醒”并暂停，等待用户选择重构或兼容方案；不得静默沿用或越界自动重构。

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

## 页面协作边界

- ViewModel 负责页面状态、命令、调用顺序与结果接纳；Feature Service 不持有可绑定页面状态、不弹窗、不访问 View 或控件。
- 导航入口必须使用窄打开请求，并显式定义 Tab identity、重复打开是激活还是新建，以及重复激活是否接纳新参数。
- Dialog、抽屉和子页面使用明确输入与结果对象表达确认、取消和关闭；调用方只在确认结果后接纳变更。
- 业务 ViewModel 只依赖抽象 Dialog/Message/Navigation 或 Feature 专用 Service，不 new View/Window，不引用 WPF-UI 具体控件。

## 质量完成边界

- 页面改动按风险选择普通单元、STA/WPF、契约、构建和人工验收证据；新交互检查键盘、焦点、Automation 与高对比度。
- 文案和格式遵守资源与文化边界；UI 线程不执行同步 I/O，大集合不关闭虚拟化或缓存容器。
- 日志、缓存、导入导出遵守脱敏、隔离/失效、权限、公式注入、编码、取消和清理门禁。

## 禁止事项

- 禁止 ViewModel 直接访问 Repository、Controller、SQL。
- 禁止业务命令藏在属性 setter。
- 禁止机械拆分 XAML 为无业务语义的小 UserControl。
- 禁止把旧 VSTO/MVVM Light 规则带入当前客户端。
