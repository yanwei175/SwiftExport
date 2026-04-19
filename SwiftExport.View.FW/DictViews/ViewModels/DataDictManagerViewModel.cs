using GalaSoft.MvvmLight.Command;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    public class DictGroup
    {
        public string GroupName { get; set; }           // 左侧分组名称（商品属性 / 结算相关 / 自定义字典）
        public List<DictMenuItem> DictMenuItems { get; set; }     // 属于该分组的字典编码列表
    }


    public class DictMenuItem
    {
        public string DictCode { get; set; }
        public string DictName { get; set; }
        public bool IsSystem { get; set; }

        public ICommand OpenCommand { get; set; }
    }


    public class DataDictManagerViewModel : ViewModelBaseEx
    {
        // ============================
        // 左侧：分组 + 菜单项
        // ============================

        public ObservableCollection<DictGroup> Groups { get; set; }
        public ObservableCollection<DictMenuItem> MenuItems { get; set; }

        // ============================
        // 右侧：当前字典项列表
        // ============================

        private ObservableCollection<DictDataModel> _currentDictItems;
        public ObservableCollection<DictDataModel> CurrentDictItems
        {
            get => _currentDictItems;
            set => SetProperty(ref _currentDictItems, value);
        }

        private DictMenuItem _selectedMenuItem;
        public DictMenuItem SelectedMenuItem
        {
            get => _selectedMenuItem;
            set => SetProperty(ref _selectedMenuItem, value);
        }
        public DataDictManagerViewModel() 
        {
            LoadGroups();
            LoadMenuItems();


        }


        // ============================
        // 1. 左侧分组（写死）
        // ============================

        private void LoadGroups()
        {
            Groups = new ObservableCollection<DictGroup>
            {
                new DictGroup
                {
                    GroupName = "商品属性",
                    DictMenuItems = new List<DictMenuItem> { new DictMenuItem { DictCode = "Color", DictName = "颜色" }, new DictMenuItem { DictCode = "Size", DictName = "尺码" }, new DictMenuItem { DictCode = "Grade", DictName = "产品等级" }, new DictMenuItem { DictCode = "OEType", DictName = "OE类型" } }
                },
                new DictGroup
                {
                    GroupName = "结算相关",
                    DictMenuItems = new List<DictMenuItem> { new DictMenuItem { DictCode = "PayMethod", DictName = "结算方式" }, new DictMenuItem { DictCode = "PayTerm", DictName = "结算期限" } }
                },
                new DictGroup
                {
                    GroupName = "客户属性",
                    DictMenuItems = new List<DictMenuItem> { new DictMenuItem { DictCode = "CustomerType", DictName = "客户类型" }, new DictMenuItem { DictCode = "CustomerLevel", DictName = "客户等级" } }
                },
                //new DictGroup
                //{
                //    GroupName = "自定义字典",
                //    DictMenuItems = LoadCustomDictCodesFromDb()
                //}
            };





        }


        // ============================
        // 2. 左侧菜单项（从 DictType 表加载）
        // ============================

        private void LoadMenuItems()
        {
            var dictTypes = LoadDictTypesFromDb(); // ← 你自己实现

            MenuItems = new ObservableCollection<DictMenuItem>();

            foreach (var group in Groups)
            {
                foreach (var code in group.DictMenuItems)
                {
                    var type = dictTypes.FirstOrDefault(t => t.DictCode == code.DictCode);
                    if (type == null) continue;

                    MenuItems.Add(new DictMenuItem
                    {
                        DictCode = type.DictCode,
                        DictName = type.DictName,
                        IsSystem = type.IsSystem,
                        OpenCommand = new RelayCommand(() => OpenDict(type.DictCode))
                    });
                }
            }
        }

        // ============================
        // 3. 点击左侧菜单 → 加载右侧字典项
        // ============================

        private void OpenDict(string dictCode)
        {
            SelectedMenuItem = MenuItems.First(m => m.DictCode == dictCode);

            var items = LoadDictDataFromDb(dictCode); // ← 你自己实现

            CurrentDictItems = new ObservableCollection<DictDataModel>(items);
        }

        // ============================
        // 4. 保存（仅用户自定义字典可编辑）
        // ============================

        public ICommand SaveCommand => new RelayCommand(Save);

        private void Save()
        {
            if (SelectedMenuItem.IsSystem)
            {

                return;
            }

            SaveDictDataToDb(CurrentDictItems.ToList()); // ← 你自己实现

        }

        // ============================
        // 你需要实现的数据库方法
        // ============================

        private List<DictTypeModel> LoadDictTypesFromDb()
        {
            // TODO: 你自己的数据库访问
            return new List<DictTypeModel>()
            {
                new DictTypeModel{ DictCode="Color",DictName="2345"},
                new DictTypeModel{ DictCode="Color",DictName="2345"},
                new DictTypeModel{ DictCode="Color",DictName="2345"},
                new DictTypeModel{ DictCode="Color",DictName="2345"},
                new DictTypeModel{ DictCode="Color",DictName="2345"},
                new DictTypeModel{ DictCode="Color",DictName="2345"}
            };
        }

        private List<string> LoadCustomDictCodesFromDb()
        {
            // TODO: 从 DictType 表里找 IsSystem = false 的
            return new List<string>();
        }

        private List<DictDataModel> LoadDictDataFromDb(string dictCode)
        {
            // TODO: 从 DictData 表加载
            return new List<DictDataModel>()
            {
                new DictDataModel{ DictCode="Color",DictLabel="asdkj",DictValue="as22676"},
                new DictDataModel{ DictCode="Color",DictLabel="asdkj",DictValue="as22676"},
                new DictDataModel{ DictCode="Color",DictLabel="asdkj",DictValue="as22676"},
                new DictDataModel{ DictCode="Color",DictLabel="asdkj",DictValue="as22676"},
                new DictDataModel{ DictCode="Color",DictLabel="asdkj",DictValue="as22676"}
            };
        }

        private void SaveDictDataToDb(List<DictDataModel> items)
        {
            // TODO: 保存到数据库
        }







    }
}
