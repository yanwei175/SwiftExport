---
name: vsto-client
description: 开发 SwiftExport.Client.VSTO 的 net48 C# 7.3 Excel 插件，涵盖 Ribbon XML、WPF 宿主、登录会话、线程与窗口释放；仅用于 VSTO 及其测试，不用于 .NET 10 WPF 客户端。
---

# VSTO 客户端技能

先读项目 `SwiftExport.Client.VSTO/AGENTS.md` 和 `docs/架构与目录说明.md`；Ribbon 任务追加 `docs/Ribbon XML开发指南.md`。路径相对仓库根目录，事实以源码为准。

## 实施规则

- 单生产程序集，net48、C# 7.3。Toolkit 只用运行库，不用源生成器。
- 页面继承 ViewModelBaseEx : ViewModelBase : ObservableObject；项目扩展集中在 Ex，避免复制 .NET 10 的 CRUD 状态框架。
- Ribbon 是 Office XML，getter 只读内存；onAction 转交协调器，刷新回到 Excel STA。
- 登录候选身份独立验证，成功才替换当前账号；失败和取消保留旧账号。旧会话响应不允许影响新身份。
- 401、403、网络失败、取消分别处理；没有服务端刷新协议就不实现静默刷新，不自动重放写请求。
- 密码不持久化，Token 用 DPAPI CurrentUser，日志不写凭据和响应正文。
- WPF 和 Excel 默认共用主 STA。异步接纳核对生命周期，COM 不进入后台计算或 ViewModel。
- 窗口／窗格拥有资源 Scope，结束时取消、解绑、释放。不得停止宿主 Dispatcher、强制 GC 或终止 Excel 进程。
- 仅修改用户授权的 VSTO、测试与专属文档。不得修改 .NET 10 Skills、后端行为或共享规则扩大例外。

## 验证

传统 VSTO 用 Visual Studio MSBuild 验证 Debug／Release。纯托管测试与 Excel 实机验收分开；缺少签名、Office 或实机条件时如实记录，不声称可发布。新增手写源码使用中文职责级注释。
