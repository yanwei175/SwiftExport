# BS Excel Grid Demo

这是一个隔离的静态前端 demo，用来验证 SwiftExport 未来 BS 前端的“单据明细类 Excel 编辑”体验。

边界：

- 不接入现有 ASP.NET 后端。
- 不修改 WPF、VSTO、Contracts、Application、Infrastructure 或 WebApi 项目。
- 不依赖 npm install，直接打开 `index.html` 即可查看。
- 表格交互为原型实现，用来验证体验边界，不作为正式组件封装。

已覆盖的体验：

- 单元格编辑。
- 区域选择。
- 复制 / 粘贴多行多列。
- 向下填充。
- 撤销 / 重做。
- 新增行 / 删除勾选行。
- 行级脏数据、错误状态、只读锁定。
- 批量保存变更集预览。
- 汇总数量和金额。

正式 BS 前端建议用 `Vue 3 + TypeScript + Element Plus + vxe-table` 重新实现这些能力。
