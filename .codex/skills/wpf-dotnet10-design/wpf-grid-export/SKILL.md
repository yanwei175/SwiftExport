---
name: wpf-grid-export
description: SwiftExport WPF 客户端数据导出边界；EnterpriseGrid 通用导出管线已退出，复用真实业务文件服务。
---

# WPF Grid 导出边界

- EnterpriseGrid 的 GridExport、CSV/XLSX Provider、请求模型、IGridValueAccessor 因无业务入口已退出；不恢复空壳接口。
- CanExport、ExportHeader、ValueAccessor 不再是 GridColumn 元数据。DataGrid 原生复制不等于 Excel 文件导出。
- 新业务导出先定位客户端现有文件服务和具体用例，明确当前视图/全部数据/选区、字段、权限及原始值/显示值，不为假想需求创建通用管线。
- 从数据项或稳定快照读取；不扫描虚拟化 DataGridRow/DataGridCell 或模板控件获取业务值。
- 文件选择、覆盖确认和通知由客户端 UI 服务负责，格式写入由客户端文件服务负责；不把业务或 Office Interop 依赖放入 Grid。
- 涉及实际导出时读 ../quality-gates/SKILL.md，按范围验证文化格式、公式注入、编码、特殊字符、取消、内存和写入失败。
