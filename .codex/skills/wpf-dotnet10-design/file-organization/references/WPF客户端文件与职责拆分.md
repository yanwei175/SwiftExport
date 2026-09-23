# WPF 客户端文件与职责拆分

## 推荐目录

```text
SwiftExport.Client.Wpf/Features/{业务域}/{功能单元}/
  Views/
  ViewModels/
  Models/
  Policies/
  Services/
  Mappings/
  Components/
  Converters/
```

目录按真实职责按需创建，不要求每个 Feature 机械拥有全部目录：

- `Views`：XAML 与对应 code-behind。
- `ViewModels`：页面、Dialog 及具有独立状态和生命周期的子 ViewModel。
- `Models`：EditModel、LineModel、ListModel、SearchModel、StateModel、打开请求和客户端结果类型。
- `Policies`：无 UI 状态、可独立测试的准入、选择、排序或计算策略。
- `Services`：当前功能单元的多调用协调、结果归一化、Dialog 组装、Loader、Cache 或平台适配。
- `Mappings`：DTO 与前端模型的 Mapster 配置；不放业务决策。
- `Components`：具有独立交互语义并值得复用的 View/控件及配套类型。
- `Converters`：无业务流程、无 I/O 的显示转换。

跨 Feature 的基础设施进入现有 `Mvvm`、`Services`、`Navigation`、`Controls` 或资源目录。不能因为第二个调用方尚未出现，就把业务专用类型提前提升为全局公共服务。

## 职责决策表

| 内容 | Owner | 文件边界 | 不应放入 |
|---|---|---|---|
| 可绑定页面状态、命令、结果接纳 | 页面 ViewModel | 页面 ViewModel 文件；独立状态机形成子 ViewModel | View、Feature Service |
| 可编辑业务输入、行状态、筛选状态 | Edit/Line/List/Search/State Model | 一个公开类型一个文件 | Contracts DTO、code-behind |
| 无 UI 状态的准入或计算规则 | Policy | `Policies/XxxPolicy.cs` | Converter、属性 setter |
| 多 API/多步骤协调或结果归一化 | Feature Service | 接口与实现分别成文件，位于 `Services` | View、通用 MVVM 基类 |
| ContentDialog、Window、文件选择等平台组装 | UI Service / Adapter | `Services`；跨 Feature 才提升到全局 `Services/UI` | 业务 ViewModel、Model |
| WPF 视觉树、焦点和编辑提交 | code-behind | 与 XAML 配对 | Feature Service、Model |
| DTO 与前端模型结构映射 | Mapster 配置 | `Mappings` | ViewModel 中的逐字段样板 |
| 可复用交互部件 | Component / Custom Control | `Components` 或全局控件目录 | 页面私有逻辑伪装的公共控件 |

Feature Service 是否需要存在由页面架构 Skill 决定；本表只规定已经需要的类型放在哪里。WPF-UI 控件选择及 Dialog/Snackbar 实现由 WPF-UI Skill 决定。

## 大 ViewModel 拆分顺序

1. 列出状态、命令、依赖、订阅、异步请求和资源释放责任。
2. 删除或复用现有基类、API Runner、导航和 UI Service 已经提供的重复能力。
3. 把 Edit/Line/List/Search/State 数据移入对应 Model；不要让 ViewModel 同时充当数据行和列表容器。
4. 把无 UI 状态的规则移入 Policy，把多调用协调移入 Feature Service，把具体 WPF/WPF-UI 组装移入 UI Service。
5. 只有某一区域拥有独立可绑定状态、命令和生命周期时，建立子 ViewModel；父 ViewModel 负责页面级组合，不镜像子对象全部属性。
6. 剩余代码若仍是同一对象、共享同一状态和生命周期，可以按稳定能力使用 `XxxViewModel.Queries.cs`、`.Posting.cs` 等 `partial` 文件。
7. 拆分后检查事件订阅、CancellationTokenSource、Busy owner、命令通知和未保存状态仍只有一个明确所有者。

不要用固定行数作为完成条件。文件很长但职责单一时可以保留；文件较短但混合 UI、I/O、规则和平台类型时仍应拆分。

## `partial` 判定

适合 `partial`：

- 同一 ViewModel 的生成器成员与不可独立存在的命令能力共享全部状态和生命周期。
- 拆成新对象只会产生大量状态镜像、转发和事件同步，且没有独立测试或复用价值。

不适合 `partial`：

- 文件之间拥有不同依赖、状态所有权或释放时机。
- 某部分可作为无 UI Policy、Feature Service、UI Service 或子 ViewModel 独立测试。
- 通过 `partial` 隐藏循环依赖、重复 Busy、重复反馈或过多可变字段。

仓库中的 `InboundManagerViewModel.*`、`OutboundManagerViewModel.*`、`PackingListDetailViewModel.*` 只说明当前存在物理切片。触碰这些职责时必须重新判断是否仍属于同一对象，不能将其直接复制为模板。

## View 与 XAML 拆分

- View 可以使用 WPF-UI 控件；业务 ViewModel 不暴露 `ContentDialog`、`SnackbarPresenter`、`Window`、`DependencyObject` 等平台类型。
- code-behind 可以提交当前 DataGrid 编辑、处理焦点、视觉树和必须依赖控件实例的事件，但只把稳定结果交给 ViewModel 命令或窄回调。
- code-behind 不保存业务事实，不直接调用 Refit API，不维护业务事务，也不复制 ViewModel 的 CanExecute。
- 子 View/UserControl 必须有明确输入、命令和输出。仅把一段 XAML 搬到新文件、仍依赖父页面大量元素名，不算职责拆分。

## 反例

- 为降低行数，把一个 ViewModel 平均切成多个无语义 `Part1/Part2` 文件。
- 创建只转发一次 API 调用的 `ManagerService`，同时让 ViewModel 和 Service 各维护一份 Busy 或错误状态。
- 新建业务基类复制 `ViewModelBaseEx` 的 Busy、消息或生命周期能力。
- 在 Model 中保存 `ContentDialog`、控件引用或 ServiceProvider。
- 为页面局部区域创建公共控件，但 API 硬编码具体业务 DTO、权限或 Repository。
- 把多个公开 Request、Result、Model 顺带声明在 ViewModel 或 code-behind 文件末尾。

## 渐进治理

- 普通修改只治理本次触碰的职责，不批量拆分整个 Feature。
- 新职责不得继续塞入已混杂文件；先确定正确 owner，再移动最小闭环及其测试。
- 拆分不得改变绑定名、导航 identity、命令语义、脏状态或错误反馈；这些变化如确有需要，应作为独立行为变更评审。
- 完成后按质量门禁选择普通单元、STA/WPF、契约、构建和人工验收证据。
