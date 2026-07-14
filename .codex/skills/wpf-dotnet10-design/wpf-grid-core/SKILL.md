---
name: wpf-grid-core
description: SwiftExport 原生 WPF DataGrid 增强核心技能。用于 EnterpriseGrid 入口、AttachedProperty、内部协调器、模块生命周期、事件订阅、Helper、Extension、资源与兼容性设计。
---

# WPF Grid Core

## 架构模式

- 保持宿主为原生 `DataGrid`；功能通过显式启用的附加属性组合。
- 每个 DataGrid 只创建一个内部协调器，由其统一分派输入、选择、编辑和验证事件。
- 协调器及模块接口首版保持 `internal`；公共 API 只暴露稳定的附加属性、命令和策略契约。
- 启用、禁用、Loaded、Unloaded 必须幂等；事件必须成对订阅/解绑，不持有页面或 ViewModel 强引用。

## 公共 API 原则

- 使用 `FrameworkPropertyMetadataOptions.BindsTwoWayByDefault` 仅限真正的双向状态。
- 区分配置属性、双向状态和只读诊断属性，不用一个 DP 同时承担三种语义。
- 对 DataGridColumn 的元数据使用附加属性，避免创建平行 Column 基类。
- 命名使用 `EnterpriseGrid`、`GridSelection`、`GridOperations`、`GridValidation`、`GridColumn` 等稳定域前缀。

## 依赖与视觉

- 框架项目只依赖 WPF/.NET；首阶段不引入 `Microsoft.Xaml.Behaviors`。
- 只有原生附加行为无法可靠解绑或复用时，先记录 ADR，再评估 Behaviors 包。
- 视觉使用命名资源和系统 Fluent 能力；不得引用宿主的 WPF-UI 控件类型。
- 宿主页面可以通过 Command -> `IDialogService/IMessageService` 使用 WPF-UI ContentDialog、Snackbar；框架模块不得直接依赖 WPF-UI 或操作 DialogHost。

## 实现检查

- 不吞掉未处理输入，不修改 `ItemsSource`，不关闭虚拟化。
- 不通过 `UpdateLayout` 全局强制布局；需要容器时先 `ScrollIntoView`，再处理生成结果。
- 设计可注入的导航、列可见性等策略，但避免过早建立通用插件平台。
- 测试重复启停、卸载重载、空 ItemsSource、列动态变化和多个 DataGrid 并存。
- `GridRowHeader.ShowRowNumber` 只能表达当前视图行号，不得读取业务 RowIndex、修改宿主 RowHeaderWidth、关闭虚拟化或长期缓存 DataGridRow。
