---
name: wpf-grid-filter
description: SwiftExport WPF DataGrid 筛选技能。用于 FilterRow、ColumnFilter、筛选描述、ICollectionView 本地筛选、服务端筛选命令和列元数据协作。
---

# WPF Grid Filter

## 模式分离

- 本地模式使用现有 `ICollectionView` 的 Filter/排序/分组能力，不复制业务集合。
- 远程模式只输出可绑定的筛选描述和命令参数，由 ViewModel 调 API；控件不访问服务。
- 页面必须显式选择 Local 或 Remote，数据量变化时不得静默切换语义。

## 筛选契约

筛选描述使用稳定 `ColumnKey/FieldPath`、操作符、值和数据类型。不要保存 DataGridColumn 或编辑控件实例。空值、字符串大小写、日期边界、数值区域和枚举显示值/实际值必须明确。

## UI 与列协作

- FilterRow/ColumnFilter 组合原生 Header、Adorner 或独立行，不替换 DataGridColumn 体系。
- 列元数据决定是否可筛选、编辑器类型和字段路径。
- 远程输入支持去抖、取消和最新请求获胜；这些状态由 VM/适配器拥有。
- 筛选应用前先提交或取消当前编辑，策略必须明确，不能无声丢值。

## 性能与验收

不对每次按键全量重建集合；测量大集合的谓词成本。覆盖空值、组合 AND/OR、清除筛选、动态列、编辑中筛选、远程失败、虚拟化和当前行被筛掉后的同步。
