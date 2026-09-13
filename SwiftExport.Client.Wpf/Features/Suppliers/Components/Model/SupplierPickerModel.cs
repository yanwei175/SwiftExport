using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExportClient.Features.Suppliers.Components.Model
{
    /// <summary>供应商选择弹窗的只读会话模型，通过 Mapster 与共享 DTO 隔离。</summary>
    public sealed class SupplierPickerModel : SupplierDto;
}
