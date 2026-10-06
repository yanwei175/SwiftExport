# M26-Excel采购完整查询-实施结果

- 轮次：M26-PURCHASE-IMPORT-20261005，执行 B。
- 主 Agent / Workflow：Backend / 后端 API 工作流；依据 MAIN 恢复实施第三批分派，保持只读查询业务语义及完整四方法接口。
- 开始：2026-10-05 12:49:16 +08:00；唯一构建及源码复核完成：12:53:15 +08:00。
- 授权写入：仅新增仓储、本报告和本人状态。源 SQL Server、服务/Core/DTO/DI/结构/公共参数/映射/锁框架、项目及旧成果未编辑；无整树同步、代理或消息发送。
- 仓储绝对路径：C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/SwiftExport.Infrastructure/Repositories/ForExcel/PostgreSqlForExcelPurchaseOrderRepository.cs。
- 仓储固定 SHA256：`D2A809A02CB061B12E823461D56929724B470C7A79129B97DAD6292F6119A094`。
- 一个 sealed 类、一个文件，完整实现 IForExcelPurchaseOrderRepository 四方法；新增 Infrastructure 查询代码，不调整服务业务规则或运行接线。报告自身 SHA 在执行 B 状态固定。

## 源事实及逐方法范围

以 MAIN ForExcelPurchaseOrderRepository、Core 接口、三个采购导入 DTO、ForExcelPurchaseOrderService 和独立采购创建的 ProcurementOrderDraftService / IndependentPurchaseOrderInputPolicy 为事实。

|方法|保留语义与 PG 翻译|
|---|---|
|FindCustomersAsync|输入 code.Trim().ToUpperInvariant() 不变；库端 UPPER(LTRIM(RTRIM(...))) 为 upper(btrim(customer_code,' ')) 加既有 CI。仅 IsDeleted=false，保留停用和全部歧义结果。原4列别名返回，按原 SqlGuid 顺序并 FOR SHARE。没有新空白短路或 TOP/LIMIT。|
|FindSupplierAsync|原 uid 参数及 QuerySingleOrDefault 不变；仅 Uid、IsDeleted=false，原4列保留 IsActive 供服务判断；已有供应商 FOR SHARE，不过滤停用、不用名称/默认供应商推断身份。|
|MatchProductsAsync|skus.Length==0 原样提前 []，包括取消请求；不访问会话，不新规范化/去重/改写输入。非空绑定一个 text[]。原客户身份、关系未删条件，库端客户型号 ASCII btrim/upper、CI 匹配；数组项只在 SQL rtrim ASCII 尾空格，保留前导空格、原大小写、null 元素及全部关联。产品匹配稳定 SkuId 且未删，LEFT JOIN LATERAL 保留可空产品侧；关系共享锁在外层 OF cp，产品共享锁在 LATERAL 实际源中。|
|FindUnitAsync|输入 unit.Trim().ToUpperInvariant()；库端 data_name_en 仅 ASCII btrim/upper；DictCode Unit 使用原等值所需 CI/ASCII 尾空格，IsEnabled=true、IsDeleted=false；返回 ASCII btrim 后原名称，不改大小写。读并锁全部匹配行，Count==1 才返回，否则 null，不自行挑第一条。|

MatchProducts 原9列：CustomerProductId、CustomerSku、ProductId、Sku、LinkSku、Unit、MainImage、MaterialType、IsUsable（ProductId nullable）。保持原 COALESCE(NULLIF(btrim(cp.link_sku,' '),''),p.link_sku)，只修剪关系 LinkSku 的 ASCII 空格，产品回退 LinkSku 原样返回。没有把数据库比较键写回 DTO。

IsUsable 仍 CASE WHEN：关系启用、产品启用、产品 SupplyType=采购、MaterialType 属于成品/零件/客供件、ASCII 修剪后 SKU 非空；满足全部为 true，否则 false，包括 LEFT JOIN 缺产品的 NULL 条件。字符串资格复用既有 CI 和 rtrim ASCII 尾空格等值；没有增加“单位必须非空”等源仓储未有条件。停用/断链/不可采购只影响 IsUsable，不从候选结果过滤。

