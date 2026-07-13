using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductAndBomListModelTests
{
    [TestMethod]
    public void MapsterMappings_ShouldPreserveRenamedAndPersistenceFields()
    {
        var product = new ProductLineModel();
        product.Load(new ProductDto
        {
            Uid = Guid.NewGuid(),
            IsDeleted = true,
            CNYPrice = 12.34m,
            USDPrice = 1.75m,
            Sku = "P-1",
        });

        var productDto = product.ToDto();
        Assert.IsTrue(productDto.IsDeleted);
        Assert.AreEqual(12.34m, productDto.CNYPrice);
        Assert.AreEqual(1.75m, productDto.USDPrice);

        var color = new ProductColorLineModel();
        var createdDate = DateTime.UtcNow.AddDays(-1);
        color.Load(new ProductColorDto
        {
            Uid = Guid.NewGuid(),
            IsDeleted = true,
            CreatedDate = createdDate,
            CreatedBy = "tester",
            ColorCodeUnique = "BK",
            ColorCode = "BK",
        });

        var colorDto = color.ToDto();
        Assert.IsTrue(colorDto.IsDeleted);
        Assert.AreEqual(createdDate, colorDto.CreatedDate);
        Assert.AreEqual("tester", colorDto.CreatedBy);
    }

    [TestMethod]
    public void ProductListModel_ShouldReuseBaseStateAndBuildBatchRequest()
    {
        var list = new ProductListModel();
        list.LoadFromDtos([new ProductDto { Uid = Guid.NewGuid(), Version = 3, Sku = "P-1" }]);
        var existing = list.Items.Single();
        existing.DescriptionZh = "修改";
        var added = list.AddNewProduct();
        added.Sku = "P-2";

        var request = list.CreateSaveRequest();

        Assert.AreEqual(1, request.CreateItems.Count);
        Assert.AreEqual("P-2", request.CreateItems[0].Data.Sku);
        Assert.AreEqual(1, request.UpdateItems.Count);
        Assert.AreEqual(existing.Uid, request.UpdateItems[0].Uid);
        Assert.IsTrue(list.HasChanges);

        list.RejectChanges();
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual(string.Empty, list.Items[0].DescriptionZh);
        Assert.IsFalse(list.HasChanges);
    }

    [TestMethod]
    public void BomListModel_ShouldReuseBaseDeletePoolAndBuildBatchRequest()
    {
        var persistedId = Guid.NewGuid();
        var list = new BomListModel();
        list.LoadFromDtos([new BomDto
        {
            Uid = persistedId,
            Version = 2,
            BomNo = "B-1",
            SubSkuId = Guid.NewGuid(),
            SubSku = "SUB-1",
            Numerator = 1,
            Denominator = 1,
        }]);
        var persisted = list.Items.Single();
        list.Remove(persisted);
        var added = list.AddNewItem("B-1");
        added.SubSkuId = Guid.NewGuid();
        added.SubSku = "SUB-2";

        var request = list.CreateSaveRequest();

        Assert.AreEqual(1, request.CreateItems.Count);
        Assert.AreEqual("B-1", request.CreateItems[0].Data.BomNo);
        Assert.AreEqual(1, request.DeleteItems.Count);
        Assert.AreEqual(persistedId, request.DeleteItems[0].Uid);

        list.AcceptChanges();
        Assert.AreEqual(1, list.Items.Count);
        Assert.IsFalse(list.HasChanges);
    }

    [TestMethod]
    public void SystemAndUiProperties_ShouldNotCreateDirtyState()
    {
        var list = new ProductListModel();
        list.LoadFromDtos([new ProductDto { Uid = Guid.NewGuid(), Version = 1, Sku = "P-1" }]);
        var line = list.Items.Single();

        line.Uid = Guid.NewGuid();
        line.Version = 9;
        line.CreatedDate = DateTime.UtcNow.AddDays(-2);
        line.CreatedBy = "loader";
        line.UpdatedDate = DateTime.UtcNow;
        line.UpdatedBy = "server";
        line.IsChecked = true;
        line.IsSelected = true;
        line.IsReadOnly = true;
        list.Reindex();

        Assert.AreEqual(LineState.Unchanged, line.State);
        Assert.IsFalse(list.HasChanges);
    }

    [TestMethod]
    public void SuppressChangeTracking_ShouldAdoptOnlyScopedPropertiesAndRemainNestedSafe()
    {
        var line = new ProductLineModel();
        line.Load(new ProductDto
        {
            Uid = Guid.NewGuid(),
            Sku = "P-1",
            DescriptionZh = "原说明",
            MaterialType = "旧物料",
            SupplyType = "旧供货",
        });
        line.DescriptionZh = "用户修改";

        using (line.SuppressChangeTracking())
        {
            line.MaterialType = "程序回填物料";
            using (line.SuppressChangeTracking())
            {
                line.SupplyType = "程序回填供货";
            }
        }

        Assert.AreEqual(LineState.Modified, line.State);
        line.RejectChanges();

        Assert.AreEqual("原说明", line.DescriptionZh);
        Assert.AreEqual("程序回填物料", line.MaterialType);
        Assert.AreEqual("程序回填供货", line.SupplyType);
        Assert.AreEqual(LineState.Unchanged, line.State);
    }

    [TestMethod]
    public void BaseListModel_ShouldMaterializeInputAndDetachOldLineEvents()
    {
        var list = new ProductListModel();
        list.LoadFromDtos(
        [
            new ProductDto { Uid = Guid.NewGuid(), Sku = "P-1" },
            new ProductDto { Uid = Guid.NewGuid(), Sku = "P-2" },
        ]);
        var oldLine = list.Items[0];

        var currentProjection = list.Items.Select(line => line.ToDto());
        list.LoadFromDtos(currentProjection);
        Assert.AreEqual(2, list.Items.Count);

        var eventCount = 0;
        list.ListStateChanged += (_, _) => eventCount++;
        oldLine.Sku = "DETACHED";
        Assert.AreEqual(0, eventCount);
    }

    [TestMethod]
    public void BaseListModel_ShouldExposeSnapshotsAndCoalesceBulkCheckEvents()
    {
        var list = new ProductListModel();
        list.LoadFromDtos(
        [
            new ProductDto { Uid = Guid.NewGuid(), Sku = "P-1" },
            new ProductDto { Uid = Guid.NewGuid(), Sku = "P-2" },
        ]);
        var eventCount = 0;
        list.ListStateChanged += (_, _) => eventCount++;

        using (list.SuppressListEvents())
        {
            list.CheckAll();
            using (list.SuppressListEvents())
            {
                list.InvertCheckAll();
                list.CheckAll();
            }
        }

        Assert.AreEqual(1, eventCount);
        Assert.AreEqual(2, list.CheckedItems.Count);
        Assert.IsTrue(list.IsAllChecked);

        var deleted = list.Items[0];
        list.Remove(deleted);
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual(2, list.AllLines.Count);
        Assert.AreEqual(1, list.DeletedItems.Count);
        Assert.AreSame(deleted, list.DeletedItems[0]);

        list.Clear();
        Assert.AreEqual(0, list.Items.Count);
        Assert.AreEqual(0, list.AllLines.Count);
    }

    [TestMethod]
    public void ListStateChanged_ShouldIncludeChangedLineAndPropertyName()
    {
        var list = new ProductListModel();
        list.LoadFromDtos([new ProductDto { Uid = Guid.NewGuid(), Sku = "P-1" }]);
        var line = list.Items.Single();
        var changes = new List<(ProductLineModel? Line, string? PropertyName)>();
        list.ListStateChanged += (_, e) => changes.Add((e.Line, e.PropertyName));

        line.Sku = "P-2";

        Assert.IsTrue(changes.Any(change => ReferenceEquals(change.Line, line)
                                            && change.PropertyName == nameof(ProductLineModel.Sku)));
        Assert.AreEqual(1, list.ModifiedItems.Count);
        Assert.AreSame(line, list.GetModifiedLines().Single());
    }
}
