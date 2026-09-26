using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Customers;
using SwiftExport.Application.Services.Customers.PoConfigs;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Entities.Relations;

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
        foreach (var value in new decimal?[] { null, 0m, 1m, 0.95m, 1.05m, 12.5m, 0.123456m, 0.9500000m })
            Assert.IsNull(CustomerPoConfigService.ValidateFieldLengths(ValidDto(value)), $"应接受 {value?.ToString() ?? "NULL"}");

        StringAssert.Contains(CustomerPoConfigService.ValidateFieldLengths(ValidDto(-0.000001m)), "不能小于 0");
        StringAssert.Contains(CustomerPoConfigService.ValidateFieldLengths(ValidDto(0.1234567m)), "6 位小数");
    }

    /// <summary>显式分页和按客户供应商查询必须返回新增字段，币种删除保护也必须识别配置引用。</summary>
    [TestMethod]
    public void SqlContracts_ShouldProjectFieldsAndProtectCurrencyReference()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Customers", "PoConfigs", "CustomerPoConfigRepository.cs");
        Assert.IsTrue(Count(repository, "cpc.Currency, cpc.DefaultDiscountRate, cpc.DiscountRemark") >= 2);
        StringAssert.Contains(repository, "@CustomerId IS NOT NULL AND Uid=@CustomerId");
        StringAssert.Contains(repository, "@SupplierId IS NOT NULL AND Uid=@SupplierId");
        StringAssert.Contains(repository, "entity.CustomerCode = ids.CustomerCode");
        var currencyRepository = Read("SwiftExport.Infrastructure", "Repositories", "Systems", "Currencies", "SysDicCurrencyRepository.cs");
        StringAssert.Contains(currencyRepository, "SELECT Currency FROM dbo.CustomerPoConfig");
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

    /// <summary>实体生成基线必须声明字段精度、检查约束和零数据库外键边界。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldDeclareExpectedNullableColumnsAndCheckConstraint()
    {
        var sql = Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql");
        StringAssert.Contains(sql, "[Currency] nvarchar(10) NULL");
        StringAssert.Contains(sql, "[DefaultDiscountRate] decimal(9,6) NULL");
        StringAssert.Contains(sql, "[DiscountRemark] nvarchar(500) NULL");
        StringAssert.Contains(sql, "CK_CustomerPoConfig_DefaultDiscountRate");
        StringAssert.Contains(sql, "[DefaultDiscountRate] >= 0");
        Assert.IsFalse(sql.Contains("DefaultDiscountRate <= 1", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase));
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