## 比较、排序及事务

复用既有 public.erp_dictionary_ci、SQL btrim/rtrim 的 ASCII U+0020 边界及 PostgreSqlParameters.Add/AddTexts，不新增全局比较框架。应用端原完整 .NET Trim/ToUpperInvariant 与库端仅 ASCII 修剪是两层不同规则，保持各自位置。匹配数组沿用现销售导入 PG 仓储的右侧 rtrim 方案，以模拟源 IN 忽略 ASCII 尾空格，不能对输入额外 Trim/大写。所有原始 DTO 字符串除源已要求的 LinkSku/单位修剪以外不改变。

客户/单位 Uid 升序、客户型号排序的 Uid 尾序复用 MAIN 已冻结 SqlGuid 字节顺序：
10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0，
通过 ARRAY[get_byte(uuid_send(...),...)]，不使用 PG 原生 uuid 或 .NET Guid 的排序。客户型号用 rtrim ASCII 尾空格、CI、NULLS FIRST，再 SqlGuid 升序。未添加分页、DISTINCT、候选限制或 C# 排序。

所有 CommandDefinition 透传原 context.Session.Transaction（允许 null）及 CancellationToken，不新建/提交/回滚事务，不增加父事务门禁，也不主动添加原没有的前后取消检查。空数组仍在访问会话/执行 SQL 前短路，原 null 参数/数组未新增兜底或 validation。原映射、QuerySingleOrDefault、计数判断、数据库与取消异常不吞并或改写。

HOLDLOCK 翻译为**已读行** FOR SHARE；独立采购创建外层已有 Serializable 时持有到外层事务结束，只读检查无外层事务则由 PG 隐式语句事务结束释放，生命周期与源无外层事务的单语句锁一致。原产品 LEFT JOIN 改固定 LATERAL 源内 FOR SHARE，外层只 FOR SHARE OF cp；避免非法对 nullable p 加锁。不增加用户/客户父头、范围锁、表锁、advisory、公共租约或新接口。

该翻译不重现 SQL Server HOLDLOCK 的不存在关系范围保护。未读到的新增关系/新单位/客户空范围、高隔离首次快照及完整创建协议仍 MAIN 待验；不能单凭读行共享锁宣称完整可串行化链兼容。多资源锁偏序、产品失效等待、共享锁兼容/取消、真实写者参与及高隔离提交失败回滚未运行。

## 实际调用和相关测试上下文

ForExcelPurchaseOrderService 检查阶段：客户恰好一个且启用才定位；供应商按实际稳定身份判资格；型号先 Normalize+Distinct 后调用 MatchProducts；服务保留所有候选解释并由 InputPolicy.Resolve 处理显式选中、歧义、不可采购/断链；单位通过 FindUnit 唯一判断。此仓储不把服务资格前移。

ProcurementOrderDraftService.CreateInTransactionAsync 使用公共执行器 Serializable；先其他 references/confirmation 重验当事方，再重新批量 MatchProducts，不能把导入 Check 当保存凭证。源 InputPolicy.Resolve 按 Normalize 校验选中候选和产品身份，独立创建仍从其 IProcurementReferenceReadRepository 验实际产品及单位；本轮不修改该链或把其单位读取误归本仓储。读取现有 ForExcelPurchaseOrderTests 相关全部候选/歧义/停用/选中/非标单位/批量规范化断言，ForExcelPurchaseOrderContractTests 固定范围与取消/事务契约及 ForExcelPurchaseOrderSqlTests 检查→创建→重放相关上下文。未新增或运行这些测试。

## 唯一验证命令与未验

工作目录 C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport：

```powershell
$env:NUGET_SCRATCH=Join-Path (Get-Location).Path '.artifacts/nuget-scratch'
foreach ($switch in @('SWIFTEXPORT_RUN_PG_TESTS','SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_RUN_PG_COMMAND_TESTS','SWIFTEXPORT_RUN_PG_RBAC_TESTS','SWIFTEXPORT_RUN_SQL_TESTS')) { Remove-Item -LiteralPath "Env:$switch" -ErrorAction SilentlyContinue }
dotnet build SwiftExport.Infrastructure/SwiftExport.Infrastructure.csproj --no-restore -p:UseSharedCompilation=false
```

