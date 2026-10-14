using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.Systems.Internal;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Services.Systems.Dictionaries.Data;

/// <summary>读取有效字典并维护用户明细或内置显示覆盖；单条及批量写入执行同一保护与事务规则。</summary>
public sealed class SysDictDataService(
    ISysDictDataRepository repository,
    ISysDictTypeRepository dictTypeRepository,
    ServiceBaseContext context,
    IProductAttributeRepository attributes)
    : BaseService<SysDictData, SysDictDataDto>(repository, context), ISysDictDataService
{
    private readonly ILogger<SysDictDataService> _logger = context.LoggerFactory.CreateLogger<SysDictDataService>();

    /// <summary>合并数据库事实与代码内置项，不初始化或修复数据库。</summary>
    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAllAsync()
    {
        try
        {
            var stored = await repository.GetForBuiltInMergeAsync();
            return ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(BuiltInDictionaryCatalog.DataDtos(stored));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "读取有效字典明细失败。");
            return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail(ServiceErrorCodes.Unexpected, "读取字典失败，请联系管理员核对配置。");
        }
    }

    /// <summary>先合并再筛选和分页，保证默认项参与总数、关键词及稳定排序。</summary>
    public async Task<ServiceResult<PagedResponseBase<SysDictDataDto>>> GetPagedAsync(SysDictDataSearchRequest request)
    {
        var result = await GetAllAsync();
        if (!result.Success)
            return ServiceResult<PagedResponseBase<SysDictDataDto>>.Fail(result.Error.Code, result.Message);
        request ??= new SysDictDataSearchRequest();
        IEnumerable<SysDictDataDto> items = result.Data;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            items = items.Where(item => new[] { item.DictCode, item.DataNameEn, item.DataNameZh, item.Remark }
                .Any(value => value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true));
        }
        var codes = request.BatchKeywords?.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codes is { Count: > 0 }) items = items.Where(item => codes.Contains(item.DictCode.Trim()));
        var filtered = items.ToArray();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        return ServiceResult<PagedResponseBase<SysDictDataDto>>.Ok(new()
        {
            Items = filtered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = filtered.Length, PageIndex = pageIndex, PageSize = pageSize
        });
    }

    /// <summary>返回指定类型的有效明细，缺失数据库类型不阻断代码内置类型读取。</summary>
    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode)
    {
        if (string.IsNullOrWhiteSpace(dictCode)) return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail("字典编码不能为空。");
        // 复用完整有效清单，跨类型的固定身份占用也必须被发现，不能只看当前组生成默认项。
        var result = await GetAllAsync();
        if (!result.Success) return result;
        return ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(result.Data
            .Where(item => string.Equals(item.DictCode?.Trim(), dictCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    /// <summary>按有效身份读取；不能把旧库已经采用的 Guid 静默映射为另一个默认身份。</summary>
    public override async Task<ServiceResult<SysDictDataDto>> GetByIdAsync(Guid uid)
    {
        var result = await GetAllAsync();
        if (!result.Success) return ServiceResult<SysDictDataDto>.Fail(result.Error.Code, result.Message);
        var item = result.Data.SingleOrDefault(item => item.Uid == uid);
        return item is null ? ServiceResult<SysDictDataDto>.Fail(ServiceErrorCodes.NotFound, "字典明细不存在。")
            : ServiceResult<SysDictDataDto>.Ok(item);
    }

    /// <summary>把单条新增适配为原子批次，避免继承通用入口绕过内置身份保护。</summary>
    public override async Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<SysDictDataDto> item)
    {
        var result = await BatchSaveAsync(new() { CreateItems = [item] });
        return result.Success ? ServiceResult<SaveItemResult>.Ok(result.Data.Items.Single())
            : ServiceResult<SaveItemResult>.Fail(result.Error.Code, result.Message);
    }

    /// <summary>单条更新也支持内置零版本首次覆盖，所有身份校验仍由批次执行。</summary>
    public override async Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<SysDictDataDto> item)
    {
        var result = await BatchSaveAsync(new() { UpdateItems = [item] });
        return result.Success ? ServiceResult<SaveItemResult>.Ok(result.Data.Items.Single())
            : ServiceResult<SaveItemResult>.Fail(result.Error.Code, result.Message);
    }

    /// <summary>单条删除复用批次中的系统项保护和版本校验。</summary>
    public override async Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item)
    {
        var result = await BatchSaveAsync(new() { DeleteItems = [item] });
        return result.Success ? ServiceResult<SaveItemResult>.Ok(result.Data.Items.Single())
            : ServiceResult<SaveItemResult>.Fail(result.Error.Code, result.Message);
    }

    /// <summary>按类型锁、明细锁、校验、写入的顺序保存整批变更；失败不接纳部分回执。</summary>
    public async Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSaveAsync(SysDictDataBatchSaveRequest batchReq)
    {
        var error = ValidateRequest(batchReq);
        if (error is not null) return ServiceResult<SysDictDataBatchSaveResponse>.Fail(ServiceErrorCodes.Validation, error);
        if (_session.Transaction is not null)
            return ServiceResult<SysDictDataBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始字典明细批量保存。");
        var response = new SysDictDataBatchSaveResponse { TotalCount = batchReq.TotalCount };
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();
            var existing = await PrepareBatchSaveAsync(batchReq);
            foreach (var item in batchReq.CreateItems)
            {
                var entity = item.Data.Adapt<SysDictData>();
                entity.IsSystem = false;
                entity.IsDeleted = false;
                // 新增身份、版本和审计由仓储生成，客户端不能伪造创建或最后修改人员。
                entity.Version = 1;
                entity.CreatedDate = entity.UpdatedDate = null;
                entity.CreatedBy = entity.UpdatedBy = null;
                var write = await repository.AddAsync(entity);
                if (!write.Success) throw new DictionaryWriteException(write.ErrorCode, write.Message ?? "新增字典明细失败。");
                response.Items.Add(SaveItemResultFactory.FromCreate(item, write));
            }
            foreach (var item in batchReq.UpdateItems)
            {
                var entity = existing[item.Uid];
                var isSystem = entity.IsSystem;
                var definition = BuiltInDictionaryCatalog.FindData(entity.DictCode, entity.DataNameEn);
                SystemBatchSaveSupport.AdaptPreservingServerFields(item.Data, entity);
                entity.IsSystem = definition is not null || isSystem;
                entity.Version = item.Version;
                if (item.Version == 0) { entity.UpdatedDate = null; entity.UpdatedBy = null; }
                // 虚拟项首次保存必须保留固定 Guid；内置正版本覆盖也负责显式恢复异常旧记录。
                var write = definition is not null
                    ? await repository.SaveBuiltInOverrideAsync(entity, item.Version)
                    : await repository.UpdateAsync(entity);
                if (!write.Success) throw new DictionaryWriteException(write.ErrorCode, write.Message ?? "更新字典明细失败。");
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, write));
            }
            foreach (var item in batchReq.DeleteItems)
            {
                var write = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                if (!write.Success) throw new DictionaryWriteException(write.ErrorCode, write.Message ?? "删除字典明细失败。");
                response.Items.Add(SaveItemResultFactory.FromDelete(item, write));
            }
            _session.Transaction.Commit();
            return ServiceResult<SysDictDataBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (DictionaryWriteException exception)
        {
            SystemBatchSaveSupport.TryRollback(_session, _logger, "字典明细批量保存");
            return ServiceResult<SysDictDataBatchSaveResponse>.Fail(exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            SystemBatchSaveSupport.TryRollback(_session, _logger, "字典明细批量保存");
            _logger.LogError(exception, "字典明细批量保存发生未预期异常。");
            return ServiceResult<SysDictDataBatchSaveResponse>.Fail(ServiceErrorCodes.Unexpected, "字典明细批量保存失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>先锁相关类型再锁目标明细；内置类型由代码定义保障，不为维护明细强制插入类型记录。</summary>
    private async Task<Dictionary<Guid, SysDictData>> PrepareBatchSaveAsync(SysDictDataBatchSaveRequest request)
    {
        foreach (var item in request.CreateItems)
        {
            var error = NormalizeAndValidate(item.Data, item.RowIndex, "新增");
            if (error is not null) throw new DictionaryWriteException(ServiceErrorCodes.Validation, error);
            if (BuiltInDictionaryCatalog.FindData(item.Data.DictCode, item.Data.DataNameEn) is not null)
                throw new DictionaryWriteException(ServiceErrorCodes.Validation, "内置字典明细不能重复新增，请读取后修改显示信息。");
        }
        foreach (var item in request.UpdateItems)
        {
            var error = NormalizeAndValidate(item.Data, item.RowIndex, "修改");
            if (error is not null) throw new DictionaryWriteException(ServiceErrorCodes.Validation, error);
        }
        var targetIds = request.UpdateItems.Select(item => item.Uid).Concat(request.DeleteItems.Select(item => item.Uid)).Distinct().ToArray();
        // 此处只读用于确定锁集合，随后重读持锁事实；不能先锁明细再反向取得类型锁。
        var stored = await repository.GetForBuiltInMergeAsync();
        var codes = request.CreateItems.Select(item => item.Data.DictCode).Concat(request.UpdateItems.Select(item => item.Data.DictCode))
            .Concat(stored.Where(item => targetIds.Contains(item.Uid)).Select(item => item.DictCode!))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var types = await dictTypeRepository.GetByDictCodesAsync(codes, withUpdLock: true);
        if (codes.Any(code => BuiltInDictionaryCatalog.FindType(code) is null
            && !types.Any(type => string.Equals(type.DictCode.Trim(), code, StringComparison.OrdinalIgnoreCase))))
            throw new DictionaryWriteException(ServiceErrorCodes.Validation, "所选字典类型不存在。");
        // 类型锁取得后再读取候选，避免等待期间别人首次配置造成陈旧的零版本判断。
        stored = await repository.GetForBuiltInMergeAsync();
        var candidateIds = stored.Where(item => request.UpdateItems.Any(update => update.Version == 0
            && BuiltInDictionaryCatalog.FindData(item.DictCode, item.DataNameEn) == BuiltInDictionaryCatalog.FindData(update.Uid)))
            .Select(item => item.Uid);
        // 明细按稳定身份锁定，包含待显式恢复的软删除内置记录，不能从无锁快照直接补写。
        var existing = (await repository.GetByIdsForUpdateAsync(targetIds.Concat(candidateIds).Distinct())).ToDictionary(item => item.Uid);
        foreach (var update in request.UpdateItems)
        {
            if (update.Version == 0)
            {
                var definition = BuiltInDictionaryCatalog.FindData(update.Uid)!;
                if (stored.Any(item => BuiltInDictionaryCatalog.FindData(item.DictCode, item.DataNameEn) == definition)
                    || existing.ContainsKey(update.Uid))
                    throw new DictionaryWriteException(ServiceErrorCodes.VersionConflict, "内置项已由其他人配置，请重新读取后保存。");
                existing[update.Uid] = BuiltInDictionaryCatalog.MergeData([]).Single(item => item.Uid == update.Uid);
            }
            if (!existing.TryGetValue(update.Uid, out var entity))
                throw new DictionaryWriteException(ServiceErrorCodes.NotFound, "修改的字典明细不存在。");
            if (entity.IsDeleted && !BuiltInDictionaryCatalog.IsBuiltIn(entity))
                throw new DictionaryWriteException(ServiceErrorCodes.NotFound, "修改的字典明细已删除。");
            if (entity.Version != update.Version)
                throw new DictionaryWriteException(ServiceErrorCodes.VersionConflict, "字典明细版本已变化，请重新读取后保存。");
            var definitionForItem = BuiltInDictionaryCatalog.FindData(entity.DictCode, entity.DataNameEn);
            if (definitionForItem is not null)
            {
                if (!string.Equals(update.Data.DictCode, definitionForItem.DictCode, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(update.Data.DataNameEn, definitionForItem.DataNameEn, StringComparison.OrdinalIgnoreCase)
                    || !update.Data.IsEnabled)
                    throw new DictionaryWriteException(ServiceErrorCodes.Validation, "内置明细的英文代码、所属类型及启用状态不能修改。");
                update.Data.DictCode = definitionForItem.DictCode;
                update.Data.DataNameEn = definitionForItem.DataNameEn;
            }
            else if (BuiltInDictionaryCatalog.FindData(update.Data.DictCode, update.Data.DataNameEn) is not null)
                throw new DictionaryWriteException(ServiceErrorCodes.Validation, "普通明细不能改成内置业务代码。");
            // 模板身份由具体明细 Uid 承担，改名不影响引用；停用或改属类型必须先解除业务引用。
            if (IsAttributeProfile(entity.DictCode)
                && (!update.Data.IsEnabled || !IsAttributeProfile(update.Data.DictCode)))
                await EnsureProfileNotReferencedAsync(entity.Uid);
            // 系列同样采用明细 Uid；允许修改显示名称，已有引用阻止停用或移出类型。
            if (IsProductSeries(entity.DictCode)
                && (!update.Data.IsEnabled || !IsProductSeries(update.Data.DictCode)))
                await EnsureSeriesNotReferencedAsync(entity.Uid);
        }
        foreach (var delete in request.DeleteItems)
        {
            if (!existing.TryGetValue(delete.Uid, out var entity))
                throw new DictionaryWriteException(ServiceErrorCodes.NotFound, "删除的字典明细不存在。");
            if (entity.IsDeleted && !BuiltInDictionaryCatalog.IsBuiltIn(entity))
                throw new DictionaryWriteException(ServiceErrorCodes.NotFound, "删除的字典明细已删除。");
            if (entity.IsSystem || BuiltInDictionaryCatalog.IsBuiltIn(entity))
                throw new DictionaryWriteException(ServiceErrorCodes.Validation, "系统字典明细不能删除。");
            if (IsAttributeProfile(entity.DictCode)) await EnsureProfileNotReferencedAsync(entity.Uid);
            if (IsProductSeries(entity.DictCode)) await EnsureSeriesNotReferencedAsync(entity.Uid);
        }
        return existing;
    }

    /// <summary>在字典明细更新锁内检查模板业务引用，与标签关联及主型号保存的共享锁互斥。</summary>
    private async Task EnsureProfileNotReferencedAsync(Guid uid)
    {
        var count = await attributes.CountProfileReferencesAsync(uid, CancellationToken.None);
        if (count > 0)
            throw new DictionaryWriteException(ServiceErrorCodes.BusinessBlocked,
                $"属性模板仍被 {count} 条主型号或标签组关联引用，不能删除、停用或移出该字典类型。");
    }

    /// <summary>按固定类型代码识别属性模板，不依赖可编辑名称或客户端标记。</summary>
    private static bool IsAttributeProfile(string? code) =>
        string.Equals(code?.Trim(), DictCodes.ProductAttributeProfile, StringComparison.OrdinalIgnoreCase);

    /// <summary>在明细更新锁内检查主型号系列引用，与主型号保存的共享锁互斥。</summary>
    private async Task EnsureSeriesNotReferencedAsync(Guid uid)
    {
        var count = await repository.CountProductSeriesReferencesAsync(uid);
        if (count > 0)
            throw new DictionaryWriteException(ServiceErrorCodes.BusinessBlocked,
                $"产品系列仍被 {count} 条有效主型号引用，不能删除、停用或移出该字典类型。");
    }

    /// <summary>系列按固定字典代码识别，显示名称修改不改变引用身份。</summary>
    private static bool IsProductSeries(string? code) =>
        string.Equals(code?.Trim(), DictCodes.ProductSeries, StringComparison.OrdinalIgnoreCase);

    /// <summary>仅字典模块允许已登记身份的零版本更新，公共批量请求规则保持不变。</summary>
    private static string? ValidateRequest(SysDictDataBatchSaveRequest? request)
    {
        if (request is null) return "请求不能为空。";
        request.CreateItems ??= [];
        request.UpdateItems ??= [];
        request.DeleteItems ??= [];
        if (request.CreateItems.Any(item => item is null) || request.UpdateItems.Any(item => item is null) || request.DeleteItems.Any(item => item is null))
            return "批量保存明细不能包含空项。";
        if (!request.HasChanges) return "没有需要保存的数据。";
        if (request.UpdateItems.Select(item => item.Uid).Distinct().Count() != request.UpdateItems.Count
            || request.DeleteItems.Select(item => item.Uid).Distinct().Count() != request.DeleteItems.Count
            || request.UpdateItems.Select(item => item.Uid).Intersect(request.DeleteItems.Select(item => item.Uid)).Any())
            return "同一明细不能重复修改、重复删除或同时修改和删除。";
        if (request.UpdateItems.Any(item => item.Uid == Guid.Empty || item.Version < 0
            || item.Version == 0 && BuiltInDictionaryCatalog.FindData(item.Uid) is null))
            return "修改标识或版本无效；零版本仅用于尚未配置的内置项。";
        if (request.DeleteItems.Any(item => item.Uid == Guid.Empty || item.Version <= 0)) return "删除标识或版本无效。";
        return null;
    }

    /// <summary>规范当前写入字段并验证必填名称，不接纳客户端提供的身份与审计事实。</summary>
    internal static string? NormalizeAndValidate(SysDictDataDto? dto, int? rowIndex, string operation)
    {
        var row = SystemBatchSaveSupport.FormatRow(rowIndex);
        if (dto is null) return $"第 {row} 行：{operation}数据不能为空。";
        if (string.IsNullOrWhiteSpace(dto.DictCode)) return $"第 {row} 行：字典编码不能为空。";
        if (string.IsNullOrWhiteSpace(dto.DataNameEn)) return $"第 {row} 行：英文名称不能为空。";
        if (string.IsNullOrWhiteSpace(dto.DataNameZh)) return $"第 {row} 行：中文名称不能为空。";
        dto.DictCode = dto.DictCode.Trim();
        dto.DataNameEn = dto.DataNameEn.Trim();
        dto.DataNameZh = dto.DataNameZh.Trim();
        dto.Remark = dto.Remark?.Trim();
        dto.CssClass = dto.CssClass?.Trim();
        return null;
    }

    /// <summary>携带明确失败类别交由本用例回滚整批，避免将版本冲突降为不明业务失败。</summary>
    private sealed class DictionaryWriteException(string? code, string message) : Exception(message)
    {
        /// <summary>向调用方保留版本冲突、输入阻断或技术失败的原始类别。</summary>
        public string Code { get; } = string.IsNullOrWhiteSpace(code) ? ServiceErrorCodes.BusinessBlocked : code;
    }
}
