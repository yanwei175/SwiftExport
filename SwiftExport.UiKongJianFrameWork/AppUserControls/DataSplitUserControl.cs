using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    public partial class DataSplitUserControl : UserControl
    {
        public DataSplitUserControl()
        {
            InitializeComponent();
            this.TsCmb_每页条数.Text = PageSize.ToString();
        }

        // 0-based page index
        public int PageIndex { get; set; } = 0;//分页里的索引
        public int PageSize { get; set; } = 20;

        // Total pages computed from TotalRecords and PageSize
        private int TotalPages { get => PageSize > 0 ? (int)Math.Ceiling(TotalRecords / (double)PageSize) : 0; }

        public int TotalRecords { get; set; }//数据总数

        public int PageCount { get; set; }//总页数

        // 1-based current page for UI
        public int CurrentPage = 1;

        // 支持异步绑定的委托（使用 Func<Task> 以便在调用处 await）
        public Func<Task> 刷新DataGridAsync;

        private void TSbtnFstPg_Click(object sender, EventArgs e) => GoToPage(1);
        private void TsbtnPrePg_Click(object sender, EventArgs e) => GoToPage(CurrentPage - 1);
        private void TsbtnNextPg_Click(object sender, EventArgs e) => GoToPage(CurrentPage + 1);
        private void TsbtnLstPg_Click(object sender, EventArgs e) => GoToPage(TotalPages);

        // 异步 void 用于直接作为控件内部调用（按钮事件路径）
        private async void GoToPage(int page)
        {
            if (page < 1) page = 1;
            if (TotalPages > 0 && page > TotalPages) page = TotalPages;

            // 更新索引（PageIndex 为 0-based）
            PageIndex = Math.Max(0, page - 1);
            CurrentPage = PageIndex + 1;

            改变控件显示状态();

            if (刷新DataGridAsync != null)
            {
                try
                {
                    await 刷新DataGridAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 异常交由调用方处理或记录；在 UI 线程捕获后可展示错误
                    // 这里吞掉异常以避免未观察到的任务异常导致进程终止
                }
            }
        }

        public void 改变控件显示状态()
        {
            // 计算页数快照
            var pages = TotalPages;

            // 当无记录时，显示为第0页/共0页
            if (pages <= 0)
            {
                TSbtnFstPg.Enabled = false;
                TsbtnPrePg.Enabled = false;
                TsbtnNextPg.Enabled = false;
                TsbtnLstPg.Enabled = false;

                CurrentPage = 0;
                PageIndex = 0;

                TSLblDataCount.Text = $"总记录{TotalRecords}条.";
                TSLblPageInfo.Text = $"第{CurrentPage}页, 共{pages}页.";
                TscmbPgIndex.Items.Clear();
                return;
            }

            // 保证 CurrentPage 在合法范围内
            if (CurrentPage < 1) CurrentPage = 1;
            if (CurrentPage > pages) CurrentPage = pages;

            PageIndex = CurrentPage - 1;

            TSbtnFstPg.Enabled = CurrentPage > 1;
            TsbtnPrePg.Enabled = CurrentPage > 1;
            TsbtnNextPg.Enabled = CurrentPage < pages;
            TsbtnLstPg.Enabled = CurrentPage < pages;

            TSLblDataCount.Text = $"总记录{TotalRecords}条.";
            TSLblPageInfo.Text = $"第{CurrentPage}页, 共{pages}页.";

            // 仅在页数变化时重建下拉选项，避免每次都清空/重建造成闪烁或性能问题
            if (TscmbPgIndex.Items.Count != pages)
            {
                TscmbPgIndex.Items.Clear();
                for (int i = 1; i <= pages; i++)
                {
                    TscmbPgIndex.Items.Add(i);
                }
            }

            // 选中当前页（SelectedIndex 使用 0-based）
            if (TscmbPgIndex.Items.Count > 0)
            {
                var selIndex = PageIndex;
                if (TscmbPgIndex.SelectedIndex != selIndex)
                {
                    TscmbPgIndex.SelectedIndex = selIndex;
                }
            }
        }

        // 下拉页码选择，异步以便在选择改变后立即刷新数据
        private async void TscmbPgIndex_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (TscmbPgIndex.SelectedIndex < 0) return;

            PageIndex = TscmbPgIndex.SelectedIndex;
            CurrentPage = PageIndex + 1;

            改变控件显示状态();

            if (刷新DataGridAsync != null)
            {
                try
                {
                    await 刷新DataGridAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 同上：调用方或更高层处理异常
                }
            }
        }

        // 每页数选择事件
        private async void TsCmb_每页条数_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (TsCmb_每页条数.SelectedItem == null) return;

            // 更新 PageSize
            if (int.TryParse(TsCmb_每页条数.SelectedItem.ToString(), out int newSize))
            {
                PageSize = newSize;
            }

            // 重置到第一页
            PageIndex = 0;
            CurrentPage = 1;

            改变控件显示状态();

            if (刷新DataGridAsync != null)
            {
                try
                {
                    await 刷新DataGridAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 异常交由调用方处理或记录
                }
            }
        }

    }
}

