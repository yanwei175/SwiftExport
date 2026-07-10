using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Enum;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Columns;

[TestClass]
public sealed class EnumDataGridColumnTests
{
    [TestMethod]
    public void EnumType_ShouldBuildNativeComboBoxItemsWithDisplayMetadataAndNullOption()
    {
        StaTest.Run(() =>
        {
            var column = new EnumDataGridColumn
            {
                IncludeNull = true,
                NullDisplayText = "未选择",
                EnumType = typeof(TestStatus)
            };

            var items = column.ItemsSource.Cast<EnumDataGridItem>().ToArray();
            Assert.HasCount(3, items);
            Assert.AreEqual(new EnumDataGridItem(null, "未选择"), items[0]);
            Assert.AreEqual(new EnumDataGridItem(TestStatus.Active, "启用"), items[1]);
            Assert.AreEqual(new EnumDataGridItem(TestStatus.Disabled, "停用说明"), items[2]);
            Assert.AreEqual(nameof(EnumDataGridItem.Value), column.SelectedValuePath);
            Assert.AreEqual(nameof(EnumDataGridItem.DisplayName), column.DisplayMemberPath);
        });
    }

    private enum TestStatus
    {
        [Display(Name = "启用")]
        Active,

        [System.ComponentModel.Description("停用说明")]
        Disabled,

        [Browsable(false)]
        Hidden
    }
}
