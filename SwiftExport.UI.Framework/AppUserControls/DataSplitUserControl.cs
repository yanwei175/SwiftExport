using System;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework.AppUserControls
{


    public partial class DataSplitUserControl : UserControl
    {
        // 定义事件：供外部 Form 监听
        public event EventHandler<PageChangedEventArgs> PageChanged;
        public DataSplitUserControl()
        {
            InitializeComponent();
            // 初始化页大小，不触发事件
            this.TsCmb_每页条数.Text = PageSize.ToString();
        }

        // 内部私有变量，防止 SelectedIndexChanged 导致的递归死循环
        private bool _isInternalUpdating = false;
        // 基础属性
        public int PageIndex { get; private set; } = 0; // 0-based 内部索引
        public int PageSize { get; private set; } = 20;
        public int TotalRecords { get; private set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalRecords / (double)PageSize) : 0;
        //public int PageCount { get; set; }//总页数

        /// <summary>
        /// ★ 关键方法：供外部 Form 调用
        /// 当 Form 查完数据库得到 TotalCount 后，调用此方法同步控件状态
        /// </summary>
        public void UpdateState(int totalRecords)
        {
            _isInternalUpdating = true; // 开启保护
            try
            {
                this.TotalRecords = totalRecords;
                int pages = TotalPages;

                // 1. 修正索引：如果删除了数据导致当前页超标，自动退回到最后一页
                if (PageIndex >= pages && pages > 0) PageIndex = pages - 1;
                if (pages == 0) PageIndex = 0;

                // 2. 更新按钮状态
                TSbtnFstPg.Enabled = PageIndex > 0;
                TsbtnPrePg.Enabled = PageIndex > 0;
                TsbtnNextPg.Enabled = PageIndex < pages - 1;
                TsbtnLstPg.Enabled = PageIndex < pages - 1;

                // 3. 更新 Label 文字
                TSLblDataCount.Text = $"总记录 {TotalRecords} 条.";
                TSLblPageInfo.Text = $"第 {PageIndex + 1} 页, 共 {pages} 页.";

                // 4. 更新“前往第几页”的下拉框列表
                if (TscmbPgIndex.Items.Count != pages)
                {
                    TscmbPgIndex.Items.Clear();
                    for (int i = 1; i <= pages; i++) TscmbPgIndex.Items.Add(i);
                }

                // 5. 同步下拉框当前的选中项
                if (TscmbPgIndex.Items.Count > 0)
                    TscmbPgIndex.SelectedIndex = PageIndex;
            }
            finally
            {
                _isInternalUpdating = false; // 关闭保护
            }
        }

        /// <summary>
        /// 统一的页码跳转请求
        /// </summary>
        private void RequestPageChange(int targetIndex)
        {
            if (_isInternalUpdating) return;

            int newIndex = Math.Max(0, Math.Min(targetIndex, TotalPages - 1));
            if (TotalPages == 0) newIndex = 0;

            // 更新内部页码
            this.PageIndex = newIndex;

            // 触发外部事件
            PageChanged?.Invoke(this, new PageChangedEventArgs(this.PageIndex, this.PageSize));
        }

        // --- 三大模式的 UI 触发事件 ---

        // 1. 翻页模式 (上下首末)
        private void TSbtnFstPg_Click(object sender, EventArgs e) => RequestPageChange(0);
        private void TsbtnPrePg_Click(object sender, EventArgs e) => RequestPageChange(PageIndex - 1);
        private void TsbtnNextPg_Click(object sender, EventArgs e) => RequestPageChange(PageIndex + 1);
        private void TsbtnLstPg_Click(object sender, EventArgs e) => RequestPageChange(TotalPages - 1);

        // 2. “前往第几页”模式 (下拉框)
        private void TscmbPgIndex_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isInternalUpdating) return;
            RequestPageChange(TscmbPgIndex.SelectedIndex);
        }

        // 3. “每页条数”模式 (20/50/100)
        private void TsCmb_每页条数_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isInternalUpdating) return;
            if (int.TryParse(TsCmb_每页条数.SelectedItem.ToString(), out int size))
            {
                this.PageSize = size;
                this.PageIndex = 0; // 改变每页条数，通常强制回到第一页
                RequestPageChange(0);
            }
        }
        public class PageChangedEventArgs : EventArgs
        {
            public int PageIndex { get; } // 0-based
            public int PageSize { get; }

            public PageChangedEventArgs(int index, int size)
            {
                PageIndex = index;
                PageSize = size;
            }
        }

    }
}

