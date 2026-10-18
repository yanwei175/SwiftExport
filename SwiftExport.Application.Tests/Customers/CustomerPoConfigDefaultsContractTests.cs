using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Customers;
using SwiftExport.Application.Services.Customers.PoConfigs;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers.PoConfigs;

namespace SwiftExport.Application.Tests.Customers;

/// <summary>验证客户采购默认配置新增字段的模型、校验和显式查询投影契约。</summary>
[TestClass]
public sealed class CustomerPoConfigDefaultsContractTests
{
    /// <summary>实体与共享 DTO 应公开相同的默认币种和折扣字段类型。</summary>
    [TestMethod]
    public void EntityAndDto_ShouldExposeDefaultCurrencyAndDiscountFields()
    {
        Assert.AreEqual(typeof(string), typeof(CustomerPoConfig).GetProperty(nameof(CustomerPoConfig.Currency))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(CustomerPoConfig).GetProperty(nameof(CustomerPoConfig.DefaultDiscountRate))!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(CustomerPoConfig).GetProperty(nameof(CustomerPoConfig.DiscountRemark))!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(CustomerPoConfigDto).GetProperty(nameof(CustomerPoConfigDto.Currency))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(CustomerPoConfigDto).GetProperty(nameof(CustomerPoConfigDto.DefaultDiscountRate))!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(CustomerPoConfigDto).GetProperty(nameof(CustomerPoConfigDto.DiscountRemark))!.PropertyType);
    }

    /// <summary>折扣率应允许涨价倍率，并由统一服务校验非负值和六位小数精度。</summary>
    [TestMethod]
    public void Validation_ShouldAcceptBoundariesAndRejectInvalidRates()
    {
        foreach (var value in new decimal?[] { null, 0m, 1m, 0.95m, 1.05m, 12.5m, 999.999999m, 0.123456m, 0.9500000m })
            Assert.IsNull(CustomerPoConfigService.ValidateFieldLengths(ValidDto(value)), $"应接受 {value?.ToString() ?? "NULL"}");

        StringAssert.Contains(CustomerPoConfigService.ValidateFieldLengths(ValidDto(-0.000001m)), "不能小于 0");
        StringAssert.Contains(CustomerPoConfigService.ValidateFieldLengths(ValidDto(0.1234567m)), "6 位小数");
        StringAssert.Contains(CustomerPoConfigService.ValidateFieldLengths(ValidDto(1000m)), "999.999999");
    }

    /// <summary>显式分页和按客户供应商查询必须返回新增字段，币种删除保护也必须识别配置引用。</summary>
    [TestMethod]
    public void SqlContracts_ShouldProjectFieldsAndProtectCurrencyReference()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Customers", "PoConfigs", "PostgreSqlCustomerPoConfigRepository.cs");
        var columns = (string)typeof(PostgreSqlCustomerPoConfigRepository)
            .GetField("Columns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        foreach (var pair in new[] { ("currency", "Currency"), ("default_discount_rate", "DefaultDiscountRate"), ("discount_remark", "DiscountRemark") })
            StringAssert.Contains(columns, $"cpc.\"{pair.Item1}\" AS \"{pair.Item2}\"");
        Assert.IsTrue(Count(repository, "SELECT {Columns} FROM public.customer_po_config cpc") >= 2);
        StringAssert.Contains(repository, "CAST(@CustomerId AS uuid) IS NOT NULL AND uid=CAST(@CustomerId AS uuid)");
        StringAssert.Contains(repository, "CAST(@SupplierId AS uuid) IS NOT NULL AND uid=CAST(@SupplierId AS uuid)");
        StringAssert.Contains(repository, "entity.CustomerCode = result.CustomerCode");
        var currencyRepository = Read("SwiftExport.Infrastructure", "Repositories", "Systems", "Currencies", "PostgreSqlSysDicCurrencyRepository.cs");
        StringAssert.Contains(currencyRepository, "SELECT \"currency\" AS code FROM \"public\".\"customer_po_config\"");
        StringAssert.Contains(currencyRepository, "reference_2 AS MATERIALIZED");
        StringAssert.Contains(currencyRepository, "SELECT code FROM reference_2");
        StringAssert.Contains(currencyRepository, "AND (\"is_deleted\"=FALSE OR \"is_deleted\" IS NULL){lockHint}");
        var metadata = EntityMetadataCache.GetMetadata(typeof(CustomerPoConfig));
        Assert.AreEqual("public", metadata.SchemaName);
        Assert.AreEqual("customer_po_config", metadata.TableName);
        Assert.AreEqual("currency", metadata.GetProperty("Currency").ColumnName);
    }

    /// <summary>币种与折扣备注长度必须匹配数据库，备注与折扣率可分别配置。</summary>
    [TestMethod]
    public void Validation_ShouldEnforceLengthsWithoutCouplingRemarkAndRate()
    {
        Assert.IsNull(CustomerPoConfigService.ValidateFieldLengths(ValidDto(null, new string('币', 10), new string('注', 500))));
        StringAssert.Contains(CustomerPoConfigService.ValidateFieldLengths(ValidDto(null, new string('币', 11))), "10 个字符");
        StringAssert.Contains(CustomerPoConfigService.ValidateFieldLengths(ValidDto(null, "CNY", new string('注', 501))), "500 个字符");
        Assert.IsNull(CustomerPoConfigService.ValidateFieldLengths(ValidDto(0.95m, "CNY", null)));
    }

    /// <summary>PG 实体生成基线必须保持字段可空、长度精度、非负检查及零数据库外键边界，不限制涨价倍率。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldDeclareExpectedNullableColumnsAndCheckConstraint()
    {
        var sql = Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.PostgreSql.Schema.preview.sql");
        StringAssert.Contains(sql, "\"currency\" varchar(10) NULL");
        StringAssert.Contains(sql, "\"default_discount_rate\" numeric(9,6) NULL");
        StringAssert.Contains(sql, "\"discount_remark\" varchar(500) NULL");
        StringAssert.Contains(sql, "ck_customer_po_config_default_discount_rate");
        StringAssert.Contains(sql, "\"default_discount_rate\" >= 0");
        Assert.IsFalse(sql.Contains("\"default_discount_rate\" <= 1", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(sql, "CREATE UNIQUE INDEX \"ux_customer_po_config_parties\" ON \"public\".\"customer_po_config\" (\"customer_id\", \"supplier_id\") WHERE \"is_deleted\" = FALSE;");
        StringAssert.Contains(sql, "ck_customer_po_config_parties");
        Assert.IsFalse(sql.Contains("ux_customer_po_config_codes", StringComparison.Ordinal));
    }

    private static CustomerPoConfigDto ValidDto(decimal? rate, string currency = "CNY", string? remark = null) => new()
    {
        CustomerCode = "C001",
        SupplierCode = "S001",
        Currency = currency,
        DefaultDiscountRate = rate,
        DiscountRemark = remark
    };

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("无法定位 SwiftExport 仓库根目录。");
    }
}
