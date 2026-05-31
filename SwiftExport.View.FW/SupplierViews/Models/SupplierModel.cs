using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.SupplierViews.Models
{
    /// <summary>
    /// 供应商行模型：继承 BaseLineModel，统一复用
    /// 1) 行状态机（Added/Modified/Deleted）
    /// 2) ChangeTracker
    /// 3) 验证体系
    /// </summary>
    public class SupplierModel : BaseLineModel
    {
        // ================= 业务数据区 =================

        private string _supplierCode;
        public string SupplierCode
        {
            get => _supplierCode;
            set => SetProperty(ref _supplierCode, value);
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _docRelateNo;
        public string DocRelateNo
        {
            get => _docRelateNo;
            set => SetProperty(ref _docRelateNo, value);
        }

        private string _contactPerson;
        public string ContactPerson
        {
            get => _contactPerson;
            set => SetProperty(ref _contactPerson, value);
        }

        private string _contactPhone;
        public string ContactPhone
        {
            get => _contactPhone;
            set => SetProperty(ref _contactPhone, value);
        }

        private string _contactEmail;
        public string ContactEmail
        {
            get => _contactEmail;
            set => SetProperty(ref _contactEmail, value);
        }

        private string _address;
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private decimal _zheKou;
        public decimal ZheKou
        {
            get => _zheKou;
            set => SetProperty(ref _zheKou, value);
        }

        private string _currency = "CNY";
        public string Currency
        {
            get => _currency;
            set => SetProperty(ref _currency, value);
        }

        private decimal _taxRate;
        public decimal TaxRate
        {
            get => _taxRate;
            set => SetProperty(ref _taxRate, value);
        }

        private int _defaultLeadTime;
        public int DefaultLeadTime
        {
            get => _defaultLeadTime;
            set => SetProperty(ref _defaultLeadTime, value);
        }

        private string _paymentTerms;
        public string PaymentTerms
        {
            get => _paymentTerms;
            set => SetProperty(ref _paymentTerms, value);
        }

        private string _zheKouBeiZhu;
        public string ZheKouBeiZhu
        {
            get => _zheKouBeiZhu;
            set => SetProperty(ref _zheKouBeiZhu, value);
        }

        // ================= 验证逻辑区 =================

        public override string ValidateRow()
        {
            if (string.IsNullOrWhiteSpace(SupplierCode)) return "供应商代码不能为空";
            if (string.IsNullOrWhiteSpace(Name)) return "供应商名称不能为空";
            if (string.IsNullOrWhiteSpace(DocRelateNo)) return "单据关联号不能为空";
            return null;
        }

        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            var source = dto as SupplierDto;
            if (source == null) return;

            using (DisabledStateScope())
            {
                Id = source.Id;
                SupplierCode = source.SupplierCode ?? string.Empty;
                Name = source.SupplierName ?? string.Empty;
                DocRelateNo = source.DocRelateNo ?? string.Empty;
                ContactPerson = source.ContactPerson ?? string.Empty;
                ContactPhone = source.ContactPhone ?? string.Empty;
                ContactEmail = source.ContactEmail ?? string.Empty;
                Address = source.Address ?? string.Empty;
                ZheKou = source.ZheKou ?? 0m;
                Currency = string.IsNullOrWhiteSpace(source.Currency) ? "CNY" : source.Currency;
                TaxRate = source.TaxRate ?? 0m;
                DefaultLeadTime = source.DefaultLeadTime ?? 0;
                PaymentTerms = source.PaymentTerms ?? string.Empty;
                ZheKouBeiZhu = source.ZheKouBeiZhu ?? string.Empty;
            }

            State = ModelState.Unchanged;
            if (!抑制清理脏数据)
                CommitChanges();
        }
    }
}