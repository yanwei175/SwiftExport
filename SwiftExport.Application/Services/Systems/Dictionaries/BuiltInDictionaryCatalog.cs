using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;

namespace SwiftExport.Application.Services.Systems.Dictionaries;

/// <summary>集中声明内置字典及其回退规则；只合并读取事实，不访问数据库或开启事务。</summary>
internal static class BuiltInDictionaryCatalog
{
    // 新增类型：在 Types 登记固定身份、编码及显示信息；新增明细：在 Data 登记所属类型及英文代码。
    // 发布后不能修改固定 Guid 或业务代码，否则空库默认项和已保存的业务引用会失去同一身份。
    // 普通选项仍由用户维护；不能因为数据库 IsSystem=true 就把旧记录自动提升为内置定义。
    // 若新增项被业务当作固定默认选择使用，还应同步核对该业务的代码选择和持锁读取，不自动扩展业务规则。

    /// <summary>系统必需的类型清单，缺少数据库记录时仍可读取，并允许用户增加明细。</summary>
    internal static IReadOnlyList<TypeDefinition> Types { get; } = Array.AsReadOnly<TypeDefinition>(
    [
        new(new("bdcdbce1-262c-4603-8a09-e95b7131b7ee"), DictCodes.CustomerType, "客户类型", 0, "供应商属性", "供应商属性"),
        new(new("9ebceade-c070-47e0-8c3c-d22d49f90af8"), DictCodes.Incoterm, "国际贸易术语", 20, "Sales", "销售"),
        new(new("3b42c1cf-7c63-4e81-b9ef-73e95a609b91"), DictCodes.PaymentMethod, "付款方式", 30, "Sales", "销售"),
        new(new("d7fc7d77-0907-48ba-9e65-baafca26c628"), DictCodes.ShippedBy, "运输方式", 40, "Sales", "销售"),
        new(new("9b05cb9b-2569-4d33-b4ba-49ed01e4f698"), DictCodes.Unit, "单位", 10, null, null),
        // 仅内置建议类型；具体建议由用户维护，业务文本无需命中字典明细。
        new(new("9a410a0d-21aa-4518-a751-aca226306588"), DictCodes.IncotermLocation, "贸易术语地点", 0, "结算相关", "结算相关"),
        new(new("6a1db685-fb9a-4f12-9ddd-6b0439359e28"), DictCodes.IncotermSortRemark, "贸易术语快速备注", 0, "结算相关", "结算相关"),
        new(new("207a119b-d9f6-4d55-be1a-92fecde3a3ca"), DictCodes.FlowTestFunction, "流量测试功能", 10, "QualityControl", "品质管理"),
        new(new("b9031caf-e535-4ba0-957b-98d501e251e4"), DictCodes.FlowLimiterSpecification, "限流器规格", 20, "QualityControl", "品质管理"),
        // 只登记模板类型；具体模板由用户维护，不能在目录中预置模板明细。
        new(new("00757a40-f61b-4e87-8750-db5a1c41b6f2"), DictCodes.ProductAttributeProfile, "产品属性模板", 10, "Products", "产品"),
        // 只登记系列类型；具体系列由用户在字典中维护，不自动生成明细。
        new(new("70ce6276-847b-46ab-926d-c0abcf08fb66"), DictCodes.ProductSeries, "产品系列", 20, "Products", "产品"),
        new(new("55a113d5-1eb8-4510-a21d-d5a0912734d1"), DictCodes.ProductPackageType, "产品包装类型", 30, "Products", "产品")
    ]);

    /// <summary>系统必需的明细清单；英文名称暂作稳定代码，中文名称等显示信息允许覆盖。</summary>
    internal static IReadOnlyList<DataDefinition> Data { get; } = Array.AsReadOnly<DataDefinition>(
    [
        new(new("979dc2d0-eeba-40a2-bfa0-1ea1c934aa4c"), DictCodes.FlowTestFunction, "Default", "Default", "默认测试功能"),
        new(new("fce647f2-2996-4995-a568-73e221bc5453"), DictCodes.FlowLimiterSpecification, "Unlimited", "不限流", "本功能未安装限流器"),
        // 包装角色只限定类型语义，不要求每个产品逐层具备全部包装。
        new(new("5ba7a73a-fdaa-462a-8aa3-d108e24caf09"), DictCodes.ProductPackageType, "InnerBox", "内盒", "产品内部包装，可按实际包装方式省略"),
        new(new("a07ab0bc-e867-4473-9a48-a9302925432f"), DictCodes.ProductPackageType, "ShippingBox", "单套运输盒", "装入一套产品，可容纳内盒或直接装产品"),
        new(new("ed67d590-f2b9-4458-b2b3-01e6d53854d4"), DictCodes.ProductPackageType, "Carton", "出货外箱", "最终出货的集装外箱"),
        new(new("aeb5ef4e-5cdd-48f1-aa42-b5d4058fbdad"), DictCodes.ProductPackageType, "Pallet", "托盘", "托盘包装规格")
    ]);

    /// <summary>按稳定编码识别内置类型，不按可编辑名称或客户端标记判断。</summary>
    internal static TypeDefinition? FindType(string? code) => Types.FirstOrDefault(item => Same(item.DictCode, code));

    /// <summary>按所属类型和英文代码识别内置明细。</summary>
    internal static DataDefinition? FindData(string? code, string? name) =>
        Data.FirstOrDefault(item => Same(item.DictCode, code) && Same(item.DataNameEn, name));

    /// <summary>识别尚未保存的内置明细身份；已有库的不同 Guid 仍通过业务键匹配。</summary>
    internal static DataDefinition? FindData(Guid uid) => Data.FirstOrDefault(item => item.Uid == uid);

