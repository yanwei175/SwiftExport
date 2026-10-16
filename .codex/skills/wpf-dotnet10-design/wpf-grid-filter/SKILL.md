---
name: wpf-grid-filter
description: SwiftExport WPF 原生集合视图与页面筛选边界；GridFilter/FilterRow 封装已退出，不恢复无业务调用模块。
---

# WPF Grid 筛选边界

- EnterpriseGrid 的 Filtering、FilterRow、描述模型和远程命令桥接因无业务入口已退出，GridColumn.CanFilter 也退出。
- 本地筛选、排序、分组直接使用 ICollectionView / CollectionViewSource，保留宿主视图所有权，不复制业务集合。
- 远程查询使用页面既有查询条件和命令；取消、去抖、最新请求获胜及错误反馈由 VM/Feature Service 负责。
- 不因筛选 UI 需求重新创建统一 Grid 描述模型；先明确真实页面字段、空值、文化、组合条件和查询语义。
- 应用视图变化前处理原生编辑提交失败，不无声丢值；当前项被筛掉后的选择由页面决定。
- 验证清除筛选、多个独立视图、编辑中筛选和虚拟化。相关原生视图兼容测试继续保留，不运行已退出封装的测试。
