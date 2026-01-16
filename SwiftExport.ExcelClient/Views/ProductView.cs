using SwiftExport.Contracts.DTOs.Product;
using SwiftExport.UI.Services;
using System.Runtime.InteropServices;

namespace SwiftExport.ExcelClient.Views;

[ComVisible(true)]
[Guid("D9567936-3990-4817-9159-26A66953761F")] // 随便生成一个 GUID，保持唯一即可
public interface IProductView
{
}


// 2️⃣ 第二步：给类加上完整的 COM 描述
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)] // 告诉它不要自动生成混乱的接口
[ComDefaultInterface(typeof(IProductView))] // 明确指定：请用上面那个接口来和我通信
[Guid("A83B4C92-061B-4712-B671-872719602058")] // 这里也必须有一个唯一的 GUID (VS里 工具->创建GUID)
public partial class ProductView : UserControl, IProductView
{
    public ProductView()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"界面初始化失败: {ex.Message}");
        }
    }

    private async void BtnSearch_Click(object sender, EventArgs e)
    {
        try
        {
            BtnSearch.Enabled = false;
            BtnSearch.Text = "查询中...";

            var keyword = TxtKeyword.Text.Trim();

            // 构造查询参数
            var search = new ProductSearchDto
            {
                PageIndex = 1,
                PageSize = 50, // 侧边栏可以多显示点
                Keyword = keyword
            };

            // ✅ 调用全局 API
            var result = await GlobalApi.Client.GetByIdAsync(5);

            if (result.Success)
            {
                // 绑定到表格
                // DataGridView 会自动识别 List<ProductDto> 的属性并创建列
                DgvResult.DataSource = result.Data;
            }
            else
            {
                MessageBox.Show($"查询失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"出错: {ex.Message}");
        }
        finally
        {
            BtnSearch.Enabled = true;
            BtnSearch.Text = "查询";
        }
    }

    private TextBox TxtKeyword;
    private Button BtnSearch;
    private DataGridView DgvResult;
    private Label label1;

    private void InitializeComponent()
    {
        TxtKeyword = new TextBox();
        BtnSearch = new Button();
        DgvResult = new DataGridView();
        label1 = new Label();
        ((System.ComponentModel.ISupportInitialize)DgvResult).BeginInit();
        SuspendLayout();
        // 
        // TxtKeyword
        // 
        TxtKeyword.Location = new Point(304, 67);
        TxtKeyword.Name = "TxtKeyword";
        TxtKeyword.Size = new Size(150, 30);
        TxtKeyword.TabIndex = 0;
        // 
        // BtnSearch
        // 
        BtnSearch.Location = new Point(496, 67);
        BtnSearch.Name = "BtnSearch";
        BtnSearch.Size = new Size(112, 34);
        BtnSearch.TabIndex = 1;
        BtnSearch.Text = "查询";
        BtnSearch.UseVisualStyleBackColor = true;
        // 
        // DgvResult
        // 
        DgvResult.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        DgvResult.Location = new Point(74, 161);
        DgvResult.Name = "DgvResult";
        DgvResult.RowHeadersWidth = 62;
        DgvResult.Size = new Size(360, 225);
        DgvResult.TabIndex = 2;
        // 
        // label1
        // 
        label1.AutoSize = true;
        label1.Location = new Point(169, 67);
        label1.Name = "label1";
        label1.Size = new Size(104, 24);
        label1.TabIndex = 3;
        label1.Text = "输入关键词:";
        // 
        // ProductView
        // 
        Controls.Add(label1);
        Controls.Add(DgvResult);
        Controls.Add(BtnSearch);
        Controls.Add(TxtKeyword);
        Name = "ProductView";
        Size = new Size(799, 552);
        ((System.ComponentModel.ISupportInitialize)DgvResult).EndInit();
        ResumeLayout(false);
        PerformLayout();

    }
}