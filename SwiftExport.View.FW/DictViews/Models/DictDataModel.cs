using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.DictViews.Models
{
    /// <summary>
    /// 字典数据模型 - 对应 SysDictDataDto
    /// </summary>
    public class DictDataModel : BaseLineModel, IDataErrorInfo
    {
        /// <summary>
        /// 从 DTO 加载数据到 Model
        /// </summary>
        public override void LoadFromDto<TDto>(TDto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {
                dto.Adapt(this);

                if (Id == 0)
                {
                    MarkAsAdded();
                }

                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        private DateTime? _changeTime;
        public DateTime? ChangeTime
        {
            get => _changeTime;
            set => SetProperty(ref _changeTime, value);
        }

        private string _dictCode;
        public string DictCode
        {
            get => _dictCode;
            set => SetProperty(ref _dictCode, value);
        }

        private string _dataNameEn;
        public string DataNameEn
        {
            get => _dataNameEn;
            set => SetProperty(ref _dataNameEn, value);
        }

        private string _dataNameZh;
        public string DataNameZh
        {
            get => _dataNameZh;
            set => SetProperty(ref _dataNameZh, value);
        }

        private string _remark;
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        private int? _sortOrder;
        public int? SortOrder
        {
            get => _sortOrder;
            set => SetProperty(ref _sortOrder, value);
        }

        private string _cssClass;
        public string CssClass
        {
            get => _cssClass;
            set => SetProperty(ref _cssClass, value);
        }

        private bool _isSystem;
        public bool IsSystem
        {
            get => _isSystem;
            set => SetProperty(ref _isSystem, value);
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public override string Error => null;

        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(DictCode) && string.IsNullOrWhiteSpace(DictCode))
                    return "字典编码不能为空";
                if (columnName == nameof(DataNameZh) && string.IsNullOrWhiteSpace(DataNameZh))
                    return "中文名称不能为空";
                if (columnName == nameof(DataNameEn) && string.IsNullOrWhiteSpace(DataNameEn))
                    return "英文名称不能为空";
                return null;
            }
        }

        public override string ValidateRow()
        {
            return null;
        }

        public override async Task<string> ValidateAsync()
        {
            return await Task.FromResult<string>(null);
        }
    }
}
