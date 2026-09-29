# VSTO 客户端开发入口

本目录是独立于 .NET 10 客户端的 Excel 插件，采用单生产程序集、.NET Framework 4.8、C# 7.3。

先读 [架构说明](docs/架构与目录说明.md) 和 [VSTO 技能](../.codex/skills/vsto-client/SKILL.md)。这里不适用 .NET 10 WPF 的 partial property／源生成器强制写法。

- 页面继承 `ViewModelBaseEx : ViewModelBase : ObservableObject`，Toolkit 仅使用运行库；属性与命令手写 C# 7.3。
- COM、Globals、窗口句柄只允许在入口、Hosting、Excel 适配层；ViewModel 不持有 Range 或 Window。
- 共享 API／DTO 只读复用 Contracts。本阶段不直接访问数据库、不引用 EnterpriseGrid 或 .NET 10 客户端。
- Ribbon 是 Office Ribbon XML，WPF 页面是 XAML。回调 getter 只读内存，状态变化后在 Excel STA InvalidateControl。
- 事件、异步操作、窗口 Scope 必须闭环释放；不停止 Excel Dispatcher、不强制 GC、不结束 Excel 进程。
- 新增手写源码使用中文职责注释，公开类型与非显然成员补 XML summary；不修改生成文件。
- 修改仅限用户授权的 VSTO 及对应测试／文档，不把 VSTO 例外扩散到 .NET 10 Skills。
- 构建使用 Visual Studio MSBuild，实机验收与自动测试分别报告。
- 主题统一由 ThemeService 初始化；WPF-UI 4.3.0 的 UiApplication 资源入口必须显式连接并在释放时恢复。设计器资源只走 Properties/DesignTimeResources.xaml，不在运行时合并设计器补丁。
- Primary 样式改动运行独立 SwiftExport.Client.VSTO.WpfTests STA 验证；颜色、画刷及控件状态按实际包模板检查。

- 普通任务窗格页面统一在 `Hosting/TaskPanes/TaskPaneRegistrar.CreateDefault()` 注册 `TaskPaneConfig`，并在 DI 注册 Scoped View／ViewModel；不为仅做打开接线的页面复制专用 Coordinator。
- 宽度及初始左／右停靠属于页面配置，不硬编码在 Manager；Manager 统一管理 Scope 和 TaskPaneContext。重复打开复用，× 隐藏保留，失效实例再次访问或插件退出时释放。接入步骤见 `docs/架构与目录说明.md`。
