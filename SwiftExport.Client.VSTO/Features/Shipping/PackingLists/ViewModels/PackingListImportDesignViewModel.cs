using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;

namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels
{
    /// <summary>仅供 XAML 设计器的装箱单示例；不接入 DI、会话、缓存、Excel 或网络。</summary>
    public sealed class PackingListImportDesignViewModel : ViewModelBaseEx
    {
        private readonly ICommand previewCommand = new RelayCommand(() => { });

        /// <summary>生成可直接调整的单头和三种行状态；所有按钮只预览外观，不执行业务。</summary>
        public PackingListImportDesignViewModel()
        {
            Header = new PackingListImportHeader
            {
                CustomerName = "LLC AQUATORIA",
                CustomerAddress = "125167, Moscow, Planetnaya street, house 11, room 9/7 RM-1",
                Attention = "Alex / +7 000 000 0000",
                InvoiceDate = new DateTime(2026, 5, 18),
                Etd = new DateTime(2026, 5, 20), Eta = new DateTime(2026, 6, 20),
                ContainerNo = "ABCD1234567", SealNo = "AQ260518", ShippingLocation = "Jiangmen",
                Note = "设计示例：保存草稿后在客户端关联来源，再整理包装。"
            };
            SelectedCustomer = new CustomerDto
            {
                Uid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                CustomerCode = "1007-AT", CustomerName = Header.CustomerName
            };
            ReferenceData.Customers.Add(SelectedCustomer);
            SelectedReadProfile = new ExcelReadProfileListItemDto { GroupCode = "PackingList", ConfigName = "装箱单示例配置" };
            ReadProfiles.Add(SelectedReadProfile);

            var matched = new PackingListImportLine
            {
                CustomerSku = "AQ4101BG", OriginalLinkSku = "AA260011BG",
                DescriptionEn = "Hook / Solid brass material", ColorNameEn = "Brushed Gold",
                QtyText = "64", Unit = "PCS", PcsPerPackageText = "32",
                NwPerPackageText = "5.2", GwPerPackageText = "5.8", CbmPerPackageText = "0.015",
                CustomerPoNo = "Order 47", InvoiceNo = "260820", Marks = "AQUATORIA", Note = "已匹配示例"
            };
            matched.AssignLineNumber(1);
            // 通过真实行模型的本地结果接纳生成绿色状态，不调用检查服务，也不放宽身份属性的 setter。
            matched.Apply(new PackingListImportLineCheckDto
            {
                MatchStatus = "Matched",
                Product = new PackingListImportProductCandidateDto
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    Sku = "AA260011BG", LinkSku = "AA260011BG", Unit = "PCS", IsUsable = true
                }
            });
            Lines.Add(matched);
            var unmatched = new PackingListImportLine
            {
                CustomerSku = "AQ4101CR", OriginalLinkSku = "AA260011CH", DescriptionEn = "Hook",
                ColorNameEn = "Chrome", QtyText = "224", Unit = "PCS", PcsPerPackageText = "32",
                NwPerPackageText = "5.2", GwPerPackageText = "5.8", CbmPerPackageText = "0.015",
                CustomerPoNo = "Order 47", InvoiceNo = "260820", Note = "未匹配示例"
            };
            unmatched.AssignLineNumber(2); Lines.Add(unmatched);
            var nonstandard = new PackingListImportLine
            {
                IsNonStandard = true, DescriptionEn = "Non-standard accessory", QtyText = "160", Unit = "PCS",
                PcsPerPackageText = "32", NwPerPackageText = "5.2", GwPerPackageText = "5.8", CbmPerPackageText = "0.015",
                CustomerPoNo = "Order 47", InvoiceNo = "260820", Note = "非标示例"
            };
            nonstandard.AssignLineNumber(3); Lines.Add(nonstandard);
        }

        /// <summary>可编辑的设计单头，与运行模型使用相同属性结构。</summary>
        public PackingListImportHeader Header { get; }
        /// <summary>设计器明细，示例值在构造函数集中调整。</summary>
        public ObservableCollection<PackingListImportLine> Lines { get; } = new ObservableCollection<PackingListImportLine>();
        /// <summary>仅提供客户候选的设计数据，不是应用缓存。</summary>
        public CustomerSamples ReferenceData { get; } = new CustomerSamples();
        /// <summary>设计时选中的示例客户。</summary>
        public CustomerDto SelectedCustomer { get; set; }
        /// <summary>设计器配置下拉示例。</summary>
        public ObservableCollection<ExcelReadProfileListItemDto> ReadProfiles { get; } = new ObservableCollection<ExcelReadProfileListItemDto>();
        /// <summary>设计器选中的读取配置。</summary>
        public ExcelReadProfileListItemDto SelectedReadProfile { get; set; }
        /// <summary>设计器当前行，只供布局交互。</summary>
        public PackingListImportLine SelectedLine { get; set; }
        /// <summary>设计时允许输入，便于查看编辑控件。</summary>
        public bool CanEdit => true;
        /// <summary>非标单位下拉的本地示例。</summary>
        public string[] Units { get; } = new[] { "PCS", "SET" };
        /// <summary>设计时的系统编号占位。</summary>
        public string PackingListNo => "保存后生成";
        /// <summary>对应示例明细的只读发票号汇总，占位值不读取 Excel。</summary>
        public string InvoiceNosSummary => "260820";
        /// <summary>设计预览提示。</summary>
        public string Message => "设计预览，按钮不会读取 Excel 或保存草稿。";
        /// <summary>与示例行对应的汇总占位，不创建业务计算链路。</summary>
        public string Summary => "3 行，总数量 448；总箱数 14；总毛重 81.20；总体积 0.210（按当前行资料暂计）";

        /// <summary>仅预览加载按钮。</summary>
        public ICommand InitializeCommand => previewCommand;
        /// <summary>仅预览单头读取按钮。</summary>
        public ICommand ReadHeaderCommand => previewCommand;
        /// <summary>仅预览明细读取按钮。</summary>
        public ICommand ReadLinesCommand => previewCommand;
        /// <summary>仅预览整单检验按钮。</summary>
        public ICommand CheckCommand => previewCommand;
        /// <summary>仅预览全部匹配按钮。</summary>
        public ICommand MatchAllSkusCommand => previewCommand;
        /// <summary>仅预览本行匹配按钮，不接纳参数或发起查询。</summary>
        public ICommand MatchLineSkuCommand => previewCommand;
        /// <summary>仅预览保存按钮，不生成保存请求。</summary>
        public ICommand SaveCommand => previewCommand;
        /// <summary>仅预览添加标准行按钮。</summary>
        public ICommand AddStandardLineCommand => previewCommand;
        /// <summary>仅预览添加非标行按钮。</summary>
        public ICommand AddNonStandardLineCommand => previewCommand;
        /// <summary>仅预览删除按钮。</summary>
        public ICommand DeleteLineCommand => previewCommand;
        /// <summary>仅预览非标复选框命令。</summary>
        public ICommand ToggleNonStandardCommand => previewCommand;
        /// <summary>仅预览客户选择命令。</summary>
        public ICommand CustomerSelectedCommand => previewCommand;
        /// <summary>仅预览客户清除命令。</summary>
        public ICommand CustomerClearedCommand => previewCommand;
        /// <summary>仅预览新导入命令。</summary>
        public ICommand NewCommand => previewCommand;
        /// <summary>仅预览配置入口，不打开窗格。</summary>
        public ICommand DesignCommand => previewCommand;

        /// <summary>承载 ReferenceData.Customers 绑定需要的示例集合，无缓存服务依赖。</summary>
        public sealed class CustomerSamples
        {
            /// <summary>供设计器客户选择控件显示。</summary>
            public ObservableCollection<CustomerDto> Customers { get; } = new ObservableCollection<CustomerDto>();
        }
    }
}
