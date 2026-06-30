# SwiftExport WPF NavigationService 草稿

> 已按 SwiftExport 项目口径整理：当前项目已有数据库路由缓存 + 静态 `ViewRoutes` 兜底，不照搬 CommunityToolkit 导航服务。

## 当前项目事实

相关文件：

- `SwiftExport.View.FW/Routing/IViewRouteService.cs`
- `SwiftExport.View.FW/Routing/ViewRouteService.cs`
- `SwiftExport.View.FW/Routing/ViewRoutes.cs`
- `SwiftExport.View.FW/DI/DependencyInjection.Routing.cs`

当前模式：

```text
targetViewKey
    -> ViewRouteService
    -> 数据库路由缓存
    -> ViewRoutes 静态兜底
    -> ViewModel / View 解析
```

## 使用规则

- 新增导航入口时优先查当前路由注册方式。
- ViewModel 不直接 new View / Window。
- NavigationService 只负责页面定位，不负责保存数据、弹确认框、业务权限判断。
- 离开页面前的未保存检查由当前页面 ViewModel 或上层流程处理。

## 禁止事项

- 不要在普通页面中新增另一套 route 字典。
- 不要用 CommunityToolkit `ObservableObject` 示例替换当前 MVVMLight / DI 路由体系。
- 不要在 NavigationService 里写业务保存逻辑。
