using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;

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
}
