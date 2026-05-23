using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.ProductViews.Models;
using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.Mappings
{
    /// <summary>
    /// ProductDto ↔ ProductModel 映射配置
    /// 使用 Mapster 进行双向映射，自动同步 ERP 产品主表数据
    /// </summary>
    public class ProductMapping : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            // DTO → Model（后端 API 返回数据 → UI 绑定）
            // 所有字段直接映射，保留原有类型（Model 中的 decimal 字段接收 DTO 中的 decimal 值）
            config.NewConfig<ProductDto, ProductModel>()
                // 基础标识字段
                .Map(dest => dest.Id, dto => dto.Id)
                .Map(dest => dest.Version, src => src.Version)
                .Map(dest => dest.IsChecked, dto => false)  // 初始化 UI 勾选状态
                .Map(dest => dest.State, dto => ModelState.Unchanged)  // 初始化状态为未修改
                // SKU 级别管理字段
                .Map(dest => dest.MasterSku, dto => dto.MasterSku)
                .Map(dest => dest.Sku, dto => dto.Sku)
                .Map(dest => dest.DescriptionEn, dto => dto.DescriptionEn)
                .Map(dest => dest.FeaturesEn, dto => dto.FeaturesEn)
                .Map(dest => dest.Finish, dto => dto.Finish)
                .Map(dest => dest.SupplyType, dto => dto.SupplyType)
                .Map(dest => dest.BomVersion, dto => dto.BomVersion)
                .Map(dest => dest.BomNo, dto => dto.BomNo)
                .Map(dest => dest.Note, dto => dto.Note)
                .Map(dest => dest.LinkSku, dto => dto.LinkSku)
                // 价格字段（与新增的 CNYPrice/USDPrice 对应）
                .Map(dest => dest.CNYPrice, dto => dto.CNYPrice)
                .Map(dest => dest.USDPrice, dto => dto.USDPrice)
                // 装箱基础数据
                .Map(dest => dest.PcsOfCarton, dto => dto.PcsOfCarton)
                .Map(dest => dest.NwOfCarton, dto => dto.NwOfCarton)
                .Map(dest => dest.GwOfCarton, dto => dto.GwOfCarton)
                .Map(dest => dest.CbmOfCarton, dto => dto.CbmOfCarton)
                // 聚合信息（后端 SQL Join 填充，UI 直接显示）
                .Map(dest => dest.CategoryId, dto => dto.CategoryId)
                .Map(dest => dest.CategoryName, dto => dto.CategoryName)
                .Map(dest => dest.ColorId, dto => dto.ColorId)
                .Map(dest => dest.SupplierName, dto => dto.SupplierName)
                // 旧系统兼容字段
                .Map(dest => dest.CustomerCode, dto => dto.CustomerCode)
                .Map(dest => dest.CSku, dto => dto.CSku)
                .Map(dest => dest.SupplierCode, dto => dto.SupplierCode)
                .Map(dest => dest.SSku, dto => dto.SSku)
                // 图片
                .Map(dest => dest.MainImage, dto => dto.MainImage)
                .TwoWays();
        }
    }
}
