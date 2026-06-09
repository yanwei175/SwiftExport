using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public class BomService(
    IBomRepository bomRepo,
    IProductRepository pdRepo,
    ISupplierRepository supplierRepo,
    ServiceBaseContext context) : BaseService<Bom, BomDto>(bomRepo, context), IBomService
{
    private readonly IBomRepository _bomRepo = bomRepo;
    private readonly IProductRepository _pdRepo = pdRepo;
    private readonly ISupplierRepository _supplierRepo = supplierRepo;

    protected override bool IsSoftDelete => false;

    #region BOM管理CRUD操作
    public override async Task<ServiceResult<Guid>> AddAsync(BomDto dto)
    {
        var entity = dto.Adapt<Bom>();

        //检查这条BOM是否在数据库中对应BomNo和ProductSku已经存在了，如果存在了就不允许新增
        //根据这条bom的BomNo和ProductSku去数据库中查询，如果查询到了数据就说明已经存在了，就不允许新增了
        var rst = await _bomRepo.IsBomItemExistAsync(entity.BomNo, entity.SubSku);

        if (rst)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning("尝试新增 {EntityName} 数据失败，原因：BomNo和ProductSku的组合已经存在！BomNo: {BomNo}, ProductSku: {ProductSku}", EntityName, entity.BomNo, entity.SubSku);
            }

            return ServiceResult<Guid>.Fail("相同BomNo和ProductSku的记录已存在，无法新增！");
        }


        entity.CreatedDate = DateTime.Now;
        entity.IsDeleted = false;

        var uid = await _repository.AddAsync(entity);

        // Information 级别
        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("成功新增 {EntityName} 数据，生成的 Uid: {Uid}", EntityName, uid);
        }

        return ServiceResult<Guid>.Ok(uid);
    }

    public async Task<ServiceResult<int>> DeleteBomByBomNoAsync(string bomNo)
    {

        var rst = await _bomRepo.DeleteBomByBomNoAsync(bomNo);

        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("[{EntityName}] 根据BomNo删除Bom成功！BomNo: {BomNo}, 受影响行数: {AffectedRows}", EntityName, bomNo, rst);
        }
        return ServiceResult<int>.Ok(rst);
    }

    public async Task<ServiceResult<List<BomDto>>> GetBomDtoByBomNoAsync(string bomNo)
    {
        var entities = await _bomRepo.GetBomDtoByBomNoAsync(bomNo);
        if (!entities.Any()) return ServiceResult<List<BomDto>>.Fail("未找到数据");

        return ServiceResult<List<BomDto>>.Ok([.. entities]);
    }

    public async Task<ServiceResult<bool>> ValidateBomAsync(string productSku)
    {
        if (string.IsNullOrWhiteSpace(productSku))
        {
            return ServiceResult<bool>.Fail("产品SKU不能为空");
        }

        // 0. 获取产品的 BOM 号
        Product? pd = await _pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
            return ServiceResult<bool>.Fail("产品型号不存在或产品没设置BOM");

        string bomNo = pd.BomNo;

        // 1. 查询该产品的 BOM 项
        var bomItems = await _bomRepo.GetBomByBomNoAsync(bomNo);
        if (bomItems == null || !bomItems.Any())
            return ServiceResult<bool>.Fail("该Bom 没有 BOM 数据，视为不合法");

        // 2. 数量合法性
        if (bomItems.Any(i => i.Numerator <= 0))
            return ServiceResult<bool>.Fail("子项的用量不能小于等于0");

        // 3. 子件存在性
        foreach (var item in bomItems)
        {
            var exists = await _pdRepo.ExistsIdAsync(item.SubSkuId);
            if (!exists) return ServiceResult<bool>.Fail($"子项{item.SubSku}的Id不存在");
        }

        // 4. 重复项检查
        if (bomItems.GroupBy(i => i.SubSku).Any(g => g.Count() > 1))
            return ServiceResult<bool>.Fail($"子项中存在重复项");

        // 5. 循环依赖检查（DFS，异步）
        var visited = new HashSet<string>();

        async Task<bool> HasCycleAsync(string currentBomNo)
        {
            if (!visited.Add(currentBomNo)) return true; // 已访问过，说明有环

            var children = await _bomRepo.GetBomByBomNoAsync(currentBomNo);
            foreach (var child in children)
            {
                // 递归检查子件的 BOM
                var subPd = await _pdRepo.GetBySkuAsync(child.SubSku);
                if (subPd != null && !string.IsNullOrEmpty(subPd.BomNo))
                {
                    if (await HasCycleAsync(subPd.BomNo)) return true;
                }
            }

            visited.Remove(currentBomNo);
            return false;
        }

        if (await HasCycleAsync(bomNo)) return ServiceResult<bool>.Fail("递归依赖检查不通过,Bom链上存在不合理.");

        return ServiceResult<bool>.Ok(true); // 所有检查通过
    }

    public async Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTreeAsync(string productSku)
    {
        if (string.IsNullOrWhiteSpace(productSku))
            return ServiceResult<IEnumerable<BomTreeDto>>.Fail("产品SKU不能为空");

        var pd = await _pdRepo.GetBySkuAsync(productSku);
        if (pd == null || string.IsNullOrEmpty(pd.BomNo))
        {
            return ServiceResult<IEnumerable<BomTreeDto>>.Ok([]);
        }

        var visited = new HashSet<string>();
        var result = await BuildBomTreeAsync(pd.BomNo, visited);
        return ServiceResult<IEnumerable<BomTreeDto>>.Ok(result);
    }

    private async Task<List<BomTreeDto>> BuildBomTreeAsync(string bomNo, HashSet<string> visited)
    {
        var resultList = new List<BomTreeDto>();
        if (!visited.Add(bomNo))
        {
            // 发生循环依赖，直接返回当前已构建部分避免死循环
            return resultList;
        }

        var bomItems = await _bomRepo.GetBomByBomNoAsync(bomNo);
        foreach (var item in bomItems)
        {
            var dto = item.Adapt<BomTreeDto>();
            var subPd = await _pdRepo.GetBySkuAsync(item.SubSku);
            if (subPd != null)
            {
                // 补齐显示用的字段
                dto.Description = subPd.DescriptionEn;
                dto.MainImage = subPd.MainImage;
                // 如果你有 Finish (表面处理/颜色)，这里可以补齐：
                // 这取决于你的底层模型。例如如果需要查询 Color，可在这里处理

                // 判断是否继续递归拆解
                // 条件：实体有BomNo 且 本身的类型不是"止拆"
                if (subPd.BomVersion > 0 && !string.IsNullOrEmpty(subPd.BomNo))
                {
                    dto.Children = await BuildBomTreeAsync(subPd.BomNo, visited);
                }
            }
            else
            {
                // 如果子件在产品表里找不到，仍然添加这个节点，但可以考虑标记一下（例如设置Description为"未知产品"）
                dto.Description = "未知Sku";
            }
            resultList.Add(dto);
        }

        visited.Remove(bomNo);
        return resultList;
    }
    #endregion




    #region 拆解BOM

    //==========拆解BOM入口========


    public async Task<List<DependentDemand>> 根据毛需求集合拆解成净需求(List<IndependentDemand> request)
    {
        if (request == null || request.Count <= 0)
            return [];
        // 中文注释：该入参可能包含不同来源单据，缓存构建只按“所有根 SKU 的并集”执行，不依赖来源单据分组。
        var rootSkus = request
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .Select(x => x.Sku!.Trim())
            .ToList();

        // 中文注释：构建拆解缓存（一次性加载相关产品、BOM、供应商数据到内存，避免递归中的 N+1 查询）。
        var cache = await 构建需求缓存Async(rootSkus);

        var addList = new List<DependentDemand>();

        // 中文注释：按毛需求逐行拆解，保持每一行都由调用方提供，BomService 内部不再要求调用方传 DTO。
        foreach (var line in request)
        {
            // 中文注释：数量口径统一按毛需求原始数量（OriginalQty）参与拆解。
            if (line.OriginalQty <= 0) continue;

            // 中文注释：记录当前行拆解前的结果下标，用于回填 IndependentDemandId（毛需求主键）。
            var startIndex = addList.Count;

            // 中文注释：非标件规则——当 SKU 为空时，按非标件直接落依赖需求，不做 BOM 递归。
            if (line.IsNonStandard)
            {
                addList.Add(Build非标件需求(line, null));
            }
            else
            {
                // 中文注释：从缓存中获取根产品；找不到产品时跳过当前行（可由上层决定是否记录异常）。
                var rootProduct = cache.GetProductBySku(line.Sku);
                if (rootProduct == null)
                    continue;

                // 中文注释：递归拆解当前行，结果直接累加到 addList。
                await 递归拆解需求Async(
                    line,
                    rootProduct,
                    line.OriginalQty,
                    0,
                    parentSku: null,
                    addList,
                    parentAddedByCustomerPart: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    cache);
            }

            // 中文注释：将当前毛需求行生成的所有依赖需求统一回填关联键，便于后续追踪“毛需求 -> 净需求”。
            for (var i = startIndex; i < addList.Count; i++)
            {
                addList[i].IndependentDemandId = line.Uid;
            }
        }

        return addList;
    }

    //==========拆解BOM核心逻辑========


    /// <summary>
    /// 中文注释：按BOM版本递归拆解单行。
    /// 递归终止条件：BomVersion<=0 或 BomNo 为空。
    /// </summary>
    private static async Task 递归拆解需求Async(IndependentDemand sourceLine, Product currentProduct,
        decimal currentQty, int bomLevel, string? parentSku, List<DependentDemand> addList,
        HashSet<string> parentAddedByCustomerPart, DecomposeCache cache)
    {
        //如果BomVersion<=0 或 BomNo 为空，视为无BOM结构，按标准件处理直接落需求池，不再继续递归。
        if (currentProduct.BomVersion <= 0 || string.IsNullOrWhiteSpace(currentProduct.BomNo))
        {
            addList.Add(Build标准件需求(sourceLine, currentProduct, currentQty, bomLevel, parentSku, null, null));
            return;
        }

        // 标准件处理：先落当前行，再继续递归拆解子件。此需求是成品需求,bomLevel是0,父级SKU是null,备注和送货地点先不填，后续如果有客供件会更新备注和送货地点
        addList.Add(Build标准件需求(sourceLine, currentProduct, currentQty, bomLevel, parentSku, null, null));

        // 从缓存中获取当前产品的BOM项列表，如果没有则视为无BOM结构直接返回。
        var bomList = cache.GetBomByBomNo(currentProduct.BomNo).ToList();
        if (bomList.Count == 0)
            return;

        //遍历BOM项，计算子件需求并递归拆解。
        foreach (var bom in bomList)
        {
            if (string.IsNullOrWhiteSpace(bom.SubSku) || bom.Denominator == 0)
                continue;
            // 从缓存中获取子件信息，如果找不到则跳过（也可以考虑记录日志）
            var childProduct = cache.GetProductBySku(bom.SubSku);
            if (childProduct == null)
                continue;

            var childQty = currentQty * bom.Numerator / bom.Denominator;
            var remark = bom.Note;

            // 中文注释：客供件逻辑：
            // 1) 客供件子件正常入池；
            // 2) 额外将父级再加入一次（无论客供件型号个数，只加一次）；
            // 3) 客供件备注写“货好后送往父级.供应商名称”。

            if (childProduct.MaterialType == MaterialTypesEnum.客供件)
            {
                var supplierName = cache.ResolveSupplierName(childProduct.DefaultSupplierCode);
                remark = $"货好后送往{currentProduct.Sku}.{supplierName}";
                var parentOnceKey = $"{sourceLine.SourceNo}|{sourceLine.SourceLineId}|{currentProduct.Sku}";
                if (parentAddedByCustomerPart.Add(parentOnceKey))
                {
                    addList.Add(Build标准件需求(sourceLine, currentProduct, currentQty, bomLevel, parentSku, "含客供件", childProduct.DefaultSupplierCode));
                }
            }

            // 中文注释：子件正常入池。
            addList.Add(Build标准件需求(sourceLine, childProduct, childQty, bomLevel + 1, currentProduct.Sku, remark, null));

            // 中文注释：继续向下递归拆解（BomVersion<=0且存在BomNo时才会继续）。
            await 递归拆解需求Async(
                sourceLine,
                childProduct,
                childQty,
                bomLevel + 1,
                currentProduct.Sku,
                addList,
                parentAddedByCustomerPart,
                cache);
        }
    }

    /// <summary>
    /// 组装依赖需求实体。
    /// </summary>
    private static DependentDemand Build标准件需求(IndependentDemand sourceLine,
        Product product, decimal qty, int bomLevel, string? parentSku, string? remark, string? shipToLocation)
    {
        return new DependentDemand
        {
            SourceUid = sourceLine.SourceUid,
            SourceType = sourceLine.SourceType,
            SourceNo = sourceLine.SourceNo,
            SourceLineId = sourceLine.SourceLineId,
            IndependentDemandId = sourceLine.Uid,
            SourceSku = sourceLine.SourceSku,
            Sku = product.Sku,
            Description = product.DescriptionEn,
            ColorName = null,
            Features = product.FeaturesEn,
            ParentSku = parentSku ?? string.Empty,
            BomLevel = bomLevel,
            SupplyType = string.IsNullOrWhiteSpace(product.SupplyType) ? SupplyTypeEnums.采购 : product.SupplyType,
            GrossQty = qty,
            NetQty = qty,
            ExecutedQty = 0,
            ReceivedQty = 0,
            RequiredDate = sourceLine.HopefulDate,
            OrderStatus = DependentDemandStatusEnum.未确认,
            Remark = remark,
            TargetNo = null,
            SupplierCode = product.DefaultSupplierCode,
            SupplierName = null,
            TargetLineId = null,
            IsDeleted = false,
            CreatedDate = DateTime.Now,
            ChangeTime = DateTime.Now,
            ShipToLocation = shipToLocation,
            Even = null,
            Status = null
        };
    }

    /// <summary>
    /// 组装非标件依赖需求实体（SKU 为空时直接落池）。
    /// </summary>
    private static DependentDemand Build非标件需求(IndependentDemand sourceLine, string? shipToLocation)
    {
        return new DependentDemand
        {   
            SourceUid = sourceLine.SourceUid,
            SourceType = sourceLine.SourceType,
            SourceNo = sourceLine.SourceNo,
            SourceLineId = sourceLine.SourceLineId,
            IndependentDemandId = sourceLine.Uid,
            SourceSku = sourceLine.SourceSku,
            Sku = sourceLine.Sku,
            Description = sourceLine.Description,
            ColorName = sourceLine.ColorName,
            Features = sourceLine.Features,
            ParentSku = string.Empty,
            BomLevel = 0,
            SupplyType = SupplyTypeEnums.采购,
            GrossQty = sourceLine.OriginalQty,
            NetQty = sourceLine.OriginalQty,
            ExecutedQty = 0,
            ReceivedQty = 0,
            RequiredDate = sourceLine.HopefulDate,
            OrderStatus = DependentDemandStatusEnum.未确认,
            Remark = "非标件需求",
            TargetNo = null,
            SupplierCode = null,
            SupplierName = null,
            TargetLineId = null,
            IsDeleted = false,
            CreatedDate = DateTime.Now,
            ChangeTime = DateTime.Now,
            ShipToLocation = shipToLocation
        };
    }
    /// <summary>
    /// 中文注释：构建拆解缓存。
    /// 目标：将递归拆解中频繁访问的 Product/BOM/Supplier 统一拉到内存，避免 N+1 查询。
    /// </summary>
    /// <param name="rootSkus">需要参与拆解的根 SKU 集合（可跨来源单据）。</param>
    private async Task<DecomposeCache> 构建需求缓存Async(IEnumerable<string> rootSkus)
    {
        var normalizedRootSkus = rootSkus
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedRootSkus.Count == 0)
            return new DecomposeCache([], [], []);

        var productBySku = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var bomByBomNo = new Dictionary<string, List<Bom>>(StringComparer.OrdinalIgnoreCase);
        var loadedBomNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pendingSkus = new HashSet<string>(normalizedRootSkus, StringComparer.OrdinalIgnoreCase);
        while (pendingSkus.Count > 0)
        {
            var skuBatch = pendingSkus.ToList();
            pendingSkus.Clear();

            var products = await _pdRepo.GetBySkuListAsync(skuBatch);
            foreach (var product in products)
            {
                if (!string.IsNullOrWhiteSpace(product.Sku))
                    productBySku[product.Sku] = product;
            }

            var toLoadBomNos = products
                .Where(x => !string.IsNullOrWhiteSpace(x.BomNo) && x.BomVersion > 0)
                .Select(x => x.BomNo!)
                .Where(x => !loadedBomNos.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (toLoadBomNos.Count == 0)
                continue;

            foreach (var bomNo in toLoadBomNos)
                loadedBomNos.Add(bomNo);

            var bomMap = await _bomRepo.Get一堆BomBy一堆Bom号(toLoadBomNos);
            var nextLevelSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in bomMap)
            {
                var bomNo = kv.Key;
                var bomList = kv.Value?.ToList() ?? [];
                bomByBomNo[bomNo] = bomList;

                foreach (var bom in bomList)
                {
                    if (string.IsNullOrWhiteSpace(bom.SubSku))
                        continue;

                    if (!productBySku.ContainsKey(bom.SubSku))
                        nextLevelSkus.Add(bom.SubSku);
                }
            }

            foreach (var sku in nextLevelSkus)
                pendingSkus.Add(sku);
        }

        var supplierCodes = productBySku.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.DefaultSupplierCode))
            .Select(x => x.DefaultSupplierCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var suppliers = supplierCodes.Count > 0 ? await _supplierRepo.GetBySupplierCodeListAsync(supplierCodes) : [];
        return new DecomposeCache(productBySku.Values, bomByBomNo.Values.SelectMany(x => x), suppliers);
    }
    /// <summary>
    /// 中文注释：拆解运行时缓存对象。
    /// </summary>
    private sealed class DecomposeCache
    {
        private readonly Dictionary<string, Product> _productBySku;
        private readonly Dictionary<string, List<Bom>> _bomByBomNo;
        private readonly Dictionary<string, Supplier> _supplierByCode;

        public DecomposeCache(IEnumerable<Product> products, IEnumerable<Bom> boms, IEnumerable<Supplier> suppliers)
        {
            _productBySku = products
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                .GroupBy(x => x.Sku!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            _bomByBomNo = boms
                .Where(x => !string.IsNullOrWhiteSpace(x.BomNo))
                .GroupBy(x => x.BomNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            _supplierByCode = suppliers
                .Select(x => new { Supplier = x, SupplierCode = x.SupplierCode?.Trim() })
                .Where(x => !string.IsNullOrWhiteSpace(x.SupplierCode))
                .GroupBy(x => x.SupplierCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Supplier, StringComparer.OrdinalIgnoreCase);
        }

        public Product? GetProductBySku(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku)) return null;
            _productBySku.TryGetValue(sku, out var product);
            return product;
        }

        public IEnumerable<Bom> GetBomByBomNo(string? bomNo)
        {
            if (string.IsNullOrWhiteSpace(bomNo)) return [];
            return _bomByBomNo.TryGetValue(bomNo, out var list) ? list : [];
        }

        public string ResolveSupplierName(string? supplierCode)
        {
            if (string.IsNullOrWhiteSpace(supplierCode)) return "未知供应商";
            if (!_supplierByCode.TryGetValue(supplierCode, out var supplier)) return supplierCode;
            return string.IsNullOrWhiteSpace(supplier.SupplierName) ? supplierCode : supplier.SupplierName;
        }
    }

    #endregion 
}
