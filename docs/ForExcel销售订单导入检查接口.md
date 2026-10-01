# ForExcel 销售订单导入检查

## 范围与入口

`POST /api/for-excel/sales-orders/check` 是只读导入检查。请求接收已解析的业务值，不接收 Excel 二维数组，不写数据库，不创建或更新订单。VSTO Excel 读取及页面接线另行实施。

Refit：`IForExcelSalesOrderApi`，客户端配置根地址 `api/for-excel/sales-orders`，接口相对路径 `/check`。要求登录及已有 `Orders.SalesOrder.Maintain` 权限，Controller 与 Application 双层门禁，全链支持取消。

新建专用检查 DTO 是因为输入没有产品 UID，且需要原始匹配条件和检查问题；现有保存 DTO 则要求已确定身份。没有修改现有保存 DTO 或新增数据库字段。

## 请求示例

```json
{
  "customerCode": "选定客户代码",
  "header": {
    "invoiceNo": "260820",
    "customerPoNo": "Order 47",
    "orderDate": "2026-08-19",
    "currency": "CNY",
    "exchangeRate": 1,
    "depositRate": 0.3
  },
  "lines": [{
    "clientLineId": "11111111-1111-1111-1111-111111111111",
    "customerSku": "AQ4101BG",
    "linkSku": "AA260011BG",
    "isNonStandard": false,
    "qty": 64,
    "cnyPrice": 37.70,
    "usdPrice": 5.18,
    "pcsOfCarton": 32,
    "gwOfCarton": 5.80,
    "cbmOfCarton": 0.015,
    "acceptLinkSkuMismatch": false
  }],
  "acceptExistingOrders": false
}
```

CustomerCode 是页面选定客户的代码；Excel 明细表的 Customer Code 是 CustomerSku，Aquavit Code 是 LinkSku。Date 是订单日期；UpdateDate 不进检查或保存契约，前端可保留原文。请求最多 500 行，ClientLineId 非空且唯一；客户端对较大订单需要另行定义整体检查策略，不能把分批成功自动当作整单通过。

## 匹配及检查

服务先按客户代码定位唯一有效客户，再通过 CustomerId＋CustomerSku 精确查询 CustomerProduct，按 SkuId 关联 Product。SKU 返回 Product 主表真实 Sku；系统 LinkSku 优先取非空客户产品 LinkSku，再回退产品 LinkSku。型号只去首尾空格并忽略大小写，不删除符号、前导零或内部空格。SQL 参数化并使用当前 DbSession／Transaction，没有 NOLOCK。

| MatchStatus | 含义 |
|---|---|
| Matched | 唯一有效关联，或用户明确选中的有效候选 |
| NotMatched | 没有客户型号关联；客户无效时也不能匹配 |
| Ambiguous | 多关联且尚未明确选择 |
| Unavailable | 关联停用、产品断链／停用、缺单位或选择不属于候选 |
| NonStandard | 用户明确标记非标，跳过产品匹配 |

多候选响应完整保留 Candidates。用户选择后把关联身份写入 SelectedCustomerProductId，重新检查；服务只允许该身份属于当前客户与型号候选，不能任意传 ProductId 越界。非标返回 Product 为空，不伪造产品 UID；必须提供描述、单位及正数量。

检查必填订单日期、CNY／USD 币种、正汇率及 0—1 定金比例；数量须正，价格可空或非负，每箱数量填写时须正，重量及体积可空或非负。检查数量乘单价溢出。不会从双币种报价推算汇率，也不会修改请求中的成交价、描述等原始值。

## 响应与确认

ServiceResult.Success 说明检查调用是否成功；业务内容有效性读取 Data.CanSave，不把 Success=true 当成订单可保存。结构、认证和权限错误使用失败包装；正常检查即使有业务问题也返回成功包装及具体 Issues。

Issues 提供稳定 Code、Field、Message、RequiresConfirmation、Acknowledged。整单问题位于响应 Issues，行问题位于 Lines 对应 ClientLineId。CanSave 仅在无阻断问题且确认提示全部确认时为 true。

- LinkSkuMismatch：原表非空中性型号与系统不同，返回系统值供对照；用户确认后在该行设置 AcceptLinkSkuMismatch 再检查。空原表型号不制造差异提示。
- ExistingOrders：同客户下任一非空 InvoiceNo 或 CustomerPoNo 精确命中未删除订单，含已确认／已关闭订单；返回候选身份、系统单号及状态。用户确认仍新建后设置 AcceptExistingOrders 再检查。不自动更新已有订单，也不把商业编号当唯一身份。
- 确认只能解除对应提示，不能解除未匹配、客户无效或数值错误。

请求内容或选择客户变化后，前端应重置检查通过状态及受影响确认，再检查。确认标记是本次检查输入，不是持久化审批或签名凭证。

## 保存边界

检查只提供当前读取快照，不锁定资料、不预留订单号，不发检查 Token。保存继续调用 `ISalesOrderDetailApi.SaveAsync`／原销售草稿服务。标准行提交匹配得到的 ProductId、CustomerProductId 和真实 Sku；非标行产品 ID 为空，仍有独立稳定明细 UID。

新订单头 Uid 为空，由已有创建流程生成；明细身份和 CommandId 由客户端稳定生成，结果不明时原请求重试。正式保存仍执行已有事务内业务重验和权限／CommandId 幂等，不信任 CanSave。现有通用保存 API 不强制检查凭证，也不持久化这些确认标记；后续前端需实施“检查通过后才开放保存”的交互。没有新增绕过保存规则的 ForExcel 写接口。

## 文件与验证（2026-09-30）

Contracts 的 Apis/ForExcel 与 DTOs/ForExcel/SalesOrders；Application 的 Interfaces/ForExcel、Services/ForExcel；Core 的 Interfaces/ForExcel；Infrastructure 的 Repositories/ForExcel；WebApi 的 Controllers/ForExcel，及各层 DI、EndpointPermissionFilter。

新增测试覆盖匹配失败／多候选／失效、非标必填、型号和重复订单确认、报价保留、批量去重、候选身份隔离、单头／数值／结构错误、认证权限与取消；另有 HTTP／Refit／序列化及 SQL 结构契约测试。ForExcel 10 项与现有权限过滤器 4 项，共 14 项通过。

普通 `dotnet build SwiftExport-WebAPI.slnx --no-restore` 被正在运行的 WebApi 占用输出 DLL 阻挡；未停止服务。使用独立 BaseOutputPath 完成解决方案构建，0 错误，存在 NU1900 漏洞审计网络警告。Contracts 已随构建验证 netstandard2.0。

尚未执行真实 SQL Server 查询、真实 JWT HTTP 或 Excel 实机验收；SQL 结构测试不证明数据库列类型、排序规则及真实资料行为。没有运行全量业务测试；没有修改 VSTO、.NET 10 前端、字段映射目录、现有草稿保存逻辑或部署运行中的 WebApi。
