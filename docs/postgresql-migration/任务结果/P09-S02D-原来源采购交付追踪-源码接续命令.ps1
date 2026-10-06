# 一次性源码接续：只改已记录的既有测试文件，不在运行时翻译SQL，也不创建生产兼容框架。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskPath='SwiftExport.Application.Tests/DemandPlanning/GrossDemandTraceSqlTests.cs'
if((Get-FileHash -LiteralPath $taskPath).Hash -ne '6512BC070172909CD1A884B809AF59FCBC71B491DCA19952FC42C854F51AC885'){throw '导入/门禁接续后的源SHA变化，停止'}
$taskText=Get-Content $taskPath -Raw
$taskTables=@{GrossDemand='gross_demand';NetDemand='net_demand';Product='product';PurchaseOrderHead='purchase_order_head';PurchaseOrderLine='purchase_order_line';DemandExecutionLink='demand_execution_link';InboundDocument='inbound_document';InboundDocumentLine='inbound_document_line';PackingListHead='packing_list_head';PackingListLine='packing_list_line'}
$taskColumns=@{Uid='uid';Version='version';LifecycleStatus='lifecycle_status';SourceType='source_type';SourceUid='source_uid';SourceNo='source_no';SourceLineId='source_line_id';CustomerId='customer_id';DemandBasisFingerprint='demand_basis_fingerprint';IsNonStandard='is_non_standard';SkuSnapshot='sku_snapshot';UnitSnapshot='unit_snapshot';DemandQty='demand_qty';GrossDemandId='gross_demand_id';SourceDemandVersionSnapshot='source_demand_version_snapshot';SourceDemandBasisFingerprintSnapshot='source_demand_basis_fingerprint_snapshot';DecompositionRevision='decomposition_revision';LineNumber='line_number';RootNetDemandId='root_net_demand_id';ParentNetDemandId='parent_net_demand_id';ProductId='product_id';DeliveryMode='delivery_mode';SupplyTypeSnapshot='supply_type_snapshot';GrossQty='gross_qty';NetQty='net_qty';OriginKind='origin_kind';ConfirmationStatus='confirmation_status';OrderNo='order_no';CustomerCode='customer_code';Currency='currency';SupplierId='supplier_id';SupplierCode='supplier_code';ExecutionRequestId='execution_request_id';OrderDate='order_date';PurchaseOrderId='purchase_order_id';Sku='sku';Unit='unit';NetDemandId='net_demand_id';CommandItemId='command_item_id';TargetType='target_type';TargetHeadId='target_head_id';TargetNo='target_no';TargetLineId='target_line_id';LinkStatus='link_status';AllocatedQty='allocated_qty';ReleasedQty='released_qty';DocumentNo='document_no';DocumentType='document_type';CreationMode='creation_mode';PostingStatus='posting_status';WarehouseSiteId='warehouse_site_id';ReceiptDate='receipt_date';InboundDocumentId='inbound_document_id';Quantity='quantity';PackingListNo='packing_list_no';InvoiceDate='invoice_date';PostedDate='posted_date';ReversedDate='reversed_date';CreateCommandId='create_command_id';PackingListHeadId='packing_list_head_id';IsNonStandardSnapshot='is_non_standard_snapshot';SourceTypeSnapshot='source_type_snapshot';SourceDocumentNoSnapshot='source_document_no_snapshot';Qty='qty';SourceVersion='source_version'}
$taskNumbers=@{'MD-TRACE'='BatchTraceNo';'PO-TRACE'='BatchOrderNo';'PO-MISSING'='BatchMissingNo';'IR-TRACE'='BatchReceiptNo';'IR-UNVERIFIED-REVERSE'='BatchUnrelatedNo';'IR-RELATED-REVERSE'='BatchRelatedNo';'MD-1'='BatchFirstNo';'MD-2'='BatchSecondNo';'PL-POSTED'='BatchPostedNo';'PL-REVERSED'='BatchReversedNo'}
$taskText=[regex]::Replace($taskText,'"""[\s\S]*?"""|"(?:[^"\\]|\\.)*"',{
 param($taskMatch)
 $taskLiteral=$taskMatch.Value
 if(-not($taskLiteral.TrimStart([char]34).TrimStart() -match '^(INSERT|UPDATE|DECLARE)\b')){return $taskLiteral}
 foreach($taskName in $taskTables.Keys){$taskLiteral=$taskLiteral.Replace('dbo.'+$taskName,'public.'+$taskTables[$taskName])}
 $taskLiteral=$taskLiteral.Replace('DECLARE @posted uniqueidentifier=NEWID();','').Replace('DECLARE @receipt uniqueidentifier=NEWID();','').Replace('NEWID()','gen_random_uuid()').Replace("N'","'")
 $taskLiteral=[regex]::Replace($taskLiteral,'\bQTY\b','quantity')
 foreach($taskName in $taskColumns.Keys){$taskLiteral=[regex]::Replace($taskLiteral,'(?<![A-Za-z0-9_@])'+[regex]::Escape($taskName)+'\b',$taskColumns[$taskName])}
 foreach($taskName in $taskNumbers.Keys){$taskLiteral=$taskLiteral.Replace("'"+$taskName+"'",'@'+$taskNumbers[$taskName])}
 $taskLiteral=[regex]::Replace($taskLiteral,'(@fingerprint,)\s*1,','$1TRUE,')
 $taskLiteral=$taskLiteral.Replace("@first,1,'MD'","@first,TRUE,'MD'")
 return $taskLiteral
})
$taskText=$taskText.Replace('new { line, warehouseId = db.ReceiptSiteId, productId = db.ComponentId });','new { line, warehouseId = db.ReceiptSiteId, productId = db.ComponentId, posted });')
$taskText=$taskText.Replace('new { warehouseId = db.ReceiptSiteId, productId = db.ComponentId, line });','new { warehouseId = db.ReceiptSiteId, productId = db.ComponentId, line, receipt });')
$taskText=$taskText.Replace('TraceView = "HasPurchase"','TraceView = "HasPurchase", Keyword = db.Batch').Replace('TraceView = "NoPurchase"','TraceView = "NoPurchase", Keyword = db.Batch').Replace('TraceView = "HasDelivery"','TraceView = "HasDelivery", Keyword = db.Batch').Replace('TraceView = "NoDelivery"','TraceView = "NoDelivery", Keyword = db.Batch').Replace('CustomerKeyword = "TEST-C"','CustomerKeyword = db.CustomerCode')
$taskHelperSource=Get-Content 'SwiftExport.Application.Tests/DemandPlanning/MaterialDemandTraceSqlTests.cs' -Raw
$taskHelperStart=$taskHelperSource.IndexOf('    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows')
$taskHelperEnd=$taskHelperSource.IndexOf('    /// <summary>只为仓储上下文提供测试身份')
if($taskHelperStart -lt 0 -or $taskHelperEnd -le $taskHelperStart){throw '既有数据保护定位失败'}
$taskProtection=$taskHelperSource.Substring($taskHelperStart,$taskHelperEnd-$taskHelperStart).Replace('p09-s02c-material-trace','p09-s02d-gross-trace').Replace('共享收货追踪不能产生库存事实','来源采购交付追踪不能产生库存事实')
$taskFixture=@'
    /// <summary>只服务原两个追踪场景的当前PG夹具；不创建库、表、账号或公共SQL翻译器。</summary>
    private sealed class TraceFixture : IAsyncDisposable
    {
        private readonly string connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        private WebApplication? app;
        public string Batch { get; } = "PGS02D_" + Guid.NewGuid().ToString("N");
        public string CustomerCode => Batch + "_TEST-C";
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ReceiptSupplierId { get; } = Guid.NewGuid();
        public Guid ProductId { get; } = Guid.NewGuid();
        public Guid ComponentId { get; } = Guid.NewGuid();
        public Guid ReceiptSiteId { get; } = Guid.NewGuid();

        /// <summary>只新增本方法实际所需事实，读取dv现有管理员资格，以当前宿主DI执行原读取权限。</summary>
        public async Task InitializeAsync(bool purchase = true)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var actor = await session.Connection.QuerySingleAsync<TraceActor>("""
                SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
                WHERE u.user_name='dv' AND u.is_active AND NOT u.is_deleted AND EXISTS(
                    SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                    WHERE ur.user_id=u.uid AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN');
                """);
            await session.Connection.ExecuteAsync("""
                INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active)
                VALUES(@CustomerId,@CustomerCode,'原来源追踪客户',@CustomerCode,0,0,TRUE);
                """, new { CustomerId, CustomerCode });
            if (purchase)
                await session.Connection.ExecuteAsync("""
                    INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active)
                    VALUES(@SupplierId,@ActualCode,'实际供应商',@ActualCode,TRUE),
                          (@ReceiptSupplierId,@ReceiptCode,'收货供应商',@ReceiptCode,TRUE);
                    INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active)
                    VALUES(@ProductId,@RootSku,'采购成品','PCS','成品','采购',0,@EmptyId,TRUE),
                          (@ComponentId,@ComponentSku,'客供件','PCS','客供件','采购',0,@EmptyId,TRUE);
                    INSERT INTO public.warehouse_site(uid,code,name,site_type,supplier_id,is_active)
                    VALUES(@ReceiptSiteId,@SiteCode,'独立新收货仓',0,@ReceiptSupplierId,TRUE);
                    """, new { SupplierId, ReceiptSupplierId, ProductId, ComponentId, ReceiptSiteId, ActualCode = Batch + "_ACTUAL",
                        ReceiptCode = Batch + "_RECEIPT", RootSku = Batch + "_FG", ComponentSku = Batch + "_B", SiteCode = Batch + "_R", EmptyId = Guid.Empty });
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(Root(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
            builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
            app = builder.Build();
        }

        /// <summary>执行已明确写成PG方言的原夹具SQL，仅补绑定本批随机单号，不在运行时改SQL或旧记录。</summary>
        public async Task<int> ExecuteAsync(string sql, object? args = null)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var parameters = new DynamicParameters(args);
            parameters.Add("BatchTraceNo", Batch + "_MD-TRACE"); parameters.Add("BatchOrderNo", Batch + "_PO-TRACE");
            parameters.Add("BatchMissingNo", Batch + "_PO-MISSING"); parameters.Add("BatchReceiptNo", Batch + "_IR-TRACE");
            parameters.Add("BatchUnrelatedNo", Batch + "_IR-UNVERIFIED-REVERSE"); parameters.Add("BatchRelatedNo", Batch + "_IR-RELATED-REVERSE");
            parameters.Add("BatchFirstNo", Batch + "_MD-1"); parameters.Add("BatchSecondNo", Batch + "_MD-2");
            parameters.Add("BatchPostedNo", Batch + "_PL-POSTED"); parameters.Add("BatchReversedNo", Batch + "_PL-REVERSED");
            return await session.Connection.ExecuteAsync(sql, parameters);
        }

        /// <summary>保持真实独立request作用域和原服务事务所有权，不模拟查询、权限或异常分类。</summary>
        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = app!.Services.CreateScope();
            var result = await action(scope.ServiceProvider);
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease);
            return result;
        }

        /// <summary>仅关闭本夹具服务和连接，所有新事实保留，不删除或清理数据。</summary>
        public async ValueTask DisposeAsync() { if (app is not null) await app.DisposeAsync(); }
    }

    /// <summary>dv现有有效管理员请求身份，权限仍由实际服务查询，不创建或改写账号。</summary>
    private sealed class TraceActor : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string UserName { get; set; } = "";
        /// <summary>身份由本库资格查询取得，不代表本批运行了HTTP/JWT登录。</summary>
        public bool IsAuthenticated => true;
    }

    /// <summary>定位宿主配置，连接仅在内存覆盖为授权failure库，不修改配置文件。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi", "appsettings.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位 WebApi 配置。");
    }
'@
$taskLastBrace=$taskText.LastIndexOf('}')
$taskText=$taskText.Substring(0,$taskLastBrace)+$taskProtection+$taskFixture+[Environment]::NewLine+'}'+[Environment]::NewLine
[IO.File]::WriteAllText((Resolve-Path -LiteralPath $taskPath).Path,$taskText,[Text.UTF8Encoding]::new($false))
'Two original trace methods now contain explicit PG SQL; only same-file fixture/protection appended.'
