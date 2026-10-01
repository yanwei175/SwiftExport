using System;
using System.Collections.Generic;
using System.Linq;
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
        private static ExcelAddressValue Value(string key, int row, object value) => new ExcelAddressValue { FieldKey = key, Row = row, Column = 1, Value = value };
        private static void Grant(ImportTestFixture f) => f.Session.TryCommit(f.Session.Capture().Version, "token", new MeDto { UserId = "admin", UserName = "admin", Permissions = new List<string> { PermissionKeys.SystemExcelFieldMappingManage } });
        private static ExcelReadProfileViewModel Config(ImportTestFixture f) => new ExcelReadProfileViewModel(f.ProfileService, f.AddressReader, ExcelImportRegistry.CreateDefault(), f.Session, f, new ApiCallRunner(new AddInLog()), f);
    }
}
