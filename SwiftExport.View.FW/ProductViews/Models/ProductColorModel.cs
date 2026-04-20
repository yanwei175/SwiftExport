using Mapster;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.View.FW.Mvvm;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.ProductViews.Models
{

    public class ProductColorModel : BaseLineModel, IDataErrorInfo
    {
        /// <summary>
        /// 从 DTO 加载数据到 Model
        /// </summary>
        public override void LoadFromDto<ProductColorModel>(ProductColorModel dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {
                dto.Adapt(this);  // 使用 Mapster 映射 DTO → Model
                if (this.Id == 0)
                {
                    this.MarkAsAdded();
                }

                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        #region 字段属性
        private string _colorCodeUnique;
        public string ColorCodeUnique
        {
            get => _colorCodeUnique;
            set
            {
                if (_colorCodeUnique != value)
                {
                    _colorCodeUnique = value;
                    MarkAsModified();
                    RaisePropertyChanged(nameof(ColorCodeUnique));
                }
            }
        }

        private string _colorCode;
        public string ColorCode
        {
            get => _colorCode;
            set { if (_colorCode != value) { _colorCode = value; MarkAsModified(); RaisePropertyChanged(nameof(ColorCode)); } }
        }

        private string _colorNameEn;
        public string ColorNameEn
        {
            get => _colorNameEn;
            set { if (_colorNameEn != value) { _colorNameEn = value; MarkAsModified(); RaisePropertyChanged(nameof(ColorNameEn)); } }
        }

        private string _colorNameZh;
        public string ColorNameZh
        {
            get => _colorNameZh;
            set { if (_colorNameZh != value) { _colorNameZh = value; MarkAsModified(); RaisePropertyChanged(nameof(ColorNameZh)); } }
        }
        #endregion

        #region IDataErrorInfo Members

        /// <summary>
        /// 行级错误（返回 null 避免整行标红）
        /// </summary>
        public override string Error => null;

        /// <summary>
        /// 字段级验证
        /// </summary>
        public override string this[string columnName]
        {
            get
            {
                string result = null;

                if (columnName == nameof(ColorCodeUnique))
                {
                    if (string.IsNullOrWhiteSpace(ColorCodeUnique))
                        result = "颜色代码(唯一)不能为空";
                }

                if (columnName == nameof(ColorNameZh))
                {
                    if (string.IsNullOrWhiteSpace(ColorNameZh))
                        result = "颜色名称不能为空";
                }

                return result;
            }
        }

        #endregion

        #region 行级/异步验证

        /// <summary>
        /// 行级验证（跨字段验证）
        /// </summary>
        public override string ValidateRow()
        {
            // 可扩展：检查编码唯一性等跨字段逻辑
            return null;
        }

        /// <summary>
        /// 异步验证（数据库验证）
        /// </summary>
        public override async Task<string> ValidateAsync()
        {
            // 可扩展：检查编码是否已存在等
            return null;
        }

        #endregion

    }
}