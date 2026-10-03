using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证物理名称与 CLR 名称分离，覆盖采购数量例外及继承字段。</summary>
[TestClass]
public sealed class EntityMappingTests
{
    /// <summary>采购数量必须使用现有 QTY 列，不能新增或改名物理字段。</summary>
    [TestMethod]
    public void PurchaseQuantity_UsesPhysicalColumn()
    {
        var metadata = EntityMetadataCache.GetMetadata<PurchaseOrderLine>();
        CollectionAssert.Contains(metadata.AllColumns, "QTY");
        CollectionAssert.DoesNotContain(metadata.AllColumns, "Quantity");
        Assert.AreEqual("Quantity", metadata.GetProperty("Quantity").ParameterName);
    }

    /// <summary>基类字段保留且忽略非持久化字段，表名继续尊重显式声明。</summary>
    [TestMethod]
    public void InheritanceAndIgnoredProperties_UseOneMapping()
    {
        var metadata = EntityMetadataCache.GetMetadata<MappingExample>();
        Assert.AreEqual("MappingExample", metadata.TableName);
        Assert.AreEqual("custom", metadata.SchemaName);
        CollectionAssert.Contains(metadata.AllColumns, "Uid");
        CollectionAssert.Contains(metadata.AllColumns, "stored_value");
        CollectionAssert.DoesNotContain(metadata.AllColumns, "Display");
    }

    /// <summary>测试映射例外，不参与生产实体扫描。</summary>
    [Table("MappingExample", Schema = "custom")]
    private sealed class MappingExample : BaseEntity
    {
        [Column("stored_value")]
        public string Value { get; set; } = "";
        [NotMapped]
        public string Display { get; set; } = "";
    }
}
