using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class MainProductManagerViewModel : ViewModelBaseEx
    {
        #region 主要字段与构造函数
        #endregion

        #region 左侧筛选区域
        private CustomerDto _filterCustomerLeft;
        public CustomerDto FilterCustomerLeft
        {
            get => _filterCustomerLeft;
            set =>SetProperty(ref _filterCustomerLeft, value);
        }
        private ProductColorDto _filterColorDto;
        public ProductColorDto FilterColorDto
        {
            get => _filterColorDto;
            set => SetProperty(ref _filterColorDto, value);
        }
        private CategoryTreeModel _filterCategory;
        public  CategoryTreeModel FilterCategory
        {
            get => _filterCategory;
            set => SetProperty(ref _filterCategory, value);
        }



        #endregion

        #region 主要字段与构造函数
        public MainProductManagerViewModel()
        {

        }


        #endregion

        #region 主要字段与构造函数
        #endregion














    }
}
