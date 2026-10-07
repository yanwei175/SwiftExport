using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.ForExcel;

namespace SwiftExport.Application.Services.ForExcel;

/// <summary>检查导入快照并精确匹配，结果不能替代草稿保存的事务内校验。</summary>
public sealed class ForExcelSalesOrderService(IForExcelSalesOrderRepository repository, ICurrentUser user,
    IPermissionEvaluationService permissions) : IForExcelSalesOrderService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CheckSalesOrderImportResponse>> CheckAsync(CheckSalesOrderImportRequest request, CancellationToken token = default)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<CheckSalesOrderImportResponse>.Fail(ServiceErrorCodes.Unauthenticated,"请先登录。");
        if (!await permissions.HasPermissionAsync(actor, PermissionKeys.SalesOrderMaintain, token))
            return ServiceResult<CheckSalesOrderImportResponse>.Fail(ServiceErrorCodes.Forbidden,"没有销售订单维护权限。");
        if (request?.Header is null || request.Lines is null || request.Lines.Count is < 1 or > 500
            || request.Lines.Any(l => l is null || l.ClientLineId == Guid.Empty)
            || request.Lines.Select(l => l.ClientLineId).Distinct().Count() != request.Lines.Count)
            return ServiceResult<CheckSalesOrderImportResponse>.Fail(ServiceErrorCodes.Validation,"必须提供单头和 1 到 500 条具有唯一非空标识的明细。");
        token.ThrowIfCancellationRequested();
        var result = new CheckSalesOrderImportResponse();
        CheckHeader(request.Header,result.Issues);
        var customers = string.IsNullOrWhiteSpace(request.CustomerCode) ? []
            : await repository.FindCustomersAsync(request.CustomerCode.Trim(),token);
        if (customers.Count != 1 || !customers[0].IsActive)
        {
            Add(result.Issues,SalesOrderImportIssueCodes.CustomerUnavailable,"CustomerCode","客户不存在、停用或代码存在歧义。");
            foreach (var line in request.Lines)
            {
                var checkedLine = new SalesOrderImportLineCheckDto { ClientLineId=line.ClientLineId, MatchStatus=line.IsNonStandard ? "NonStandard" : "NotMatched" };
                CheckNumbers(line,checkedLine.Issues);
                if (line.IsNonStandard) CheckNonStandard(line,checkedLine.Issues);
                else Add(checkedLine.Issues,SalesOrderImportIssueCodes.CustomerRequired,"CustomerSku","请先定位有效客户。");
                result.Lines.Add(checkedLine);
            }
            return ServiceResult<CheckSalesOrderImportResponse>.Ok(result);
        }
        result.Customer = customers[0];
        var skus = request.Lines.Where(l=>!l.IsNonStandard && !string.IsNullOrWhiteSpace(l.CustomerSku))
            .Select(l=>Normalize(l.CustomerSku)).Distinct().ToArray();
        var candidates = await repository.MatchProductsAsync(result.Customer.Uid,skus,token);
        result.ExistingOrders = (await repository.FindExistingOrdersAsync(result.Customer.Uid,
            request.Header.InvoiceNo,request.Header.CustomerPoNo,token)).ToList();
        if (result.ExistingOrders.Count>0)
            Add(result.Issues,SalesOrderImportIssueCodes.ExistingOrders,"Header","该客户已有相同发票号或 PO 号的订单，请确认是否仍新建。",true,request.AcceptExistingOrders);
        foreach (var line in request.Lines)
        {
            token.ThrowIfCancellationRequested();
            var item = new SalesOrderImportLineCheckDto { ClientLineId=line.ClientLineId };
            CheckNumbers(line,item.Issues);
            if (line.IsNonStandard) { item.MatchStatus="NonStandard"; CheckNonStandard(line,item.Issues); }
            else
            {
                item.Candidates = candidates.Where(c=>Normalize(c.CustomerSku)==Normalize(line.CustomerSku)).ToList();
                // 用户可从返回候选显式选择，但不能传入其他客户或其他型号的关联身份。
                var selected = line.SelectedCustomerProductId.HasValue
                    ? item.Candidates.SingleOrDefault(c => c.CustomerProductId == line.SelectedCustomerProductId.Value)
                    : null;
                if (line.SelectedCustomerProductId.HasValue && selected is null)
                { item.MatchStatus="Unavailable"; Add(item.Issues,SalesOrderImportIssueCodes.SelectionInvalid,"SelectedCustomerProductId","所选关联不属于当前客户及型号候选。"); }
                else if (item.Candidates.Count==0) { item.MatchStatus="NotMatched"; Add(item.Issues,SalesOrderImportIssueCodes.ProductNotMatched,"CustomerSku","客户产品型号未匹配，请修正或明确标记非标。"); }
                else if (item.Candidates.Count!=1 && selected is null) { item.MatchStatus="Ambiguous"; Add(item.Issues,SalesOrderImportIssueCodes.ProductAmbiguous,"CustomerSku","同一客户型号存在多个关联，不能自动选择。"); }
                else if (!(selected ?? item.Candidates[0]).IsUsable || !(selected ?? item.Candidates[0]).ProductId.HasValue || (selected ?? item.Candidates[0]).ProductId==Guid.Empty
                    || string.IsNullOrWhiteSpace((selected ?? item.Candidates[0]).Sku) || string.IsNullOrWhiteSpace((selected ?? item.Candidates[0]).Unit))
                { item.MatchStatus="Unavailable"; Add(item.Issues,SalesOrderImportIssueCodes.ProductUnavailable,"CustomerSku","关联或产品不可用，或产品未设置单位。"); }
                else
                {
                    item.MatchStatus="Matched"; item.Product=selected ?? item.Candidates[0];
                    if (!string.IsNullOrWhiteSpace(line.LinkSku) && Normalize(line.LinkSku)!=Normalize(item.Product.LinkSku))
                        Add(item.Issues,SalesOrderImportIssueCodes.LinkSkuMismatch,"LinkSku","原表中性型号与系统不同，请比较后确认。",true,line.AcceptLinkSkuMismatch);
                }
            }
            result.Lines.Add(item);
        }
        result.CanSave = !result.Issues.Concat(result.Lines.SelectMany(l=>l.Issues)).Any(i=>!i.RequiresConfirmation || !i.Acknowledged);
        return ServiceResult<CheckSalesOrderImportResponse>.Ok(result);
    }
    // 型号仅去首尾空格及忽略大小写，不删除符号、前导零或内部空格。
    private static string Normalize(string value) => value?.Trim().ToUpperInvariant() ?? "";
    /// <summary>检查导入单头必填项与数值范围，向既有问题集合追加阻断信息。</summary>
    private static void CheckHeader(SalesOrderImportHeaderInput header,List<SalesOrderImportIssueDto> issues)
    {
        if (!header.OrderDate.HasValue) Add(issues,SalesOrderImportIssueCodes.OrderDateRequired,"Header.OrderDate","订单日期不能为空。");
        if (!(header.ExchangeRate>0)) Add(issues,SalesOrderImportIssueCodes.ExchangeRateInvalid,"Header.ExchangeRate","汇率必须大于零。");
        if (Normalize(header.Currency) is not ("CNY" or "USD")) Add(issues,SalesOrderImportIssueCodes.CurrencyRequired,"Header.Currency","请明确选择 CNY 或 USD。");
        if (header.DepositRate is <0 or >1) Add(issues,SalesOrderImportIssueCodes.DepositRateInvalid,"Header.DepositRate","定金比例必须在 0 到 1 之间。");
    }
    /// <summary>将非标行缺少描述或单位记录为导入问题，保持检查请求的正常响应。</summary>
    private static void CheckNonStandard(SalesOrderImportLineInput line,List<SalesOrderImportIssueDto> issues)
    {
        if (string.IsNullOrWhiteSpace(line.DescriptionEn)) Add(issues,SalesOrderImportIssueCodes.DescriptionRequired,"DescriptionEn","非标件必须填写描述。");
        if (string.IsNullOrWhiteSpace(line.Unit)) Add(issues,SalesOrderImportIssueCodes.UnitRequired,"Unit","非标件必须填写单位。");
    }
    /// <summary>检查数量、包装和金额边界；问题只阻止保存，不变更导入检查的 HTTP 状态。</summary>
    private static void CheckNumbers(SalesOrderImportLineInput line,List<SalesOrderImportIssueDto> issues)
    {
        if (!(line.Qty>0)) Add(issues,SalesOrderImportIssueCodes.QuantityInvalid,"Qty","数量必须大于零。");
        if (line.PcsOfCarton.HasValue && line.PcsOfCarton<=0) Add(issues,SalesOrderImportIssueCodes.PackingInvalid,"PcsOfCarton","每箱数量必须大于零。");
        foreach (var (value,field) in new[]{(line.CnyPrice,"CnyPrice"),(line.UsdPrice,"UsdPrice"),(line.GwOfCarton,"GwOfCarton"),(line.NwOfCarton,"NwOfCarton"),(line.CbmOfCarton,"CbmOfCarton")})
            if (value<0) Add(issues,SalesOrderImportIssueCodes.NumberNegative,field,"不能为负数。");
        try { _=line.Qty*line.CnyPrice; _=line.Qty*line.UsdPrice; }
        catch (OverflowException) { Add(issues,SalesOrderImportIssueCodes.AmountOverflow,"Qty","数量与单价乘积超出支持范围。"); }
    }
    private static void Add(List<SalesOrderImportIssueDto> issues,string code,string field,string message,bool confirmation=false,bool acknowledged=false)
        => issues.Add(new SalesOrderImportIssueDto { Code=code,Field=field,Message=message,RequiresConfirmation=confirmation,Acknowledged=acknowledged });
}
