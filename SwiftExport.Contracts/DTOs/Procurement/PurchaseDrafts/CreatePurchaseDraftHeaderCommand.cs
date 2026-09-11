namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>新建采购草稿时由采购员确认的单头可编辑快照。</summary>
    public class CreatePurchaseDraftHeaderCommand
    {
        public string InletHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string InstructionManualType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }
        public string SalesPerson { get; set; }
        public string PurchasePerson { get; set; }
        public string Remark { get; set; }
    }
}
