using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Services;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.ViewModels;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>采购前端纯托管回归，通过真实 Refit 在内存中检查协议和生命周期。</summary>
    [TestClass]
    public sealed class PurchaseOrderImportTests
    {
        /// <summary>正确匹配系统身份，原商业值和优惠仅记录，不发送包装要求。</summary>
        [TestMethod]
        public async Task MatchCreateAndStartAnotherDocument()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                Assert.AreEqual(0,f.Base.Cache.Loads); await f.Prepare(); await f.Model.InitializeAsync(); Assert.AreEqual(1,f.Base.Cache.Loads);
                await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft); var l=f.Model.Lines.Single();
                Assert.AreEqual("REAL-SKU",l.Sku); Assert.AreEqual(f.Transport.ProductId,l.ProductId); Assert.AreEqual("AA1",l.OriginalLinkSku);
                Assert.AreEqual("原表描述",l.DescriptionZh); Assert.AreEqual(331.52m,f.Model.TotalAmount);
                await f.Model.SaveDraftAsync(); var request=f.Transport.Sent.Single();
                Assert.AreEqual(f.Transport.CustomerId,request.CustomerId); Assert.AreEqual("BO1",request.BoNo); Assert.AreEqual("特殊要求",request.Remark);
                Assert.IsNull(request.PackagingRequirements); Assert.AreEqual(0.95m,request.DiscountRate); Assert.AreEqual(-10m,request.DiscountAmount);
                Assert.AreEqual(5.18m,request.Lines.Single().UnitPrice); Assert.AreEqual("PO-TEST",f.Model.OrderNo); Assert.IsFalse(f.Model.HasEdits);
                await f.Model.SaveDraftAsync(); Assert.AreEqual(1,f.Transport.Creates);
                f.Model.NewImportCommand.Execute(null); Assert.AreEqual(0,f.Model.Lines.Count); Assert.IsTrue(f.Model.IsEditable);
            }
        }
        /// <summary>匹配失败不能保存，明确非标且完整输入才能检查通过。</summary>
        [TestMethod]
        public async Task NonStandardIsExplicitAndRetainsCommercialValues()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare(); f.Transport.CanSave=false; await f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.CanSaveDraft);
                var l=f.Model.Lines[0]; f.Model.ToggleNonStandardCommand.Execute(l); Assert.IsNull(l.ProductId); Assert.AreEqual("",l.Sku);
                l.Unit="PCS"; f.Transport.CanSave=true; await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                await f.Model.SaveDraftAsync(); var sent=f.Transport.Sent.Single().Lines.Single();
                Assert.IsTrue(sent.IsNonStandard); Assert.AreEqual(Guid.Empty,sent.ProductId); Assert.IsNull(sent.SelectedCustomerProductId); Assert.IsNull(sent.DeliverySupplierId);
                Assert.AreEqual(64m,sent.Quantity); Assert.AreEqual("原表描述",sent.DescriptionZh);
            }
        }
        /// <summary>非法或不完整数值保持原文且不发送检查，统计显示不完整。</summary>
        [TestMethod]
        public async Task InvalidNumbersAndEditedSnapshotBlockSave()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare(); var l=f.Model.Lines[0]; l.UnitPriceText="12.";
                l.QuantityText="bad"; await f.Model.CheckImportAsync(); Assert.AreEqual(0,f.Transport.Checks); Assert.AreEqual("bad",l.QuantityText);
                Assert.IsTrue(f.Model.ValidationSummary.Contains("不完整"));
                l.QuantityText="64"; l.UnitPriceText="5.18"; await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                l.SubNote="新备注"; Assert.IsFalse(f.Model.CanSaveDraft); await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                f.Model.Header.DiscountRateText="abc"; await f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.CanSaveDraft);
                Assert.AreEqual(2,f.Transport.Checks);
            }
        }
        /// <summary>账号变化、删除行、输入修改和释放均不得接纳旧结果。</summary>
        [TestMethod]
        public async Task LateChecksCannotWriteToChangedOrDisposedPage()
        {
            for(var scenario=0;scenario<4;scenario++)
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare(); var completion=new TaskCompletionSource<CheckPurchaseOrderImportResponse>();
                f.Transport.CheckOverride=r=>completion.Task; var task=f.Model.CheckImportAsync();
                var line=f.Model.Lines[0]; var response=f.Transport.Checked(f.Transport.LastCheck);
                if(scenario==0) f.Base.Login("another");
                if(scenario==1) f.Model.Lines.Remove(line);
                if(scenario==2) line.CustomerSku="NEW";
                if(scenario==3) f.Model.Dispose();
                completion.SetResult(response); await task; Assert.IsFalse(f.Model.CanSaveDraft); Assert.IsNull(line.ProductId);
                if(scenario==0) { Assert.AreEqual(0,f.Model.Lines.Count); Assert.IsNull(f.Model.SelectedSupplier); }
            }
        }
        /// <summary>结果未知冻结请求；显式重试保持 CommandId、所有商业值和明细身份。</summary>
        [TestMethod]
        public async Task UnknownCreateReplaysOriginalIntent()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare(); await f.Model.CheckImportAsync();
                f.Transport.CreateOverride=r=>throw new HttpRequestException("断网");
                await f.Model.SaveDraftAsync(); Assert.IsTrue(f.Model.HasPendingSave); Assert.IsFalse(f.Model.IsEditable);
                var id=f.Transport.Sent[0].CommandId; f.Model.Header.Remark="外部修改不会改变冻结请求";
                f.Transport.CreateOverride=r=>Task.FromResult(f.Transport.Saved()); await f.Model.SaveDraftAsync();
                Assert.AreEqual(id,f.Transport.Sent[1].CommandId); Assert.AreEqual("特殊要求",f.Transport.Sent[1].Remark); Assert.IsFalse(f.Model.HasPendingSave);
                f.Model.NewImportCommand.Execute(null); await f.Prepare(); await f.Model.CheckImportAsync();
                f.Transport.CreateOverride=r=>Task.FromResult(SwiftExport.Contracts.Wrappers.ServiceResult<SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementOrderCommandResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Forbidden,"明确拒绝"));
                await f.Model.SaveDraftAsync(); Assert.IsFalse(f.Model.HasPendingSave); Assert.IsTrue(f.Model.IsEditable);
            }
        }
        /// <summary>客户型号或原中性型号变化使差异确认失效，需重新检查。</summary>
        [TestMethod]
        public async Task MismatchRequiresExplicitConfirmationAndRecheck()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare();
                f.Transport.CheckOverride=r=> { var response=f.Transport.Checked(r); response.CanSave=r.Lines[0].AcceptLinkSkuMismatch; response.Lines[0].Product.LinkSku="OTHER"; return Task.FromResult(response); };
                await f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.CanSaveDraft);
                f.Model.Lines[0].AcceptLinkSkuMismatch=true; Assert.IsFalse(f.Model.CanSaveDraft); await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                f.Model.Lines[0].OriginalLinkSku="NEW"; Assert.IsFalse(f.Model.Lines[0].AcceptLinkSkuMismatch); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        /// <summary>地址目录仅包含可导入商业值，日期系统和非法文本不猜测。</summary>
        [TestMethod]
        public void AddressModelDirectoryAndDateSystems()
        {
            var registry=ExcelImportRegistry.CreateDefault(); var definition=registry.Find("PurchaseOrder"); var line=new PurchaseOrderImportLine();
            Assert.IsTrue(definition.ContainsField("Header.Remark")); Assert.IsFalse(definition.ContainsField("Header.PackagingRequirements"));
            foreach(var key in new[] { "Line.ProductId","Line.Sku","Line.IsNonStandard","Line.AcceptLinkSkuMismatch","Header.SupplierId" }) Assert.IsFalse(definition.ContainsField(key));
            Assert.IsTrue(definition.TryAssign(line,"Line.Quantity","bad",CultureInfo.InvariantCulture,out var error)); Assert.AreEqual("bad",line.QuantityText);
            var snapshot=new ExcelAddressSnapshot { Date1904=true,Rows=new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> { new ExcelAddressValue { FieldKey="Header.OrderDate",Value=1d,Row=2,Column=2 } } } };
            var parser=new PurchaseOrderAddressParser(registry); Assert.AreEqual(new DateTime(1904,1,2),parser.Parse(snapshot).Header.OrderDate);
            snapshot.Date1904=false; snapshot.Rows[0][0].Value=60d; var parsed=parser.Parse(snapshot); Assert.IsNull(parsed.Header.OrderDate); Assert.AreEqual(1,parsed.Issues.Count);
        }
        /// <summary>配置页采购字段由模型生成，分组切换取消保留原编辑。</summary>
        [TestMethod]
        public async Task ProfileGroupingAndCancelledSwitch()
        {
            using(var f=new PurchaseImportTestFixture())
            using(var model=new ExcelReadProfileViewModel(f.Base.ProfileService,f.Base.AddressReader,ExcelImportRegistry.CreateDefault(),f.Base.Session,f.Base,new ApiCallRunner(new AddInLog()),f.Base))
            {
                Assert.IsTrue(await model.SelectGroupAsync("PurchaseOrder")); Assert.AreEqual("PurchaseOrder",model.CurrentGroupCode);
                Assert.IsTrue(model.HeaderFields.Any(r=>r.FieldKey=="Header.Remark")); Assert.IsTrue(model.LineFields.Any(r=>r.FieldKey=="Line.Quantity"));
                model.ConfigName="修改"; f.Base.Confirm=false; Assert.IsFalse(await model.SelectGroupAsync("SalesOrder"));
                Assert.AreEqual("PurchaseOrder",model.CurrentGroupCode); Assert.AreEqual("修改",model.ConfigName);
            }
        }
        /// <summary>基础资料失败可重试；多选删除不留下通知；关闭取消保留编辑。</summary>
        [TestMethod]
        public async Task RetryLoadingMultiDeleteAndCancelClose()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                f.Base.Cache.Load=()=>Task.FromResult(SwiftExport.Client.VSTO.Services.Caching.CacheRefreshResult.Fail("网络失败"));
                await f.Model.InitializeAsync(); Assert.IsFalse(f.Model.IsEditable);
                f.Base.Cache.Load=()=>Task.FromResult(SwiftExport.Client.VSTO.Services.Caching.CacheRefreshResult.Ok()); await f.Prepare();
                f.Model.AddNonStandardLineCommand.Execute(null); f.Model.SetSelectedLines(f.Model.Lines.ToArray()); f.Model.DeleteSelectedLineCommand.Execute(null);
                Assert.AreEqual(0,f.Model.Lines.Count); f.Base.Confirm=false; Assert.IsFalse(f.Model.CanClose());
            }
        }
        /// <summary>重新读取仅替换对应部分，取消保持原输入；保存未知时禁止再次读取。</summary>
        [TestMethod]
        public async Task AddressReadReplacesOnlyConfirmedPart()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare(); await f.Model.EnsureReadProfilesAsync(); var original=f.Model.Lines[0];
                f.Base.AddressReader.Read=detail=>Task.FromResult(new ExcelAddressSnapshot { Detail=detail,WorkbookName="采购.xlsx",WorksheetName="Sheet1",
                    Rows=new List<List<ExcelAddressValue>> { detail ? new List<ExcelAddressValue> {
                        new ExcelAddressValue { FieldKey="Line.CustomerSku",Row=5,Column=3,Value="NEW-SKU" },
                        new ExcelAddressValue { FieldKey="Line.Quantity",Row=5,Column=4,Value="bad" } }
                    : new List<ExcelAddressValue> { new ExcelAddressValue { FieldKey="Header.BoNo",Row=2,Column=2,Value="NEW-BO" } } } });
                f.Base.Confirm=false; await f.Model.ReadHeaderCommand.ExecuteAsync(null); Assert.AreEqual("BO1",f.Model.Header.BoNo); Assert.AreSame(original,f.Model.Lines[0]);
                f.Base.Confirm=true; await f.Model.ReadHeaderCommand.ExecuteAsync(null); Assert.AreEqual("NEW-BO",f.Model.Header.BoNo);
                Assert.AreSame(original,f.Model.Lines[0]); Assert.AreEqual("C1",f.Model.Header.CustomerCode); Assert.IsNotNull(f.Model.SelectedSupplier);
                await f.Model.ReadLinesCommand.ExecuteAsync(null); Assert.AreEqual("NEW-BO",f.Model.Header.BoNo); Assert.AreEqual("NEW-SKU",f.Model.Lines[0].CustomerSku);
                Assert.AreEqual("bad",f.Model.Lines[0].QuantityText); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        /// <summary>歧义不自动取首项，用户明确采用可用候选后才提交关联身份。</summary>
        [TestMethod]
        public async Task AmbiguousCandidateAndSupplierChangeRequireRecheck()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare(); var chosen=Guid.NewGuid();
                f.Transport.CheckOverride=r=> {
                    var response=f.Transport.Checked(r); var line=response.Lines[0];
                    if(!r.Lines[0].SelectedCustomerProductId.HasValue)
                    {
                        response.CanSave=false; line.MatchStatus="Ambiguous"; line.Product=null;
                        line.Candidates=new List<PurchaseOrderImportProductCandidateDto> {
                            new PurchaseOrderImportProductCandidateDto { CustomerProductId=Guid.NewGuid(),Sku="停用",IsUsable=false },
                            new PurchaseOrderImportProductCandidateDto { CustomerProductId=chosen,ProductId=f.Transport.ProductId,Sku="REAL-SKU",IsUsable=true } };
                    }
                    else line.Product.CustomerProductId=chosen;
                    return Task.FromResult(response);
                };
                await f.Model.CheckImportAsync(); var l=f.Model.Lines[0]; Assert.IsFalse(f.Model.CanSaveDraft); Assert.IsNull(l.ProductId);
                l.SelectedMatchCandidate=l.MatchCandidates[0]; f.Model.ChooseMatchCommand.Execute(l); Assert.IsNull(l.SelectedCustomerProductId);
                l.SelectedMatchCandidate=l.MatchCandidates[1]; f.Model.ChooseMatchCommand.Execute(l); Assert.AreEqual(chosen,l.SelectedCustomerProductId);
                await f.Model.CheckImportAsync(); Assert.AreEqual(chosen,f.Transport.LastCheck.Lines[0].SelectedCustomerProductId); Assert.IsTrue(f.Model.CanSaveDraft);
                f.Model.SelectedSupplier=null; Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        /// <summary>明确权限拒绝保留当前账号并允许修正，保存请求不自动重放。</summary>
        [TestMethod]
        public async Task DefiniteForbiddenKeepsAccountAndUnlocksInput()
        {
            using(var f=new PurchaseImportTestFixture())
            {
                await f.Prepare(); await f.Model.CheckImportAsync();
                f.Transport.CreateOverride=r=>Task.FromResult(SwiftExport.Contracts.Wrappers.ServiceResult<SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementOrderCommandResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Forbidden,"无权限"));
                await f.Model.SaveDraftAsync();
                Assert.IsNotNull(f.Base.Session.Capture().User); Assert.IsFalse(f.Model.HasPendingSave); Assert.IsTrue(f.Model.IsEditable);
                Assert.IsFalse(f.Model.CanSaveDraft); Assert.AreEqual(1,f.Transport.Creates);
            }
        }
    }
}
