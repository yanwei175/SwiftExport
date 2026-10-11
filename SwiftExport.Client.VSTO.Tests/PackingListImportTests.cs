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
                Assert.AreEqual("manual note", f.Transport.Commands[0].Header.Note); Assert.IsFalse(f.Model.CanEdit); StringAssert.Contains(f.Model.Message, "PL-TEST");
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
        /// <summary>旧账号、已编辑或释放后的检查均不得接纳；更换客户保留输入并清除产品身份。</summary>
        [TestMethod]
        public async Task LateResultsAndCustomerChangesPreserveInputs()
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
                await f.Start(); f.Model.Header.CustomerAddress = "keep address";
                var replacement = new SwiftExport.Contracts.DTOs.Customers.CustomerDto { Uid = Guid.NewGuid(), CustomerCode = "C2" };
                f.Base.Cache.CustomerItems.Add(replacement);
                await f.Model.CheckCommand.ExecuteAsync(null);
                var line = f.Model.Lines[0]; line.AcceptLinkSkuMismatch = true;
                var prompts = f.Base.Prompts; f.Base.Confirm = false; f.Model.SelectedCustomer = replacement;
                Assert.AreSame(replacement, f.Model.SelectedCustomer); Assert.AreEqual(1, f.Model.Lines.Count);
                Assert.AreEqual(prompts, f.Base.Prompts); Assert.AreEqual("manual", line.DescriptionEn); Assert.AreEqual("3", line.QtyText);
                Assert.IsNull(line.ToInput(1).ProductId); Assert.AreEqual("", line.Sku); Assert.AreEqual("", line.MainImage); Assert.IsFalse(line.AcceptLinkSkuMismatch);
                Assert.AreEqual("keep address", f.Model.Header.CustomerAddress);
                Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
                f.Model.CustomerClearedCommand.Execute(null);
                Assert.IsNull(f.Model.SelectedCustomer); Assert.AreSame(line, f.Model.Lines[0]); Assert.IsFalse(f.Model.MatchAllSkusCommand.CanExecute(null));
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
        /// <summary>独立匹配无需完整单头，非法数值不因请求降为空或零而显示通过；本行不改其他行。</summary>
        [TestMethod]
        public async Task IndependentMatchingAndInvalidInput()
        {
            using (var f = new Fixture())
            {
                await f.Start(); var first = f.Model.Lines[0];
                f.Model.Header.InvoiceDate = null; first.QtyText = "bad"; first.GwPerPackageText = "bad";
                await f.Model.MatchAllSkusCommand.ExecuteAsync(null);
                Assert.AreEqual("REAL-SKU", first.Sku); Assert.IsFalse(first.IsCheckPassed); Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
                Assert.AreEqual("bad", first.QtyText); Assert.AreEqual("bad", first.GwPerPackageText);
                first.QtyText = "3"; first.GwPerPackageText = "";
                Assert.AreEqual("REAL-SKU", first.Sku); Assert.AreEqual("https://test.invalid/product.png", first.MainImage);
                await f.Model.MatchLineSkuCommand.ExecuteAsync(first);
                Assert.IsTrue(first.IsCheckPassed); Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
                f.Model.AddStandardLineCommand.Execute(null); var second = f.Model.Lines[1]; second.CustomerSku = "C2"; second.QtyText = "2";
                await f.Model.MatchLineSkuCommand.ExecuteAsync(second);
                Assert.AreEqual(second.Uid, f.Transport.LastCheck.Lines.Single().Uid); Assert.IsTrue(first.IsCheckPassed); Assert.IsTrue(second.IsCheckPassed);
                second.CustomerSku = "changed"; Assert.AreEqual("", second.Sku); Assert.AreEqual("", second.MainImage);
                f.Model.Header.InvoiceDate = DateTime.Today;
                await f.Model.CheckCommand.ExecuteAsync(null); Assert.IsTrue(f.Model.SaveCommand.CanExecute(null));
                second.DescriptionEn = "edited"; Assert.IsFalse(f.Model.SaveCommand.CanExecute(null)); Assert.AreEqual("REAL-SKU", second.Sku);
            }
        }
        /// <summary>本行匹配丢弃已删除行、编辑及账号变化后的结果；不完整身份不能接纳。</summary>
        [TestMethod]
        public async Task LateMatchingAndIncompleteResponse()
        {
            for (var scenario = 0; scenario < 4; scenario++)
            using (var f = new Fixture())
            {
                await f.Start(); var row = f.Model.Lines[0]; var source = new TaskCompletionSource<CheckPackingListImportResponse>();
                f.Transport.Check = request => source.Task; var matching = f.Model.MatchLineSkuCommand.ExecuteAsync(row);
                if (scenario == 0) f.Model.Lines.Remove(row);
                if (scenario == 1) row.QtyText = "5";
                if (scenario == 2) f.Base.Login("another-user");
                if (scenario == 3) f.Model.Dispose();
                source.SetResult(f.Transport.Response(f.Transport.LastCheck)); await matching;
                Assert.AreEqual("", row.Sku); Assert.IsFalse(f.Model.SaveCommand.CanExecute(null));
            }
            using (var f = new Fixture())
            {
                await f.Start();
                f.Transport.Check = request => Task.FromResult(new CheckPackingListImportResponse { CanSave = true });
                await f.Model.MatchAllSkusCommand.ExecuteAsync(null);
                Assert.AreEqual("", f.Model.Lines[0].Sku); StringAssert.Contains(f.Model.ErrorMessage, "身份不完整");
            }
        }
        /// <summary>地址读取只覆盖实际字段，空单元格清空；取消及读取中修订保留原输入。</summary>
        [TestMethod]
        public async Task ControlledHeaderReadAndRetry()
        {
            using (var f = new Fixture())
            {
                await f.Start(); var header = f.Model.Header; var line = f.Model.Lines[0];
                header.CustomerName = "manual name"; header.CustomerAddress = "manual address"; header.Eta = new DateTime(2026, 12, 3);
                f.Model.SelectedReadProfile = f.Model.ReadProfiles[0];
                f.Base.AddressReader.Read = detail => Task.FromResult(new ExcelAddressSnapshot { Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> {
                    new ExcelAddressValue { FieldKey = "Header.SealNo", Value = "SEAL-25" }, new ExcelAddressValue { FieldKey = "Header.CustomerName", Value = null },
                    new ExcelAddressValue { FieldKey = "Header.ExcelDocumentNo", Value = "Excel number differs from lines" } } } });
                f.Base.Confirm = false; await f.Model.ReadHeaderCommand.ExecuteAsync(null); Assert.AreEqual("manual name", header.CustomerName);
                f.Base.Confirm = true; await f.Model.ReadHeaderCommand.ExecuteAsync(null);
                Assert.AreSame(header, f.Model.Header); Assert.IsTrue(string.IsNullOrEmpty(header.CustomerName)); Assert.AreEqual("SEAL-25", header.SealNo);
                Assert.AreEqual("Excel number differs from lines", header.ExcelDocumentNo);
                Assert.AreEqual("", f.Model.InvoiceNosSummary); Assert.AreEqual("manual note", header.Note);
                Assert.AreEqual("manual address", header.CustomerAddress); Assert.AreEqual(new DateTime(2026, 12, 3), header.Eta);
                Assert.AreSame(line, f.Model.Lines[0]); Assert.IsNotNull(f.Model.SelectedCustomer);
                var late = new TaskCompletionSource<ExcelAddressSnapshot>(); f.Base.AddressReader.Read = detail => late.Task;
                var reading = f.Model.ReadHeaderCommand.ExecuteAsync(null); header.SealNo = "edited";
                late.SetResult(new ExcelAddressSnapshot { Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> { new ExcelAddressValue { FieldKey = "Header.SealNo", Value = "old" } } } });
                await reading; Assert.AreEqual("edited", header.SealNo);
            }
        }
        /// <summary>导入顺序稳定，批量替换重新编号；删除、多选和非标切换不丢失商业原文。</summary>
        [TestMethod]
        public async Task NumberingDeletionAndNonStandard()
        {
            using (var f = new Fixture())
            {
                await f.Start(); var first = f.Model.Lines[0];
                f.Model.AddNonStandardLineCommand.Execute(null); var second = f.Model.Lines[1]; second.QtyText = "2"; second.DescriptionEn = "nonstandard"; second.Unit = "PCS";
                Assert.IsTrue(second.IsNonStandard); Assert.AreEqual(1, first.LineNumber); Assert.AreEqual(2, second.LineNumber);
                await f.Model.MatchAllSkusCommand.ExecuteAsync(null); Assert.IsTrue(second.IsCheckPassed);
                f.Model.ToggleNonStandardCommand.Execute(first); Assert.IsTrue(first.IsNonStandard); Assert.AreEqual("manual", first.DescriptionEn); Assert.AreEqual("3", first.QtyText); Assert.IsNull(first.ToInput(1).ProductId);
                f.Model.SetSelectedLines(new[] { first }); f.Model.DeleteLineCommand.Execute(null); Assert.AreEqual(2, second.LineNumber);
                PackingListImportLine added = null; f.Model.LineAdded += line => added = line; f.Model.AddStandardLineCommand.Execute(null); Assert.AreEqual(3, added.LineNumber);
                f.Model.SetSelectedLines(f.Model.Lines.ToArray()); f.Model.DeleteLineCommand.Execute(null); Assert.AreEqual(0, f.Model.Lines.Count);
                f.Model.SelectedReadProfile = f.Model.ReadProfiles[0]; f.Base.AddressReader.Read = detail => Task.FromResult(new ExcelAddressSnapshot { Detail = true, Rows = new List<List<ExcelAddressValue>> {
                    new List<ExcelAddressValue> { new ExcelAddressValue { FieldKey = "Line.CustomerSku", Value = "A" } },
                    new List<ExcelAddressValue> { new ExcelAddressValue { FieldKey = "Line.CustomerSku", Value = "B" } } } });
                added = null; await f.Model.ReadLinesCommand.ExecuteAsync(null);
                CollectionAssert.AreEqual(new[] { 1, 2 }, f.Model.Lines.Select(line => line.LineNumber).ToArray()); Assert.IsNull(added);
                Assert.IsFalse(ExcelImportRegistry.CreateDefault().Find("PackingList").ContainsField("Line.LineNumber"));
            }
        }
        /// <summary>逐行包装保留小数，不自动拼箱；缺失、非法、非正数量及溢出均明确提示。</summary>
        [TestMethod]
        public async Task PackagingSummaryAndUnboundSave()
        {
            using (var f = new Fixture())
            {
                await f.Start(); var row = f.Model.Lines[0]; row.PcsPerPackageText = "2"; row.GwPerPackageText = "1.2"; row.CbmPerPackageText = "0.1";
                StringAssert.Contains(f.Model.Summary, "总箱数：" + 1.5m.ToString("N2")); StringAssert.Contains(f.Model.Summary, "总毛重：" + 1.8m.ToString("N2"));
                StringAssert.Contains(f.Model.Summary, "总体积：" + .15m.ToString("N3")); Assert.IsFalse(f.Model.Summary.Contains("不完整"));
                row.QtyText = "0"; Assert.IsTrue(row.IsQuantityNonPositive); StringAssert.Contains(f.Model.Summary, "不完整");
                row.QtyText = "3"; row.GwPerPackageText = decimal.MaxValue.ToString(); StringAssert.Contains(f.Model.Summary, "包装汇总不完整");
                row.GwPerPackageText = ""; row.PcsPerPackageText = ""; row.CbmPerPackageText = "";
                await f.Model.CheckCommand.ExecuteAsync(null); Assert.IsTrue(f.Model.SaveCommand.CanExecute(null));
                await f.Model.SaveCommand.ExecuteAsync(null);
                var input = f.Transport.Commands.Single().Lines.Single(); Assert.IsNull(input.GrossDemandId); Assert.IsNull(input.PackingGroupId);
                Assert.AreEqual(1, input.LineNumber); Assert.AreEqual("PL-TEST", f.Model.PackingListNo);
            }
        }

        /// <summary>发票号汇总随增删和编辑变化，保存后使用服务器值；新导入和会话切换清除旧汇总。</summary>
        [TestMethod]
        public async Task InvoiceSummaryTracksRowsAndUsesServerValue()
        {
            for (var changeSession = 0; changeSession < 2; changeSession++)
            using (var f = new Fixture())
            {
                await f.Start(); var notifications = 0;
                f.Model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(f.Model.InvoiceNosSummary)) notifications++; };
                f.Model.Lines[0].InvoiceNo = " INV-2 ";
                foreach (var invoice in new[] { "INV-1", " INV-2 ", "inv-1", "   " })
                {
                    f.Model.AddStandardLineCommand.Execute(null);
                    var added = f.Model.Lines.Last(); added.CustomerSku = "C1"; added.QtyText = "1"; added.InvoiceNo = invoice;
                }
                Assert.AreEqual("INV-2, INV-1, inv-1", f.Model.InvoiceNosSummary);
                f.Model.Lines[0].InvoiceNo = " INV-3 ";
                Assert.AreEqual("INV-3, INV-1, INV-2, inv-1", f.Model.InvoiceNosSummary);
                f.Model.SetSelectedLines(new[] { f.Model.Lines[2] }); f.Model.DeleteLineCommand.Execute(null);
                Assert.AreEqual("INV-3, INV-1, inv-1", f.Model.InvoiceNosSummary); Assert.IsTrue(notifications > 0);
                f.Transport.InvoiceSummary = "SERVER-RESULT";
                await f.Model.CheckCommand.ExecuteAsync(null); await f.Model.SaveCommand.ExecuteAsync(null);
                Assert.AreEqual("SERVER-RESULT", f.Model.InvoiceNosSummary);
                Assert.AreEqual("Excel raw document", f.Model.Header.ExcelDocumentNo);
                Assert.AreEqual("manual note", f.Transport.Commands.Single().Header.Note);
                if (changeSession == 0) f.Model.NewCommand.Execute(null); else f.Base.Login("new-user");
                Assert.AreEqual("", f.Model.InvoiceNosSummary);
            }
        }
        /// <summary>单头发票号地址可配置和读取，但不替代明细汇总或改写备注；系统汇总不暴露为导入字段。</summary>
        [TestMethod]
        public void DocumentNumberAddressIsActiveAndReadsLocally()
        {
            var registry = ExcelImportRegistry.CreateDefault(); var definition = registry.Find("PackingList");
            Assert.IsTrue(definition.ContainsField("Header.ExcelDocumentNo")); Assert.IsFalse(definition.ContainsField("Header.InvoiceNosSummary"));
            Assert.AreEqual(40, definition.Fields.Single(field => field.FieldKey == "Header.ExcelDocumentNo").DisplayOrder);
            Assert.IsFalse(ExcelAddressRules.IsDeprecatedField("PackingList", "Header.ExcelDocumentNo"));
            Assert.IsFalse(ExcelAddressRules.IsDeprecatedField("PackingList", "Line.InvoiceNo"));
            using (var f = new Fixture())
            {
                var document = new SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadFieldDto
                    { TargetFieldKey = "Header.ExcelDocumentNo", CellAddress = "not-an-address" };
                f.Base.ProfileApi.Profile.Fields.Add(document);
                Assert.IsTrue(ExcelAddressRules.Validate(f.Base.ProfileApi.Profile, definition).Any(issue => issue.Contains("Header.ExcelDocumentNo")));
                document.CellAddress = "M12";
                Assert.AreEqual(0, ExcelAddressRules.Validate(f.Base.ProfileApi.Profile, definition).Count);
            }
            var snapshot = new ExcelAddressSnapshot { Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> {
                new ExcelAddressValue { FieldKey = "Header.ExcelDocumentNo", Value = "24th" } } } };
            var parsed = new PackingListAddressParser(registry).Parse(snapshot);
            Assert.AreEqual("24th", parsed.Header.ExcelDocumentNo); Assert.AreEqual("", parsed.Header.Note);
            Assert.AreEqual(0, parsed.Issues.Count);
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
                Base.ProfileApi.Profile.Fields[0].TargetFieldKey = "Header.SealNo";
                clients = new ApiClientRegistration(new Uri("https://test.invalid/"), Base.Session, new Transport(), Transport);
                Model = new PackingListImportViewModel(Base.Session, Base, Base.AddressReader, Base.ProfileService, ExcelImportRegistry.CreateDefault(), Base.Cache,
                    clients.Create<IForExcelPackingListApi>("api/for-excel/packing-lists"), clients.Create<IPackingListApi>("api/shipping/packing-lists"), Base, new ApiCallRunner(new AddInLog()));
            }
            internal async Task Start()
            {
                await Model.InitializeAsync(); Model.SelectedCustomer = Base.Cache.Customers[0]; Model.Header.Note = "manual note";
                Model.Header.ExcelDocumentNo = "Excel raw document";
                Model.AddStandardLineCommand.Execute(null); var row = Model.Lines[0]; row.CustomerSku = "C1"; row.OriginalLinkSku = "LINK"; row.DescriptionEn = "manual"; row.QtyText = "3";
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
            internal string InvoiceSummary;
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
                    result = ServiceResult<PackingListDraftCommandResult>.Ok(new PackingListDraftCommandResult { Uid = command.Uid, Detail = new PackingListDetailDto { Header = new PackingListHeaderDto { Uid = command.Uid, PackingListNo = "PL-TEST", InvoiceNosSummary = InvoiceSummary } } });
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result, result.GetType(), Json), Encoding.UTF8, "application/json") };
            }
            internal CheckPackingListImportResponse Response(CheckPackingListImportRequest request) => new CheckPackingListImportResponse { CanSave = true,
                Lines = request.Lines.Select(x => new PackingListImportLineCheckDto { ClientLineId = x.Uid, MatchStatus = x.IsNonStandardSnapshot ? "NonStandard" : "Matched", Product = x.IsNonStandardSnapshot ? null : new PackingListImportProductCandidateDto { ProductId = ProductId, Sku = "REAL-SKU", LinkSku = "LINK", Unit = "PCS", MainImage = "https://test.invalid/product.png", IsUsable = true } }).ToList() };
        }
    }
}
