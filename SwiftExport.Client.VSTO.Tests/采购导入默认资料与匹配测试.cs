using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>采购双方配置、受控读取及独立匹配回归；通过真实 Refit 内存请求执行。</summary>
    [TestClass]
    public sealed class 采购导入默认资料与匹配测试
    {
        /// <summary>无客户禁用；供应商、日期、报价尚未完成时仍可匹配，不能据此保存。</summary>
        [TestMethod]
        public async Task MatchOnlyRequiresCustomerAndPreservesInvalidText()
        {
            using (var f = new PurchaseImportTestFixture())
            {
                await f.Prepare(); f.Model.CustomerClearedCommand.Execute(null);
                Assert.IsFalse(f.Model.MatchAllSkusCommand.CanExecute(null));
                f.Model.SelectedCustomer = f.Base.Cache.Customers[0]; f.Model.CustomerSelectedCommand.Execute(null);
                f.Model.SelectedSupplier = null; f.Model.Header.OrderDate = null;
                var line = f.Model.Lines[0]; line.QuantityText = "bad"; line.UnitPriceText = "";
                f.Transport.CheckOverride = r => { var data = f.Transport.Checked(r); data.Supplier = null; data.CanSave = false; return Task.FromResult(data); };
                Assert.IsTrue(f.Model.MatchAllSkusCommand.CanExecute(null));
                await f.Model.MatchAllSkusCommand.ExecuteAsync(null);
                Assert.AreEqual("REAL-SKU", line.Sku); Assert.AreEqual("bad", line.QuantityText);
                Assert.AreEqual(0m, f.Transport.LastCheck.Lines[0].Quantity);
                Assert.IsFalse(line.IsCheckPassed); Assert.IsFalse(f.Model.CanSaveDraft);
                Assert.AreEqual("原表描述", line.DescriptionZh);
            }
        }
        /// <summary>本行匹配不更新另一行，客户切换保留所有商业内容但清旧身份。</summary>
        [TestMethod]
        public async Task LineMatchingAndCustomerChangeAreIsolated()
        {
            using (var f = new PurchaseImportTestFixture())
            {
                await f.Prepare(); f.Model.AddStandardLineCommand.Execute(null);
                var first = f.Model.Lines[0]; var second = f.Model.Lines[1];
                second.CustomerSku = "SECOND"; second.UnitPriceText = "8";
                await f.Model.MatchLineSkuCommand.ExecuteAsync(first);
                Assert.IsTrue(first.IsCheckPassed); Assert.IsNull(second.ProductId); Assert.AreEqual(1, f.Transport.LastCheck.Lines.Count);
                Assert.IsFalse(f.Model.CanSaveDraft);
                var next = new CustomerDto { Uid = Guid.NewGuid(), CustomerCode = "C2" };
                f.Base.Cache.CustomerItems.Add(next); f.Model.SelectedCustomer = next; f.Model.CustomerSelectedCommand.Execute(null);
                Assert.AreEqual(2, f.Model.Lines.Count); Assert.IsNull(first.ProductId); Assert.IsFalse(first.IsCheckPassed);
                Assert.AreEqual("64", first.QuantityText); Assert.AreEqual("5.18", first.UnitPriceText);
                Assert.AreEqual("AQ1", first.CustomerSku); Assert.AreEqual("原表描述", first.DescriptionZh);
            }
        }
        /// <summary>型号差异不能显示绿灯，必须确认后重新检验；非标切换撤销身份。</summary>
        [TestMethod]
        public async Task RowCheckIncludesMismatchAcknowledgement()
        {
            using (var f = new PurchaseImportTestFixture())
            {
                await f.Prepare(); var line = f.Model.Lines[0];
                f.Transport.CheckOverride = r => { var d = f.Transport.Checked(r); d.Lines[0].Issues.Add(new PurchaseOrderImportIssueDto {
                    Code = "LinkSkuMismatch", Message = "型号不同", RequiresConfirmation = true, Acknowledged = r.Lines[0].AcceptLinkSkuMismatch }); return Task.FromResult(d); };
                await f.Model.MatchAllSkusCommand.ExecuteAsync(null); Assert.IsFalse(line.IsCheckPassed);
                line.AcceptLinkSkuMismatch = true; Assert.IsFalse(line.IsCheckPassed);
                await f.Model.MatchLineSkuCommand.ExecuteAsync(line); Assert.IsTrue(line.IsCheckPassed);
                f.Model.ToggleNonStandardCommand.Execute(line); Assert.IsNull(line.ProductId); Assert.IsFalse(line.IsCheckPassed);
            }
        }
        /// <summary>配置仅补空值，三处异名显式映射；报价、日期、整单优惠和明细不变。</summary>
        [TestMethod]
        public async Task DefaultsFillOnlyBlankFieldsAndRetryExplicitly()
        {
            using (var f = new PurchaseImportTestFixture())
            {
                await f.Prepare(); var date = f.Model.Header.OrderDate;
                var config = Config(f.Transport.CustomerId, f.Transport.SupplierId); config.Currency = "USD";
                f.Model.Header.InletHoseType = "Excel进水管"; f.Model.Header.Remark = "手工特殊要求";
                f.Transport.ConfigOverride = (c,s) => Task.FromResult(ServiceResult<CustomerPoConfigDto>.Ok(config));
                GrantConfig(f); await f.Prepare(); // 会话变化清空页面，重新建立原编辑内容。
                f.Model.Header.InletHoseType = "Excel进水管"; f.Model.Header.Remark = "手工特殊要求";
                await Settled(f); f.Model.Header.InstructionManualType = "";
                await f.Model.RefreshDefaultsCommand.ExecuteAsync(null);
                Assert.AreEqual("Excel进水管", f.Model.Header.InletHoseType); Assert.AreEqual("手工特殊要求", f.Model.Header.Remark);
                Assert.AreEqual("说明书", f.Model.Header.InstructionManualType); Assert.AreEqual("跟单", f.Model.Header.PurchasePerson);
                Assert.AreEqual("0.95", f.Model.Header.DiscountRateText); Assert.AreEqual("-10", f.Model.Header.DiscountAmountText);
                Assert.AreEqual("BO1", f.Model.Header.BoNo); Assert.AreEqual(date, f.Model.Header.OrderDate);
                Assert.IsTrue(f.Model.DefaultsMessage.Contains("CNY")); Assert.AreEqual("默认备注", config.Note);
                var count = f.Transport.ConfigCalls;
                f.Model.SelectedSupplier = new SupplierDto { Uid = f.Transport.SupplierId }; await Settled(f);
                Assert.AreEqual(count, f.Transport.ConfigCalls);
            }
        }
        /// <summary>配置查询可并行手工编辑，但整单检验禁用；迟到结果不能覆盖新组合、释放或新账号。</summary>
        [TestMethod]
        public async Task LateDefaultsCannotWriteAcrossPartyOrLifetimeBoundary()
        {
            for (var scenario = 0; scenario < 4; scenario++)
            using (var f = new PurchaseImportTestFixture())
            {
                GrantConfig(f); await f.Prepare(); await Settled(f);
                var tcs = new TaskCompletionSource<ServiceResult<CustomerPoConfigDto>>();
                f.Transport.ConfigOverride = (c,s) => tcs.Task;
                var task = f.Model.RefreshDefaultsCommand.ExecuteAsync(null);
                Assert.IsTrue(f.Model.IsEditable); Assert.IsFalse(f.Model.CheckImportCommand.CanExecute(null));
                f.Model.Header.BoxType = "手工内盒";
                if (scenario == 0) f.Model.SelectedSupplier = null;
                if (scenario == 1) f.Model.CustomerClearedCommand.Execute(null);
                if (scenario == 2) f.Model.Dispose();
                if (scenario == 3) f.Base.Login("another");
                tcs.SetResult(ServiceResult<CustomerPoConfigDto>.Ok(Config(f.Transport.CustomerId, f.Transport.SupplierId))); await task;
                Assert.AreNotEqual("说明书", f.Model.Header.InstructionManualType);
                if (scenario != 3) Assert.AreEqual("手工内盒", f.Model.Header.BoxType);
            }
        }
        /// <summary>权限、无配置及网络失败仅提示，保留输入并允许显式重试。</summary>
        [TestMethod]
        public async Task ConfigFailureDoesNotBlockManualEntry()
        {
            using (var f = new PurchaseImportTestFixture())
            {
                await f.Prepare(); Assert.AreEqual(0, f.Transport.ConfigCalls); Assert.IsTrue(f.Model.DefaultsMessage.Contains("权限"));
                GrantConfig(f); await f.Prepare(); await Settled(f);
                f.Transport.ConfigOverride = (c,s) => throw new HttpRequestException("离线");
                await f.Model.RefreshDefaultsCommand.ExecuteAsync(null);
                Assert.AreEqual("特殊要求", f.Model.Header.Remark); Assert.IsTrue(f.Model.IsEditable);
                Assert.IsFalse(f.Model.IsDefaultsLoading); Assert.IsTrue(f.Model.RefreshDefaultsCommand.CanExecute(null));
                f.Transport.ConfigOverride = (c,s) => Task.FromResult(ServiceResult<CustomerPoConfigDto>.Ok(Config(c,s)));
                await f.Model.RefreshDefaultsCommand.ExecuteAsync(null); Assert.AreEqual("说明书", f.Model.Header.InstructionManualType);
            }
        }
        /// <summary>未配置单头值保留，配置空值明确清空；未知客户代码保留但不允许匹配。</summary>
        [TestMethod]
        public async Task ReadHeaderIsControlledAndCustomerMustResolve()
        {
            using (var f = new PurchaseImportTestFixture())
            {
                await f.Prepare(); await f.Model.EnsureReadProfilesAsync(); f.Model.Header.BoxType = "保留";
                f.Base.AddressReader.Read = detail => Task.FromResult(new SwiftExport.Client.VSTO.Services.ExcelImport.ExcelAddressSnapshot {
                    Rows = new List<List<SwiftExport.Client.VSTO.Services.ExcelImport.ExcelAddressValue>> { new List<SwiftExport.Client.VSTO.Services.ExcelImport.ExcelAddressValue> {
                        new SwiftExport.Client.VSTO.Services.ExcelImport.ExcelAddressValue { FieldKey = "Header.BoNo", Value = null },
                        new SwiftExport.Client.VSTO.Services.ExcelImport.ExcelAddressValue { FieldKey = "Header.CustomerCode", Value = "unknown" } } } });
                var line = f.Model.Lines[0]; await f.Model.ReadHeaderCommand.ExecuteAsync(null);
                Assert.AreEqual("保留", f.Model.Header.BoxType); Assert.AreEqual("", f.Model.Header.BoNo);
                Assert.AreEqual("unknown", f.Model.Header.CustomerCode); Assert.IsNull(f.Model.SelectedCustomer);
                Assert.AreSame(line, f.Model.Lines[0]); Assert.IsFalse(f.Model.MatchAllSkusCommand.CanExecute(null));
                Assert.IsNotNull(f.Model.SelectedSupplier);
            }
        }
        /// <summary>本行匹配的迟到响应隔离编辑、删除、客户、账号和释放。</summary>
        [TestMethod]
        public async Task LateLineMatchDoesNotWriteBack()
        {
            for (var scenario = 0; scenario < 5; scenario++)
            using (var f = new PurchaseImportTestFixture())
            {
                await f.Prepare(); var line = f.Model.Lines[0]; var tcs = new TaskCompletionSource<CheckPurchaseOrderImportResponse>();
                f.Transport.CheckOverride = r => tcs.Task; var task = f.Model.MatchLineSkuCommand.ExecuteAsync(line);
                var response = f.Transport.Checked(f.Transport.LastCheck);
                if (scenario == 0) line.CustomerSku = "NEW";
                if (scenario == 1) f.Model.Lines.Remove(line);
                if (scenario == 2) f.Model.Header.CustomerCode = "unknown";
                if (scenario == 3) f.Base.Login("other");
                if (scenario == 4) f.Model.Dispose();
                tcs.SetResult(response); await task; Assert.IsNull(line.ProductId); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        private static CustomerPoConfigDto Config(Guid customer, Guid supplier) => new CustomerPoConfigDto { CustomerId = customer, SupplierId = supplier,
            InletHoseType = "默认进水管", IMType = "说明书", Purchaser = "跟单", Note = "默认备注", Currency = "CNY", DefaultDiscountRate = 0.9m };
        // 模拟明确授予配置权限；会话版本变化与真实登录相同，测试随后重新初始化页面。
        private static void GrantConfig(PurchaseImportTestFixture f) => f.Base.Session.TryCommit(f.Base.Session.Capture().Version, "test-token",
            new MeDto { UserId = "buyer", Permissions = new List<string> { PermissionKeys.ProcurementOrderCreate, PermissionKeys.CustomersPoConfigView, PermissionKeys.SystemExcelFieldMappingManage } });
        private static async Task Settled(PurchaseImportTestFixture f)
        {
            for (var i = 0; f.Model.IsDefaultsLoading && i < 200; i++) await Task.Delay(5);
            Assert.IsFalse(f.Model.IsDefaultsLoading, "配置测试请求应结束。");
        }
    }
}
