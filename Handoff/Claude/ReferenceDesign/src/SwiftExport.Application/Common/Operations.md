# Operations（操作名常量：幂等记录与审计共用）

`SwiftExport.Application/Common/Operations.cs`

## 为什么用常量字符串，而不是枚举或对象
`command_receipt.command_type`
- 枚举存整数：改顺序或插入新成员会让历史数据含义错乱；存枚举名又要靠 `ToString()`，改名同样破坏历史。
- 包装成类（如 `record OperationName(string Value)`）只是在常量外面多套一层，没有带来新的保证。
- 关键是**不在业务代码里手写字面量**：拼错一个字母，编译器不报错，幂等重放就失效。常量能让编译器和“查找引用”起作用。

## 定义

```csharp
namespace SwiftExport.Application.Common;

// 格式：实体.动作。一旦上线不可改名（历史幂等记录和审计依赖它）；废弃的保留并标 [Obsolete]
public static class Operations
{
	public static class Product
	{
		public const string Create     = "Product.Create";
		public const string Update     = "Product.Update";
		public const string Activate   = "Product.Activate";
		public const string Deactivate = "Product.Deactivate";
	}

	public static class PurchaseOrder
	{
		public const string Create    = "PurchaseOrder.Create";
		public const string Update    = "PurchaseOrder.Update";
		public const string Delete    = "PurchaseOrder.Delete";
		public const string Confirm   = "PurchaseOrder.Confirm";
		public const string Unconfirm = "PurchaseOrder.Unconfirm";
		public const string Void      = "PurchaseOrder.Void";
		public const string Close     = "PurchaseOrder.Close";
	}

	public static class PurchaseReceipt
	{
		public const string Create  = "PurchaseReceipt.Create";
		public const string Update  = "PurchaseReceipt.Update";
		public const string Delete  = "PurchaseReceipt.Delete";
		public const string Post    = "PurchaseReceipt.Post";
		public const string Reverse = "PurchaseReceipt.Reverse";
	}
}
```

## 审计接口相应简化
操作名已经包含实体类型，`IAuditLog` 不再单独传实体类型字符串：

```csharp
public interface IAuditLog
{
	// operation 取自 Operations；entity_type 由 operation 的前缀得到，不再手写
	Task WriteAsync(string operation, Guid entityId, object? detail, CancellationToken ct);
}
```

## 与权限码的区别
| | 权限码 `Permissions` | 操作名 `Operations` |
|---|---|---|
| 位置 | Contracts（客户端要用来隐藏按钮） | Application（只有后端用） |
| 粒度 | 一个权限可覆盖多个操作（如 Confirm 权限覆盖确认与取消确认） | 一个操作一个名字 |
| 用途 | 授权 | 幂等记录、审计、日志 |

两者不要合并：粒度不同，合并后任何一方调整都会影响另一方。

## 规则
- Service 中出现的操作名、审计动作**只能**引用 `Operations.Xxx.Yyy`，禁止字符串字面量。
- 新增单据命令时先在此处加常量。
