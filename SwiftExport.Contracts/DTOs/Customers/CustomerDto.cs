using SwiftExport.Contracts.DTOs.Base;


namespace SwiftExport.Contracts.DTOs.Customers
{
    public class CustomerDto : BaseDto
    {
        public string CustomerCode { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string PhoneNumber { get; set; }
        public string ContactPerson { get; set; }
        public string LabelFolderPath { get; set; }
        public string ManualFolderPath { get; set; }
        public string DrawingFolderPath { get; set; }
        public string LinedrawingFolderPath { get; set; }
        public string BarcodeFolderPath { get; set; }
        public string SpecSheetFolderPath { get; set; }
        public string DocRelateNo { get; set; }
    }
}