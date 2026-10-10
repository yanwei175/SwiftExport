using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Services;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.ViewModels;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证配置快照、结束规则、受控赋值和会话安全，不加载 Office。</summary>
    [TestClass]
    public sealed class ExcelAddressReadTests
    {
        /// <summary>按真实 Value2 的一基数组查找末行，跳过末尾合并合计及备注，不因内部空行提前结束。</summary>
        [TestMethod]
        public void LastRowSkipsMergedSummaryAndEmptyCells()
        {
            var values = (object[,])Array.CreateInstance(typeof(object), new[] { 7, 1 }, new[] { 1, 1 });
            values[1, 1] = "AQ1"; values[3, 1] = 0d; values[4, 1] = "  ";
            values[5, 1] = "TOTAL"; values[7, 1] = "合并备注";
            var visited = new List<int>();
            var result = ExcelAddressRules.FindLastUnmergedRow(values, 8, 7,
                row => { visited.Add(row); return row == 12 || row == 14; }, CancellationToken.None);
            Assert.AreEqual(10, result);
            CollectionAssert.AreEqual(new[] { 14, 12, 10 }, visited);
        }
        /// <summary>块内只有空白或合并内容时继续找上一个块，不把合并首行认作明细末行。</summary>
        [TestMethod]
        public void LastRowReturnsBeforeBlockWhenNoEligibleCell()
        {
            var values = new object[,] { { null }, { "" }, { "合并汇总" } };
            Assert.AreEqual(7, ExcelAddressRules.FindLastUnmergedRow(values, 8, 3, row => true, CancellationToken.None));
            Assert.AreEqual(20, ExcelAddressRules.FindLastUnmergedRow("  ", 21, 1,
                row => { Assert.Fail("空白不应查询合并状态"); return false; }, CancellationToken.None));
        }
        /// <summary>单格 Value2 保留零值，并严格按是否合并判断，不按汇总文字自动过滤未合并的单元格。</summary>
        [TestMethod]
        public void LastRowHandlesSingleCellAndDoesNotGuessSummaryText()
        {
            Assert.AreEqual(21, ExcelAddressRules.FindLastUnmergedRow(0d, 21, 1, row => false, CancellationToken.None));
            Assert.AreEqual(20, ExcelAddressRules.FindLastUnmergedRow(5d, 21, 1, row => true, CancellationToken.None));
            Assert.AreEqual(21, ExcelAddressRules.FindLastUnmergedRow("TOTAL", 21, 1, row => false, CancellationToken.None));
        }
        /// <summary>已取消的末行扫描不再查询候选，避免旧页面操作继续触碰 Excel。</summary>
        [TestMethod]
        public void LastRowHonorsCancellationBeforeMergeLookup()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.ThrowsExactly<OperationCanceledException>(() => ExcelAddressRules.FindLastUnmergedRow("AQ1", 8, 1,
                    row => { Assert.Fail("已取消不得查询合并状态"); return false; }, cancellation.Token));
            }
        }
        /// <summary>合并合计不复制进每箱字段，型号与数量继续导入；来源值和区域提示保留供人工核对。</summary>
        [TestMethod]
        public void SalesMergedPackagingTotalsRemainUnassigned()
        {
            var snapshot = new ExcelAddressSnapshot { Detail = true };
            var keys = new[] { "Line.NwOfCarton", "Line.GwOfCarton", "Line.CbmOfCarton" };
            for (var row = 8; row <= 9; row++)
            {
                var cells = new List<ExcelAddressValue> { Value("Line.CustomerSku", row, "AQ" + row), Value("Line.Qty", row, row - 6) };
                foreach (var key in keys)
                {
                    Assert.IsTrue(ExcelAddressRules.AllowMergedDetailSkip("SalesOrder", key));
                    cells.Add(new ExcelAddressValue { FieldKey = key, Row = row, Column = 12, Value = 12.5d,
                        Issue = "跨行合并区域 L8:L9 的包装值不自动分配到各明细，目标字段已留空，请核对后补充。" });
                }
                snapshot.Rows.Add(cells);
            }
            var result = new SalesOrderAddressParser(ExcelImportRegistry.CreateDefault()).Parse(snapshot);
            Assert.AreEqual(2, result.Lines.Count); Assert.AreEqual(5m, result.Lines.Sum(line => line.Qty.Value));
            Assert.AreEqual(6, result.Issues.Count); Assert.IsTrue(result.Message.Contains("L8:L9"));
            foreach (var line in result.Lines)
            {
                Assert.IsNull(line.NwOfCarton); Assert.IsNull(line.GwOfCarton); Assert.IsNull(line.CbmOfCarton);
                Assert.AreEqual(3, line.Source.Cells.Count(cell => keys.Contains(cell.FieldKey) && cell.Text == "12.5"));
                Assert.IsFalse(string.IsNullOrWhiteSpace(line.CustomerSku));
            }
        }
        /// <summary>包装合计容错仅限已确认字段，不放开数量、型号、单价、每箱数量及其他业务组。</summary>
        [TestMethod]
        public void SalesMergedPackagingDoesNotRelaxIdentityOrQuantity()
        {
            foreach (var key in new[] { "Line.CustomerSku", "Line.LinkSku", "Line.Qty", "Line.PcsOfCarton", "Line.CnyPrice" })
                Assert.IsFalse(ExcelAddressRules.AllowMergedDetailSkip("SalesOrder", key));
            Assert.IsFalse(ExcelAddressRules.AllowMergedDetailSkip("PurchaseOrder", "Line.NwOfCarton"));
            Assert.IsFalse(ExcelAddressRules.AllowMergedDetailSkip("Unknown", "Line.GwOfCarton"));
        }
        /// <summary>地址支持绝对标记，拒绝范围、公式和越界值。</summary>
        [TestMethod]
        public void AddressBoundsAndDirections()
        {
            Assert.IsTrue(ExcelAddressRules.TryCell("$xFd$1048576", out var r, out var c)); Assert.AreEqual(16384, c);
            foreach (var value in new[] { "A1:B2", "=A1", "Sheet1!A1", "XFE1", "A1048577", "A0" }) Assert.IsFalse(ExcelAddressRules.TryCell(value, out r, out c));
            var api = new AddressProfileApi(); var definition = ExcelImportRegistry.CreateDefault().Find("SalesOrder");
            Assert.AreEqual(0, ExcelAddressRules.Validate(api.Profile, definition).Count);
            api.Profile.EndMode = 0; Assert.IsTrue(ExcelAddressRules.Validate(api.Profile, definition).Any(x => x.Contains("结束")));
            api.Profile.EndMode = 2; api.Profile.Fields[1].IsDownward = false; Assert.IsTrue(ExcelAddressRules.Validate(api.Profile, definition).Any(x => x.Contains("向下")));
            api.Profile.Fields[1].IsDownward = true; api.Profile.Fields[2].CellAddress = "J19";
            Assert.IsTrue(ExcelAddressRules.Validate(api.Profile, definition).Any(x => x.Contains("同一行")));
        }
        /// <summary>内部全空行跳过，部分为空保留，非法数值仍能人工修正。</summary>
        [TestMethod]
        public void EmptyRowsAndInvalidInputRemainVisible()
        {
            var snapshot = new ExcelAddressSnapshot { Detail = true, Rows = new List<List<ExcelAddressValue>> {
                new List<ExcelAddressValue> { Value("Line.CustomerSku",18,null), Value("Line.Qty",18,null) },
                new List<ExcelAddressValue> { Value("Line.CustomerSku",19,"AA"), Value("Line.Qty",19,"bad") } } };
            var result = new SalesOrderAddressParser(ExcelImportRegistry.CreateDefault()).Parse(snapshot);
            Assert.AreEqual(1, result.Lines.Count); Assert.AreEqual("bad", result.Lines[0].QtyText);
            Assert.IsNull(result.Lines[0].Qty); Assert.IsTrue(result.Message.Contains("18")); Assert.IsNull(result.Lines[0].ProductId);
        }
        /// <summary>日期采用工作簿日期体系，保留原始来源。</summary>
        [TestMethod]
        public void WorkbookDateSystemIsExplicit()
        {
            var snapshot = new ExcelAddressSnapshot { Date1904 = true, Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> { Value("Header.OrderDate",1,1d) } } };
            var result = new SalesOrderAddressParser(ExcelImportRegistry.CreateDefault()).Parse(snapshot);
            Assert.AreEqual(new DateTime(1904, 1, 2), result.Header.OrderDate);
            Assert.AreEqual("1", result.Header.Source.Cells[0].Text);
        }
        /// <summary>配置获取不影响业务值，读取取消确认保留原单头与明细。</summary>
        [TestMethod]
        public async Task ReadingRequiresExplicitAcceptance()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.Header.InvoiceNo = "OLD"; f.Model.AddStandardLineCommand.Execute(null);
                await f.Model.EnsureReadProfilesAsync(); Assert.AreEqual("OLD", f.Model.Header.InvoiceNo);
                f.Confirm = false; await f.Model.ReadHeaderCommand.ExecuteAsync(null); Assert.AreEqual("OLD", f.Model.Header.InvoiceNo);
                f.Confirm = true; await f.Model.ReadHeaderCommand.ExecuteAsync(null); Assert.AreEqual("260820", f.Model.Header.InvoiceNo); Assert.AreEqual(1, f.Model.Lines.Count);
            }
        }
        /// <summary>旧账号读取和已释放页面不能接纳迟到快照。</summary>
        [TestMethod]
        public async Task LateReadAfterSessionChangeOrDisposeIsRejected()
        {
            foreach (var dispose in new[] { false, true }) using (var f = new ImportTestFixture())
            {
                await f.Start(); await f.Model.EnsureReadProfilesAsync();
                var pending = new TaskCompletionSource<ExcelAddressSnapshot>(); f.AddressReader.Read = _ => pending.Task;
                var task = f.Model.ReadHeaderCommand.ExecuteAsync(null);
                if (dispose) f.Model.Dispose(); else f.Login("next");
                pending.SetResult(new ExcelAddressSnapshot { Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> { Value("Header.InvoiceNo",12,"LATE") } } });
                await task; Assert.AreNotEqual("LATE", f.Model.Header.InvoiceNo);
            }
        }
        /// <summary>完整快照保存统一可空工作表、移除空地址并保留未知字段。</summary>
        [TestMethod]
        public async Task FullSnapshotPreservesUnknownFieldsAndClearsAddress()
        {
            using (var f = new ImportTestFixture())
            {
                Grant(f); f.ProfileApi.Profile.Fields.Add(new ExcelReadFieldDto { TargetFieldKey = "Header.FutureField", CellAddress = "Z1" });
                using (var model = Config(f))
                {
                    await model.InitializeAsync(); Assert.AreEqual(0, f.ProfileApi.Saves);
                    model.WorksheetName = "参考"; model.LineFields.First(r => r.FieldKey == "Line.Qty").CellAddress = "";
                    var request = model.BuildRequest(); Assert.IsTrue(request.Fields.All(x => x.WorksheetName == "参考"));
                    Assert.IsTrue(request.Fields.Any(x => x.TargetFieldKey == "Header.FutureField")); Assert.IsFalse(request.Fields.Any(x => x.TargetFieldKey == "Line.Qty"));
                    model.WorksheetName = ""; Assert.IsTrue(model.BuildRequest().Fields.All(x => x.WorksheetName == null));
                    await model.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, f.ProfileApi.Saves); Assert.IsFalse(model.HasChanges);
                }
            }
        }
        /// <summary>版本冲突不覆盖本地编辑，地址选择取消不改变配置。</summary>
        [TestMethod]
        public async Task ConflictAndSelectionCancellationPreserveEdits()
        {
            using (var f = new ImportTestFixture())
            {
                Grant(f); using (var model = Config(f))
                {
                    await model.InitializeAsync(); var row = model.HeaderFields.First(r => r.FieldKey == "Header.InvoiceNo"); row.CellAddress = "M99";
                    await model.PickCommand.ExecuteAsync(row); Assert.AreEqual("M99", row.CellAddress);
                    f.ProfileApi.FailSave = true; await model.SaveCommand.ExecuteAsync(null);
                    Assert.AreEqual("M99", row.CellAddress); Assert.IsTrue(model.HasChanges); Assert.IsTrue(model.ErrorMessage.Contains("版本"));
                    row.IsDownward = true; await model.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, f.ProfileApi.Saves);
                }
            }
        }
        /// <summary>配置详情迟到且页面释放后不能更新字段集合。</summary>
        [TestMethod]
        public async Task ConfigDisposeRejectsLateResponse()
        {
            using (var f = new ImportTestFixture())
            {
                Grant(f); var pending = new TaskCompletionSource<ServiceResult<ExcelReadProfileDto>>(); f.ProfileApi.GetOverride = () => pending.Task;
                var model = Config(f); var task = model.InitializeAsync(); model.Dispose(); pending.SetResult(ServiceResult<ExcelReadProfileDto>.Ok(f.ProfileApi.Profile)); await task;
                Assert.AreEqual(0, model.HeaderFields.Count);
            }
        }
        /// <summary>保存配置通知导入页刷新，但不自动读取或覆盖业务内容。</summary>
        [TestMethod]
        public async Task SavedConfigRefreshesSelectionWithoutReading()
        {
            using (var f = new ImportTestFixture())
            {
                Grant(f); await f.Start(); await f.Model.EnsureReadProfilesAsync(); f.Model.Header.InvoiceNo = "KEEP";
                var lists = f.ProfileApi.Lists;
                f.AddressReader.Read = _ => throw new InvalidOperationException("保存配置不应读取 Excel");
                using (var model = Config(f))
                {
                    await model.InitializeAsync(); model.ConfigName = "新名称"; await model.SaveCommand.ExecuteAsync(null);
                    Assert.IsTrue(f.ProfileApi.Lists > lists); Assert.AreEqual("新名称", f.Model.SelectedReadProfile.ConfigName);
                    Assert.AreEqual("KEEP", f.Model.Header.InvoiceNo);
                }
            }
        }
        /// <summary>销售旧汇率配置的地址、方向和原值错误均忽略，其他字段仍正常校验与解析。</summary>
        [TestMethod]
        public void DeprecatedExchangeRateDoesNotBlockAddressValidationOrParsing()
        {
            var api = new AddressProfileApi(); var definition = ExcelImportRegistry.CreateDefault().Find("SalesOrder");
            api.Profile.Fields.Add(new ExcelReadFieldDto { TargetFieldKey = "Header.ExchangeRate", CellAddress = "invalid", IsDownward = true });
            Assert.AreEqual(0, ExcelAddressRules.Validate(api.Profile, definition).Count);
            foreach (var value in new object[] { null, "bad", 0m, -7m })
            {
                var snapshot = new ExcelAddressSnapshot { Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> {
                    Value("Header.InvoiceNo", 1, "INV"), new ExcelAddressValue { FieldKey = "Header.ExchangeRate", Row = 1, Column = 2, Value = value, Issue = "Excel 错误值" } } } };
                var result = new SalesOrderAddressParser(ExcelImportRegistry.CreateDefault()).Parse(snapshot);
                Assert.AreEqual(0, result.Issues.Count); Assert.AreEqual("INV", result.Header.InvoiceNo);
                Assert.IsNull(result.Header.ExchangeRate); Assert.AreEqual(2, result.Header.Source.Cells.Count);
            }
            api.Profile.Fields[0].CellAddress = "invalid";
            Assert.IsTrue(ExcelAddressRules.Validate(api.Profile, definition).Any(issue => issue.Contains("Header.InvoiceNo")));
        }

        /// <summary>只有旧汇率地址时读取成为无副作用的跳过，不触碰 Excel 或替换现有销售输入。</summary>
        [TestMethod]
        public async Task DeprecatedOnlyHeaderReadPreservesCurrentInputs()
        {
            using (var f = new ImportTestFixture())
            {
                f.ProfileApi.Profile.Fields = new List<ExcelReadFieldDto> { new ExcelReadFieldDto { TargetFieldKey = "Header.ExchangeRate", CellAddress = "invalid" } };
                f.ProfileApi.Profile.EndMode = 0;
                await f.Start(); await f.Model.EnsureReadProfilesAsync(); f.Model.Header.InvoiceNo = "KEEP";
                f.AddressReader.Read = _ => throw new InvalidOperationException("废弃配置不得读取 Excel");
                await f.Model.ReadHeaderCommand.ExecuteAsync(null);
                Assert.AreEqual("KEEP", f.Model.Header.InvoiceNo); Assert.AreEqual("", f.Model.ErrorMessage);
                Assert.IsTrue(f.Model.ParseMessage.Contains("已弃用"));
            }
        }

        /// <summary>旧汇率不影响活动参考表名，退出编辑目录但保存时保留原记录；重载后不串配置。</summary>
        [TestMethod]
        public async Task DeprecatedExchangeRateConfigurationIsPreservedWithoutEditor()
        {
            using (var f = new ImportTestFixture())
            {
                Grant(f);
                foreach (var field in f.ProfileApi.Profile.Fields) field.WorksheetName = "活动表";
                f.ProfileApi.Profile.Fields.Add(new ExcelReadFieldDto { TargetFieldKey = "Header.ExchangeRate", CellAddress = "$H$2", WorksheetName = "旧表", IsDownward = false });
                using (var model = Config(f))
                {
                    await model.InitializeAsync();
                    Assert.AreEqual("活动表", model.WorksheetName);
                    Assert.IsFalse(model.HeaderFields.Concat(model.LineFields).Any(row => row.FieldKey == "Header.ExchangeRate"));
                    Assert.IsTrue(model.Status.Contains("已弃用")); model.WorksheetName = "新表";
                    var legacy = model.BuildRequest().Fields.Single(field => field.TargetFieldKey == "Header.ExchangeRate");
                    Assert.AreEqual("$H$2", legacy.CellAddress); Assert.AreEqual("旧表", legacy.WorksheetName); Assert.IsFalse(legacy.IsDownward);
                    legacy.CellAddress = "改动请求副本";
                    Assert.AreEqual("$H$2", model.BuildRequest().Fields.Single(field => field.TargetFieldKey == "Header.ExchangeRate").CellAddress);
                    await model.SaveCommand.ExecuteAsync(null);
                    Assert.AreEqual("$H$2", f.ProfileApi.LastRequest.Fields.Single(field => field.TargetFieldKey == "Header.ExchangeRate").CellAddress);
                    f.ProfileApi.Profile.Fields.RemoveAll(field => field.TargetFieldKey == "Header.ExchangeRate");
                    await model.ReloadCommand.ExecuteAsync(null);
                    Assert.IsFalse(model.BuildRequest().Fields.Any(field => field.TargetFieldKey == "Header.ExchangeRate"));
                }
            }
        }

        private static ExcelAddressValue Value(string key, int row, object value) => new ExcelAddressValue { FieldKey = key, Row = row, Column = 1, Value = value };
        private static void Grant(ImportTestFixture f) => f.Session.TryCommit(f.Session.Capture().Version, "token", new MeDto { UserId = "admin", UserName = "admin", Permissions = new List<string> { PermissionKeys.SystemExcelFieldMappingManage } });
        private static ExcelReadProfileViewModel Config(ImportTestFixture f) => new ExcelReadProfileViewModel(f.ProfileService, f.AddressReader, ExcelImportRegistry.CreateDefault(), f.Session, f, new ApiCallRunner(new AddInLog()), f);
    }
}
