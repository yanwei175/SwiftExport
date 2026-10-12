namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>在新缓存中无法匹配的已选身份；只有明确移除操作才能从最终选择中删除。</summary>
public sealed class ProductMasterMissingTagModel
{
    /// <summary>初始化失效选择的身份与可识别名称。</summary>
    public ProductMasterMissingTagModel(Guid optionId, string displayText)
    {
        OptionId = optionId;
        DisplayText = displayText;
    }

    /// <summary>获取失效选项身份。</summary>
    public Guid OptionId { get; }
    /// <summary>获取保留的组名、值名或身份文本。</summary>
    public string DisplayText { get; }
}
