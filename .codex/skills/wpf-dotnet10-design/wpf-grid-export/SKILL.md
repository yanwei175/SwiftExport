---
name: wpf-grid-export
description: SwiftExport WPF DataGrid 数据导出技能。用于按列元数据导出当前视图或数据源、CSV/Excel 提供器、格式转换、取消进度和可测试导出管线。
---

# WPF Grid Export

涉及业务导出范围、权限、敏感字段、文件路径/临时文件、取消进度、公式注入或完成证据时，同时读取 `../quality-gates/SKILL.md`；本 Skill 只定义 Grid 通用导出管线。

## 数据优先

- 从 ItemsSource/视图快照和列元数据导出，不扫描 DataGridRow/DataGridCell 可视树。
- 默认按 `DisplayIndex` 导出可见列；是否包含隐藏列、筛选后行、选中行由请求显式指定。
- 模板列没有 FieldPath/ValueSelector 时标记不可自动导出，不尝试解析视觉内容。

## 分层

- Grid 适配器生成与 UI 无关的 `GridExportRequest`：行快照、列描述、文化、选项。
- `IGridExportProvider` 负责 CSV/Excel 格式；文件选择、覆盖确认和通知属于宿主 UI 服务。
- 首版可先实现零依赖 CSV；Excel 提供器作为可选程序集，在单独决策中选择 Open XML 或兼容许可库。
- 不在框架核心强制引入大型 Excel 包，不依赖 Office Interop。

## 值与格式

区分原始值、显示值和格式字符串。日期、Decimal、枚举、Lookup、布尔和空值必须可配置；公式注入、CSV 分隔符/编码与超大数据内存占用必须纳入安全检查。

## 异步与验收

提供取消和进度，UI 线程只抓取稳定快照。覆盖重排列、隐藏列、筛选/排序视图、模板列、Lookup 显示值、特殊字符、超大数据、取消和写入失败。
