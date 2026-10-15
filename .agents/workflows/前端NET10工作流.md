# 前端 .NET 10 工作流

## 适用

`SwiftExport.Client.Wpf` 当前 .NET 10 WPF + CommunityToolkit MVVM 前端。

## 步骤

明确局部修正直接进入实施，复用仍有效的页面、架构及契约结论；以下设计/检查项只核对本次改变的边界。新增或重构页面切片才展开完整页面设计，不能因触碰ViewModel就重做全部页面规划。

1. 先读 `AGENTS.md`、`.agents/router.toml`、`.agents/frontend.toml`。
2. 先读 `.codex/skills/wpf-dotnet10-design/index/SKILL.md`，再按任务读取页面、MVVM、控件、样式子 skill。
3. 涉及 ViewModel / Model / Command 时必须读 `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`。
4. 需要决定新类型/文件的职责归属、拆分/合并、公共复用边界或专项职责审查时，读 `.codex/skills/wpf-dotnet10-design/file-organization/SKILL.md`；现有类型内的局部实现不因文件被修改而自动触发完整拆分盘点。
5. 涉及 lepoco/WPF-UI 控件、FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog、SymbolIcon 时读 `.codex/skills/wpf-dotnet10-design/wpfui-controls/SKILL.md`。
6. 涉及异步加载、搜索防抖、保存或业务命令、Busy、导航生命周期、页面关闭、错误反馈、事件/Messenger/Timer/CTS 释放时，读运行时安全Skill及 `.codex/skills/wpf-dotnet10-design/runtime-safety/references/运行时安全门禁.md` 的相关章节。
7. 涉及 ViewModel/API/Feature Service 职责、页面权限、普通表单验证、导航参数、Document Tab identity 或 Dialog/抽屉/子页面返回协议时，读页面架构Skill及 `.codex/skills/wpf-dotnet10-design/page-architecture/references/页面架构边界决策表.md` 的相关章节。
8. 为实际行为选择普通/STA/契约测试，或改变可访问性、文化格式、性能、日志、缓存、文件安全边界时，读质量Skill及矩阵的适用章节；模块/完整UI或专项非功能验收再读取代表性检查清单。仅汇报已有结果不触发整套资料。
9. 涉及 `SwiftExport.Contracts/**` 时必须读 `.codex/skills/aspdotnet10/contracts-refit-dto/SKILL.md` 和 `.codex/skills/aspdotnet10/contracts-netstandard20/SKILL.md`。
10. 前端任务新增或复用 Contracts DTO 时，必须读 `.codex/skills/aspdotnet10/contracts-dto-models/references/DTO复用与文件组织门禁.md`，搜索现有契约、API 和调用方，并记录复用或新建理由。
11. 涉及 Contracts DTO 与 ViewModel、EditModel、LineModel、TreeItemModel 转换时，必须读 `.codex/skills/aspdotnet10/application-mapster-mapping/SKILL.md`，并检查 `SwiftExport.Client.Wpf/Mappings/MapsterConfig.cs` 与目标 Feature 的 `Mappings` 配置。
12. 分层/公共依赖变化或边界不明时读工程总纲相关章节；业务职责、状态/数量或API/DTO语义改变时补读命中业务/模型及地图章节。新增或重构页面纵向切片才读切片教程；改变采购—入库—库存交接时核对三方契约，不为普通小改找旧偏差ID。
13. 从目标Feature和直接调用方/相关测试确认事实；只补读本次影响的View、模型、Service、导航、API注册、Runner、UI Service和XAML，存在疑点再扩大。
14. 新增或改变页面依赖、权限、验证、导航或Dialog协议时，明确相应owner与契约；未改变的已有结论复用，不重新建全套矩阵。
15. 只为本次新增或改变的异步入口确认查询/写命令语义、取消、Busy、存活检查、反馈和释放owner；仍覆盖实际受影响的竞态与失败路径。
16. 按路由policy分类偏差；过期实现描述及无关历史偏差不扩大任务，改变有效目标/授权或豁免门禁时才暂停依赖决定的部分。
17. 本次确需文件/职责组织决策时，按对应Skill盘点相关基类和公共服务，再确定owner与拆分；不得以行数或 `partial` 文件数量代替职责判定。
18. ViewModel / Model 使用 `partial`、`[ObservableProperty]`、`[RelayCommand]`。
19. 页面专用 UI 状态放前端 Model；DTO 按 API 用例设计，只有满足强语义复用条件时才跨页面或跨接口复用。“窄”指职责单一，不以字段少为目标；拆分现有 DTO 前先建立字段处置矩阵，未迁移调用方仍需字段不得删除。
20. DTO 与前端模型的结构性字段复制默认使用 Mapster；复杂字段差异集中配置，不新增大段逐字段转换或只包装逐字段复制的 Mapping 类。业务计算、状态决策和跨来源组装保留显式代码。
21. 查询同时具备取消旧请求与提交前身份/存活验证；写命令入口拒绝并发，同一用户操作只有一个最外层 Busy owner。
22. 导航初始化与重复激活必须幂等；离开/释放时对称清理事件、Messenger、Timer、CTS 和子对象；搜索、刷新、分页、切换和关闭前检查未保存变更。
23. 错误按 HTTP、业务失败、字段验证、并发冲突、认证授权、网络/超时和未知异常分类；未知技术异常由基础设施记录，ViewModel 只提供业务上下文和一次用户反馈。
24. 不在构造函数中手写 `new RelayCommand` / `new AsyncRelayCommand`。
25. 不从 Git 历史或归档分支把旧 MVVMLight、VSTO、MateWpfThemes 规则带入新客户端。
26. 按风险明确普通单元、STA/WPF、契约、构建与人工验收证据；检查键盘/Automation/高对比度、文化格式、UI 性能、日志脱敏、缓存失效和导入导出安全的适用项与豁免理由。
27. 按测试总纲完成受影响项目或测试项目构建及相关方法/类测试；同批有效结果复用，失败先验证失败用例。整项目回归与完整UI按共享影响或模块/阶段验收选择，不默认启动完整客户端或截图。既有测试直接覆盖时不无条件修改Test文件；未验证项如实说明，实质契约/架构变化同步对应权威文档。

## 边界

- 前端任务默认只写 `SwiftExport.Client.Wpf/**`、必要的 `SwiftExport.Contracts/**`，以及 `SwiftExport.Client.Wpf.Tests/**` 中本次相关测试、夹具和测试数据。
- 补测不允许删除有效断言或绕过失败；后端缺陷交还后端角色，不借补测改变后端行为。
- EnterpriseGrid 框架及其专项测试由 Grid Architect 按授权负责；运行专项测试不授予前端写入权限。
- 不借前端任务修改 WebApi、Application、Infrastructure、Core。
