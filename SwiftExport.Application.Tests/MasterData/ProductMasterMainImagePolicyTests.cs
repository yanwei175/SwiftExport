using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products;
using SwiftExport.Application.Services.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证主型号详情保存后的默认主图唯一性策略。</summary>
[TestClass]
public sealed class ProductMasterMainImagePolicyTests
{
    /// <summary>验证保留旧默认并新增另一个默认时会被判定为冲突。</summary>
    [TestMethod]
    public void ExistingAndNewMainImages_ShouldConflict()
    {
        var existing = DetailWithMainImage(out _);
        var request = new SaveProductMasterDetailRequest
        {
            CreateNeutrals =
            {
                new CreateItemRequest<ProductNeutralWriteDto>
                {
                    Data = new ProductNeutralWriteDto { ProductCode = "N-NEW", IsMainImage = true }
                }
            }
        };

        Assert.IsTrue(ProductMasterMainImagePolicy.HasConflict(existing, request));
    }

    /// <summary>验证切换默认或删除旧默认后新增默认均保持唯一。</summary>
    [TestMethod]
    public void ReplacingOrDeletingMainImage_ShouldRemainValid()
    {
        var existing = DetailWithMainImage(out var existingId);
        var replacementId = Guid.NewGuid();
        existing.Neutrals.Add(new ProductNeutralDto
        {
            Uid = replacementId,
            Data = new ProductNeutralWriteDto { ProductCode = "N-REPLACEMENT" }
        });
        var replaceRequest = new SaveProductMasterDetailRequest
        {
            UpdateNeutrals =
            {
                new UpdateItemRequest<ProductNeutralWriteDto>
                {
                    Uid = existingId,
                    Data = new ProductNeutralWriteDto { ProductCode = "N-OLD", IsMainImage = false }
                },
                new UpdateItemRequest<ProductNeutralWriteDto>
                {
                    Uid = replacementId,
                    Data = new ProductNeutralWriteDto { ProductCode = "N-REPLACEMENT", IsMainImage = true }
                }
            }
        };
        Assert.IsFalse(ProductMasterMainImagePolicy.HasConflict(existing, replaceRequest));

        var deleteAndCreateRequest = new SaveProductMasterDetailRequest
        {
            DeleteNeutrals = { new DeleteItemRequest { Uid = existingId } },
            CreateNeutrals =
            {
                new CreateItemRequest<ProductNeutralWriteDto>
                {
                    Data = new ProductNeutralWriteDto { ProductCode = "N-NEW", IsMainImage = true }
                }
            }
        };
        Assert.IsFalse(ProductMasterMainImagePolicy.HasConflict(existing, deleteAndCreateRequest));
    }

    private static ProductMasterDetailDto DetailWithMainImage(out Guid neutralId)
    {
        neutralId = Guid.NewGuid();
        return new ProductMasterDetailDto
        {
            Neutrals =
            {
                new ProductNeutralDto
                {
                    Uid = neutralId,
                    Data = new ProductNeutralWriteDto { ProductCode = "N-OLD", IsMainImage = true }
                }
            }
        };
    }
}
