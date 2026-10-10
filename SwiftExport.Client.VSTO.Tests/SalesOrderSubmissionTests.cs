using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证导入检查、身份匹配、新建草稿和未知结果的幂等边界，不访问真实数据库。</summary>
    [TestClass]
    public sealed class SalesOrderSubmissionTests
    {
        private static async Task Prepare(ImportTestFixture f)
        {
            f.Session.TryCommit(f.Session.Capture().Version, "test-token", new MeDto { UserId = "writer", Permissions = new List<string> { PermissionKeys.SalesOrderMaintain } });
            await f.Start(); f.Model.AddStandardLineCommand.Execute(null);
            var l = f.Model.Lines[0]; l.CustomerSku = "AQ1"; l.OriginalLinkSku = "AA1"; l.QtyText = "64"; l.UsdPriceText = "5.18";
            f.CheckApi.Check = (r, t) => Task.FromResult(Checked(f, r));
        }
        private static ServiceResult<CheckSalesOrderImportResponse> Checked(ImportTestFixture f, CheckSalesOrderImportRequest request)
        {
            return ServiceResult<CheckSalesOrderImportResponse>.Ok(new CheckSalesOrderImportResponse {
                CanSave = true, Customer = new SalesOrderImportCustomerDto { Uid = f.Model.Header.CustomerId, CustomerCode = request.CustomerCode, IsActive = true },
                Lines = request.Lines.Select(l => new SalesOrderImportLineCheckDto { ClientLineId = l.ClientLineId,
                    MatchStatus = l.IsNonStandard ? "NonStandard" : "Matched", Product = l.IsNonStandard ? null : Candidate() }).ToList() });
        }
        private static SalesOrderImportProductCandidateDto Candidate() => new SalesOrderImportProductCandidateDto {
            CustomerProductId = Guid.NewGuid(), ProductId = Guid.NewGuid(), CustomerSku = "AQ1", Sku = "REAL1", LinkSku = "SYSTEM-AA1", Unit = "PCS", IsUsable = true };
        private static ServiceResult<SaveSalesOrderResponse> Saved(ImportTestFixture f) => ServiceResult<SaveSalesOrderResponse>.Ok(new SaveSalesOrderResponse {
            Document = new SalesOrderDetailDto { Header = new SalesOrderEditHeaderDto { Uid = Guid.NewGuid(), CustomerId = f.Model.Header.CustomerId, OrderNo = "SO1" } } });

        /// <summary>所有匹配入口依赖已接纳客户；只有选中行时才启用局部匹配。</summary>
        [TestMethod]
        public async Task MatchingRequiresCustomerAndSelection()
        {
            using (var f = new ImportTestFixture())
            {
                f.Session.TryCommit(f.Session.Capture().Version, "test-token", new MeDto { UserId = "writer", Permissions = new List<string> { PermissionKeys.SalesOrderMaintain } });
                await f.Model.InitializeAsync();
                var line = new SalesOrderImportLine(false, null); f.Model.Lines.Add(line); f.Model.SetSelectedLines(new[] { line });
                Assert.IsFalse(f.Model.CheckImportCommand.CanExecute(null));
                Assert.IsFalse(f.Model.MatchAllSkusCommand.CanExecute(null));
                Assert.IsFalse(f.Model.MatchSelectedSkusCommand.CanExecute(null));
                f.Model.SelectedCustomer = f.Cache.Customers[0]; f.Model.CustomerSelectedCommand.Execute(null);
                f.Model.AddStandardLineCommand.Execute(null);
                Assert.IsTrue(f.Model.CheckImportCommand.CanExecute(null)); Assert.IsTrue(f.Model.MatchAllSkusCommand.CanExecute(null));
                Assert.IsFalse(f.Model.MatchSelectedSkusCommand.CanExecute(null));
                f.Model.SetSelectedLines(f.Model.Lines); Assert.IsTrue(f.Model.MatchSelectedSkusCommand.CanExecute(null));
            }
        }

        /// <summary>独立匹配不要求单头完成，选中范围不污染其他行，全部匹配也不能代替整单保存检验。</summary>
        [TestMethod]
        public async Task IndependentMatchingPreservesScopeAndSaveGate()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); f.Model.AddStandardLineCommand.Execute(null);
                var first = f.Model.Lines[0]; var second = f.Model.Lines[1]; second.CustomerSku = "AQ2";
                f.Model.Header.ExchangeRateText = "";
                CheckSalesOrderImportRequest sent = null;
                f.CheckApi.Check = (r, t) => { sent = r; return Task.FromResult(Checked(f, r)); };
                f.Model.SetSelectedLines(new[] { second });
                await f.Model.MatchSelectedSkusCommand.ExecuteAsync(null);
                Assert.AreEqual(f.Model.Header.CustomerCode, sent.CustomerCode); Assert.AreEqual(1, sent.Lines.Count);
                Assert.AreEqual(second.Uid, sent.Lines[0].ClientLineId); Assert.IsNull(first.ProductId);
                Assert.AreEqual("REAL1", second.Sku); Assert.IsTrue(second.IsCheckPassed); Assert.IsFalse(f.Model.CanSaveDraft);
                await f.Model.MatchAllSkusCommand.ExecuteAsync(null);
                Assert.AreEqual(2, sent.Lines.Count); Assert.IsTrue(first.IsCheckPassed); Assert.IsFalse(f.Model.CanSaveDraft);
                await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                await f.Model.MatchSelectedSkusCommand.ExecuteAsync(null); Assert.IsFalse(f.Model.CanSaveDraft);
                second.QtyText = "0"; Assert.IsFalse(second.IsCheckPassed);
            }
        }

        /// <summary>旧汇率文本不阻断检查或保存，不发生空值解引用，双币种报价保持原输入。</summary>
        [TestMethod]
        [DataRow("")]
        [DataRow("0")]
        [DataRow("-7")]
        [DataRow("bad")]
        public async Task DeprecatedExchangeRateDoesNotBlockDraft(string text)
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); f.Model.Header.ExchangeRateText = text;
                f.Model.Lines[0].CnyPriceText = "37.7";
                f.CheckApi.Check = (r, t) => { Assert.IsNull(r.Header.ExchangeRate); return Task.FromResult(Checked(f, r)); };
                await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                f.Model.Header.ExchangeRateText = "旧文本变化";
                Assert.IsTrue(f.Model.CanSaveDraft, "废弃字段不应使活动输入检查失效");
                SaveSalesOrderRequest sent = null;
                f.Api.Save = r => { sent = r; return Task.FromResult(Saved(f)); };
                await f.Model.SaveDraftAsync();
                Assert.IsNotNull(sent); Assert.IsNull(sent.Header.ExchangeRate);
                Assert.AreEqual(f.Model.Header.Currency, sent.Header.Currency);
                Assert.AreEqual(5.18m, sent.Lines[0].UsdPrice); Assert.AreEqual(37.7m, sent.Lines[0].CnyPrice);
                Assert.AreEqual("SO1", f.Model.Header.OrderNo);
            }
        }

        /// <summary>局部匹配共用并发锁，编辑、删除、换号和释放后均拒绝迟到身份回填。</summary>
        [TestMethod]
        public async Task LateSkuMatchingCannotWriteBack()
        {
            for (var action = 0; action < 4; action++)
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); var row = f.Model.Lines[0]; f.Model.SetSelectedLines(new[] { row });
                var wait = new TaskCompletionSource<ServiceResult<CheckSalesOrderImportResponse>>();
                CheckSalesOrderImportRequest sent = null; var calls = 0;
                f.CheckApi.Check = (r, t) => { sent = r; calls++; return wait.Task; };
                var task = f.Model.MatchSelectedSkusCommand.ExecuteAsync(null);
                Assert.IsFalse(f.Model.MatchAllSkusCommand.CanExecute(null)); Assert.IsFalse(f.Model.CheckImportCommand.CanExecute(null));
                await f.Model.MatchAllSkusCommand.ExecuteAsync(null); Assert.AreEqual(1, calls);
                var response = Checked(f, sent);
                if (action == 0) row.CustomerSku = "changed";
                else if (action == 1) f.Model.Lines.Remove(row);
                else if (action == 2) f.Login("other");
                else f.Model.Dispose();
                wait.SetResult(response); await task;
                Assert.IsNull(row.ProductId); Assert.IsFalse(row.IsCheckPassed); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }

        /// <summary>产品身份由检查回填，商业资料保持原值；新建只接受空头身份和稳定行身份。</summary>
        [TestMethod]
        public async Task CheckedIdentityAndSaveReceipt()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); var l = f.Model.Lines[0]; SaveSalesOrderRequest sent = null;
                f.Api.Save = r => { sent = r; return Task.FromResult(Saved(f)); };
                await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft); Assert.AreEqual("REAL1", l.Sku);
                Assert.AreEqual("AA1", l.OriginalLinkSku); Assert.AreEqual("5.18", l.UsdPriceText);
                await f.Model.SaveDraftAsync(); Assert.AreEqual(Guid.Empty, sent.Header.Uid); Assert.AreEqual(l.Uid, sent.Lines[0].Uid);
                Assert.AreEqual("SYSTEM-AA1", sent.Lines[0].LinkSku); Assert.AreEqual(5.18m, sent.Lines[0].UsdPrice);
                Assert.AreEqual("SO1", f.Model.Header.OrderNo); Assert.IsFalse(f.Model.IsEditable); Assert.IsFalse(f.Model.HasEdits);
                Assert.IsFalse(f.Model.SaveDraftCommand.CanExecute(null)); f.Model.NewImportCommand.Execute(null);
                Assert.IsTrue(f.Model.IsEditable); Assert.AreEqual(0, f.Model.Lines.Count); Assert.AreEqual("", f.Model.Header.OrderNo);
            }
        }
        /// <summary>接口成功不能取代 CanSave；非法可空数字不能当作空值提交。</summary>
        [TestMethod]
        public async Task BlockingResultAndInvalidOptionalNumbers()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); var calls = 0;
                f.CheckApi.Check = (r, t) => { calls++; var v = Checked(f, r); v.Data.CanSave = false; return Task.FromResult(v); };
                await f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.CanSaveDraft);
                f.Model.Lines[0].CnyPriceText = "abc"; await f.Model.CheckImportAsync(); Assert.AreEqual(1, calls);
                Assert.IsTrue(f.Model.SubmissionMessage.Contains("CNY"));
            }
        }
        /// <summary>后端待确认型号差异须用户勾选再检验。</summary>
        [TestMethod]
        public async Task MismatchRequiresRecheck()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f);
                f.CheckApi.Check = (r, t) => { var v = Checked(f, r); var accepted = r.Lines[0].AcceptLinkSkuMismatch;
                    v.Data.CanSave = accepted; v.Data.Lines[0].Issues.Add(new SalesOrderImportIssueDto {
                        Code = "LinkSkuMismatch", Message = "型号不同", RequiresConfirmation = true, Acknowledged = accepted }); return Task.FromResult(v); };
                await f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.CanSaveDraft);
                f.Model.Lines[0].AcceptLinkSkuMismatch = true; Assert.IsFalse(f.Model.CanSaveDraft);
                await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                f.Model.Lines[0].OriginalLinkSku = "NEW"; Assert.IsFalse(f.Model.Lines[0].AcceptLinkSkuMismatch); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        /// <summary>已有订单确认只允许新建，不采用已有 Uid；取消不会自动保存。</summary>
        [TestMethod]
        public async Task ExistingOrderConfirmation()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); var calls = 0;
                f.CheckApi.Check = (r, t) => { calls++; var v = Checked(f, r); v.Data.CanSave = r.AcceptExistingOrders;
                    v.Data.ExistingOrders.Add(new SalesOrderImportExistingOrderDto { Uid = Guid.NewGuid(), OrderNo = "OLD" });
                    v.Data.Issues.Add(new SalesOrderImportIssueDto { Code = "ExistingOrders", RequiresConfirmation = true, Acknowledged = r.AcceptExistingOrders, Message = "重复" }); return Task.FromResult(v); };
                f.Confirm = false; await f.Model.CheckImportAsync(); Assert.AreEqual(1, calls); Assert.IsFalse(f.Model.CanSaveDraft);
                f.Confirm = true; await f.Model.CheckImportAsync(); Assert.AreEqual(3, calls); Assert.IsTrue(f.Model.CanSaveDraft);
            }
        }
        /// <summary>多候选需明确选择并重验，不静默取第一项。</summary>
        [TestMethod]
        public async Task AmbiguousSelectionAndNonStandard()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); var candidate = Candidate(); var l = f.Model.Lines[0];
                f.CheckApi.Check = (r, t) => { var v = Checked(f, r);
                    if (!r.Lines[0].SelectedCustomerProductId.HasValue && !r.Lines[0].IsNonStandard) {
                        v.Data.CanSave = false; v.Data.Lines[0].MatchStatus = "Ambiguous"; v.Data.Lines[0].Product = null;
                        v.Data.Lines[0].Candidates.Add(candidate); }
                    return Task.FromResult(v); };
                await f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.CanSaveDraft); Assert.IsNull(l.ProductId);
                l.SelectedMatchCandidate = l.MatchCandidates[0]; f.Model.ChooseMatchCommand.Execute(l);
                Assert.AreEqual(candidate.CustomerProductId, l.SelectedMatchId); await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
                f.Model.ToggleNonStandardCommand.Execute(l); Assert.IsTrue(l.IsNonStandard); Assert.IsNull(l.ProductId);
                l.DescriptionEn = "手工件"; l.UnitSnapshot = "PCS"; await f.Model.CheckImportAsync(); Assert.IsTrue(f.Model.CanSaveDraft);
            }
        }
        /// <summary>单头、明细或增删任何一次编辑均使整单检查失效。</summary>
        [TestMethod]
        public async Task EditingInvalidatesCheck()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); await f.Model.CheckImportAsync(); f.Model.Header.Remark = "changed"; Assert.IsFalse(f.Model.CanSaveDraft);
                await f.Model.CheckImportAsync(); f.Model.Lines[0].QtyText = "65"; Assert.IsFalse(f.Model.CanSaveDraft);
                await f.Model.CheckImportAsync(); f.Model.AddNonStandardLineCommand.Execute(null); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        /// <summary>延迟检查在编辑变化、会话更换或释放后不能回填产品身份。</summary>
        [TestMethod]
        public async Task LateCheckCannotWriteBack()
        {
            foreach (var action in new[] { 0, 1, 2 }) using (var f = new ImportTestFixture())
            {
                await Prepare(f); var row = f.Model.Lines[0]; var wait = new TaskCompletionSource<ServiceResult<CheckSalesOrderImportResponse>>();
                CheckSalesOrderImportRequest request = null;
                f.CheckApi.Check = (r, t) => { request = r; return wait.Task; };
                var task = f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.IsEditable); Assert.IsFalse(f.Model.CheckImportCommand.CanExecute(null));
                var response = Checked(f, request);
                if (action == 0) row.QtyText = "100"; else if (action == 1) f.Login("other"); else f.Model.Dispose();
                wait.SetResult(response); await task; Assert.IsNull(row.ProductId); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        /// <summary>断网后保持完整原请求，重试权限失败不解除未知锁，成功后仅接纳一次回执。</summary>
        [TestMethod]
        public async Task UnknownOutcomeUsesOriginalCommandAndSnapshot()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); await f.Model.CheckImportAsync(); var seen = new List<SaveSalesOrderRequest>(); var count = 0;
                f.Api.Save = r => { seen.Add(r); count++;
                    if (count == 1) throw new HttpRequestException("network");
                    return Task.FromResult(count == 2 ? ServiceResult<SaveSalesOrderResponse>.Fail(ServiceErrorCodes.Forbidden, "无权") : Saved(f)); };
                await f.Model.SaveDraftAsync(); Assert.IsTrue(f.Model.HasPendingSave); Assert.IsFalse(f.Model.IsEditable);
                f.Model.Lines[0].QtyText = "999"; await f.Model.SaveDraftAsync(); Assert.IsTrue(f.Model.HasPendingSave);
                await f.Model.SaveDraftAsync(); Assert.AreSame(seen[0], seen[2]); Assert.AreEqual(64m, seen[2].Lines[0].Qty);
                Assert.AreEqual(seen[0].CommandId, seen[2].CommandId); Assert.IsFalse(f.Model.HasPendingSave);
            }
        }
        /// <summary>确定业务拒绝保留编辑并要求重验；保存并发和迟到的旧账号回执均拒绝。</summary>
        [TestMethod]
        public async Task KnownFailureAndLateSave()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); await f.Model.CheckImportAsync(); f.Api.Save = r => Task.FromResult(ServiceResult<SaveSalesOrderResponse>.Fail(ServiceErrorCodes.Validation, "无效"));
                await f.Model.SaveDraftAsync(); Assert.IsTrue(f.Model.IsEditable); Assert.IsFalse(f.Model.CanSaveDraft); Assert.AreEqual("64", f.Model.Lines[0].QtyText);
                await f.Model.CheckImportAsync(); var wait = new TaskCompletionSource<ServiceResult<SaveSalesOrderResponse>>(); var count = 0;
                f.Api.Save = r => { count++; return wait.Task; }; var task = f.Model.SaveDraftAsync(); await f.Model.SaveDraftAsync(); Assert.AreEqual(1, count);
                f.Login("other"); wait.SetResult(Saved(f)); await task; Assert.AreEqual("", f.Model.Header.OrderNo); Assert.AreEqual(0, f.Model.Lines.Count);
            }
        }
        /// <summary>重复订单确认的嵌套消息循环发生换号时，旧提示与授权不污染新页面。</summary>
        [TestMethod]
        public async Task AccountChangesInsideConfirmation()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f);
                f.CheckApi.Check = (r, t) => { var v = Checked(f, r); v.Data.CanSave = false;
                    v.Data.ExistingOrders.Add(new SalesOrderImportExistingOrderDto { Uid = Guid.NewGuid(), OrderNo = "OLD" });
                    v.Data.Issues.Add(new SalesOrderImportIssueDto { Code = "ExistingOrders", RequiresConfirmation = true, Message = "旧账号重复" }); return Task.FromResult(v); };
                f.OnConfirm = () => f.Login("other"); await f.Model.CheckImportAsync();
                Assert.AreEqual(0, f.Model.Lines.Count); Assert.AreEqual("", f.Model.SubmissionMessage); Assert.IsFalse(f.Model.CanSaveDraft);
            }
        }
        /// <summary>成功却缺失或身份错误的回执不能宣称落库；超过上限拒绝拆分检验。</summary>
        [TestMethod]
        public async Task InvalidReceiptRemainsUnknownAndRowLimit()
        {
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); await f.Model.CheckImportAsync();
                f.Api.Save = r => Task.FromResult(ServiceResult<SaveSalesOrderResponse>.Ok(new SaveSalesOrderResponse {
                    Document = new SalesOrderDetailDto { Header = new SalesOrderEditHeaderDto { Uid = Guid.NewGuid(), CustomerId = Guid.NewGuid(), OrderNo = "OTHER" } } }));
                await f.Model.SaveDraftAsync(); Assert.IsTrue(f.Model.HasPendingSave); Assert.AreEqual("", f.Model.Header.OrderNo);
                f.Confirm = false; Assert.IsFalse(f.Model.CanClose());
            }
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); var calls = 0;
                f.CheckApi.Check = (r, t) => { calls++; return Task.FromResult(Checked(f, r)); };
                for (var i = 0; i < 500; i++) f.Model.Lines.Add(new SalesOrderImportLine(true, null) { DescriptionEn = "manual", UnitSnapshot = "PCS" });
                await f.Model.CheckImportAsync(); Assert.AreEqual(0, calls); Assert.IsTrue(f.Model.SubmissionMessage.Contains("500"));
            }
        }
        /// <summary>缺权限和结构不完整的响应都不能开放保存。</summary>
        [TestMethod]
        public async Task PermissionsAndMalformedResponse()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.AddNonStandardLineCommand.Execute(null); Assert.IsFalse(f.Model.CheckImportCommand.CanExecute(null));
            }
            using (var f = new ImportTestFixture())
            {
                await Prepare(f); f.CheckApi.Check = (r, t) => { var v = Checked(f, r); v.Data.Lines.Clear(); return Task.FromResult(v); };
                await f.Model.CheckImportAsync(); Assert.IsFalse(f.Model.CanSaveDraft); Assert.IsTrue(f.Model.SubmissionMessage.Contains("完整"));
            }
        }
    }
}
