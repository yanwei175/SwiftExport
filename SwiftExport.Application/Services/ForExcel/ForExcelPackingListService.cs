using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Customers.Management;
namespace SwiftExport.Application.Services.ForExcel;
/// <summary>按装箱创建权限执行只读导入检查；结果不能替代保存的独立复核。</summary>
public sealed class ForExcelPackingListService(PackingListImportChecker checker, ICustomerRepository customers,
    ICurrentUser user, IPermissionEvaluationService permissions) : IForExcelPackingListService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CheckPackingListImportResponse>> CheckAsync(CheckPackingListImportRequest request, CancellationToken token = default)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor) return ServiceResult<CheckPackingListImportResponse>.Fail(ServiceErrorCodes.Unauthenticated, "请先登录。");
        if (!await permissions.HasPermissionAsync(actor, "Shipping.PackingList.Create", token)) return ServiceResult<CheckPackingListImportResponse>.Fail(ServiceErrorCodes.Forbidden, "没有装箱单创建权限。");
        if (request?.Header == null || request.Lines == null || request.Lines.Count is < 1 or > 500 || request.Lines.Any(x => x == null || x.Uid == Guid.Empty)
            || request.Lines.Select(x => x.Uid).Distinct().Count() != request.Lines.Count)
            return ServiceResult<CheckPackingListImportResponse>.Fail(ServiceErrorCodes.Validation, "请提供单头与 1 至 500 条具有唯一身份的明细。");
        var result = new CheckPackingListImportResponse();
        var customer = await customers.GetByIdAsync(request.Header.CustomerId);
        if (customer == null || customer.IsDeleted || !customer.IsActive)
            result.Issues.Add(new() { Code = PackingListImportIssueCodes.CustomerUnavailable, Field = "Header.CustomerId", Message = "请选择有效客户。" });
        if (request.Header.InvoiceDate == default)
            result.Issues.Add(new() { Code = PackingListImportIssueCodes.DateRequired, Field = "Header.InvoiceDate", Message = "请填写单据日期。" });
        foreach (var text in new[] { (request.Header.CustomerNameSnapshot, 300, "CustomerNameSnapshot"),
            (request.Header.CustomerAddressSnapshot, 2000, "CustomerAddressSnapshot"), (request.Header.AttentionSnapshot, 2000, "AttentionSnapshot"),
            (request.Header.ContainerNo, 100, "ContainerNo"), (request.Header.SealNo, 100, "SealNo"),
            (request.Header.ShippingLocationSnapshot, 200, "ShippingLocationSnapshot"), (request.Header.Note, 2000, "Note") })
            if (text.Item1?.Trim().Length > text.Item2)
                result.Issues.Add(new() { Code = PackingListImportIssueCodes.TextTooLong, Field = "Header." + text.Item3, Message = $"文本长度不能超过 {text.Item2} 个字符。" });
        result.Lines = await checker.CheckAsync(request.Header.CustomerId, request.Lines, token);
        result.CanSave = !result.Issues.Concat(result.Lines.SelectMany(x => x.Issues)).Any(x => !x.RequiresConfirmation || !x.Acknowledged);
        return ServiceResult<CheckPackingListImportResponse>.Ok(result);
    }
}
