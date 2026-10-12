namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>通知管理页一个主型号详情已经提交；只传递身份，不把详情草稿跨页面共享。</summary>
public sealed class ProductMasterDetailSavedMessage(Guid productMasterId)
{
    /// <summary>已保存主型号的稳定身份，管理页按身份重读服务端当前投影。</summary>
    public Guid ProductMasterId { get; } = productMasterId;
}
