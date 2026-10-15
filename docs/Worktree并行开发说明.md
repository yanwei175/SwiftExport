# SwiftExport Worktree 并行开发说明

核对日期：2026-10-15。第一阶段只准备使用说明和配置调整清单，**没有创建/删除Worktree、切换分支、提交、合并、修改环境脚本或操作数据库**。本说明不授予这些操作权限，也不处理当前工作区已有业务改动。

规则权威继续是 [AGENTS](../AGENTS.md)、[路由与共享工作区规则](../.agents/references/架构治理与路由判定规则.md) 和 [测试总纲](../.agents/references/测试与验收总纲.md)。角色、Router及纵向/横向切片边界不变；Worktree只隔离检出文件与各自索引，不共享聊天上下文，也不自动隔离数据库、端口或桌面。

## 1. 当前检查结果与待调整配置

| 对象 | 已确认事实 | 后续调整建议（本阶段未实施） |
| --- | --- | --- |
| 初始化Restore | `.codex/environments/environment.toml`先restore WebAPI解决方案，再MSBuild restore完整解决方案；两者包含同一后端产品及测试图 | 按任务目标选一次restore图。WPF/后端任务选其必要项目/组合；VSTO按其独立工具链处理，不先后无条件restore两个重叠图 |
| 环境清理 | 同文件cleanup递归删除当前目录的bin/obj/.vs | 不在每次暂停、恢复、切换模块时清理；完整删除/归档Worktree本身无需提前重复清空。显式排障才清所需产物，操作前核实绝对目标在授权Worktree内 |
| 配置维护方式 | 环境TOML首行标记自动生成；本次未修改 | 后续通过环境管理入口调整，复核生成diff；不要把本文建议直接当作已生效脚本 |
| NuGet | 当前进程`NUGET_PACKAGES=D:\DevCaches\NuGet` | 后续确认新工作区进程继承同一缓存与访问权限；不把个人机器路径硬写入所有项目，不因缓存存在跳过每个工作区必要资产生成 |
| Git符号链接 | 索引中6个Web发现入口均为120000；当前实际目录可解析，但仓库`.git/config`的`core.symlinks=false` | 新检出可能成为保存目标字符串的普通文件，不能由当前可用推定新Worktree可用。后续确认Windows链接权限和合适的仓库/Worktree设置；仅改开关不会自动修复已有占位文件 |
| Worktree配置作用域 | 当前`extensions.worktreeConfig=true`，`core.symlinks=false`来自公共仓库配置 | 如后续调整单工作区配置，明确与公共配置的作用域；本次不更改Git配置 |
| 启动端口 | WebApi launchSettings使用HTTP5057/HTTPS7286；Web Playwright验收固定4173且不复用服务器 | 后续按启动入口支持的环境变量/参数分配端口，并使测试baseURL、就绪探测和宿主一致；未完成端口接线前串行执行相应宿主 |
| 共享PG夹具 | Legacy夹具的`serial.lock`位于本检出目录下的TestResults；原有实例/库白名单仍有效 | 不同Worktree的本地文件锁不能互斥同一数据库。先沿用单一执行者/串行租用；独立库或跨Worktree协调方案另按授权处理，不能只随机换库名或绕过白名单 |

检查依据：[环境脚本](../.codex/environments/environment.toml)、[WebAPI启动配置](../SwiftExport.WebApi/Properties/launchSettings.json)、[Web验收配置](../SwiftExport.WebApp/playwright.config.ts)、[PG夹具](../SwiftExport.Application.Tests/Database/PostgreSqlLegacyTestDatabase.cs)。没有执行restore、启动或夹具。

## 2. 新Worktree的规则与Skill可达性

