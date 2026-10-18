using System.Globalization;
using SwiftExport.Application.Services.SalesOrders.Internal;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Systems.SystemParameters;

namespace SwiftExport.Application.Services.SalesOrders.Draft;

/// <summary>仅为新建且未指定交期的订单建立日期快照，不重算既有订单或行交期。</summary>
public sealed class SalesOrderDefaultDeliveryDatePolicy(ISystemParameterRepository repository)
{
    /// <summary>尚未配置系统参数时采用的自然日天数。</summary>
    public const int InitialDays = 60;

    /// <summary>在新建保存分支应用默认值；显式交期优先，非法持久值和日期溢出明确阻断保存。</summary>
    public async Task ApplyForCreateAsync(SalesOrderHeader header, CancellationToken cancellationToken = default)
    {
        if (header.DeliveryDate.HasValue) return;
        if (!header.OrderDate.HasValue) throw new SalesOrderValidationException("订单日期不能为空。");
        var parameter = await repository.FindAsync(SystemParameterKeys.SalesDefaultDeliveryDays, cancellationToken);
        var days = InitialDays;
        if (parameter is not null && !TryReadDays(parameter.ParameterValue, out days))
            throw new SalesOrderValidationException("销售默认交期天数的已保存参数无效，请管理员设置非负整数后重试。");
        try { header.DeliveryDate = header.OrderDate.Value.Date.AddDays(days); }
        catch (ArgumentOutOfRangeException)
        { throw new SalesOrderValidationException("订单日期加默认交期天数超出有效日期范围，请指定有效交期或调整系统参数。"); }
    }

    /// <summary>校验非负整数自然日；不接受小数、负数或超出整数范围的文本。</summary>
    internal static bool TryReadDays(string? value, out int days) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out days) && days >= 0;
}
