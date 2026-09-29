# 从本项目读懂 Ribbon XML

## XML 与 XAML 的区别

`SwiftExportRibbon.xml` 是 Office RibbonX 标记，由 Excel 读取；它不能使用 WPF Binding、Grid、Style 或 DataContext。`Authentication/LoginWindow.xaml` 才是 WPF 页面。

本项目接线顺序：ThisAddIn.CreateRibbonExtensibilityObject → SwiftExportRibbon → GetCustomUI 读取嵌入 XML → Office 调用 Ribbon_Load 保存 IRibbonUI。

GetCustomUI 只返回 XML，不初始化容器或调用网络。Ribbon 可能先于 Startup 创建，因此 Attach 在服务准备后绑定，getter 在此之前也必须安全返回。

## 从外到内阅读

```xml
<customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui" onLoad="Ribbon_Load">
  <ribbon><tabs>
    <tab id="swiftExportTab" label="SwiftExport">
      <group id="accountGroup" label="账号">
        <labelControl id="accountStatus" getLabel="GetStatus" />
        <button id="login" label="登录" onAction="OnLogin"
                getVisible="GetLoginVisible" getEnabled="GetAccountEnabled" />
      </group>
    </tab>
  </tabs></ribbon>
</customUI>
```

tab 是页签，group 是组，labelControl 是文字，button 是按钮。每个自定义控件的 id 必须唯一且稳定。label 是固定文字，getLabel 是让 Excel 回调代码取得动态文字；不要为同一属性同时填写固定值和 getter。

`idMso` 引用 Office 内建控件身份，不能当作自定义 id；本项目以前的 TabAddIns 表示内建加载项页签，现已使用独立 swiftExportTab。`imageMso` 只指定 Office 内建图标，不改变控件身份，名称必须查官方控件标识资料。首期账号按钮不依赖未验证图标。

## 回调签名

XML 中的方法名必须与 public C# 方法完全一致，方法位于 ComVisible 的 SwiftExportRibbon 类。

| XML 属性 | 本项目方法签名 | 作用 |
|---|---|---|
| onLoad | void Ribbon_Load(Office.IRibbonUI value) | 保存刷新入口 |
| getLabel | string GetStatus(Office.IRibbonControl control) | 返回当前用户文字 |
| getVisible | bool GetLoginVisible(Office.IRibbonControl control) | 决定是否出现 |
| getEnabled | bool GetAccountEnabled(Office.IRibbonControl control) | 决定能否点击 |
| onAction | void OnLogin(Office.IRibbonControl control) | 执行按钮动作 |

这里列的是 button 等当前控件的签名；toggleButton、editBox 等其他控件的动作参数不同，不要直接套用。control.Id 是触发控件标识，control.Context 可用于解析所属 Excel 窗口。

## 为什么用户名不会自动刷新

Office 缓存 getter 结果，不会像 WPF Binding 那样订阅 PropertyChanged。登录成功后必须：

```csharp
// 已回到 Excel 主 STA；让 Office 下次重新调用 GetStatus。
ribbon.InvalidateControl("accountStatus");
```

本项目由 SessionService → Bootstrapper（回主线程）→ Changed → Ribbon.Refresh 完成。Refresh 同时失效登录、切换、退出的可见性和可用性。getter 只返回内存值，不执行 /me、不读磁盘、不弹窗。

## 增加按钮的步骤

1. 在合适 group 添加 button，使用新 id、中文 label 和 onAction。
2. 在 SwiftExportRibbon 添加对应 public void 回调，参数为 Office.IRibbonControl。
3. 回调转交宿主协调器打开页面，不在 Ribbon 写查询、COM 批量操作或业务规则。
4. 需要权限控制时增加 getEnabled／getVisible，仅查询当前已验证权限键。
5. 状态会变化的控件 id 加入 Refresh；不要每次点击无差别重建 Ribbon。
6. 编译后重启调试 Excel 验证结构变化；InvalidateControl 只刷新属性，不重新加载整个 XML 结构。

## 常见问题

- 页签完全不出现：检查 ThisAddIn override、加载项是否启用、XML 是否有效。
- 找不到资源：XML 的 Build Action 必须是 EmbeddedResource，资源名要与命名空间和文件名一致。
- 按钮没有动作：检查 public、方法名大小写和参数签名；XML 没有 C# 编译器式回调检查。
- 用户名不更新：确认 IRibbonUI 已由 onLoad 保存，且在 Excel STA 调用 InvalidateControl。
- 异常被 Office 隐藏：在 Excel 选项 → 高级 → 常规中启用“显示加载项用户界面错误”（不同 Office 版本名称略有差异）。
- 点击登录卡住 Excel：禁止在回调使用 Result／Wait，getter 不允许网络请求。
- 多窗口归属错误：使用 control.Context 对应窗口，不把启动时 Application.Hwnd 永久缓存为所有窗口 Owner。

## 官方资料

- [Ribbon XML](https://learn.microsoft.com/en-us/visualstudio/vsto/ribbon-xml?view=visualstudio)
- [Ribbon 概述](https://learn.microsoft.com/en-us/visualstudio/vsto/ribbon-overview?view=visualstudio)
- [InvalidateControl](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.core.iribbonui.invalidatecontrol?view=office-pia)

本教程以项目实际账号组为准，不介绍 WPF Ribbon 控件，因为那不是 Excel 的 Ribbon 扩展机制。

## 打开注册的任务窗格

销售、采购按钮的 `onAction` 仍调用 Ribbon 回调，再由 Bootstrapper 打开页面；Ribbon XML 不设置窗格宽度，也不直接创建 View 或 DI Scope。例如销售入口最终调用：

```csharp
taskPanes.TogglePane(context.GetWindow(control), TaskPaneRegistrar.SalesOrderImport);
```

`control.Context` 优先确定触发按钮的 Excel 窗口，缺少上下文时使用活动窗口。入口保留初始化、登录验证和登录窗口打开状态门禁；Manager 再检查当前身份与有效窗口。getter 仍只读取内存状态。

标题、默认宽度和左右停靠在 `TaskPaneRegistrar.CreateDefault()` 中对应的 `TaskPaneConfig` 设置。新增页面还需在 ServiceRegistration 注册 Scoped View／ViewModel、添加唯一 XML 控件 ID 和对应回调，不再新增专用窗格 Coordinator。完整步骤见 [架构与目录说明](架构与目录说明.md#注册新的侧边栏页面)。

重复点击按旧示例切换窗格显示／隐藏；点 × 隐藏后可再次点击按钮恢复，保留编辑内容与调整后的宽度。不同 Excel 窗口使用独立页面，所属窗口实际关闭后释放。
