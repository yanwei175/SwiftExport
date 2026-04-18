using Mapster;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.Mvvm;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.CustomerViews.Models
{

    public class CustomerModel : BaseLineModel, IBaseModel, IDataErrorInfo
    {
        public override void LoadFromDto<CustomerDto>(CustomerDto dto, bool 抑制清理脏数据 = false)//加载数据方法
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {

                dto.Adapt(this);//使用 Mapster 将 DTO 的属性值映射到 Model(this) 上
                if (this.Id == 0)
                { this.MarkAsAdded(); }

                // 根据调用方需要决定是否立即提交/清理脏数据
                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        #region 字段属性

        private string _customerCode;
        public string CustomerCode
        {
            get => _customerCode;
            set => SetProperty(ref _customerCode, value);
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _address;
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private string _phoneNumber;
        public string PhoneNumber
        {
            get => _phoneNumber;
            set => SetProperty(ref _phoneNumber, value);
        }

        private string _contactPerson;
        public string ContactPerson
        {
            get => _contactPerson;
            set => SetProperty(ref _contactPerson, value);
        }

        private string _docRelateNo;
        public string DocRelateNo
        {
            get => _docRelateNo;
            set => SetProperty(ref _docRelateNo, value);
        }

        // ================= 文件夹路径部分 =================
        private string _labelFolderPath;
        public string LabelFolderPath
        {
            get => _labelFolderPath;
            set => SetProperty(ref _labelFolderPath, value);
        }

        private string _manualFolderPath;
        public string ManualFolderPath
        {
            get => _manualFolderPath;
            set => SetProperty(ref _manualFolderPath, value);
        }

        private string _drawingFolderPath;
        public string DrawingFolderPath
        {
            get => _drawingFolderPath;
            set => SetProperty(ref _drawingFolderPath, value);
        }

        private string _linedrawingFolderPath;
        public string LinedrawingFolderPath
        {
            get => _linedrawingFolderPath;
            set => SetProperty(ref _linedrawingFolderPath, value);
        }

        private string _barcodeFolderPath;
        public string BarcodeFolderPath
        {
            get => _barcodeFolderPath;
            set => SetProperty(ref _barcodeFolderPath, value);
        }

        private string _specSheetFolderPath;
        public string SpecSheetFolderPath
        {
            get => _specSheetFolderPath;
            set => SetProperty(ref _specSheetFolderPath, value);
        }
        #endregion

        // ============================================================
        // 验证（IDataErrorInfo）用于字段级验证，适合验证单个字段的有效性，例如：必填项、格式验证等。
        // ============================================================
        #region  字段级验证IDataErrorInfo Members

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

        public override string this[string columnName]
        {
            get
            {
                string result = null;

                if (columnName == nameof(CustomerCode))
                {
                    if (string.IsNullOrWhiteSpace(CustomerCode))
                        result = "客户编码不能为空";
                }
                return result;
            }
        }

        /// <summary>
        /// 行级验证（跨字段验证）
        /// 用于验证“多个字段组合是否有效”
        /// 例如：数量和单价必须同时有效、包装信息必须完整等。
        /// 保存时会自动调用 IsValid → ValidateRow。
        /// </summary>
        public override string ValidateRow()
        {
            //// 示例：数量和单价必须同时有效
            //if (数量 > 0 && 美金单价 > 0)
            return null;

            //return "数量和单价必须同时有效";
        }

        /// <summary>
        /// 异步验证（可选）
        /// 用于数据库验证，例如：SKU 是否存在、产品是否停产。
        /// 默认不做任何事，不会影响性能，也不会影响 UI。
        /// 保存时你可以手动调用：await line.ValidateAsync()
        /// </summary>
        public override async Task<string> ValidateAsync()
        {
            // 示例（伪代码）：
            // if (!await ProductService.ExistsAsync(物料编码))
            //     return "SKU 不存在于数据库";

            return null; // 默认无错误
        }

        #endregion

    }
}