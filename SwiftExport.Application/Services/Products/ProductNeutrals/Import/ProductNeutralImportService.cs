using System.Globalization;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Import;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;

namespace SwiftExport.Application.Services.Products.ProductNeutrals.Import;

/// <summary>中性资料的一百行导入用例；按全局编码识别记录，逐行锁定引用并提交，保留主图标记。</summary>
public sealed class ProductNeutralImportService(IProductMasterRepository masters, IProductColorRepository colors,
    IProductNeutralRepository neutrals, ICurrentUser user, IPermissionEvaluationService permissions,
    ServiceBaseContext context) : IProductNeutralImportService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<ProductNeutralImportService>();

    /// <inheritdoc />
    public async Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<ProductNeutralImportRowDto> rows, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is not { } id || id == Guid.Empty)
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Unauthenticated, "请先登录。");
        var projection = await permissions.GetProjectionAsync(id, cancellationToken);
        if (projection is null) return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Unauthenticated, "账号无效或已停用。");
        if (!projection.IsAdmin) return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Forbidden, "只有管理员可以提交中性产品导入。");
        if (rows is not { Count: > 0 and <= 100 } || rows.Any(row => row is null || row.SourceRowNumber is < 2 or > 1_048_576)
            || rows.Select(row => row.SourceRowNumber).Distinct().Count() != rows.Count)
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Validation, "每批须为 1 至 100 行，原文件行号必须有效且不重复。");
        if (context.Session.Transaction is not null)
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已有未完成事务。");
        var duplicates = rows.GroupBy(row => Text(row.ProductCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).SelectMany(group => group).Select(row => row.SourceRowNumber).ToHashSet();
        var response = new BatchSaveResponseBase { TotalCount = rows.Count };
        try
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                response.Items.Add(await SaveRowAsync(row, duplicates.Contains(row.SourceRowNumber), cancellationToken));
            }
            return ServiceResult<BatchSaveResponseBase>.Ok(response, $"成功 {response.SuccessCount} 行，失败 {response.FailedCount} 行。");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "中性导入批次中断，之前完成的行可能已经提交");
            return ServiceResult<BatchSaveResponseBase>.Fail(ServiceErrorCodes.Unexpected, "批次处理已中断，部分行可能已保存，请核对后重新导入。");
        }
    }

    /// <summary>先观察稳定身份，再按颜色、父级、中性顺序锁定；普通导入不能将编码搬到另一父级。</summary>
    private async Task<SaveItemResult> SaveRowAsync(ProductNeutralImportRowDto row, bool duplicate, CancellationToken token)
    {
        var receipt = new SaveItemResult { RowIndex = row.SourceRowNumber, Operation = SaveOperation.Create };
        try
        {
            var data = Parse(row);
            if (duplicate) throw new RowFailure(ServiceErrorCodes.Validation, "文件中中性产品编码重复。");
            context.Session.Transaction = context.Session.Connection.BeginTransaction();
            var observedMasters = await masters.GetByMasterSkusAsync([Text(row.MasterSku)!], false, token);
            if (observedMasters.Count != 1) throw new RowFailure(ServiceErrorCodes.Validation, "主型号不存在或匹配不唯一。");
            var parent = observedMasters[0];
            var observedOwners = await neutrals.GetByProductCodesAsync([data.ProductCode], false, token);
            if (observedOwners.Count > 1) throw new RowFailure(ServiceErrorCodes.Validation, "中性产品编码匹配不唯一。");
            var observed = observedOwners.SingleOrDefault();
            if (observed is not null && observed.ProductMasterId != parent.Uid)
                throw new RowFailure(ServiceErrorCodes.Validation, "中性产品编码已属于其他主型号，本模板不调整所属主型号。");
            var foundColors = await colors.GetByColorCodesAsync([Text(row.ColorCodeUnique)!]);
            if (foundColors.Count != 1) throw new RowFailure(ServiceErrorCodes.Validation, "颜色唯一编码不存在或匹配不唯一。");
            token.ThrowIfCancellationRequested();
            var color = await colors.GetByIdAsync(foundColors[0].Uid, true);
            if (color is null || color.IsDeleted || !SameCode(color.ColorCodeUnique, row.ColorCodeUnique))
                throw new RowFailure(ServiceErrorCodes.Validation, "颜色已删除或编码变化，请核对后重试。");
            var lockedParents = await masters.GetByMasterSkusAsync([Text(row.MasterSku)!], true, token);
            if (lockedParents.Count != 1 || lockedParents[0].IsDeleted || lockedParents[0].Uid != parent.Uid)
                throw new RowFailure(ServiceErrorCodes.VersionConflict, "主型号身份或编码已变化，请核对后重试。");
            // 先无锁重验归属，再只锁本父级的已知 Uid，避免持父锁等待另一父级的中性行。
            var owners = await neutrals.GetByProductCodesAsync([data.ProductCode], false, token);
            if (owners.Count > 1) throw new RowFailure(ServiceErrorCodes.Validation, "中性产品编码匹配不唯一。");
            var currentOwner = owners.SingleOrDefault();
            // 父锁保护当前归属；观察后新增、删除或搬走的编码不得静默改变保存操作。
            if (currentOwner?.Uid != observed?.Uid || currentOwner is not null && currentOwner.ProductMasterId != parent.Uid)
                throw new RowFailure(ServiceErrorCodes.VersionConflict, "中性产品编码身份或所属主型号已变化，请核对后重试。");
            var existing = currentOwner is null ? null : await neutrals.GetByIdAsync(currentOwner.Uid, true);
            if (currentOwner is not null && (existing is null || existing.IsDeleted || existing.ProductMasterId != parent.Uid || !SameCode(existing.ProductCode, data.ProductCode)))
                throw new RowFailure(ServiceErrorCodes.VersionConflict, "中性产品编码身份或所属主型号已变化，请核对后重试。");
            receipt.Operation = existing is null ? SaveOperation.Create : SaveOperation.Update;
            receipt.Uid = existing?.Uid; receipt.OldVersion = existing?.Version;
            var entity = existing?.Adapt<ProductNeutral>() ?? new ProductNeutral();
            entity.ProductCode = existing?.ProductCode ?? data.ProductCode;
            entity.ProductMasterId = parent.Uid; entity.ColorId = color.Uid;
            entity.DefaultPurchasePriceCny = data.DefaultPurchasePriceCny;
            entity.DefaultSalesPriceUsd = data.DefaultSalesPriceUsd;
            entity.DefaultExchangeRate = data.DefaultExchangeRate;
            entity.ProfitMargin = data.ProfitMargin;
            entity.ImagePath = data.ImagePath;
            // 复制原实体保留主图与审计；新实体使用 false 默认主图，不修改父级或物料。
            token.ThrowIfCancellationRequested();
            var write = existing is null ? await neutrals.AddAsync(entity) : await neutrals.UpdateAsync(entity);
            if (!write.Success)
            {
                var code = write.ErrorCode switch
                {
                    ProductErrorCodes.ProductNeutralCodeOccupied => ServiceErrorCodes.Validation,
                    ServiceErrorCodes.ConcurrencyConflict => ServiceErrorCodes.VersionConflict,
                    _ => ServiceErrorCodes.BusinessBlocked
                };
                throw new RowFailure(code, write.Message ?? "保存失败，请核对后重试。");
            }
            token.ThrowIfCancellationRequested();
            context.Session.Transaction.Commit();
            receipt.Success = true; receipt.Uid = write.Uid; receipt.NewVersion = write.NewVersion;
            return receipt;
        }
        catch (RowFailure failure)
        {
            context.Session.Transaction?.Rollback();
            receipt.ErrorCode = failure.Code; receipt.Message = failure.Message;
            return receipt;
        }
        catch
        {
            // 技术故障或取消停止批次；此前提交的行不撤销，不能自动重试整个请求。
            try { context.Session.Transaction?.Rollback(); }
            catch (Exception failure) { _logger.LogError(failure, "中性导入行回滚失败"); }
            throw;
        }
        finally { context.Session.Transaction?.Dispose(); context.Session.Transaction = null; }
    }

    /// <summary>固定文化解析完整输入，分别限制价格、汇率和利润率精度，不进行价格计算。</summary>
    private static ProductNeutral Parse(ProductNeutralImportRowDto row)
    {
        var errors = new List<string>();
        var data = new ProductNeutral
        {
            ProductCode = Value(row.ProductCode, 300, "中性产品编码")!,
            DefaultPurchasePriceCny = Number(row.DefaultPurchasePriceCny, 4, "默认人民币采购价"),
            DefaultSalesPriceUsd = Number(row.DefaultSalesPriceUsd, 4, "默认美元销售价"),
            DefaultExchangeRate = Number(row.DefaultExchangeRate, 6, "默认汇率", positive: true),
            ProfitMargin = Number(row.ProfitMargin, 4, "利润率", margin: true),
            ImagePath = Value(row.ImagePath, 1000, "图片地址")
        };
        if (Value(row.MasterSku, 300, "主型号") is null) errors.Add("主型号不能为空。");
        if (data.ProductCode is null) errors.Add("中性产品编码不能为空。");
        if (Value(row.ColorCodeUnique, 300, "颜色唯一编码") is null) errors.Add("颜色唯一编码不能为空。");
        if (errors.Count > 0) throw new RowFailure(ServiceErrorCodes.Validation, string.Join("；", errors));
        return data;

        string? Value(string? raw, int length, string name)
        { var value = Text(raw); if (value?.Length > length) errors.Add($"{name}不能超过 {length} 字符"); return value; }
        decimal? Number(string? raw, int scale, string name, bool positive = false, bool margin = false)
        {
            if (Text(raw) is not { } value) return null;
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && (positive ? number > 0 : number >= 0) && (!margin || number < 1)
                && number < (scale == 4 ? 100_000_000_000_000m : 1_000_000_000_000m) && decimal.Round(number, scale) == number) return number;
            errors.Add($"{name}须为{(margin ? "大于等于零且小于一" : positive ? "正数" : "非负数")}，最多 {scale} 位小数且不超数据库容量"); return null;
        }
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool SameCode(string? first, string? second) => string.Equals(Text(first), Text(second), StringComparison.OrdinalIgnoreCase);
    /// <summary>只携带可确定返回给单行的业务失败，技术错误不得伪装成普通回执。</summary>
    private sealed class RowFailure(string code, string message) : Exception(message)
    { internal string Code { get; } = code; }
}
