using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class BomApiContractTests
{
    [TestMethod]
    public void BatchSave_ShouldMatchExistingControllerRoute()
    {
        var method = typeof(IBomApi).GetMethod(nameof(IBomApi.BatchSaveAsync));
        var post = method?.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().SingleOrDefault();

        Assert.IsNotNull(method);
        Assert.IsNotNull(post);
        Assert.AreEqual("/batch-save", post.Path);
        Assert.IsNotNull(method!.GetParameters()[0].GetCustomAttributes(typeof(BodyAttribute), false).SingleOrDefault());
    }

    /// <summary>Lookup 请求体及可取消读取契约保持轻量，不携带产品写入资料。</summary>
    [TestMethod]
    public void ProductLookup_ShouldUseDedicatedPostBodyContract()
    {
        var method = typeof(IProductApi).GetMethod(nameof(IProductApi.SearchLookupAsync));
        var post = method?.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().SingleOrDefault();

        Assert.IsNotNull(method);
        Assert.IsNotNull(post);
        Assert.AreEqual("/lookup", post.Path);
        Assert.AreEqual(typeof(ProductLookupRequest), method!.GetParameters()[0].ParameterType);
        Assert.AreEqual(typeof(CancellationToken), method.GetParameters()[1].ParameterType);
        Assert.IsNotNull(method.GetParameters()[0].GetCustomAttributes(typeof(BodyAttribute), false).SingleOrDefault());
        Assert.AreEqual(20, new ProductLookupRequest().Limit);

        var requestProperties = typeof(ProductLookupRequest).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { nameof(ProductLookupRequest.Keyword), nameof(ProductLookupRequest.ExcludeProductId), nameof(ProductLookupRequest.Limit) },
            requestProperties);
        var dtoProperties = typeof(ProductLookupDto).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(ProductLookupDto.Uid),
                nameof(ProductLookupDto.Sku),
                nameof(ProductLookupDto.DescriptionEn),
                nameof(ProductLookupDto.DescriptionZh),
                nameof(ProductLookupDto.LinkSku),
                nameof(ProductLookupDto.CategoryName),
                nameof(ProductLookupDto.MainImage),
                nameof(ProductLookupDto.Finish),
                nameof(ProductLookupDto.FeaturesEn),
                nameof(ProductLookupDto.FeaturesZh),
                nameof(ProductLookupDto.MaterialType),
                nameof(ProductLookupDto.Unit),
                nameof(ProductLookupDto.USDPrice),
                nameof(ProductLookupDto.CNYPrice),
            },
            dtoProperties);
    }
}
