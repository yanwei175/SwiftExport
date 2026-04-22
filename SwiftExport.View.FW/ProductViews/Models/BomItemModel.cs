using SwiftExport.Contracts.SysEnums;
using SwiftExport.View.FW.Mvvm;
using System.ComponentModel;

namespace SwiftExport.View.FW.ProductViews.Models
{
    public class BomItemModel : BaseLineModel, IBaseModel, IDataErrorInfo
    {
        public override void LoadFromDto<BomDto>(BomDto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;
            using (程序内部修改Scope())
            {
                Mapster.TypeAdapter.Adapt(dto, this);
                if (this.Id == 0)
                { this.MarkAsAdded(); }

                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        //BomNo不能修改
        public string BomNo { get; set; }

        //子件SubSku
        private string _subSku = string.Empty;
        public string SubSku
        {
            get { return _subSku; }
            set { if (SetProperty(ref _subSku, value)) MarkAsModified(); }
        }

        private string _description = string.Empty;
        public string Description
        {
            get { return _description; }
            set { if (SetProperty(ref _description, value)) MarkAsModified(); }
        }

        //定义为可空类型，以便在验证时区分未设置和设置为0的情况
        private int? _numerator = 1;
        public int? Numerator
        {
            get { return _numerator; }
            set { if (SetProperty(ref _numerator, value)) MarkAsModified(); }
        }

        //定义为可空类型，以便在验证时区分未设置和设置为0的情况
        private int? _denominator = 1;
        public int? Denominator
        {
            get { return _denominator; }
            set { if (SetProperty(ref _denominator, value)) MarkAsModified(); }
        }

        private string _bomSubType = string.Empty;
        public string BomSubType
        {
            get { return _bomSubType; }
            set { if (SetProperty(ref _bomSubType, value)) MarkAsModified(); }
        }

        private string _mainImage = string.Empty;
        public string MainImage
        {
            get { return _mainImage; }
            set { if (SetProperty(ref _mainImage, value)) MarkAsModified(); }
        }

        private string _note = string.Empty;
        public string Note
        {
            get { return _note; }
            set { if (SetProperty(ref _note, value)) MarkAsModified(); }
        }


        protected override void MarkAsModified()
        {
            if (State == ModelState.Unchanged)
            {
                State = ModelState.Modified;
            }
        }

        public override void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            base.RaisePropertyChanged(propertyName);

            if (propertyName != "Item[]" && propertyName != nameof(State) && propertyName != nameof(IsChecked))
            {
                base.RaisePropertyChanged("Item[]");
            }
        }

        #region IDataErrorInfo Members

        public override string Error
        {
            get
            {
                if (!string.IsNullOrEmpty(this[nameof(SubSku)])) return this[nameof(SubSku)];
                if (!string.IsNullOrEmpty(this[nameof(BomSubType)])) return this[nameof(BomSubType)];
                if (!string.IsNullOrEmpty(this[nameof(Numerator)])) return this[nameof(Numerator)];
                if (!string.IsNullOrEmpty(this[nameof(Denominator)])) return this[nameof(Denominator)];
                return null;
            }
        }

        public override string this[string columnName]
        {
            get
            {
                string result = null;

                if (columnName == nameof(SubSku))
                {
                    if (string.IsNullOrWhiteSpace(SubSku))
                        result = "子件型号不能为空";
                }
                else if (columnName == nameof(BomSubType))
                {
                    if (string.IsNullOrWhiteSpace(BomSubType))
                        result = "子件类型不能为空";
                }
                else if (columnName == nameof(Numerator))
                {
                    if (Numerator == null || Numerator <= 0)
                        result = "用量分子必须大于0";
                }
                else if (columnName == nameof(Denominator))
                {
                    if (Denominator == null || Denominator <= 0)
                        result = "用量分母必须大于0";
                }

                return result;
            }
        }

        #endregion


    }
}
