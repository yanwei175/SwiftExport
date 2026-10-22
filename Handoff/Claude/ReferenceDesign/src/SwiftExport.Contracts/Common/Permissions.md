# Permissions（权限码常量）

```csharp
namespace SwiftExport.Contracts.Common;

// 格式：模块.功能.操作。客户端用于隐藏按钮，后端用于 [HasPermission]。
public static class Permissions
{
	public static class Products
	{
		public const string View = "MasterData.Product.View";
		public const string Edit = "MasterData.Product.Edit";
	}
	public static class PurchaseOrders
	{
		public const string View    = "Procurement.PurchaseOrder.View";
		public const string Edit    = "Procurement.PurchaseOrder.Edit";
		public const string Confirm = "Procurement.PurchaseOrder.Confirm"; // 确认 + 取消确认
		public const string Void    = "Procurement.PurchaseOrder.Void";
		public const string Close   = "Procurement.PurchaseOrder.Close";
	}
	public static class PurchaseReceipts
	{
		public const string View    = "Inventory.PurchaseReceipt.View";
		public const string Edit    = "Inventory.PurchaseReceipt.Edit";
		public const string Post    = "Inventory.PurchaseReceipt.Post";
		public const string Reverse = "Inventory.PurchaseReceipt.Reverse";
	}
}
```

规则：新增功能先在这里加常量，再在数据库权限表登记；客户端通过 `GET api/me/permissions` 获取当前用户权限列表。
