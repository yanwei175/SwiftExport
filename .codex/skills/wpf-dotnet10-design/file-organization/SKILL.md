---
name: wpf-dotnet10-file-organization
description: SwiftExport .NET 10 WPF 客户端文件与职责拆分技能。用于新增、拆分、合并或审查 View、code-behind、ViewModel、子 ViewModel、Model、Policy、Feature Service、UI Service、Adapter、Mapper 和 Component；不负责具体 MVVM、WPF-UI 控件或运行时机制。
---

# WPF 客户端文件与职责拆分 Skill

## 适用边界

本 Skill 只决定代码应由哪个类型、目录和文件承担，以及何时需要拆分。具体实现分别服从对应专项 Skill：

- CommunityToolkit 源生成器、命令和基类 API：`../communitytoolkit-mvvm/SKILL.md`
- Refit、Feature Service、权限、验证、导航和 Dialog 协议：`../page-architecture/SKILL.md`
- WPF-UI 控件和适配实现：`../wpfui-controls/SKILL.md`
- 异步、Busy、并发、生命周期和资源释放：`../runtime-safety/SKILL.md`
- 页面组成、DataGrid 行为和页面命令：`../feature-page/SKILL.md`
- 可复用控件 API：`../custom-controls/SKILL.md`

## 拆分前检查

- 先读取目标 Feature 的 View、code-behind、ViewModel、Model、Service、映射、DI 注册和相关测试，不根据单个文件孤立判断。
- 先盘点 `ViewModelBaseEx`、`BaseLineModel<TDto>`、`BaseListModel<TLine,TDto>`、`IApiCallRunner`、Navigation、`IDialogService`、`IMessageService` 等现有能力。
- 禁止通过拆分重复实现 Busy、并发保护、脏跟踪、删除池、错误反馈、导航身份、Dialog 外壳或生命周期管理。
- 文件行数只能触发审查，不能直接决定拆分。优先识别独立状态、生命周期、依赖、复用边界和可单独测试的职责。

## 核心规则

- Feature 按业务域和功能单元内聚，不建立全项目级 `Views`、`ViewModels`、`Models` 大桶。
- 默认一个公开顶级类型一个文件；紧密依附且不应独立消费的私有或文件内辅助类型可以例外。
- 大 ViewModel 优先依次抽取 Model/Policy、Feature Service、UI Service 或有独立状态与生命周期的子 ViewModel；只有剩余成员仍属于同一对象的不可分割能力时才使用 `partial` 文件。
- `partial` 只改变物理文件，不形成依赖、状态或生命周期边界，不能作为职责拆分完成的证据。
- code-behind 只承载真实 WPF 视觉树、焦点、编辑提交、控件事件桥接和必须访问 DependencyObject 的逻辑；业务状态、业务规则和 API 编排不得因实现方便留在 View。
- WPF-UI 具体类型只进入 View、Shell 或 UI Adapter；业务 ViewModel 和业务 Model 依赖项目抽象。
- XAML 或 UserControl 只有具备独立交互、复用、生命周期、可测试行为或显著降低父视图认知负担时才拆分，禁止按行数机械切片。
- 不为一次性转发创建 Service、Adapter、Mapper 或子 ViewModel；拆分必须产生清楚的责任边界。
- 不要求批量重构现有宽文件。触碰相关职责时先阻止偏差扩大，再完成当前用例所需的最小安全迁移。

## 详细参考

- 设计目录、职责归属、大 ViewModel 拆分或评估现有 `partial` 文件时读 `references/WPF客户端文件与职责拆分.md`。