只有一次 Infrastructure 构建：退出0，耗时2.71秒，0警告0错误；Contracts/Core/Infrastructure 成功。复用资产，没有 restore、改项目/包源或禁审计。人工复核四接口、全部固定投影、资格、空数组、nullable LEFT JOIN、共享锁位置、SqlGuid 表达式及参数；新源码0替换字符、0尾空格。

无 DB 连接、SQL/PG 测试或 DDL/DML；无普通新增测试/模拟矩阵/全量测试，遵守阶段集中验证；无配置/DI/应用启动、Git 命令、提交/推送、同步整树或旧成果修改。

**代码完成待验证**：动态 PG SQL/LATERAL 行锁执行、具体 ICU/Unicode/B10 及 UPPER 差异、标点/前导零/数组 ASCII 尾空格/null 项、SqlGuid实际排序、停用/断链/全部候选/单位恰好唯一、null事务和外层Serializable下共享锁等待/释放、取消、缺失范围并发插入、高隔离完整创建及回滚均未在实库运行。冻结比较表达式不等于新路径全面兼容验收；由 MAIN 接回后串行安排必要场景。

## 两树依赖差异与冻结

20份只读关键路径、B/MAIN共40条 SHA，起止全部相同。MAIN 为源行为事实，B 为本次编译环境；既有差异保留，不精确同步也不声称两树相同。API Add/AddTexts 与 SqlGuid表达式已读 MAIN 核对；下表只是关键依赖审计，不能替代全树 Git diff。

