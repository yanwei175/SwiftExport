using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证唯一 PG 映射的物理名称、CLR 参数、显式声明及继承字段。</summary>
[TestClass]
public sealed class EntityMappingTests
{
    /// <summary>采购数量使用既有 PG quantity 列，参数继续绑定 CLR Quantity 属性。</summary>
    [TestMethod]
    public void PurchaseQuantity_UsesPhysicalColumn()
    {
        var metadata = EntityMetadataCache.GetMetadata<PurchaseOrderLine>();
        var columns = metadata.Columns.Select(column => column.ColumnName).ToArray();
        CollectionAssert.Contains(columns, "quantity");
        CollectionAssert.DoesNotContain(columns, "QTY");
        Assert.AreEqual("Quantity", metadata.GetProperty("Quantity").ParameterName);
    }

    /// <summary>基类字段保留且忽略非持久化字段，表名继续尊重显式声明。</summary>
    [TestMethod]
    public void InheritanceAndIgnoredProperties_UseOneMapping()
    {
        var metadata = EntityMetadataCache.GetMetadata<MappingExample>();
        var columns = metadata.Columns.Select(column => column.ColumnName).ToArray();
        Assert.AreEqual("mapping_example", metadata.TableName);
        Assert.AreEqual("custom", metadata.SchemaName);
        CollectionAssert.Contains(columns, "uid");
        CollectionAssert.Contains(columns, "stored_value");
        CollectionAssert.DoesNotContain(columns, "display");
    }

    /// <summary>测试映射例外，不参与生产实体扫描。</summary>
    [Table("mapping_example", Schema = "custom")]
    private sealed class MappingExample : BaseEntity
    {
        [Column("stored_value")]
        public string Value { get; set; } = "";
        [NotMapped]
        public string Display { get; set; } = "";
    }
}
