using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Orders.RequirementPools;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders
{
    /// <summary>
    /// 销售订单拆解中间表应用服务。
    /// </summary>
    public class RequirementPoolService(
        IRequirementPoolRepository repository,
        IPurchaseOrderRepository purchaseOrderRepository,
        IPurchaseOrderLineRepository purchaseOrderItemRepository,
        IRepository<SalesOrderHeader> salesOrderHeaderRepository,
        IProductRepository productRepository,
        IBomRepository bomRepository,
        ICustomerRepository customerRepository,
        ISupplierRepository supplierRepository,
        IDocumentNoService documentNoService,
        ServiceBaseContext context)
        : BaseService<RequirementPool, RequirementPoolDto>(repository, context), IRequirementPoolService
    {
        /// <summary>
        /// 中文注释：需求池转采购行的专用 Mapster 配置。
        /// 仅维护共性字段映射（如 SupplyType），其余依赖产品档案的字段在转单时补齐。
        /// </summary>
        private static readonly TypeAdapterConfig RequirementPoolToPurchaseOrderLineMapConfig = BuildRequirementPoolToPurchaseOrderLineMapConfig();

        private readonly IRequirementPoolRepository _rpRepository = repository;
        private readonly IPurchaseOrderRepository _purchaseOrderRepository = purchaseOrderRepository;
        private readonly IPurchaseOrderLineRepository _purchaseOrderItemRepository = purchaseOrderItemRepository;
        private readonly IRepository<SalesOrderHeader> _salesOrderHeaderRepository = salesOrderHeaderRepository;
        private readonly IProductRepository _productRepository = productRepository;
        private readonly IBomRepository _bomRepository = bomRepository;
        private readonly ICustomerRepository _customerRepository = customerRepository;
        private readonly ISupplierRepository _supplierRepository = supplierRepository;
        private readonly IDocumentNoService _documentNoService = documentNoService;

        /// <summary>
        /// 中文注释：构建需求池转采购行映射配置。
        /// </summary>
        private static TypeAdapterConfig BuildRequirementPoolToPurchaseOrderLineMapConfig()
        {
            var config = new TypeAdapterConfig();
            config.NewConfig<RequirementPool, PurchaseOrderLine>()
                .Map(dest => dest.Sku, src => src.Sku)
                .Map(dest => dest.QTY, src => src.NetQty)
                .Map(dest => dest.SoLineId, src => src.SourceLineId)
                .Map(dest => dest.SupplyType, src => src.SupplyType)
                .Map(dest => dest.FollowExpectedDate, src => src.RequiredDate)
                .Map(dest => dest.SubNote, src => src.Remark);
            return config;
        }

        /// <summary>
        /// 分页查询拆解需求池列表。
        /// </summary>
        /// <param name="search">查询条件</param>
        /// <returns>分页结果</returns>
        public async Task<ServiceResult<PagedResultDto<RequirementPoolDto>>> GetPagedListAsync(RequirementPoolSearchDto search)
        {
            search ??= new RequirementPoolSearchDto();

            var criteria = BuildCriteria(search);
            int pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
            int pageSize = search.PageSize > 0 ? search.PageSize : 50;

            var paged = await _rpRepository.GetPagedListAsync(criteria, pageIndex, pageSize, "Id DESC");

            return ServiceResult<PagedResultDto<RequirementPoolDto>>.Ok(new PagedResultDto<RequirementPoolDto>
            {
                Items = paged.Items.Adapt<List<RequirementPoolDto>>(),
                TotalCount = paged.TotalCount,
                PageIndex = paged.PageIndex,
                PageSize = paged.PageSize
            });
        }


        /// <summary>
        /// 构建分页查询条件。
        /// </summary>
        private static QueryCriteria BuildCriteria(RequirementPoolSearchDto search)
        {
            var criteria = new QueryCriteria();

            if (!string.IsNullOrWhiteSpace(search.SourceNo))
                criteria.Add("SourceNo", 查询操作符.模糊匹配, search.SourceNo.Trim());

            if (!string.IsNullOrWhiteSpace(search.SourceType))
                criteria.Add("SourceType", 查询操作符.等于, search.SourceType.Trim());

            if (!string.IsNullOrWhiteSpace(search.Sku))
                criteria.Add("Sku", 查询操作符.模糊匹配, search.Sku.Trim());

            if (!string.IsNullOrWhiteSpace(search.SupplyType))
                criteria.Add("SupplyType", 查询操作符.等于, search.SupplyType.Trim());

            if (search.BomLevel.HasValue)
            {
                // 中文注释：层级过滤支持“成品/子件”工作台筛选。
                if (search.BomLevel.Value == 1)
                    criteria.Add("BomLevel", 查询操作符.大于等于, 1);
                else
                    criteria.Add("BomLevel", 查询操作符.等于, search.BomLevel.Value);
            }

            if (search.StatusFlag.HasValue)
                criteria.Add("StatusFlag", 查询操作符.等于, search.StatusFlag.Value);

            if (search.IsKitted.HasValue)
                criteria.Add("IsKitted", 查询操作符.等于, search.IsKitted.Value);

            if (!string.IsNullOrWhiteSpace(search.SupplierKeyword))
            {
                var supplierKeyword = search.SupplierKeyword.Trim();
                criteria.AddOrGroup(
                    ("SupplierCode", 查询操作符.模糊匹配, supplierKeyword),
                    ("SupplierName", 查询操作符.模糊匹配, supplierKeyword));
            }

            if (!string.IsNullOrWhiteSpace(search.Keyword))
            {
                var keyword = search.Keyword.Trim();
                criteria.AddOrGroup(
                    ("SourceNo", 查询操作符.模糊匹配, keyword),
                    ("Sku", 查询操作符.模糊匹配, keyword),
                    ("TargetNo", 查询操作符.模糊匹配, keyword));
            }

            return criteria;
        }


        /// <summary>
        /// 拆解单张订单
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public async Task<ServiceResult<List<RequirementPool>>> ExecuteDecomposeToPoolAsync(DecomposeRequestDto source)
        {
            if (source == null)
                return ServiceResult<List<RequirementPool>>.Fail("拆解源不能为空");

            return await ExecuteDecomposeToPoolAsync(new List<DecomposeRequestDto> { source });
        }

        /// <summary>
        /// 批量拆解需求到需求池。
        /// </summary>
        /// <param name="sourceList"></param>
        /// <returns>需求行集合</returns>

        public async Task<ServiceResult<List<RequirementPool>>> ExecuteDecomposeToPoolAsync(List<DecomposeRequestDto> sourceList)
        {
            if (sourceList == null || sourceList.Count == 0)
                return ServiceResult<List<RequirementPool>>.Fail("拆解源列表不能为空");

            var addList = new List<RequirementPool>();
            // 中文注释：性能优化 —— 基于本次待拆解源，定向构建缓存，避免全表扫描。
            var cache = await BuildDecomposeCacheAsync(sourceList);

            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                foreach (var source in sourceList)
                {
                    if (source?.Lines == null || source.Lines.Count == 0) continue;

                    foreach (var line in source.Lines)
                    {
                        if (string.IsNullOrWhiteSpace(line.Sku) || line.Quantity <= 0) continue;

                        var rootProduct = cache.GetProductBySku(line.Sku);
                        if (rootProduct == null)
                        {
                            continue;
                        }

                        await DecomposeLineAsync(
                            source,
                            line,
                            rootProduct,
                            line.Quantity,
                            0,
                            parentSku: null,
                            addList,
                            parentAddedByCustomerPart: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                            cache);
                    }
                }

                if (addList.Count > 0)
                {
                    await _rpRepository.AddRangeAsync(addList);
                }

                _session.Transaction.Commit();
                return ServiceResult<List<RequirementPool>>.Ok(addList);
            }
            catch (Exception ex)
            {
                _session.Transaction.Rollback();
                return ServiceResult<List<RequirementPool>>.Fail($"拆解失败：{ex.Message}");
            }
            finally
            {
                _session.Transaction.Dispose();
                _session.Transaction = null;
            }
        }

        /// <summary>
        /// 根据来源单据判断是否允许回退审批。
        /// 规则：需求池中存在执行态（StatusFlag > 0）则不允许回退。
        /// </summary>
        public async Task<ServiceResult<bool>> CanRollbackBySourceAsync(string sourceType, string sourceNo)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
                return ServiceResult<bool>.Fail("来源类型和来源单号不能为空");

            var pools = await _rpRepository.GetBySourceAsync(sourceType, sourceNo);
            if (pools == null || pools.Count == 0)
                return ServiceResult<bool>.Ok(true);

            var hasExecuted = pools.Any(x => x.StatusFlag > 0);
            if (hasExecuted)
                return ServiceResult<bool>.Fail("订单中存在已执行记录，不允许回退审批");

            // 中文注释：若已挂接目标执行单（计划单/采购单），即便状态未推进，也不允许回退。
            var hasTargetLinked = pools.Any(x => !string.IsNullOrWhiteSpace(x.TargetNo) || x.TargetLineId.HasValue);
            if (hasTargetLinked)
                return ServiceResult<bool>.Fail("订单中存在已关联执行单，不允许回退审批");

            return ServiceResult<bool>.Ok(true);
        }

        /// <summary>
        /// 按来源删除需求池记录（用于审批回退）。
        /// </summary>
        public async Task<ServiceResult<int>> RemoveBySourceAsync(string sourceType, string sourceNo)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
                return ServiceResult<int>.Fail("来源类型和来源单号不能为空");

            var deleteCount = await _rpRepository.DeleteBySourceAsync(sourceType, sourceNo);
            return ServiceResult<int>.Ok(deleteCount);
        }

        /// <summary>
        /// 将某个来源单据下的待采购需求按供应商分组转为采购单。
        /// 逻辑：
        /// 1) 仅处理 SupplyType=Purchase/直接采购 且 StatusFlag=0 的行；
        /// 2) 按 VendorCode 分组，一组生成一张采购单；
        /// 3) 回写 RequirementPool 的 TargetNo 与 StatusFlag=1。
        /// </summary>
        public async Task<List<string>> ConvertSourceToPurchaseOrdersAsync(string sourceNo)
        {
            if (string.IsNullOrWhiteSpace(sourceNo))
                return [];

            var criteria = new QueryCriteria();
            criteria.Add("SourceNo", 查询操作符.等于, sourceNo.Trim());
            criteria.Add("StatusFlag", 查询操作符.等于, 0);
            criteria.Add("SupplyType", 查询操作符.等于, "Purchase");

            var pools = (await _rpRepository.GetListAsync(criteria)).ToList();

            // 中文注释：兼容当前业务中“直接采购”中文枚举值。
            if (pools.Count == 0)
            {
                var criteriaCn = new QueryCriteria();
                criteriaCn.Add("SourceNo", 查询操作符.等于, sourceNo.Trim());
                criteriaCn.Add("StatusFlag", 查询操作符.等于, 0);
                criteriaCn.Add("SupplyType", 查询操作符.等于, SupplyTypeEnums.直接采购);
                pools = (await _rpRepository.GetListAsync(criteriaCn)).ToList();
            }

            if (pools.Count == 0)
                return [];

            // 中文注释：先构建产品索引，用于供应商和价格回填。
            var products = await _productRepository.GetBySkuListAsync(pools.Select(x => x.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            var productBySku = products
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                .ToDictionary(x => x.Sku!, x => x, StringComparer.OrdinalIgnoreCase);

            var supplierBySku = products
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku) && !string.IsNullOrWhiteSpace(x.SupplierCode))
                .ToDictionary(x => x.Sku!, x => x.SupplierCode!, StringComparer.OrdinalIgnoreCase);

            var missingSupplierSkus = pools
                .Where(x => string.IsNullOrWhiteSpace(x.Sku) || !supplierBySku.ContainsKey(x.Sku))
                .Select(x => string.IsNullOrWhiteSpace(x.Sku) ? "(空SKU)" : x.Sku)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 中文注释：如果存在未维护供应商，禁止执行转单并返回明确 SKU 列表。
            if (missingSupplierSkus.Count > 0)
                throw new Exception("以下SKU未维护默认供应商：" + string.Join(", ", missingSupplierSkus));

            var supplierCodes = supplierBySku.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var supplierList = await _supplierRepository.GetBySupplierCodeListAsync(supplierCodes);
            var supplierNameByCode = supplierList
                .Where(x => !string.IsNullOrWhiteSpace(x.SupplierCode))
                .ToDictionary(
                    x => x.SupplierCode!,
                    x => string.IsNullOrWhiteSpace(x.SupplierName) ? x.SupplierCode! : x.SupplierName,
                    StringComparer.OrdinalIgnoreCase);

            // 中文注释：来源销售单抬头，用于 PoNo 业务标签中的 InvoiceNo 与客户关联码。
            var salesOrderHeaders = await _salesOrderHeaderRepository.GetListAsync(new { OrderNo = sourceNo.Trim() });
            var salesOrderHeader = salesOrderHeaders.FirstOrDefault();

            Customer? sourceCustomer = null;
            if (salesOrderHeader != null && !string.IsNullOrWhiteSpace(salesOrderHeader.CustomerCode))
            {
                var customers = await _customerRepository.GetListAsync(new { CustomerCode = salesOrderHeader.CustomerCode.Trim() });
                sourceCustomer = customers.FirstOrDefault();
            }

            var groups = pools
                .GroupBy(x => GetSupplierCodeBySku(x.Sku, supplierBySku), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var createdOrderNos = new List<string>();
            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                foreach (var group in groups)
                {
                    var first = group.First();
                    var orderNo = await BuildOrderNoByDocumentServiceAsync();

                    var supplierCode = group.Key;
                    var supplierName = supplierNameByCode.ContainsKey(supplierCode)
                        ? supplierNameByCode[supplierCode]
                        : supplierCode;
                    var supplier = supplierList.FirstOrDefault(x => string.Equals(x.SupplierCode, supplierCode, StringComparison.OrdinalIgnoreCase));

                    var customerDocRelateNo = sourceCustomer?.DocRelateNo ?? string.Empty;
                    var invoiceOrSourceNo = string.IsNullOrWhiteSpace(salesOrderHeader?.InvoiceNo)
                        ? sourceNo.Trim()
                        : salesOrderHeader!.InvoiceNo!.Trim();
                    var supplierDocRelateNo = supplier?.DocRelateNo ?? string.Empty;
                    var businessPoNo = string.Concat(customerDocRelateNo, invoiceOrSourceNo, supplierDocRelateNo);

                    var expectedDate = first.RequiredDate;
                    if (!expectedDate.HasValue)
                    {
                        var leadTime = supplier?.DefaultLeadTime ?? 0;
                        expectedDate = DateTime.Now.AddDays(leadTime > 0 ? leadTime : 0);
                    }

                    var po = new PurchaseOrderHead
                    {
                        PoNo = businessPoNo,
                        SalesOrderNo = sourceNo,
                        OrderNo = orderNo,
                        OrderDate = DateTime.Now,
                        ExpectedDate = expectedDate,
                        SupplierCode = supplierCode,
                        SupplierName = supplierName,
                        Currency = supplier?.Currency,
                        Remark = $"来源单据：{sourceNo}",
                        LineCount = group.Count(),
                        TotalQty = group.Sum(x => x.NetQty),
                        TotalAmountLocalCurrency = group.Sum(x => ResolveUnitPriceBySku(x.Sku, productBySku) * x.NetQty),
                        IsDeleted = false,
                        CreatedDate = DateTime.Now,
                        Status = null,
                        Even = null
                    };
                    await _purchaseOrderRepository.AddAsync(po);

                    var itemList = group.Select(x =>
                    {
                        // 中文注释：按 SKU 关联产品档案，提取物料分类与统计字段。
                        productBySku.TryGetValue(x.Sku, out var product);
                        var unitPrice = ResolveUnitPriceBySku(x.Sku, productBySku);
                        var item = x.Adapt<PurchaseOrderLine>(RequirementPoolToPurchaseOrderLineMapConfig);

                        item.OrderNo = orderNo;
                        item.UnitPrice = unitPrice;
                        item.UnitAmount = unitPrice * x.NetQty;
                        // 中文注释：MaterialType/UnitCBM/UnitWeight 来自产品档案。
                        item.MaterialType = product?.MaterialType;
                        item.UnitCBM = product?.UnitCBM;
                        item.UnitWeight = product?.UnitWeight;
                        // 中文注释：新建采购行时，完工数量与财务实收数量统一初始化为 0。
                        item.ReadyQty = 0;
                        item.RecQty = 0;
                        item.IsDeleted = false;
                        item.CreatedDate = DateTime.Now;
                        item.Status = null;
                        item.Even = null;

                        return item;
                    }).ToList();

                    if (itemList.Count > 0)
                    {
                        await _purchaseOrderItemRepository.AddRangeAsync(itemList);
                    }

                    foreach (var rp in group)
                    {
                        rp.TargetNo = orderNo;
                        rp.StatusFlag = 1;
                        rp.ChangeTime = DateTime.Now;
                    }
                    await _rpRepository.UpdateRangeAsync(group.ToList());

                    createdOrderNos.Add(orderNo);
                }

                _session.Transaction.Commit();
                return createdOrderNos;
            }
            catch
            {
                _session.Transaction.Rollback();
                throw;
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }

        /// <summary>
        /// 通过单号服务生成系统内码（OrderNo）。
        /// </summary>
        private async Task<string> BuildOrderNoByDocumentServiceAsync()
        {
            var rst = await _documentNoService.GetNextDocumentNoAsync(new Contracts.DTOs.SystemDtos.DocumentNoRequestDto
            {
                DocType = DocumentTypeStaticEnums.PurchaseOrder,
                BusinessDate = DateTime.Now,
                SerialWidth = 4,
                Operator = "system"
            });

            if (rst.Success && rst.Data != null && !string.IsNullOrWhiteSpace(rst.Data.DocumentNo))
                return rst.Data.DocumentNo;

            // 中文注释：降级兜底，保证流程不中断。
            return $"PO{DateTime.Now:yyyyMMddHHmmssfff}";
        }

        /// <summary>
        /// 按 SKU 获取默认供应商编码。
        /// </summary>
        private static string GetSupplierCodeBySku(string sku, Dictionary<string, string> supplierBySku)
        {
            if (string.IsNullOrWhiteSpace(sku) || supplierBySku == null)
                return string.Empty;

            string supplierCode;
            if (!supplierBySku.TryGetValue(sku, out supplierCode))
                return string.Empty;

            return (supplierCode ?? string.Empty).Trim();
        }

        /// <summary>
        /// 按 SKU 解析采购单价（优先产品人民币单价）。
        /// </summary>
        private static decimal ResolveUnitPriceBySku(string sku, Dictionary<string, Product> productBySku)
        {
            if (string.IsNullOrWhiteSpace(sku) || productBySku == null)
                return 0m;

            Product product;
            if (!productBySku.TryGetValue(sku, out product) || product == null)
                return 0m;

            return product.CNYPrice ?? 0m;
        }










        #region 私有方法
        /// <summary>
        /// 中文注释：按 SupplyType 拆解单行需求。
        /// </summary>
        private static async Task DecomposeLineAsync(DecomposeRequestDto source, DecomposeLineRequestDto sourceLine, Product currentProduct,
            decimal currentQty, int bomLevel, string? parentSku, List<RequirementPool> addList,
            HashSet<string> parentAddedByCustomerPart, DecomposeCache cache)
        {
            // 中文注释：直接采购不向下拆，直接落当前物料需求。
            if (string.Equals(currentProduct.SupplyType, SupplyTypeEnums.直接采购, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(currentProduct.BomNo)
                || !string.Equals(currentProduct.SupplyType, SupplyTypeEnums.按BOM拆分, StringComparison.OrdinalIgnoreCase))
            {
                addList.Add(BuildRequirement(source, sourceLine, currentProduct, currentQty, bomLevel, parentSku, null));
                return;
            }

            // 中文注释：BOM 父项先入池（标识该层需求）。
            addList.Add(BuildRequirement(source, sourceLine, currentProduct, currentQty, bomLevel, parentSku, null));

            var bomList = cache.GetBomByBomNo(currentProduct.BomNo).ToList();
            if (bomList.Count == 0)
                return;

            foreach (var bom in bomList)
            {
                if (string.IsNullOrWhiteSpace(bom.SubSku) || bom.Denominator == 0)
                    continue;

                var childProduct = cache.GetProductBySku(bom.SubSku);
                if (childProduct == null)
                    continue;

                var childQty = currentQty * bom.Numerator / bom.Denominator;
                var remark = bom.Note;

                // 中文注释：客供件逻辑：
                // 1) 客供件子件正常入池；
                // 2) 额外将父级再加入一次（无论客供件型号个数，只加一次）；
                // 3) 客供件备注写“货好后送往父级.供应商名称”。
                if (string.Equals(childProduct.MaterialType, "客供件", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(childProduct.MaterialType, "客供", StringComparison.OrdinalIgnoreCase))
                {
                    var supplierName = cache.ResolveSupplierName(childProduct.SupplierCode);
                    remark = $"货好后送往{currentProduct.Sku}.{supplierName}";

                    var parentOnceKey = $"{source.SourceNo}|{sourceLine.SourceLineId}|{currentProduct.Sku}";
                    if (!parentAddedByCustomerPart.Contains(parentOnceKey))
                    {
                        parentAddedByCustomerPart.Add(parentOnceKey);
                        addList.Add(BuildRequirement(source, sourceLine, currentProduct, currentQty, bomLevel, parentSku, "含客供件"));
                    }
                }

                // 中文注释：子件正常入池。
                addList.Add(BuildRequirement(source, sourceLine, childProduct, childQty, bomLevel + 1, currentProduct.Sku, remark));

                // 中文注释：继续向下递归拆解（仅按BOM拆分/含客供BOM且存在BomNo时才会继续）。
                await DecomposeLineAsync(
                    source,
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
        /// 组装需求池实体。
        /// </summary>
        private static RequirementPool BuildRequirement(DecomposeRequestDto source, DecomposeLineRequestDto sourceLine,
            Product product, decimal qty, int bomLevel, string? parentSku, string? remark)
        {
            return new RequirementPool
            {
                SourceType = source.SourceType,
                SourceNo = source.SourceNo,
                SourceLineId = sourceLine.SourceLineId,
                Sku = product.Sku,
                ParentSku = parentSku ?? string.Empty,
                BomLevel = bomLevel,
                SupplyType = product.SupplyType ?? SupplyTypeEnums.直接采购,
                GrossQty = qty,
                NetQty = qty,
                ExecutedQty = 0,
                ReceivedQty = 0,
                ShippedQty = 0,
                RequiredDate = sourceLine.RequiredDate,
                StatusFlag = 0,
                IsKitted = false,
                Remark = remark,
                TargetNo = null,
                TargetLineId = null,
                IsDeleted = false,
                CreatedDate = DateTime.Now,
                ChangeTime = DateTime.Now,
                Even = null,
                Status = null
            };
        }

        /// <summary>
        /// 中文注释：构建拆解缓存。
        /// 目标：将递归拆解中频繁访问的 Product/BOM/Supplier 统一拉到内存，
        /// 避免每层递归逐条查询数据库导致性能劣化。
        /// </summary>
        private async Task<DecomposeCache> BuildDecomposeCacheAsync(List<DecomposeRequestDto> sourceList)
        {
            // 中文注释：第一步，先收集本次拆解入口 SKU。
            var rootSkus = sourceList
                .Where(x => x?.Lines != null)
                .SelectMany(x => x.Lines)
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                .Select(x => x.Sku.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rootSkus.Count == 0)
            {
                return new DecomposeCache([], [], []);
            }

            // 中文注释：第二步，分层批量加载 Product 与 BOM。
            var productBySku = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var bomByBomNo = new Dictionary<string, List<Bom>>(StringComparer.OrdinalIgnoreCase);
            var loadedBomNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var pendingSkus = new HashSet<string>(rootSkus, StringComparer.OrdinalIgnoreCase);
            while (pendingSkus.Count > 0)
            {
                var skuBatch = pendingSkus.ToList();
                pendingSkus.Clear();

                var products = await _productRepository.GetBySkuListAsync(skuBatch);
                foreach (var product in products)
                {
                    if (!string.IsNullOrWhiteSpace(product.Sku))
                        productBySku[product.Sku] = product;
                }

                var toLoadBomNos = products
                    .Where(x => !string.IsNullOrWhiteSpace(x.BomNo)
                                && string.Equals(x.SupplyType, SupplyTypeEnums.按BOM拆分, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.BomNo!)
                    .Where(x => !loadedBomNos.Contains(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (toLoadBomNos.Count == 0)
                    continue;

                foreach (var bomNo in toLoadBomNos)
                    loadedBomNos.Add(bomNo);

                // 中文注释：按 BOM 号批量查询 BOM 明细（仓储层已支持批量接口）。
                var bomMap = await _bomRepository.Get一堆BomBy一堆Bom号(toLoadBomNos);
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

            // 中文注释：第三步，按实际涉及到的供应商编码，定向批量加载供应商。
            var supplierCodes = productBySku.Values
                .Where(x => !string.IsNullOrWhiteSpace(x.SupplierCode))
                .Select(x => x.SupplierCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var suppliers = await _supplierRepository.GetBySupplierCodeListAsync(supplierCodes);

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
                    .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                _bomByBomNo = boms
                    .Where(x => !string.IsNullOrWhiteSpace(x.BomNo))
                    .GroupBy(x => x.BomNo, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                _supplierByCode = suppliers
                    .Select(x => new
                    {
                        Supplier = x,
                        SupplierCode = x.SupplierCode?.Trim()
                    })
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
        #endregion
        }
    }
}