|只读路径|B 起止 SHA256|MAIN 起止 SHA256|
|---|---|---|
|SwiftExport.Infrastructure/Repositories/ForExcel/ForExcelPurchaseOrderRepository.cs|`CF604B8B8DAA51477007567544F5B17D0F2D8BD8F1FA5B823082CECDC86E7F7D`|`9B1B107DF2DD4CB09B9AF7A291E2D045B5D1C0AC85018A6E0018084D3AAE1FBA`|
|SwiftExport.Core/Interfaces/ForExcel/IForExcelPurchaseOrderRepository.cs|`FD4F996ED4143EF0AFEFD72C99F6C8A7744E366336BB8D65BEFA2C2D59EFB720`|`E50681C710BFD6136310792FE2E1FF7899AD0BBBFEF320BDB0C1EDE0A65A136A`|
|SwiftExport.Application/Services/ForExcel/ForExcelPurchaseOrderService.cs|`80B808C8D30DC8AF6FE9966F3D0F2F7822E5D86E6CBD6E0FDE573F3C71116A28`|`447268D21668195CB8BC655396B1F611D7DD1ADC4CFF060CFF329B68C814545E`|
|SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/ProcurementOrderDraftService.cs|`3FA10ABCC9EB21D82212EE85338DDE59FBDC99B1B2D5DEB9889CFB366FD70009`|`3FA10ABCC9EB21D82212EE85338DDE59FBDC99B1B2D5DEB9889CFB366FD70009`|
|SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/IndependentPurchaseOrderInputPolicy.cs|`9A356D8CEE30D4EBBDBDD03CA5CDC2739FA9264F4485B5302D99ADAA58ECFF08`|`E4ECEBD25EB844EC6E3700D33ED91BFD66B34BD33681894B42E274C423586AE7`|
|SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders/PurchaseOrderImportCustomerDto.cs|`E5C28C4FA328BAFD8B81858FDD09C8E0C7F48BF7CCB2920E8ACCC6A88C7BBDC9`|`0E529435FB9EDAEB9A80EB252F52A8B00E225A59E82FAF1A5157CD4825708250`|
|SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders/PurchaseOrderImportSupplierDto.cs|`793B08146D2F3032DD5AE38847BFBB8F622CC9C27A8123608572B37BF88281A7`|`5FC6DEF2AACAA0FA18D4BA7C221CAECC4958FBFF9A8C08F0CA1FD9224A5A3BD9`|
|SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders/PurchaseOrderImportProductCandidateDto.cs|`0158E6AEF7F91FE5B98F2FC277139E73F4D7F5A47467ED34DD7C7CC8D81E8E05`|`C2890B106392F47DD835EAEAA4A101ED875810BDB9EC5CC5A95F52E91F287CC4`|
|SwiftExport.Core/Entities/MasterData/Customer.cs|`AE6B5615A33C830772E34E57243903E0718142B35C656DD000E886EBFFFC6C85`|`AD458941497EABF8E30441A8FCFD46655162FAAF3C4D91854ED8461F421F098A`|
|SwiftExport.Core/Entities/MasterData/Supplier.cs|`D4CEF1BD2F1F84836620C19C882D809F9436F4CFA1458B2F912755F3DD5AFF87`|`7A5E2A564E7383B3FD7756680A42048B1B3A43C6B7C24CAC3F91519FA7AAC8F1`|
|SwiftExport.Core/Entities/MasterData/Product.cs|`39248EC747ADB59577854372BF02A8AB5F62269ED42F8A6BAFD55A4E3D10DC57`|`12A845EFC6E2DD6F3BFFB85DAF84957ED5646824CA2F2A323C51F7A36383525E`|
|SwiftExport.Core/Entities/Relations/CustomerProduct.cs|`FA1DABCACC4241A4D13354584E1CBC23FD6E766DDC8AA61F410B1782E928B1F0`|`B909FA8E96615AA434814608787E25A9091EB37BF191D1692B6BA052DBBED46C`|
|SwiftExport.Core/Entities/Systems/Dictionaries/Data/SysDictData.cs|`3DA052F165C373DE8352DA7821B411C772F97048AD9EA854A9B3824A97BFF2B8`|`91A464BB78FFFBB44955B1592114750BE0DAB675C77BCA6CB59D9A20CAC1F588`|
|SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs|`B5583A2F128A0A8AAABB275E3E812D4197F242A4628FE632110A47BADC55F675`|`AFD137139BE722A82E53BB28EFB26FF2684DF5FD9CB00140E631C484CD33B57A`|
|SwiftExport.Infrastructure/Repositories/Base/PostgreSqlEntityMetadata.cs|`8D0E928B456E2C7261CDC7A7626414D3A398D28B92BF6DB68681D03CFF3B2A2F`|`1C025B0FE154139E4D7B3B81913A2997214FF967ABDAE40EC62BFCDDFF89F064`|
|SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlNames.cs|`E2E52C228EFF31906DB1B0667988BA3317FD304A24471431CF7B80DC583D4F1D`|`38B5C22277F7C6DB2538AD4A81990ACD4E96CD5A6477A7F941621D9075CEA0A1`|
|SwiftExport.Infrastructure/Repositories/ForExcel/PostgreSqlForExcelSalesOrderRepository.cs|`C29A5691B5C64C50ECA627A3005AEFC0B83049B03F1CF7ACDB08926D4EE9442F`|`C29A5691B5C64C50ECA627A3005AEFC0B83049B03F1CF7ACDB08926D4EE9442F`|
|SwiftExport.Infrastructure/Repositories/Inventory/Query/PostgreSqlInventoryQueryRepository.cs|`99C5A28D84BA2E708FA7EC5CA68F91AB791E2BC4C28E4B5533B82602DCE5C6CF`|`99C5A28D84BA2E708FA7EC5CA68F91AB791E2BC4C28E4B5533B82602DCE5C6CF`|
|SwiftExport.Infrastructure/Contexts/RepositoryBaseContext.cs|`A14A23D21E83157DF6D3101AEC9045FDB45CE0E95D2A9F3FF222F21A428B3DAB`|`A14A23D21E83157DF6D3101AEC9045FDB45CE0E95D2A9F3FF222F21A428B3DAB`|
|SwiftExport.Infrastructure/Data/DbSession.cs|`AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077`|`AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077`|

第二批两个 Document 及报告已接回归主控冻结，本轮不编辑。报告自身 SHA 与 STOPPED 时间在本人状态登记，固定交付等待 MAIN 接回。