AGENTS、`.agents`角色/Router/Workflow、`.codex/skills`正文及环境TOML均为Git跟踪内容。正常从包含它们的提交检出会得到对应版本；**本阶段尚未提交的治理修改不能假定自动出现在新Worktree**。不同创建/Handoff入口可能处理工作树变更，必须明确实际起点与带入范围，不把旧窗口提示词当成已同步文件。Codex管理的Worktree与命令行自建Worktree的复制/生命周期行为也需区分。[Codex官方说明](https://learn.chatgpt.com/docs/environments/git-worktrees)

后续获准创建后，先做只读可达性核对，不默认启动业务程序：

```powershell
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
git status --short
git config --get core.symlinks
Get-Item .agents/skills/react-web | Format-List LinkType,Target
Test-Path .agents/skills/react-web/SKILL.md
Test-Path .codex/skills/react-web/index/SKILL.md
```

Git Worktree根部的`.git`通常是指向管理目录的文件，不应被工具当作固定目录；共享Git对象/引用与独立工作树、HEAD/索引的边界见 [Git官方说明](https://git-scm.com/docs/git-worktree)。同一分支不应强制同时检出到多个Worktree。

Web发现入口只维护符号链接，正文仍唯一保存在`.codex/skills/react-web`。若链接不可用，报告原生发现限制，按根AGENTS/Router直接读取正文并从正文目录解析相对引用；不要复制第二份Skill或静默声称原生发现通过。链接修复与Windows权限配置是后续独立配置动作，本次未做新Worktree验收。

## 3. 缓存与构建产物

- 可以复用NuGet全局包缓存；它提供已下载的包，不是特定检出目录的`project.assets.json`。[NuGet官方说明](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)
- `bin/obj`、生成代码中间产物和测试输出默认留在各自Worktree。不要跨检出目录链接/复制obj或共用固定输出目录来省restore；路径、配置、项目图和资源身份可能不同。
- 同一Worktree里，只在源码/依赖、配置和产物身份仍有效时复用构建，见测试总纲。目标测试未构建不能用`--no-build`；有包缓存但无有效资产仍需必要restore。
- 若后续使用集中artifacts目录，按Worktree/任务、配置和运行标识分开，避免两个任务写同名TRX、PNG或生成快照。缓存清理不作为正常任务收尾步骤。

## 4. Worktree之外仍共享的资源

| 资源 | 使用边界 |
| --- | --- |
| PostgreSQL | 每次沿用明确实例、库名、初始化/清理范围；优先已授权独立测试库，否则单一执行者串行。Worktree隔离不扩大数据权限，不并行重置同一库，不删除导出或失败现场来提速 |
| WebApi/HTTP宿主与Web验收端口 | 可使用动态端口的测试宿主保留动态端口；固定端口需分配并同步客户端/探测配置，未接线则串行；只停止本任务启动的进程 |
| WPF桌面/STA窗口 | 同一用户桌面上，焦点/键盘/Popup可能互扰；涉及真实交互的定向测试由一个执行者占用，或者使用已经明确隔离的桌面会话。纯VM测试不占用完整UI验收流程 |
| 文件/用户级缓存 | 输出与临时文件按任务隔离；用户级Token/图片缓存不因Worktree改变而自动隔离，不复制真实凭据来制造可复现环境 |
| 公共契约/规则 | 隔离目录防文件互盖，不防两个分支做出冲突决策；共享Contracts、DI、权限、导航和治理入口需明确设计/集成责任，保持单一权威 |

## 5. 提交与集成方式（将来获得授权后）

1. 先选明确基线和任务写入范围；不要自动搬运当前未提交业务改动。为任务使用独立分支，默认前缀`codex/`；本次未创建分支或Worktree。
2. 每个任务只修改其授权内容，记录共享契约/依赖、验证项目与filter、配置、基线和未运行项。读取同名规则时注意这是该Worktree的版本，不是其他窗口的最新版本。
3. 经授权提交时只纳入该任务文件/变更片段，不使用全仓暂存覆盖其他工作；隔离检出不能作为放宽提交、推送或合并权限的理由。
4. 由明确的集成人按依赖顺序选择合并或cherry-pick，不同时混用重复引入同批提交；先共享契约/治理基线，再消费者。发生业务/架构语义冲突先确认依据，不以某分支“更新”自动胜出。
5. 在最终整合版本按测试总纲验证实际影响；独立Worktree绿色不代表集成绿色，也不要求把所有已通过且仍有效的集合重新执行。记录冲突处理引起的证据失效范围。
6. 成果和必要的未跟踪/忽略文件确认保留后，再按授权和所用工具生命周期处理归档/清理；本阶段不执行删除或归档。

## 6. 旧窗口恢复

恢复前核对当前聊天实际目录、Worktree/分支或detached状态、目标授权和最新规则；未合并的治理更新需由开发者明确采用，不自行切分支或覆盖规则。重新读取目标文件、共享依赖与必要diff；同一HEAD也可能有新工作树修改。

复用现有任务书、页面卡、整改卡或交付摘要，保留最后安全点、待决事项、当前写入者/已停止依据、剩余步骤和验证身份。只重做受影响的读取与验证，不为恢复重跑完整规划。发现原任务仍在写或归属不清时遵循共享工作区规则，不自动接管。

## 7. 本阶段结论与后续边界

需要后续单独实施的配置清单为：环境setup去重restore、cleanup改为明确生命周期动作、Windows符号链接策略、缓存继承检查、固定端口参数接线，以及PG/桌面共享资源协调。没有修改环境/部署/Git配置、链接或测试夹具；没有用新Worktree验证这些建议的运行效果。

高频入口精简参考了OpenAI 2026-09-11的 [Rethinking skills and prompts for GPT-6 Astra](https://developers.openai.com/blog/rethinking-skills-and-prompts-for-gpt-6-astra)：只在相关任务加载规则、缩短描述并渐进披露细节。该建议不替代SwiftExport已确认的架构、业务和高风险验证约束。
