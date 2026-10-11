using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Client.VSTO.Testing
{
    /// <summary>通过真实认证 Handler 和 Refit 代理模拟采购协议，所有 HTTP 在内存完成。</summary>
    internal sealed class PurchaseImportTestFixture : IDisposable
    {
        internal readonly ImportTestFixture Base = new ImportTestFixture();
        internal readonly PurchaseTransport Transport = new PurchaseTransport();
        private readonly ApiClientRegistration clients;
        internal readonly PurchaseOrderImportViewModel Model;
        internal PurchaseImportTestFixture()
        {
            Base.Cache.CustomerItems[0].Uid = Transport.CustomerId;
            Base.Session.TryCommit(Base.Session.Capture().Version,"test-token",new MeDto { UserId="buyer",UserName="buyer",Permissions=new List<string> { PermissionKeys.ProcurementOrderCreate,PermissionKeys.SystemExcelFieldMappingManage } });
            Base.ProfileApi.Profile.GroupCode="PurchaseOrder";
            Base.ProfileApi.Profile.Fields=new List<SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadFieldDto> {
                new SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadFieldDto { TargetFieldKey="Header.BoNo",CellAddress="B2" },
                new SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadFieldDto { TargetFieldKey="Line.CustomerSku",CellAddress="C5",IsDownward=true },
                new SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadFieldDto { TargetFieldKey="Line.Quantity",CellAddress="D5",IsDownward=true } };
            clients=new ApiClientRegistration(new Uri("https://test.invalid/"),Base.Session,new PurchaseTransport(),Transport);
            Model=CreateModel();
        }
        internal PurchaseOrderImportViewModel CreateModel() => new PurchaseOrderImportViewModel(Base.Cache,Base.Session,Base,
            new ApiCallRunner(new AddInLog()),Base,Base.ProfileService,Base.AddressReader,ExcelImportRegistry.CreateDefault(),
            clients.Create<IForExcelPurchaseOrderApi>("api/for-excel/purchase-orders"),clients.Create<IProcurementOrderApi>("api/procurement/orders"),clients.Create<ICustomerPoConfigApi>("api/CustomerPoConfig"));
        internal async Task Prepare()
        {
            await Model.InitializeAsync(); Model.Header.CustomerCode="C1"; Model.SelectedSupplier=new SupplierDto { Uid=Transport.SupplierId,SupplierName="测试工厂",SupplierCode="S1" };
            Model.Header.BoNo="BO1"; Model.Header.Remark="特殊要求"; Model.Header.DiscountRateText="0.95"; Model.Header.DiscountAmountText="-10";
            Model.AddStandardLineCommand.Execute(null);
            var line=Model.Lines[0]; line.CustomerSku="AQ1"; line.OriginalLinkSku="AA1"; line.QuantityText="64"; line.UnitPriceText="5.18"; line.DescriptionZh="原表描述";
        }
        /// <summary>模型先释放，随后释放网络与会话。</summary>
        public void Dispose() { Model.Dispose(); clients.Dispose(); Base.Dispose(); }
    }
    /// <summary>可控只读匹配和创建回执，支持延迟、失效、403 及未知结果回归。</summary>
    internal sealed class PurchaseTransport : HttpMessageHandler
    {
        internal readonly Guid CustomerId=Guid.NewGuid(),SupplierId=Guid.NewGuid(),ProductId=Guid.NewGuid(),CustomerProductId=Guid.NewGuid();
        internal int Checks,Creates;
        internal int ConfigCalls;
        internal Func<Guid,Guid,Task<ServiceResult<CustomerPoConfigDto>>> ConfigOverride = (c,s) => Task.FromResult(ServiceResult<CustomerPoConfigDto>.Ok(null));
        internal bool CanSave=true;
        internal Func<CheckPurchaseOrderImportRequest,Task<CheckPurchaseOrderImportResponse>> CheckOverride = null;
        internal Func<CreateIndependentPurchaseOrderCommand,Task<ServiceResult<ProcurementOrderCommandResult>>> CreateOverride = null;
        internal CheckPurchaseOrderImportRequest LastCheck;
        internal readonly List<CreateIndependentPurchaseOrderCommand> Sent=new List<CreateIndependentPurchaseOrderCommand>();
        private static readonly JsonSerializerOptions Json=new JsonSerializerOptions { PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
        /// <summary>返回与真实契约一致的 JSON，不访问真实服务器。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var body=request.Content == null ? "" : await request.Content.ReadAsStringAsync(); object result;
            if(request.RequestUri.AbsolutePath.EndsWith("/by-parties"))
            {
                ConfigCalls++;
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                result = await ConfigOverride(Guid.Parse(query["customerId"]),Guid.Parse(query["supplierId"]));
            }
            else if(request.RequestUri.AbsolutePath.EndsWith("/check"))
            {
                Checks++; LastCheck=JsonSerializer.Deserialize<CheckPurchaseOrderImportRequest>(body,Json);
                result=ServiceResult<CheckPurchaseOrderImportResponse>.Ok(CheckOverride==null ? Checked(LastCheck) : await CheckOverride(LastCheck));
            }
            else
            {
                Creates++; var command=JsonSerializer.Deserialize<CreateIndependentPurchaseOrderCommand>(body,Json); Sent.Add(command);
                result=CreateOverride==null ? Saved() : await CreateOverride(command);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(result,result.GetType(),Json),Encoding.UTF8,"application/json") };
        }
        internal ServiceResult<ProcurementOrderCommandResult> Saved() => ServiceResult<ProcurementOrderCommandResult>.Ok(new ProcurementOrderCommandResult { PurchaseOrderId=Guid.NewGuid(),OrderNo="PO-TEST",Version=1 });
        internal CheckPurchaseOrderImportResponse Checked(CheckPurchaseOrderImportRequest request) => new CheckPurchaseOrderImportResponse {
            CanSave=CanSave,Customer=new PurchaseOrderImportCustomerDto { Uid=CustomerId,CustomerCode=request.CustomerCode,CustomerName="测试客户",IsActive=true },
            Supplier=new PurchaseOrderImportSupplierDto { Uid=request.SupplierId,IsActive=true },
            Lines=request.Lines.Select(l=>new PurchaseOrderImportLineCheckDto { ClientLineId=l.ClientLineId,MatchStatus=l.IsNonStandard ? "NonStandard" : "Matched",
                Product=l.IsNonStandard ? null : new PurchaseOrderImportProductCandidateDto { CustomerProductId=CustomerProductId,ProductId=ProductId,CustomerSku=l.CustomerSku,Sku="REAL-SKU",LinkSku="AA1",Unit="PCS",MaterialType="成品",IsUsable=true } }).ToList() };
    }
}
