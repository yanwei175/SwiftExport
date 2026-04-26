using SwiftExport.Contracts.SysEnums;
using SwiftExport.View.FW.Mvvm;
using System.ComponentModel;

namespace SwiftExport.View.FW.ProductViews.Models
{
    public class BomItemModel : BaseLineModel
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
            set => SetProperty(ref _subSku, value);
        }

        //定义为可空类型，以便在验证时区分未设置和设置为0的情况
        private int? _numerator = 1;
        public int? Numerator
        {
            get { return _numerator; }
            set => SetProperty(ref _numerator, value);
        }

        //定义为可空类型，以便在验证时区分未设置和设置为0的情况
        private int? _denominator = 1;
        public int? Denominator
        {
            get { return _denominator; }
            set => SetProperty(ref _denominator, value);
        }


        private string _note = string.Empty;
        public string Note
        {
            get { return _note; }
            set => SetProperty(ref _note, value);
        }


        private string _description = string.Empty;
        public string Description
        {
            get { return _description; }
            set => SetProperty(ref _description, value);
        }

        private string _finish = string.Empty;
        public string Finish
        {
            get { return _finish; }
            set => SetProperty(ref _finish, value);
        }

        private string _features = string.Empty;
        public string Features
        {
            get { return _features; }
            set => SetProperty(ref _features, value);
        }

        private string _mainImage = string.Empty;
        public string MainImage
        {
            get { return _mainImage; }
            set => SetProperty(ref _mainImage, value);
        }






        #region IDataErrorInfo Members

        public override string Error => null;

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
