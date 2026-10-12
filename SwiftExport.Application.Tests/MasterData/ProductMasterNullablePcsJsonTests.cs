using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证后端 Newtonsoft.Json 与客户端默认 JSON 序列化都保留父级装率空值及整数。</summary>
[TestClass]
public sealed class ProductMasterNullablePcsJsonTests
{
    /// <summary>未提供字段或显式 null 均表示未设置，整数输入在两端保持一致。</summary>
    [TestMethod]
    [DataRow("{}", null)]
    [DataRow("{\"DefaultPcsPerPackage\":null}", null)]
    [DataRow("{\"DefaultPcsPerPackage\":1}", 1)]
    [DataRow("{\"DefaultPcsPerPackage\":12}", 12)]
    public void JsonInput_ShouldPreserveUnsetAndIntegerValues(string json, int? pcs)
    {
        Assert.AreEqual(pcs, JsonConvert.DeserializeObject<ProductMasterWriteDto>(json)!.DefaultPcsPerPackage);
        Assert.AreEqual(pcs, System.Text.Json.JsonSerializer.Deserialize<ProductMasterWriteDto>(json)!.DefaultPcsPerPackage);
    }

    /// <summary>序列化及反序列化后，空装率不能变为 1。</summary>
    [TestMethod]
    public void JsonRoundTrip_ShouldPreserveNull()
    {
        var dto = new ProductMasterWriteDto { MasterSku = "MASTER", DefaultPcsPerPackage = null };
        var serverJson = JsonConvert.SerializeObject(dto);
        var clientJson = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.IsNull(JsonConvert.DeserializeObject<ProductMasterWriteDto>(clientJson)!.DefaultPcsPerPackage);
        Assert.IsNull(System.Text.Json.JsonSerializer.Deserialize<ProductMasterWriteDto>(serverJson)!.DefaultPcsPerPackage);
    }

    /// <summary>小数不是装率契约允许的整数，两个 JSON 边界都应拒绝该输入。</summary>
    [TestMethod]
    public void FractionalJson_ShouldRejectInvalidIntegerInput()
    {
        const string json = "{\"DefaultPcsPerPackage\":3.5}";
        Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => JsonConvert.DeserializeObject<ProductMasterWriteDto>(json));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<ProductMasterWriteDto>(json));
    }
}