    /// <summary>判断数据库明细是否对应已登记定义，不能用 IsSystem 代替内置身份。</summary>
    internal static bool IsBuiltIn(SysDictData item) => FindData(item.DictCode, item.DataNameEn) is not null;

    /// <summary>保留已有类型身份和显示配置，缺失时补代码默认值；不会修改输入实体。</summary>
    internal static IReadOnlyList<SysDictType> MergeTypes(IEnumerable<SysDictType> source)
    {
        var stored = source.ToArray();
        var result = stored.Where(item => !item.IsDeleted && FindType(item.DictCode) is null)
            .Select(item => item.Adapt<SysDictType>()).ToList();
        foreach (var definition in Types)
        {
            if (stored.Any(item => item.Uid == definition.Uid && !Same(item.DictCode, definition.DictCode)))
                throw new InvalidOperationException($"内置字典类型身份被其他编码占用：{definition.DictCode}。");
            var candidates = stored.Where(item => Same(item.DictCode, definition.DictCode)).ToArray();
            var active = candidates.Where(item => !item.IsDeleted).ToArray();
            if (active.Length > 1 || active.Length == 0 && candidates.Length > 1)
                throw new InvalidOperationException($"内置字典类型存在重复候选：{definition.DictCode}。");
            var existing = active.SingleOrDefault() ?? candidates.SingleOrDefault();
            var item = existing is { IsDeleted: false } ? existing.Adapt<SysDictType>() : new SysDictType
            {
                Uid = existing?.Uid ?? definition.Uid, Version = existing?.Version ?? 0,
                CreatedDate = existing?.CreatedDate, CreatedBy = existing?.CreatedBy,
                UpdatedDate = existing?.UpdatedDate, UpdatedBy = existing?.UpdatedBy,
                DictCode = definition.DictCode, DictName = definition.DictName,
                SortOrder = definition.SortOrder, GroupCode = definition.GroupCode, GroupName = definition.GroupName
            };
            item.IsSystem = true;
            result.Add(item);
        }
        return result.OrderBy(item => item.SortOrder ?? int.MaxValue)
            .ThenBy(item => item.DictCode, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>按业务键合并明细，旧 Guid 优先；停用或删除的内置记录采用默认显示值但保留身份和版本。</summary>
    internal static IReadOnlyList<SysDictData> MergeData(IEnumerable<SysDictData> source)
    {
        var stored = source.ToArray();
        var result = stored.Where(item => !item.IsDeleted && !IsBuiltIn(item))
            .Select(item => item.Adapt<SysDictData>()).ToList();
        foreach (var definition in Data)
        {
            if (stored.Any(item => item.Uid == definition.Uid
                && (!Same(item.DictCode, definition.DictCode) || !Same(item.DataNameEn, definition.DataNameEn))))
                throw new InvalidOperationException($"内置字典明细身份被其他代码占用：{definition.DictCode}/{definition.DataNameEn}。");
            var candidates = stored.Where(item => Same(item.DictCode, definition.DictCode)
                && Same(item.DataNameEn, definition.DataNameEn)).ToArray();
            var active = candidates.Where(item => !item.IsDeleted).ToArray();
            if (active.Length > 1 || active.Length == 0 && candidates.Length > 1)
                throw new InvalidOperationException($"内置字典明细存在重复候选：{definition.DictCode}/{definition.DataNameEn}。");
            var existing = active.SingleOrDefault() ?? candidates.SingleOrDefault();
            var item = existing is { IsDeleted: false, IsEnabled: true } ? existing.Adapt<SysDictData>() : new SysDictData
            {
                Uid = existing?.Uid ?? definition.Uid, Version = existing?.Version ?? 0,
                CreatedDate = existing?.CreatedDate, CreatedBy = existing?.CreatedBy,
                UpdatedDate = existing?.UpdatedDate, UpdatedBy = existing?.UpdatedBy,
                DataNameZh = definition.DataNameZh, Remark = definition.Remark, SortOrder = 0
            };
            // 内置项始终有效；异常旧记录只在显式保存覆盖时恢复数据库标记，读取本身不写库。
            item.DictCode = definition.DictCode;
            item.DataNameEn = definition.DataNameEn;
            item.IsSystem = true;
            item.IsEnabled = true;
            item.IsDeleted = false;
            result.Add(item);
        }
        return result.OrderBy(item => item.DictCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SortOrder ?? int.MaxValue)
            .ThenBy(item => item.DataNameZh, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Uid).ToArray();
    }

    /// <summary>把有效类型转为查询契约；IsBuiltIn 仅由服务端定义确定，不参与实体写入。</summary>
    internal static IReadOnlyList<SysDictTypeDto> TypeDtos(IEnumerable<SysDictType> source) => MergeTypes(source)
        .Select(item =>
        {
            var dto = item.Adapt<SysDictTypeDto>();
            dto.IsBuiltIn = FindType(item.DictCode) is not null;
            return dto;
        }).ToArray();

    /// <summary>把有效明细转为查询契约，供各客户端按相同事实控制内置项编辑。</summary>
    internal static IReadOnlyList<SysDictDataDto> DataDtos(IEnumerable<SysDictData> source) => MergeData(source)
        .Select(item =>
        {
            var dto = item.Adapt<SysDictDataDto>();
            dto.IsBuiltIn = IsBuiltIn(item);
            return dto;
        }).ToArray();

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>声明一个系统类型的固定身份及默认显示元数据。</summary>
    internal sealed record TypeDefinition(Guid Uid, string DictCode, string DictName, int SortOrder, string? GroupCode, string? GroupName);

    /// <summary>声明一个必需明细的固定身份、稳定业务键及默认显示信息。</summary>
    internal sealed record DataDefinition(Guid Uid, string DictCode, string DataNameEn, string DataNameZh, string Remark);
}
