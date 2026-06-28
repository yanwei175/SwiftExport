# SwiftExport AI Wiki Log

## 2026-06-28 workflow | 任务路由迁移到 index

- 调整 `AGENTS.md`：移除具体后端、前端、项目地图任务路由条目。
- 将具体任务路由集中到 `wiki/index.md`，后续新增 skill / workflow 只更新 index 和 log。
- 保留 `AGENTS.md` 中的稳定铁律：任务先读 index、AI 资产 ingest 必须更新 index 和 log、源码事实优先。

## 2026-06-28 workflow | 补充 AI 资产 Ingest 规则

- 更新 `AGENTS.md`，新增“AI 资产 Ingest 工作流”。
- 明确手动新增 skill、workflow、project doc 后，应读取新资产并更新 `wiki/index.md`。
- 明确 ingest 必须更新 `wiki/log.md`，并在 raw/wiki 与源码冲突时标记“可能过期”。
- 明确 ingest 文档不应修改业务源码，也不应自动删除用户手动新增的 raw 文件。

## 2026-06-28 workflow | 建立 AI Wiki + Codex 工作流

- 重写 `AGENTS.md`，将其定位为 Codex 行为规则层。
- 新建 `raw/` + `wiki/` 工作流结构。
- 新建 `wiki/index.md`，作为 Codex 查询、skill、workflow、项目文档的统一入口。
- 新建 `raw/skills/backend/**`，迁移旧 `.codex/skills/backend/**` 的后端 skill。
- 新建 `raw/workflows/frontend/wpf-vsto-workflow.md`，定义 WPF/VSTO 前端开发边界。
- 新建 `raw/project/**`，沉淀项目地图和入门说明。
- `.codex/skills/**` 暂时保留作为历史兼容入口，后续以 `raw/skills/**` 为权威。
