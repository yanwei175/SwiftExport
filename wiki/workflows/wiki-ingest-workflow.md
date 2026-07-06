# Wiki Ingest Workflow

## 适用场景

用户新增 raw 来源，并要求 “ingest”“整理到 wiki”“更新索引”“纳入 AI Wiki” 时读取本 workflow。

## 最小读取集

1. [Router](../index.md)
2. 用户指定的 `raw/**` 来源
3. 目标 wiki 页面
4. 如涉及源码事实，再读取真实源码

## raw 边界

- `raw/` 是来源层，不参与日常任务路由。
- 不自动删除、移动或重写 raw 原文，除非用户明确要求。
- 不把 raw 原文大段复制进 wiki；wiki 只保留项目化后的规则、事实、流程和风险。
- 已提炼、模板化或不适合 SwiftExport 当前架构的 raw，不进入 `wiki/index.md` 短路由。

## Ingest 步骤

1. 读取 `wiki/index.md`，确认当前短路由结构。
2. 读取用户指定的 raw 文件或目录；如果用户只说新增了来源，先用文件搜索确认新增路径。
3. 判断来源类型：项目事实、业务流程、技术规范、skill、workflow、UI 设计、截图、对话或外部文档。
4. 提取对 SwiftExport 当前架构长期有用的内容，整理到对应 `wiki/**` 页面。
5. 如果新增正式页面，更新：
   - `wiki/index.md`：只加入会被日常任务命中的短路由。
   - `wiki/catalog.md`：加入完整目录。
   - 相关子索引：`wiki/workflows/index.md`、`wiki/guides/index.md` 或 `wiki/skills/index.md`。
6. 更新 `wiki/log.md`，记录来源、更新页面和是否发现冲突。
7. 如 raw/wiki 与源码冲突，优先相信源码，并在 `wiki/log.md` 标记“可能过期”。

## 禁止事项

- 禁止因为 ingest 修改业务源码。
- 禁止把通用模板未经项目化直接放入正式 wiki。
- 禁止为了新增 skill/workflow 修改 `AGENTS.md`。
- 禁止把 `wiki/index.md` 重新扩展成完整目录。
