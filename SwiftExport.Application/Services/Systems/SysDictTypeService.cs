using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 系统字典类型服务实现
    /// 支持标准 CRUD 操作和分组树查询
    /// </summary>
    public partial class SysDictTypeService(
        ISysDictTypeRepository repository,
        ISysDictDataRepository dictDataRepository,
        ISysViewRouteRepository viewRouteRepository,
        ServiceBaseContext context)
        : BaseService<SysDictType, SysDictTypeDto>(repository, context), ISysDictTypeService
    {
        private readonly ILogger<SysDictTypeService> _logger = context.LoggerFactory.CreateLogger<SysDictTypeService>();

        /// <summary>
        /// 获取字典类型分组树
        /// 
        /// 算法：
        /// 1. 获取所有字典类型（未删除的）
        /// 2. 按 GroupCode 分组
        /// 3. 构建分组树（分组节点 + 字典类型子节点）
        /// 4. 按 SortOrder 排序
        /// </summary>
        public async Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTreeAsync()
        {
            try
            {
                // 1. 获取所有字典类型（仅用泛型 Repository 的 GetAllAsync）
                // 防御性处理：确保不会将可能为 null 的返回值赋给不可为 null 的变量，避免 CS8600
                var allDictTypesReadOnly = (await repository.GetAllAsync()) ?? Enumerable.Empty<SysDictType>();

                // GetAllAsync 返回值不可为 null，无需 null 判断，直接检查是否为空集合
                if (!allDictTypesReadOnly.Any())
                {
                    _logger.LogInformation("没有可用的字典类型数据");
                    return ServiceResult<List<DictTypeGroupTreeDto>>.Ok(new List<DictTypeGroupTreeDto>());
                }

                // 转换为 List
                var allDictTypes = allDictTypesReadOnly.ToList();

                // 2. 按 GroupCode 分组，组织树形结构
                var groupedTree = BuildGroupedTree(allDictTypes);

                _logger.LogInformation("成功构建字典类型分组树，共 {GroupCount} 个分组", groupedTree.Count);
                return ServiceResult<List<DictTypeGroupTreeDto>>.Ok(groupedTree);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "构建字典类型分组树失败");
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<List<DictTypeGroupTreeDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        /// <summary>
        /// 构建分组树的方法
        /// 
        /// 核心逻辑：
        /// - 第一层：按 GroupCode 聚合成分组节点
        /// - 如果 GroupCode 为空，全部归入一个"未分组"节点
        /// - 第二层+：不再按 GroupCode 聚合，直接基于 ParentId 建立树形关系
        /// 
        /// 返回结构：
        /// 未分组
        ///   ├─ DictType1 (GroupCode = null)
        ///   └─ DictType2 (GroupCode = null)
        /// Group1
        ///   ├─ DictType3 (ParentId = null/0)
        ///   ├─ DictType4 (ParentId = null/0)
        ///   │   ├─ DictType4_1 (ParentId = DictType4.Uid)
        ///   │   └─ DictType4_2 (ParentId = DictType4.Uid)
        ///   └─ DictType5
        /// Group2
        ///   └─ ...
        /// </summary>
        private static List<DictTypeGroupTreeDto> BuildGroupedTree(List<SysDictType> allDictTypes)
        {
            // 1. 使用 Mapster 将 Entity 转换为 DTO
            // Adapt 可能返回 null（取决于 Mapster 配置），使用空列表替代以避免 CS8600
            var dictTypeDtos = allDictTypes.Adapt<List<SysDictTypeDto>>() ?? new List<SysDictTypeDto>();

            // 2. 按 GroupCode 分组（第一层级聚合）
            // 防止列表中出现 null 元素，先过滤 null
            var groupedByCode = dictTypeDtos
                .Where(x => x is not null)
                .GroupBy(x => new { x!.GroupCode, x!.GroupName })
                .OrderBy(g => g.First().SortOrder)
                .ToList();

            var treeRoot = new List<DictTypeGroupTreeDto>();

            // 3. 创建未分组节点（用于收集所有 GroupCode 为空的字典类型）
            DictTypeGroupTreeDto? ungroupedNode = null;

            foreach (var group in groupedByCode)
            {
                // 如果 GroupCode 为空，作为未分组的，加入统一的未分组节点
                if (string.IsNullOrEmpty(group.Key.GroupCode))
                {
                    // 首次创建未分组节点
                    if (ungroupedNode == null)
                    {
                        ungroupedNode = new DictTypeGroupTreeDto
                        {
                            GroupCode = "_UNGROUPED",
                            GroupName = "未分组",
                            GroupOrDictType = "Group",
                            SortOrder = 0,  // 未分组置于最前面
                            Children = new List<DictTypeTreeDto>()
                        };
                    }

                    // 将所有未分组的字典类型加入到未分组节点
                    var ungroupedDictTypes = group.ToList();
                    var ungroupedTree = BuildDictTypeTree(ungroupedDictTypes);
                    ungroupedNode.Children.AddRange(ungroupedTree);
                }
                else
                {
                    // 创建分组节点
                    var groupNode = new DictTypeGroupTreeDto
                    {
                        GroupCode = group.Key.GroupCode,
                        GroupName = group.Key.GroupName ?? group.Key.GroupCode,
                        GroupOrDictType = "Group",
                        SortOrder = group.First().SortOrder,
                        Children = new List<DictTypeTreeDto>()
                    };

                    // 为分组下的所有字典类型构建树形结构（第二层及以上基于 ParentId）
                    var groupDictTypes = group.ToList();
                    var dictTypeTree = BuildDictTypeTree(groupDictTypes);
                    groupNode.Children = dictTypeTree;

                    treeRoot.Add(groupNode);
                }
            }

            // 4. 如果有未分组的节点，添加到最前面
            if (ungroupedNode != null)
            {
                treeRoot.Insert(0, ungroupedNode);
            }

            // 最终按 SortOrder 排序
            return treeRoot.OrderBy(x => x.SortOrder).ToList();
        }

        /// <summary>
        /// 在树形结构中构建字典类型树的私有方法
        /// 
        /// 功能：
        /// - 基于 ParentId 和 Uid 构建多层级树形结构
        /// - 支持无限级嵌套
        /// - 不再按 GroupCode 聚合（只在第一层做过聚合）
        /// 
        /// 使用场景：
        /// 1. 在分组内部构建树形结构
        /// 2. 支持分类树、导航树等需要多层级的场景
        /// </summary>
        private static List<DictTypeTreeDto> BuildDictTypeTree(List<SysDictTypeDto> dictTypes)
        {
            if (dictTypes == null || dictTypes.Count == 0)
            {
                return new List<DictTypeTreeDto>();
            }

            var rootNodes = new List<DictTypeTreeDto>();

            // 过滤任何可能为 null 的元素并构建字典，避免将 null 值转换为不可为 null 的键/值（CS8600）
            var nonNullDictTypes = dictTypes.Where(x => x is not null).Select(x => x!).ToList();

            var nodeDict = nonNullDictTypes.ToDictionary(x => x.Uid);
            var treeNodeDict = new Dictionary<Guid, DictTypeTreeDto>();

            // 1. 先创建所有树形节点
            foreach (var dictType in nonNullDictTypes.OrderBy(x => x.SortOrder))
            {
                var treeNode = new DictTypeTreeDto
                {
                    Uid = dictType.Uid,
                    DictCode = dictType.DictCode,
                    DictName = dictType.DictName,
                    IsSystem = dictType.IsSystem,
                    Remark = dictType.Remark,
                    ParentId = dictType.ParentId,
                    SortOrder = dictType.SortOrder,
                    TargetViewKey = dictType.TargetViewKey,
                    GroupCode = dictType.GroupCode,
                    GroupName = dictType.GroupName,
                    Children = new List<DictTypeTreeDto>()
                };

                treeNodeDict[dictType.Uid] = treeNode;
            }

            // 2. 建立父子关系（安全处理可空 ParentId，避免 CS8629）
            foreach (var dictType in nonNullDictTypes.OrderBy(x => x.SortOrder))
            {
                var treeNode = treeNodeDict[dictType.Uid];

                // 使用局部变量并检查 HasValue，保证在使用 Value 前非空
                if (dictType.ParentId == null || !Guid.TryParse(dictType.ParentId.ToString(), out var parentUid) || !nodeDict.ContainsKey(parentUid))
                {
                    // 视为根节点
                    rootNodes.Add(treeNode);
                }
                else
                {
                    if (treeNodeDict.TryGetValue(parentUid, out DictTypeTreeDto? value))
                    {
                        value.Children.Add(treeNode);
                    }
                    else
                    {
                        // 如果父节点在 treeNodeDict 中不存在，则仍视为根节点
                        rootNodes.Add(treeNode);
                    }
                }
            }

            return rootNodes.OrderBy(x => x.SortOrder).ToList();
        }



        public async Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllAsync()
        {
            try
            {
                var entities = await repository.GetAllAsync();
                return entities.Any()
                    ? ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(entities.Adapt<IReadOnlyList<SysDictTypeDto>>())
                    : ServiceResult<IReadOnlyList<SysDictTypeDto>>.Fail("未找到数据");
            }
            catch (Exception ex)
            {
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                Logger.LogError(ex, "应用服务执行失败。");
                return ServiceResult<IReadOnlyList<SysDictTypeDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        public async Task<ServiceResult<PagedResponseBase<SysDictTypeDto>>> GetPagedAsync(SysDictTypeSearchRequest request)
        {
            try
            {
                return ServiceResult<PagedResponseBase<SysDictTypeDto>>.Ok(
                    await repository.GetPagedAsync(request ?? new SysDictTypeSearchRequest()));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "分页查询字典类型失败。");
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<PagedResponseBase<SysDictTypeDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        public async Task<ServiceResult<SysDictTypeBatchSaveResponse>> BatchSaveAsync(SysDictTypeBatchSaveRequest batchReq)
        {
            var requestError = SystemBatchSaveSupport.ValidateRequest(batchReq);
            if (requestError is not null) return ServiceResult<SysDictTypeBatchSaveResponse>.Fail(requestError);
            if (_session.Transaction is not null)
                return ServiceResult<SysDictTypeBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始字典类型批量保存。");

            var response = new SysDictTypeBatchSaveResponse { TotalCount = batchReq.TotalCount };
            try
            {
                _session.Transaction = _session.Connection.BeginTransaction();
                var preparation = await PrepareBatchSaveAsync(batchReq);
                if (preparation.Error is not null)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<SysDictTypeBatchSaveResponse>.Fail(preparation.Error);
                }

                var persistedByLogicalId = preparation.Existing.Keys.ToDictionary(uid => uid, uid => uid);
                foreach (var item in preparation.OrderedCreates)
                {
                    var entity = item.Data.Adapt<SysDictType>();
                    entity.IsSystem = DictCodes.DictCodeList.Contains(
                        entity.DictCode,
                        StringComparer.OrdinalIgnoreCase);
                    entity.ParentId = ResolveParentId(item.Data.ParentId, persistedByLogicalId);
                    var writeResult = await repository.AddAsync(entity);
                    response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "新增字典类型失败。");
                    persistedByLogicalId[item.Data.Uid] = writeResult.Uid;
                }
                foreach (var item in batchReq.UpdateItems)
                {
                    var entity = preparation.Existing[item.Uid];
                    var isSystem = entity.IsSystem
                        || DictCodes.DictCodeList.Contains(entity.DictCode, StringComparer.OrdinalIgnoreCase)
                        || DictCodes.DictCodeList.Contains(item.Data.DictCode, StringComparer.OrdinalIgnoreCase);
                    SystemBatchSaveSupport.AdaptPreservingServerFields(item.Data, entity);
                    entity.IsSystem = isSystem;
                    entity.ParentId = ResolveParentId(item.Data.ParentId, persistedByLogicalId);
                    entity.Version = item.Version;
                    var writeResult = await repository.UpdateAsync(entity);
                    response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "更新字典类型失败。");
                }
                foreach (var item in batchReq.DeleteItems)
                {
                    var writeResult = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                    response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "删除字典类型失败。");
                }

                _session.Transaction.Commit();
                return ServiceResult<SysDictTypeBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
            }
            catch (SystemBatchWriteException exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "字典类型批量保存");
                return ServiceResult<SysDictTypeBatchSaveResponse>.Fail($"字典类型批量保存失败，所有操作已回滚。原因：{exception.Message}");
            }
            catch (Exception exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "字典类型批量保存");
                _logger.LogError(exception, "字典类型批量保存发生未预期异常。");
                return ServiceResult<SysDictTypeBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "字典类型批量保存失败，所有操作已回滚，请刷新后重试。");
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }

        private async Task<DictTypeBatchPreparation> PrepareBatchSaveAsync(SysDictTypeBatchSaveRequest request)
        {
            foreach (var item in request.CreateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "新增");
                if (error is not null) return DictTypeBatchPreparation.Fail(error);
                if (item.Data.Uid == Guid.Empty) item.Data.Uid = Guid.NewGuid();
            }
            foreach (var item in request.UpdateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "修改");
                if (error is not null) return DictTypeBatchPreparation.Fail(error);
            }

            var submittedRouteKeys = request.CreateItems.Select(x => x.Data.TargetViewKey)
                .Concat(request.UpdateItems.Select(x => x.Data.TargetViewKey))
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var routes = await viewRouteRepository.GetByTargetViewKeysAsync(submittedRouteKeys, withUpdLock: true);
            var existingRouteKeys = routes.Select(x => x.TargetViewKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingRouteKeys = submittedRouteKeys.Where(x => !existingRouteKeys.Contains(x)).ToArray();
            if (missingRouteKeys.Length > 0)
                return DictTypeBatchPreparation.Fail("以下目标路由不存在：" + string.Join(", ", missingRouteKeys));

            var all = await repository.GetAllForUpdateAsync();
            var existing = all.ToDictionary(x => x.Uid);
            var targetIds = request.UpdateItems.Select(x => x.Uid).Concat(request.DeleteItems.Select(x => x.Uid)).Distinct().ToArray();
            var missing = targetIds.Where(uid => !existing.ContainsKey(uid)).ToArray();
            if (missing.Length > 0) return DictTypeBatchPreparation.Fail("修改或删除失败，以下字典类型不存在：" + string.Join(", ", missing));
            var systemProtectionError = ValidateSystemDictionaryProtection(request, existing);
            if (systemProtectionError is not null) return DictTypeBatchPreparation.Fail(systemProtectionError);

            var createIds = request.CreateItems.Select(x => x.Data.Uid).ToArray();
            if (createIds.Distinct().Count() != createIds.Length || createIds.Any(existing.ContainsKey))
                return DictTypeBatchPreparation.Fail("新增字典类型的逻辑标识重复或与现有数据冲突。");

            var deleteIds = request.DeleteItems.Select(x => x.Uid).ToHashSet();
            var updateById = request.UpdateItems.ToDictionary(x => x.Uid);
            var nodes = new Dictionary<Guid, DictTypeNode>();
            foreach (var entity in all.Where(x => !deleteIds.Contains(x.Uid)))
            {
                var node = updateById.TryGetValue(entity.Uid, out var update)
                    ? new DictTypeNode(entity.Uid, update.Data.ParentId, update.Data.DictCode, update.Data.TargetViewKey)
                    : new DictTypeNode(entity.Uid, entity.ParentId, entity.DictCode, entity.TargetViewKey);
                nodes.Add(node.Uid, node);
            }
            foreach (var item in request.CreateItems)
            {
                var node = new DictTypeNode(item.Data.Uid, item.Data.ParentId, item.Data.DictCode, item.Data.TargetViewKey);
                nodes.Add(node.Uid, node);
            }

            var graphError = ValidateFinalGraph(nodes);
            if (graphError is not null) return DictTypeBatchPreparation.Fail(graphError);

            var changedOrDeletedCodes = request.DeleteItems.Select(x => existing[x.Uid].DictCode)
                .Concat(request.UpdateItems.Where(x => !string.Equals(existing[x.Uid].DictCode, x.Data.DictCode, StringComparison.OrdinalIgnoreCase))
                    .Select(x => existing[x.Uid].DictCode))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var codesWithData = await dictDataRepository.GetDictCodesWithDataAsync(changedOrDeletedCodes);
            if (codesWithData.Count > 0)
                return DictTypeBatchPreparation.Fail("以下字典类型仍有字典明细，不能改编码或删除：" + string.Join(", ", codesWithData));

            var orderedCreates = OrderCreates(request.CreateItems, existing.Keys.ToHashSet());
            return orderedCreates.Error is null
                ? DictTypeBatchPreparation.Ok(existing, orderedCreates.Items)
                : DictTypeBatchPreparation.Fail(orderedCreates.Error);
        }

        internal static string? ValidateFinalGraph(IReadOnlyDictionary<Guid, DictTypeNode> nodes)
        {
            var duplicate = nodes.Values.GroupBy(x => x.DictCode, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null) return $"字典编码不能重复：{duplicate.Key}。";
            foreach (var node in nodes.Values)
            {
                if (node.ParentId is null) continue;
                if (node.ParentId == node.Uid) return $"字典类型不能引用自身作为上级：{node.DictCode}。";
                if (!nodes.ContainsKey(node.ParentId.Value)) return $"字典类型的上级不存在：{node.DictCode}。";

                var visited = new HashSet<Guid> { node.Uid };
                var current = node;
                while (current.ParentId.HasValue)
                {
                    if (!visited.Add(current.ParentId.Value)) return $"字典类型层级存在循环引用：{node.DictCode}。";
                    if (!nodes.TryGetValue(current.ParentId.Value, out var parent))
                        return $"字典类型的上级不存在：{node.DictCode}。";
                    current = parent;
                }
            }
            return null;
        }

        internal static string? ValidateSystemDictionaryProtection(
            SysDictTypeBatchSaveRequest request,
            IReadOnlyDictionary<Guid, SysDictType> existing)
        {
            var protectedItems = SystemBatchSaveSupport.FindProtectedDeleteIds(
                request.DeleteItems,
                existing,
                entity => entity.IsSystem
                    || DictCodes.DictCodeList.Contains(entity.DictCode, StringComparer.OrdinalIgnoreCase));
            if (protectedItems.Length > 0)
                return "系统字典类型不能删除：" + string.Join(", ", protectedItems);

            var renamedSystemCodes = request.UpdateItems
                .Where(item => DictCodes.DictCodeList.Contains(existing[item.Uid].DictCode, StringComparer.OrdinalIgnoreCase)
                    && !string.Equals(existing[item.Uid].DictCode, item.Data.DictCode, StringComparison.OrdinalIgnoreCase))
                .Select(item => existing[item.Uid].DictCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return renamedSystemCodes.Length > 0
                ? "系统字典编码不能修改：" + string.Join(", ", renamedSystemCodes)
                : null;
        }

        private static (string? Error, List<CreateItemRequest<SysDictTypeDto>> Items) OrderCreates(
            List<CreateItemRequest<SysDictTypeDto>> creates,
            HashSet<Guid> persistedIds)
        {
            var pending = creates.ToList();
            var ordered = new List<CreateItemRequest<SysDictTypeDto>>(creates.Count);
            while (pending.Count > 0)
            {
                var ready = pending.Where(x => !x.Data.ParentId.HasValue || persistedIds.Contains(x.Data.ParentId.Value)).ToList();
                if (ready.Count == 0) return ("新增字典类型无法按父子顺序保存。", []);
                foreach (var item in ready)
                {
                    pending.Remove(item);
                    ordered.Add(item);
                    persistedIds.Add(item.Data.Uid);
                }
            }
            return (null, ordered);
        }

        private static Guid? ResolveParentId(Guid? parentId, IReadOnlyDictionary<Guid, Guid> persistedByLogicalId)
        {
            if (!parentId.HasValue) return null;
            return persistedByLogicalId.TryGetValue(parentId.Value, out var persisted) ? persisted : parentId;
        }

        private static string? NormalizeAndValidate(SysDictTypeDto? dto, int? rowIndex, string operation)
        {
            var row = SystemBatchSaveSupport.FormatRow(rowIndex);
            if (dto is null) return $"第 {row} 行：{operation}数据不能为空。";
            if (string.IsNullOrWhiteSpace(dto.DictCode)) return $"第 {row} 行：字典编码不能为空。";
            if (string.IsNullOrWhiteSpace(dto.DictName)) return $"第 {row} 行：字典名称不能为空。";
            dto.DictCode = dto.DictCode.Trim();
            dto.DictName = dto.DictName.Trim();
            dto.TargetViewKey = dto.TargetViewKey?.Trim();
            dto.GroupCode = dto.GroupCode?.Trim();
            dto.GroupName = dto.GroupName?.Trim();
            dto.Remark = dto.Remark?.Trim();
            return null;
        }

        internal sealed record DictTypeNode(Guid Uid, Guid? ParentId, string DictCode, string? TargetViewKey);

        private sealed record DictTypeBatchPreparation(
            string? Error,
            Dictionary<Guid, SysDictType> Existing,
            List<CreateItemRequest<SysDictTypeDto>> OrderedCreates)
        {
            public static DictTypeBatchPreparation Fail(string error) => new(error, [], []);
            public static DictTypeBatchPreparation Ok(Dictionary<Guid, SysDictType> existing, List<CreateItemRequest<SysDictTypeDto>> creates) => new(null, existing, creates);
        }
    }
}
