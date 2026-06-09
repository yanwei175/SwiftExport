using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.CustomerViews.Models
{
    public class CustomerPoConfigModel : BaseLineModel
    {
        #region  字段

        private string _customerCode;
        public string CustomerCode
        {
            get => _customerCode;
            set => SetProperty(ref _customerCode, value);
        }

        private string _supplierCode;
        public string SupplierCode
        {
            get => _supplierCode;
            set => SetProperty(ref _supplierCode, value);
        }

        private string _inletHoseType;
        public string InletHoseType
        {
            get => _inletHoseType;
            set => SetProperty(ref _inletHoseType, value);
        }

        private string _aeratorType;
        public string AeratorType
        {
            get => _aeratorType;
            set => SetProperty(ref _aeratorType, value);
        }

        private string _palletType;
        public string PalletType
        {
            get => _palletType;
            set => SetProperty(ref _palletType, value);
        }

        private string _boxType;
        public string BoxType
        {
            get => _boxType;
            set => SetProperty(ref _boxType, value);
        }

        private string _labelType;
        public string LabelType
        {
            get => _labelType;
            set => SetProperty(ref _labelType, value);
        }

        private string _imType;
        public string IMType
        {
            get => _imType;
            set => SetProperty(ref _imType, value);
        }

        private string _cartonType;
        public string CartonType
        {
            get => _cartonType;
            set => SetProperty(ref _cartonType, value);
        }

        private string _logoType;
        public string LogoType
        {
            get => _logoType;
            set => SetProperty(ref _logoType, value);
        }

        private string _salesPerson;
        public string SalesPerson
        {
            get => _salesPerson;
            set => SetProperty(ref _salesPerson, value);
        }

        private string _purchaser;
        public string Purchaser
        {
            get => _purchaser;
            set => SetProperty(ref _purchaser, value);
        }

        private string _note;
        public string Note
        {
            get => _note;
            set => SetProperty(ref _note, value);
        }

        #endregion 
    }
}
