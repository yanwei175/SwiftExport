using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.QualityControl;

namespace SwiftExport.Application.Services.QualityControl;

/// <summary>售后数量、确认和字段锁定纯规则；不读写数据库。</summary>
public static class AfterSalesPolicy
{
    /// <summary>验证行基本事实；空数量可保留为投诉，正数量确认时要求完整实物内容。</summary>
    public static void ValidateLine(AfterSalesLine line, bool confirming)
    {
        void Reject(string field,string message) => throw new AfterSalesException("VALIDATION",message,$"Lines[{line.Uid}].{field}");
        if (string.IsNullOrWhiteSpace(line.IssueDescription)) Reject("IssueDescription","必须填写投诉描述。");
        if (line.Quantity < 0 || line.Quantity > 99999999999999.9999m || decimal.Round(line.Quantity ?? 0,4) != (line.Quantity ?? 0)) Reject("Quantity","数量必须非负，整数最多十四位、小数最多四位。");
        if (line.ServiceAmountCny < 0 || line.ServiceAmountCny > 9999999999999999.99m || decimal.Round(line.ServiceAmountCny ?? 0,2) != (line.ServiceAmountCny ?? 0)) Reject("ServiceAmountCny","人民币金额必须非负，整数最多十六位、小数最多两位。");
        if (line.IsNonStandard && line.ProductId.HasValue) Reject("ProductId","非标不能绑定产品。");
        if (!confirming || !(line.Quantity > 0)) return;
        // 来源需求现有描述和规格列上限为300；保留投诉事实长度，交接前明确拒绝超长输入。
        if(line.DescriptionSnapshot?.Length>300) Reject("DescriptionSnapshot","来源需求的补发描述最多300字。");
        if(line.SpecificationSnapshot?.Length>300) Reject("SpecificationSnapshot","来源需求的补发规格最多300字。");
        // 来源需求现有描述和规格列上限为300；保留投诉事实长度，交接前明确拒绝超长输入。
        if(line.DescriptionSnapshot?.Length>300) Reject("DescriptionSnapshot","来源需求的补发描述最多300字。");
        if(line.SpecificationSnapshot?.Length>300) Reject("SpecificationSnapshot","来源需求的补发规格最多300字。");
        if (!line.ResponsibleSupplierId.HasValue) Reject("ResponsibleSupplierId","正数量行必须选择责任供应商。");
        if (!line.ServiceAmountCny.HasValue) Reject("ServiceAmountCny","正数量行必须确定人民币金额，免费请填0。");
        if (line.IsNonStandard)
        {
            if (string.IsNullOrWhiteSpace(line.DescriptionSnapshot)) Reject("DescriptionSnapshot","非标必须填写补发描述。");
            if (line.SkuSnapshot != NonStandardSkuGenerator.Create(line.Uid)) Reject("SkuSnapshot","非标SKU身份不一致。");
        }
        else if (!line.ProductId.HasValue) Reject("ProductId","标准补发必须选择产品。");
        if (string.IsNullOrWhiteSpace(line.SkuSnapshot)) Reject("SkuSnapshot","补发SKU不能为空。");
        if (string.IsNullOrWhiteSpace(line.UnitSnapshot)) Reject("UnitSnapshot","补发单位不能为空。");
    }

    /// <summary>比较已确认行的不可变事实，仅允许文字补充和退回收到日期变化。</summary>
    public static bool LockedFactsEqual(AfterSalesEditLineDto left, AfterSalesEditLineDto right)
    {
        // 排除明确允许编辑的补充字段，不通过手写长字段复制遗漏新加入的事实字段。
        var allowed = new HashSet<string> { "Version","IssueDescription","CauseAnalysis","TreatmentDescription","Remark","ReturnedReceivedDate","CustomerComplaintNo" };
        return typeof(AfterSalesEditLineDto).GetProperties().Where(p=>!allowed.Contains(p.Name))
            .All(p=>Equals(p.GetValue(left),p.GetValue(right)));
    }

    /// <summary>提供状态准入；下游资格仍在执行命令时最终复核。</summary>
    public static AfterSalesCapabilitiesDto Capabilities(AfterSalesHeader head, bool hasPositive)
    {
        var open=head.DocumentStatus==DocumentStatusEnum.Open;
        var confirmed=head.ConfirmationStatus==ConfirmationStatusEnum.已确认;
        return new() {CanSave=open,CanEditFacts=open&&!confirmed,CanConfirm=open&&!confirmed&&hasPositive,
            CanCancelConfirmation=open&&confirmed,CanClose=open&&(!hasPositive||confirmed),
            CanReopen=head.DocumentStatus==DocumentStatusEnum.Closed,CanVoid=open&&!confirmed};
    }
}
