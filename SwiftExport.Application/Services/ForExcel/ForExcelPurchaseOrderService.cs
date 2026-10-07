using Mapster;
using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.ForExcel;
namespace SwiftExport.Application.Services.ForExcel;

/// <summary>只读检查采购快照；不改输入、写库、关联需求或计算整单优惠。</summary>
public sealed class ForExcelPurchaseOrderService(IForExcelPurchaseOrderRepository repository, ICurrentUser user,
    IPermissionEvaluationService permissions) : IForExcelPurchaseOrderService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CheckPurchaseOrderImportResponse>> CheckAsync(CheckPurchaseOrderImportRequest request, CancellationToken token = default)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<CheckPurchaseOrderImportResponse>.Fail(ServiceErrorCodes.Unauthenticated,"请先登录。");
        if (!await permissions.HasPermissionAsync(actor,PermissionKeys.ProcurementOrderCreate,token))
            return ServiceResult<CheckPurchaseOrderImportResponse>.Fail(ServiceErrorCodes.Forbidden,"没有采购创建权限。");
        if (request?.Header is null || request.Lines is null || request.Lines.Count is < 1 or > 500
            || request.Lines.Any(x => x is null || x.ClientLineId == Guid.Empty)
            || request.Lines.Select(x => x.ClientLineId).Distinct().Count() != request.Lines.Count)
            return ServiceResult<CheckPurchaseOrderImportResponse>.Fail(ServiceErrorCodes.Validation,"须提供单头和1至500条具有唯一非空标识的明细。");
        token.ThrowIfCancellationRequested();
        var result = new CheckPurchaseOrderImportResponse();
        try { IndependentPurchaseOrderInputPolicy.ValidateHeader(request.Header.Adapt<CreateIndependentPurchaseOrderCommand>()); }
        catch (ProcurementOperationException ex) { Add(result.Issues,ex.Code,ex.Field,ex.Message); }
        var customers = string.IsNullOrWhiteSpace(request.CustomerCode) ? []
            : await repository.FindCustomersAsync(request.CustomerCode.Trim(),token);
        if (customers.Count != 1 || !customers[0].IsActive || customers[0].Uid == Guid.Empty)
            Add(result.Issues,PurchaseOrderImportIssueCodes.CustomerUnavailable,"CustomerCode","客户不存在、停用或代码有歧义。");
        else result.Customer = customers[0];
        var supplier = request.SupplierId == Guid.Empty ? null : await repository.FindSupplierAsync(request.SupplierId,token);
        if (!UsableSupplier(supplier))
            Add(result.Issues,PurchaseOrderImportIssueCodes.SupplierUnavailable,"SupplierId","请选择有效实际供应商。");
        else result.Supplier = supplier;
        var skus = request.Lines.Where(x => !x.IsNonStandard && !string.IsNullOrWhiteSpace(x.CustomerSku))
            .Select(x => IndependentPurchaseOrderInputPolicy.Normalize(x.CustomerSku)).Distinct().ToArray();
        var candidates = result.Customer is null ? [] : await repository.MatchProductsAsync(result.Customer.Uid,skus,token);
        foreach (var line in request.Lines)
        {
            token.ThrowIfCancellationRequested();
            var item = new PurchaseOrderImportLineCheckDto
            {
                ClientLineId = line.ClientLineId,
                MatchStatus = line.IsNonStandard ? "NonStandard" : "NotMatched"
            };
            try { IndependentPurchaseOrderInputPolicy.ValidateLine(line.Adapt<CreateIndependentPurchaseOrderLineCommand>()); }
            catch (ProcurementOperationException ex) { Add(item.Issues,ex.Code,ex.Field,ex.Message); }
            if (line.IsNonStandard)
            {
                if (!string.IsNullOrWhiteSpace(line.Unit) && await repository.FindUnitAsync(line.Unit,token) is null)
                    Add(item.Issues,PurchaseOrderImportIssueCodes.UnitUnavailable,"Unit","非标单位未启用或有歧义。");
            }
            else if (result.Customer is null)
                Add(item.Issues,PurchaseOrderImportIssueCodes.CustomerRequired,"CustomerSku","请先定位有效客户。");
            else
            {
                item.Candidates = candidates.Where(x => IndependentPurchaseOrderInputPolicy.Normalize(x.CustomerSku)
                    == IndependentPurchaseOrderInputPolicy.Normalize(line.CustomerSku)).ToList();
                try
                {
                    var product = IndependentPurchaseOrderInputPolicy.Resolve(line.CustomerSku,line.SelectedCustomerProductId,candidates);
                    if (await repository.FindUnitAsync(product.Unit,token) is null)
                        throw new ProcurementOperationException(ProcurementErrorCodes.ProductUnavailable,"产品基础单位未启用或有歧义。","Unit");
                    item.Product = product; item.MatchStatus = "Matched";
                    if (!string.IsNullOrWhiteSpace(line.LinkSku) && IndependentPurchaseOrderInputPolicy.Normalize(line.LinkSku)
                        != IndependentPurchaseOrderInputPolicy.Normalize(product.LinkSku))
                        Add(item.Issues,PurchaseOrderImportIssueCodes.LinkSkuMismatch,"LinkSku","本司型号与系统中性型号不同，请核对后确认。",true,line.AcceptLinkSkuMismatch);
                    if (product.MaterialType == MaterialTypesEnum.客供件
                        && (!line.DeliverySupplierId.HasValue || !UsableSupplier(await repository.FindSupplierAsync(line.DeliverySupplierId.Value,token))))
                        Add(item.Issues,PurchaseOrderImportIssueCodes.DeliverySupplierRequired,"DeliverySupplierId","客供件必须选择有效送达供应商。");
                }
                catch (ProcurementOperationException ex)
                {
                    item.MatchStatus = ex.Code == ProcurementErrorCodes.ProductAmbiguous ? "Ambiguous"
                        : ex.Code == ProcurementErrorCodes.ProductNotMatched ? "NotMatched" : "Unavailable";
                    Add(item.Issues,ex.Code,ex.Field,ex.Message);
                }
            }
            result.Lines.Add(item);
        }
        result.CanSave = !result.Issues.Concat(result.Lines.SelectMany(x => x.Issues))
            .Any(x => !x.RequiresConfirmation || !x.Acknowledged);
        return ServiceResult<CheckPurchaseOrderImportResponse>.Ok(result);
    }

    /// <summary>供应商资格与采购创建保持一致；不以默认供应商或名称推断身份。</summary>
    private static bool UsableSupplier(PurchaseOrderImportSupplierDto? supplier)
        => supplier is { IsActive: true } && supplier.Uid != Guid.Empty
            && !string.IsNullOrWhiteSpace(supplier.SupplierCode) && !string.IsNullOrWhiteSpace(supplier.SupplierName);

    /// <summary>记录字段问题，用户确认只能解除明确允许的差异提示。</summary>
    private static void Add(List<PurchaseOrderImportIssueDto> issues,string code,string? field,string message,
        bool confirmation = false,bool acknowledged = false)
        => issues.Add(new() { Code=code,Field=field ?? "",Message=message,RequiresConfirmation=confirmation,Acknowledged=acknowledged });
}
