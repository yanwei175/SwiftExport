using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Services;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>实际附件快照、通知模型、认证 HTTP 和 Refit 路由的纯托管回归。</summary>
    [TestClass]
    public sealed class PackingListImportTests
    {
        /// <summary>完整区域包含 Q/R，21 行数量195；系统身份不能配置，包装合并只对白名单容错。</summary>
        [TestMethod]
        public void AttachmentRowsAndFieldDirectory()
        {
            var data = JsonSerializer.Deserialize<List<List<string>>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData/PackingListAttachmentRows.json")));
            var snapshot = new ExcelAddressSnapshot { Detail = true, WorkbookName = "24th", WorksheetName = "PL" };
            var keys = new[] { "Line.CustomerSku", "Line.OriginalLinkSku", "Line.DescriptionEn", "Line.ColorNameEn", "Line.Qty", "Line.CustomerPoNo", "Line.InvoiceNo" };
            var cols = new[] { 4, 5, 6, 7, 8, 17, 18 };
            for (var i = 0; i < data.Count; i++) snapshot.Rows.Add(data[i].Select((v, j) => new ExcelAddressValue { FieldKey = keys[j], Value = v, Row = i + 15, Column = cols[j] }).ToList());
            var registry = ExcelImportRegistry.CreateDefault(); var result = new PackingListAddressParser(registry).Parse(snapshot);
            Assert.AreEqual(21, result.Lines.Count); Assert.AreEqual(195m, result.Lines.Sum(x => x.Qty.Value));
            Assert.IsTrue(result.Lines.Any(x => x.InvoiceNo == "AFS260130004" && string.IsNullOrEmpty(x.CustomerPoNo)));
            Assert.IsTrue(result.Lines.Any(x => !string.IsNullOrWhiteSpace(x.CustomerPoNo))); Assert.IsTrue(result.Lines.All(x => !string.IsNullOrWhiteSpace(x.InvoiceNo)));
            foreach (var field in new[] { "Line.ProductId", "Line.GrossDemandId", "Line.Sku", "Line.IsNonStandard", "Header.CustomerId" }) Assert.IsFalse(registry.Find("PackingList").ContainsField(field));
            Assert.IsTrue(ExcelAddressRules.AllowMergedDetailSkip("PackingList", "Line.PcsPerPackage"));
            Assert.IsFalse(ExcelAddressRules.AllowMergedDetailSkip("PackingList", "Line.Qty")); Assert.IsFalse(ExcelAddressRules.AllowMergedDetailSkip("PurchaseOrder", "Line.PcsPerPackage"));
            snapshot.Rows[0].Add(new ExcelAddressValue { FieldKey = "Line.PcsPerPackage", Row = 15, Column = 9, Issue = "合并包装留空" });
            var tolerant = new PackingListAddressParser(registry).Parse(snapshot);
            Assert.AreEqual(195m, tolerant.Lines.Sum(x => x.Qty.Value)); Assert.AreEqual("", tolerant.Lines[0].PcsPerPackageText); Assert.AreEqual(1, tolerant.Issues.Count);
            var head = new ExcelAddressSnapshot { Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> {
                new ExcelAddressValue { FieldKey = "Header.InvoiceDate", Value = "5/12/2026", Row = 10, Column = 13 } } } };
            Assert.AreEqual(new DateTime(2026, 5, 12), new PackingListAddressParser(registry).Parse(head).Header.InvoiceDate);
        }
        /// <summary>检查接纳身份，不覆盖手工资料；任一编辑使保存失效并清除差异确认。</summary>
        [TestMethod]
        public async Task CheckSaveAndEditsInvalidate()
        {
            using (var f = new Fixture())
            {
                await f.Start(); var line = f.Model.Lines[0];
                await f.Model.CheckCommand.ExecuteAsync(null); Assert.IsTrue(f.Model.SaveCommand.CanExecute(null));
                Assert.AreEqual("REAL-SKU", line.Sku); Assert.AreEqual("manual", line.DescriptionEn); Assert.AreEqual("PCS", line.Unit);
                line.AcceptLinkSkuMismatch = true; Assert.IsFalse(f.Model.SaveCommand.CanExecute(null)); line.OriginalLinkSku = "changed"; Assert.IsFalse(line.AcceptLinkSkuMismatch);
                await f.Model.CheckCommand.ExecuteAsync(null); await f.Model.SaveCommand.ExecuteAsync(null);
                Assert.IsNull(f.Transport.Commands[0].Lines[0].GrossDemandId); Assert.AreEqual(f.Transport.ProductId, f.Transport.Commands[0].Lines[0].ProductId);
                StringAssert.Contains(f.Transport.Commands[0].Header.Note, "24th"); Assert.IsFalse(f.Model.CanEdit); StringAssert.Contains(f.Model.Message, "PL-TEST");
            }
        }
        /// <summary>网络结果未知时冻结请求并使用同一 CommandId 和快照重试；非法包装文字不变成空值。</summary>
        [TestMethod]
        public async Task UnknownSaveRetriesFrozenIntent()
        {
            using (var f = new Fixture())
            {
                await f.Start(); var line = f.Model.Lines[0]; line.NwPerPackageText = "bad";
                await f.Model.CheckCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Transport.Checks);
                line.NwPerPackageText = ""; await f.Model.CheckCommand.ExecuteAsync(null);
                f.Transport.FailWrite = true; await f.Model.SaveCommand.ExecuteAsync(null);
                Assert.IsFalse(f.Model.CanEdit); Assert.IsFalse(f.Model.CanClose());
                var commandId = f.Transport.Commands[0].CommandId; line.QtyText = "999";
                f.Transport.FailWrite = false; await f.Model.SaveCommand.ExecuteAsync(null);
                Assert.AreEqual(commandId, f.Transport.Commands[1].CommandId); Assert.AreEqual(3m, f.Transport.Commands[1].Lines[0].Qty);
            }
        }
        /// <summary>旧账号、已编辑或释放后的检查均不得接纳。</summary>
        [TestMethod]
        public async Task LateResultsAndCustomerCancellation()
        {
            for (var scenario = 0; scenario < 3; scenario++)
            using (var f = new Fixture())
            {
                await f.Start(); var line = f.Model.Lines[0]; var tcs = new TaskCompletionSource<CheckPackingListImportResponse>();
                f.Transport.Check = request => tcs.Task; var checking = f.Model.CheckCommand.ExecuteAsync(null);
                if (scenario == 0) f.Base.Login("new-user");
                if (scenario == 1) line.CustomerSku = "changed";
                if (scenario == 2) f.Model.Dispose();
                tcs.SetResult(f.Transport.Response(f.Transport.LastCheck)); await checking;
                Assert.AreEqual("", line.Sku); Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
            }
            using (var f = new Fixture())
            {
                await f.Start(); var old = f.Model.SelectedCustomer;
                f.Base.Confirm = false; f.Model.SelectedCustomer = new SwiftExport.Contracts.DTOs.Customers.CustomerDto { Uid = Guid.NewGuid() };
                Assert.AreSame(old, f.Model.SelectedCustomer); Assert.AreEqual(1, f.Model.Lines.Count);
            }
        }
        /// <summary>更换候选对象后保留所选关系；确认差异、改单及重新检查后仍可保存，关系消失时清除选择。</summary>
        [TestMethod]
        public async Task CandidateSelectionSurvivesRepeatedChecks()
        {
            using (var f = new Fixture())
            {
                await f.Start(); var line = f.Model.Lines[0]; var choices = new[] { Guid.NewGuid(), Guid.NewGuid() };
                f.Transport.Check = request => Task.FromResult(CandidateResponse(request, choices));
                await f.Model.CheckCommand.ExecuteAsync(null);
                Assert.IsNull(line.SelectedCandidate); Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
                line.SelectedCandidate = line.Candidates[1];
                await f.Model.CheckCommand.ExecuteAsync(null);
                Assert.AreSame(line.Candidates[1], line.SelectedCandidate);
                Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
                line.AcceptLinkSkuMismatch = true; await f.Model.CheckCommand.ExecuteAsync(null);
                Assert.IsTrue(f.Model.SaveCommand.CanExecute(null));
                line.QtyText = "4"; Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
                await f.Model.CheckCommand.ExecuteAsync(null);
                Assert.AreEqual(choices[1], f.Transport.LastCheck.Lines[0].SelectedCustomerProductId);
                Assert.IsTrue(f.Model.SaveCommand.CanExecute(null));
                f.Transport.Check = request => Task.FromResult(CandidateResponse(request, new[] { choices[0] }));
                await f.Model.CheckCommand.ExecuteAsync(null);
                Assert.IsNull(line.SelectedCandidate); Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
            }
        }
        // 每次响应使用新对象模拟 HTTP 反序列化；服务器只接纳用户显式选择且已确认型号差异的关系。
        internal static CheckPackingListImportResponse CandidateResponse(CheckPackingListImportRequest request, Guid[] choices)
        {
            var input = request.Lines.Single();
            var candidates = choices.Select((id, index) => new PackingListImportProductCandidateDto
            { CustomerProductId = id, ProductId = id, Sku = "SKU-" + index, LinkSku = "DIFFERENT", Unit = "PCS", IsUsable = true }).ToList();
            var selected = candidates.FirstOrDefault(x => x.CustomerProductId == input.SelectedCustomerProductId);
            return new CheckPackingListImportResponse { CanSave = selected != null && input.AcceptLinkSkuMismatch,
                Lines = new List<PackingListImportLineCheckDto> { new PackingListImportLineCheckDto { ClientLineId = input.Uid, Candidates = candidates,
                    Product = selected, MatchStatus = selected == null ? "Ambiguous" : "Matched" } } };
        }

        /// <summary>真实 Refit 与认证管线在内存传输运行，不连接业务服务器。</summary>
        internal sealed class Fixture : IDisposable
        {
            internal readonly ImportTestFixture Base = new ImportTestFixture();
            internal readonly Transport Transport = new Transport();
            internal readonly PackingListImportViewModel Model;
            private readonly ApiClientRegistration clients;
            internal Fixture()
            {
                Base.Session.TryCommit(Base.Session.Capture().Version, "test", new MeDto { UserId = "packing", Permissions = new List<string> { "Shipping.PackingList.Create" } });
                Base.ProfileApi.Profile.GroupCode = "PackingList";
                clients = new ApiClientRegistration(new Uri("https://test.invalid/"), Base.Session, new Transport(), Transport);
                Model = new PackingListImportViewModel(Base.Session, Base, Base.AddressReader, Base.ProfileService, ExcelImportRegistry.CreateDefault(), Base.Cache,
                    clients.Create<IForExcelPackingListApi>("api/for-excel/packing-lists"), clients.Create<IPackingListApi>("api/shipping/packing-lists"), Base, new ApiCallRunner(new AddInLog()));
            }
            internal async Task Start()
            {
                await Model.InitializeAsync(); Model.SelectedCustomer = Base.Cache.Customers[0]; Model.Header.ExcelDocumentNo = "24th";
                Model.AddLineCommand.Execute(null); var row = Model.Lines[0]; row.CustomerSku = "C1"; row.OriginalLinkSku = "LINK"; row.DescriptionEn = "manual"; row.QtyText = "3";
            }
            /// <summary>释放页面及自有 HTTP，借用服务最后释放。</summary>
            public void Dispose() { Model.Dispose(); clients.Dispose(); Base.Dispose(); }
        }
        /// <summary>返回明确行身份及可控延迟，记录完整创建意图。</summary>
        internal sealed class Transport : HttpMessageHandler
        {
            internal Guid ProductId = Guid.NewGuid();
            internal int Checks;
            internal bool FailWrite;
            internal CheckPackingListImportRequest LastCheck;
            internal Func<CheckPackingListImportRequest, Task<CheckPackingListImportResponse>> Check;
            internal readonly List<CreatePackingListDraftCommand> Commands = new List<CreatePackingListDraftCommand>();
            private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            /// <summary>只支持本用例的两个路由，避免未接线误当成功。</summary>
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var body = await request.Content.ReadAsStringAsync(); object result;
                if (request.RequestUri.AbsolutePath == "/api/for-excel/packing-lists/check")
                { Checks++; LastCheck = JsonSerializer.Deserialize<CheckPackingListImportRequest>(body, Json); result = ServiceResult<CheckPackingListImportResponse>.Ok(Check == null ? Response(LastCheck) : await Check(LastCheck)); }
                else
                {
                    Assert.AreEqual("/api/shipping/packing-lists/drafts", request.RequestUri.AbsolutePath);
                    var command = JsonSerializer.Deserialize<CreatePackingListDraftCommand>(body, Json); Commands.Add(command);
                    if (FailWrite) throw new HttpRequestException("断网");
                    result = ServiceResult<PackingListDraftCommandResult>.Ok(new PackingListDraftCommandResult { Uid = command.Uid, Detail = new PackingListDetailDto { Header = new PackingListHeaderDto { Uid = command.Uid, PackingListNo = "PL-TEST" } } });
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result, result.GetType(), Json), Encoding.UTF8, "application/json") };
            }
            internal CheckPackingListImportResponse Response(CheckPackingListImportRequest request) => new CheckPackingListImportResponse { CanSave = true,
                Lines = request.Lines.Select(x => new PackingListImportLineCheckDto { ClientLineId = x.Uid, MatchStatus = "Matched", Product = new PackingListImportProductCandidateDto { ProductId = ProductId, Sku = "REAL-SKU", LinkSku = "LINK", Unit = "PCS", IsUsable = true } }).ToList() };
        }
    }
}
