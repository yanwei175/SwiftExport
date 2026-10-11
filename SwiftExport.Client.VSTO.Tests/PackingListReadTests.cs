using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Services;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证装箱单合并箱唛读取与配置选择，不访问 Office 或真实服务器。</summary>
    [TestClass]
    public sealed class PackingListReadTests
    {
        /// <summary>使用读取器实际调用的合并规则，箱唛保留换行并逐行复制，空行不因箱唛而生成产品。</summary>
        [TestMethod]
        public void MergedMarksCopyToActualDetailRows()
        {
            var snapshot = new ExcelAddressSnapshot { Detail = true };
            for (var row = 15; row <= 19; row++)
            {
                var value = ExcelAddressRules.ResolveMergedDetailValue("PackingList",
                    new ExcelAddressValue { FieldKey = "Line.Marks", Row = row, Column = 1, Value = row <= 17 ? "AQUATORIA\nORDER 47" : "SECOND GROUP" },
                    row <= 17 ? "A15:A17" : "A18:A19");
                Assert.AreEqual(row, value.Row); Assert.AreEqual(1, value.Column); Assert.IsNull(value.Issue);
                snapshot.Rows.Add(new List<ExcelAddressValue> { value,
                    new ExcelAddressValue { FieldKey = "Line.CustomerSku", Row = row, Column = 4, Value = row == 16 ? null : "C" + row },
                    new ExcelAddressValue { FieldKey = "Line.Qty", Row = row, Column = 8, Value = row == 16 ? null : (object)2d } });
            }
            var result = new PackingListAddressParser(ExcelImportRegistry.CreateDefault()).Parse(snapshot);
            Assert.AreEqual(4, result.Lines.Count); Assert.AreEqual(8m, result.Lines.Sum(line => line.Qty.Value));
            Assert.AreEqual("AQUATORIA\nORDER 47", result.Lines[0].Marks); Assert.AreEqual(result.Lines[0].Marks, result.Lines[1].Marks);
            Assert.AreEqual("SECOND GROUP", result.Lines[2].Marks); Assert.AreEqual(result.Lines[2].Marks, result.Lines[3].Marks);
            Assert.AreEqual(1, result.Issues.Count); StringAssert.Contains(result.Issues[0], "16");
        }

        /// <summary>箱唛空值与公式错误不污染文字；包装合计仍留空，型号和数量的跨行合并仍拒绝。</summary>
        [TestMethod]
        public void MergedMarksPreserveErrorsAndStrictFieldBoundaries()
        {
            var blank = ExcelAddressRules.ResolveMergedDetailValue("PackingList", new ExcelAddressValue { FieldKey = "Line.Marks", Row = 15 }, "A15:A16");
            Assert.IsNull(blank.Value); Assert.IsNull(blank.Issue);
            var error = ExcelAddressRules.ResolveMergedDetailValue("PackingList", new ExcelAddressValue
                { FieldKey = "Line.Marks", Row = 15, Value = new System.Runtime.InteropServices.ErrorWrapper(-2146826281), Issue = "Excel 单元格为错误值" }, "A15:A16");
            var snapshot = new ExcelAddressSnapshot { Detail = true, Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue>
                { error, new ExcelAddressValue { FieldKey = "Line.CustomerSku", Row = 15, Value = "C1" },
                    new ExcelAddressValue { FieldKey = "Line.Qty", Row = 15, Value = 2d } } } };
            var result = new PackingListAddressParser(ExcelImportRegistry.CreateDefault()).Parse(snapshot);
            Assert.AreEqual("", result.Lines[0].Marks); Assert.AreEqual(2m, result.Lines[0].Qty); Assert.AreEqual(1, result.Issues.Count);
            foreach (var key in new[] { "Line.PcsPerPackage", "Line.NwPerPackage", "Line.GwPerPackage", "Line.CbmPerPackage" })
                StringAssert.Contains(ExcelAddressRules.ResolveMergedDetailValue("PackingList",
                    new ExcelAddressValue { FieldKey = key, Row = 15, Value = 12d }, "J15:J16").Issue, "已留空");
            foreach (var key in new[] { "Line.CustomerSku", "Line.Qty", "Line.OriginalLinkSku" })
                Assert.ThrowsExactly<InvalidOperationException>(() => ExcelAddressRules.ResolveMergedDetailValue("PackingList",
                    new ExcelAddressValue { FieldKey = key }, "D15:D16"));
            Assert.ThrowsExactly<InvalidOperationException>(() => ExcelAddressRules.ResolveMergedDetailValue("SalesOrder",
                new ExcelAddressValue { FieldKey = "Line.Marks" }, "A15:A16"));
        }

        /// <summary>首项自动选择，刷新按 Uid 恢复；配置删除和空列表回退，不触碰编辑内容。</summary>
        [TestMethod]
        public async Task ProfileSelectionSurvivesRefreshAndHandlesEmptyList()
        {
            using (var fixture = new PackingListImportTests.Fixture())
            {
                var first = Profile("首项"); var second = Profile("第二项"); var items = new[] { first, second };
                fixture.Base.ProfileApi.ListOverride = (group, token) =>
                { Assert.AreEqual("PackingList", group); return Task.FromResult(ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Ok(items)); };
                await fixture.Start(); Assert.AreSame(first, fixture.Model.SelectedReadProfile);
                var line = fixture.Model.Lines[0]; var header = fixture.Model.Header; var calls = fixture.Base.ProfileApi.Lists;
                await fixture.Model.InitializeAsync(); Assert.AreEqual(calls, fixture.Base.ProfileApi.Lists);
                fixture.Model.SelectedReadProfile = second;
                var updated = Profile("新名称"); updated.Uid = second.Uid;
                items = new[] { updated, first }; await fixture.Model.InitializeCommand.ExecuteAsync(null);
                Assert.AreSame(updated, fixture.Model.SelectedReadProfile); Assert.AreSame(header, fixture.Model.Header); Assert.AreSame(line, fixture.Model.Lines[0]);
                Assert.AreEqual("manual note", header.Note);
                items = new[] { first }; await fixture.Model.InitializeCommand.ExecuteAsync(null); Assert.AreSame(first, fixture.Model.SelectedReadProfile);
                fixture.Base.ProfileApi.ListOverride = (group, token) => Task.FromResult(ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Fail("配置读取失败"));
                await fixture.Model.InitializeCommand.ExecuteAsync(null); Assert.AreSame(first, fixture.Model.SelectedReadProfile); Assert.AreEqual(1, fixture.Model.ReadProfiles.Count);
                fixture.Base.ProfileApi.ListOverride = (group, token) => Task.FromResult(ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Ok(new ExcelReadProfileListItemDto[0]));
                await fixture.Model.InitializeCommand.ExecuteAsync(null);
                Assert.IsNull(fixture.Model.SelectedReadProfile); Assert.IsFalse(fixture.Model.ReadLinesCommand.CanExecute(null)); StringAssert.Contains(fixture.Model.Message, "创建配置");
            }
        }

        /// <summary>初始化失败可重试，退出或释放后忽略迟到的配置列表。</summary>
        [TestMethod]
        public async Task ProfileLoadingRetriesAndRejectsLateResults()
        {
            using (var fixture = new PackingListImportTests.Fixture())
            {
                fixture.Base.ProfileApi.ListOverride = (group, token) => Task.FromResult(ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Fail("失败"));
                await fixture.Model.InitializeAsync(); Assert.IsNull(fixture.Model.SelectedReadProfile);
                fixture.Base.ProfileApi.ListOverride = null; await fixture.Model.InitializeAsync(); Assert.IsNotNull(fixture.Model.SelectedReadProfile);
                var response = new TaskCompletionSource<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>>();
                fixture.Base.ProfileApi.ListOverride = (group, token) => response.Task;
                var pending = fixture.Model.InitializeCommand.ExecuteAsync(null);
                fixture.Base.Session.TryEnd(fixture.Base.Session.Capture().Version);
                response.SetResult(ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Ok(new[] { Profile("旧账号") }));
                await pending; Assert.AreEqual(0, fixture.Model.ReadProfiles.Count); Assert.IsNull(fixture.Model.SelectedReadProfile);
            }
            using (var fixture = new PackingListImportTests.Fixture())
            {
                var response = new TaskCompletionSource<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>>();
                fixture.Base.ProfileApi.ListOverride = (group, token) => response.Task;
                var pending = fixture.Model.InitializeAsync(); fixture.Model.Dispose();
                response.SetResult(ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Ok(new[] { Profile("已关闭") }));
                await pending; Assert.AreEqual(0, fixture.Model.ReadProfiles.Count); Assert.IsNull(fixture.Model.SelectedReadProfile);
            }
        }
        private static ExcelReadProfileListItemDto Profile(string name) => new ExcelReadProfileListItemDto
            { Uid = Guid.NewGuid(), GroupCode = "PackingList", ConfigName = name };
    }
}
