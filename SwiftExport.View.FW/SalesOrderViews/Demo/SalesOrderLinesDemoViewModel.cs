using GalaSoft.MvvmLight.Command;
using MateWpfThemes.FW.Controls;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SwiftExport.View.FW.SalesOrderViews.Demo
{
    /// <summary>
    /// 通用 DataGrid 编辑控件 Demo ViewModel。
    /// </summary>
    public class SalesOrderLinesDemoViewModel : ViewModelBaseEx
    {
        private readonly List<ProductLookupDemoItem> _allProducts;

        public SalesOrderLinesDemoViewModel()
        {
            _allProducts = CreateProducts();

            LookupCommittedCommand = new RelayCommand<LookupCommitContext>(OnLookupCommitted);
            AddNewRowCommand = new RelayCommand(AddNewRow);
            DeleteRowCommand = new RelayCommand<object>(OnDeleteRow);
            PasteRowsCommand = new RelayCommand<string>(OnPasteRows);

            Lines.Add(CreateLine(1, _allProducts[0]));
            Lines.Add(CreateLine(2, _allProducts[1]));
            Lines.Add(new SalesOrderLineDemoModel { Qty = 1 });

            foreach (var line in Lines)
            {
                AttachLine(line);
            }

            RefreshLookupItems(string.Empty);
        }

        public ObservableCollection<SalesOrderLineDemoModel> Lines { get; } = new ObservableCollection<SalesOrderLineDemoModel>();
        public ObservableCollection<ProductLookupDemoItem> ProductLookupItems { get; } = new ObservableCollection<ProductLookupDemoItem>();

        public RelayCommand<LookupCommitContext> LookupCommittedCommand { get; }
        public RelayCommand AddNewRowCommand { get; }
        public RelayCommand<object> DeleteRowCommand { get; }
        public RelayCommand<string> PasteRowsCommand { get; }

        private string _lastCommitMessage = "等待选择产品...";
        public string LastCommitMessage
        {
            get => _lastCommitMessage;
            set => SetProperty(ref _lastCommitMessage, value);
        }

        private void OnLookupCommitted(LookupCommitContext context)
        {
            if (context == null)
            {
                return;
            }

            var row = context.RowItem as SalesOrderLineDemoModel;
            var product = context.SelectedItem as ProductLookupDemoItem;
            if (row == null || product == null)
            {
                return;
            }

            row.SelectedProductLookup = product;
            row.ProductId = product.ProductId;
            row.CompanyModel = product.CompanyModel;
            row.DescriptionEn = product.DescriptionEn;
            row.UnitPrice = product.UnitPrice;

            LastCommitMessage = $"[{context.CommitReason}] {context.ColumnKey}: {product.CompanyModel}";
        }

        private void AddNewRow()
        {
            var line = new SalesOrderLineDemoModel { Qty = 1 };
            AttachLine(line);
            Lines.Add(line);
        }

        private void OnDeleteRow(object row)
        {
            if (row is SalesOrderLineDemoModel line && Lines.Contains(line))
            {
                line.PropertyChanged -= Line_PropertyChanged;
                Lines.Remove(line);
            }
        }

        private void OnPasteRows(string clipboardText)
        {
            LastCommitMessage = string.IsNullOrWhiteSpace(clipboardText)
                ? "剪贴板为空"
                : "收到粘贴文本，业务层可在这里解析多行数据";
        }

        private void AttachLine(SalesOrderLineDemoModel line)
        {
            line.PropertyChanged += Line_PropertyChanged;
        }

        private void Line_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SalesOrderLineDemoModel.ProductSearchText) &&
                sender is SalesOrderLineDemoModel line)
            {
                RefreshLookupItems(line.ProductSearchText);
            }
        }

        private void RefreshLookupItems(string keyword)
        {
            var text = keyword ?? string.Empty;
            var items = _allProducts
                .Where(x => string.IsNullOrWhiteSpace(text) ||
                            x.CompanyModel.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            x.DescriptionEn.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            x.DescriptionZh.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(20)
                .ToList();

            ProductLookupItems.Clear();
            foreach (var item in items)
            {
                ProductLookupItems.Add(item);
            }
        }

        private static SalesOrderLineDemoModel CreateLine(int qty, ProductLookupDemoItem product)
        {
            return new SalesOrderLineDemoModel
            {
                ProductId = product.ProductId,
                CompanyModel = product.CompanyModel,
                DescriptionEn = product.DescriptionEn,
                SelectedProductLookup = product,
                Qty = qty,
                UnitPrice = product.UnitPrice
            };
        }

        private static List<ProductLookupDemoItem> CreateProducts()
        {
            return new List<ProductLookupDemoItem>
            {
                new ProductLookupDemoItem { ProductId = Guid.NewGuid(), CompanyModel = "FAUCET-A100", DescriptionEn = "Single handle basin faucet", DescriptionZh = "单把面盆龙头", UnitPrice = 18.5m },
                new ProductLookupDemoItem { ProductId = Guid.NewGuid(), CompanyModel = "FAUCET-B220", DescriptionEn = "Kitchen mixer faucet", DescriptionZh = "厨房混合龙头", UnitPrice = 26.8m },
                new ProductLookupDemoItem { ProductId = Guid.NewGuid(), CompanyModel = "SHOWER-C310", DescriptionEn = "Rain shower set", DescriptionZh = "花洒套装", UnitPrice = 42.3m },
                new ProductLookupDemoItem { ProductId = Guid.NewGuid(), CompanyModel = "VALVE-D018", DescriptionEn = "Brass angle valve", DescriptionZh = "黄铜角阀", UnitPrice = 4.9m },
                new ProductLookupDemoItem { ProductId = Guid.NewGuid(), CompanyModel = "DRAIN-E066", DescriptionEn = "Pop-up drain", DescriptionZh = "弹跳下水器", UnitPrice = 3.6m }
            };
        }
    }
}
