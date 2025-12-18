namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    partial class UserDataGrid
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            this.TSDataStatus = new System.Windows.Forms.ToolStrip();
            this.TSLblDataCount = new System.Windows.Forms.ToolStripLabel();
            this.TSLblPageInfo = new System.Windows.Forms.ToolStripLabel();
            this.TSlblPgUnit = new System.Windows.Forms.ToolStripLabel();
            this.TscmbPgIndex = new System.Windows.Forms.ToolStripComboBox();
            this.TslblGotoPg = new System.Windows.Forms.ToolStripLabel();
            this.FenGeFu = new System.Windows.Forms.ToolStripSeparator();
            this.TsbtnLstPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnNextPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnPrePg = new System.Windows.Forms.ToolStripButton();
            this.TSbtnFstPg = new System.Windows.Forms.ToolStripButton();
            this.AllSptContainer = new System.Windows.Forms.SplitContainer();
            this.button1 = new System.Windows.Forms.Button();
            this.Data = new System.Windows.Forms.SplitContainer();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.Column1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column3 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column4 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.TSDataStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.AllSptContainer)).BeginInit();
            this.AllSptContainer.Panel1.SuspendLayout();
            this.AllSptContainer.Panel2.SuspendLayout();
            this.AllSptContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Data)).BeginInit();
            this.Data.Panel1.SuspendLayout();
            this.Data.Panel2.SuspendLayout();
            this.Data.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // TSDataStatus
            // 
            this.TSDataStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TSDataStatus.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.TSDataStatus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.TSLblDataCount,
            this.TSLblPageInfo,
            this.TSlblPgUnit,
            this.TscmbPgIndex,
            this.TslblGotoPg,
            this.FenGeFu,
            this.TsbtnLstPg,
            this.TsbtnNextPg,
            this.TsbtnPrePg,
            this.TSbtnFstPg});
            this.TSDataStatus.Location = new System.Drawing.Point(0, 0);
            this.TSDataStatus.Name = "TSDataStatus";
            this.TSDataStatus.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.TSDataStatus.Size = new System.Drawing.Size(1281, 57);
            this.TSDataStatus.TabIndex = 9;
            this.TSDataStatus.Text = "toolStrip2";
            // 
            // TSLblDataCount
            // 
            this.TSLblDataCount.Name = "TSLblDataCount";
            this.TSLblDataCount.Size = new System.Drawing.Size(130, 51);
            this.TSLblDataCount.Text = "总记录  条.";
            // 
            // TSLblPageInfo
            // 
            this.TSLblPageInfo.Name = "TSLblPageInfo";
            this.TSLblPageInfo.Size = new System.Drawing.Size(147, 51);
            this.TSLblPageInfo.Text = "当前第  / 页.";
            // 
            // TSlblPgUnit
            // 
            this.TSlblPgUnit.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSlblPgUnit.Name = "TSlblPgUnit";
            this.TSlblPgUnit.Size = new System.Drawing.Size(38, 51);
            this.TSlblPgUnit.Text = "页";
            // 
            // TscmbPgIndex
            // 
            this.TscmbPgIndex.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TscmbPgIndex.Name = "TscmbPgIndex";
            this.TscmbPgIndex.Size = new System.Drawing.Size(91, 57);
            // 
            // TslblGotoPg
            // 
            this.TslblGotoPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TslblGotoPg.Name = "TslblGotoPg";
            this.TslblGotoPg.Size = new System.Drawing.Size(62, 51);
            this.TslblGotoPg.Text = "前往";
            // 
            // FenGeFu
            // 
            this.FenGeFu.Name = "FenGeFu";
            this.FenGeFu.Size = new System.Drawing.Size(6, 57);
            // 
            // TsbtnLstPg
            // 
            this.TsbtnLstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnLstPg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsbtnLstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnLstPg.Name = "TsbtnLstPg";
            this.TsbtnLstPg.Size = new System.Drawing.Size(66, 51);
            this.TsbtnLstPg.Text = "尾页";
            // 
            // TsbtnNextPg
            // 
            this.TsbtnNextPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnNextPg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsbtnNextPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnNextPg.Name = "TsbtnNextPg";
            this.TsbtnNextPg.Size = new System.Drawing.Size(66, 51);
            this.TsbtnNextPg.Text = "下页";
            // 
            // TsbtnPrePg
            // 
            this.TsbtnPrePg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnPrePg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsbtnPrePg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnPrePg.Name = "TsbtnPrePg";
            this.TsbtnPrePg.Size = new System.Drawing.Size(66, 51);
            this.TsbtnPrePg.Text = "上页";
            // 
            // TSbtnFstPg
            // 
            this.TSbtnFstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSbtnFstPg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TSbtnFstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TSbtnFstPg.Name = "TSbtnFstPg";
            this.TSbtnFstPg.Size = new System.Drawing.Size(66, 51);
            this.TSbtnFstPg.Text = "首页";
            // 
            // AllSptContainer
            // 
            this.AllSptContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AllSptContainer.Location = new System.Drawing.Point(0, 0);
            this.AllSptContainer.Name = "AllSptContainer";
            this.AllSptContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // AllSptContainer.Panel1
            // 
            this.AllSptContainer.Panel1.Controls.Add(this.button1);
            // 
            // AllSptContainer.Panel2
            // 
            this.AllSptContainer.Panel2.Controls.Add(this.Data);
            this.AllSptContainer.Size = new System.Drawing.Size(1281, 769);
            this.AllSptContainer.SplitterDistance = 383;
            this.AllSptContainer.TabIndex = 10;
            // 
            // button1
            // 
            this.button1.BackColor = System.Drawing.Color.WhiteSmoke;
            this.button1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button1.Location = new System.Drawing.Point(0, 0);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(1281, 383);
            this.button1.TabIndex = 0;
            this.button1.UseVisualStyleBackColor = false;
            // 
            // Data
            // 
            this.Data.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Data.Location = new System.Drawing.Point(0, 0);
            this.Data.Name = "Data";
            this.Data.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // Data.Panel1
            // 
            this.Data.Panel1.Controls.Add(this.dataGridView1);
            // 
            // Data.Panel2
            // 
            this.Data.Panel2.Controls.Add(this.TSDataStatus);
            this.Data.Size = new System.Drawing.Size(1281, 382);
            this.Data.SplitterDistance = 321;
            this.Data.TabIndex = 0;
            // 
            // dataGridView1
            // 
            this.dataGridView1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView1.BackgroundColor = System.Drawing.SystemColors.Window;
            this.dataGridView1.ClipboardCopyMode = System.Windows.Forms.DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            this.dataGridView1.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.LightBlue;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView1.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Column1,
            this.Column2,
            this.Column3,
            this.Column4});
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("微软雅黑", 8F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dataGridView1.DefaultCellStyle = dataGridViewCellStyle2;
            this.dataGridView1.EnableHeadersVisualStyles = false;
            this.dataGridView1.Location = new System.Drawing.Point(65, 3);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = System.Drawing.Color.LightBlue;
            dataGridViewCellStyle3.Font = new System.Drawing.Font("微软雅黑", 8F);
            dataGridViewCellStyle3.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.Color.Honeydew;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView1.RowHeadersDefaultCellStyle = dataGridViewCellStyle3;
            this.dataGridView1.RowHeadersWidth = 62;
            this.dataGridView1.RowTemplate.Height = 37;
            this.dataGridView1.Size = new System.Drawing.Size(1122, 290);
            this.dataGridView1.TabIndex = 0;
            // 
            // Column1
            // 
            this.Column1.HeaderText = "Column1";
            this.Column1.MinimumWidth = 10;
            this.Column1.Name = "Column1";
            // 
            // Column2
            // 
            this.Column2.HeaderText = "Column2";
            this.Column2.MinimumWidth = 10;
            this.Column2.Name = "Column2";
            // 
            // Column3
            // 
            this.Column3.HeaderText = "Column3";
            this.Column3.MinimumWidth = 10;
            this.Column3.Name = "Column3";
            // 
            // Column4
            // 
            this.Column4.HeaderText = "Column4";
            this.Column4.MinimumWidth = 10;
            this.Column4.Name = "Column4";
            // 
            // UserDataGrid
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(16F, 35F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.AllSptContainer);
            this.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Name = "UserDataGrid";
            this.Size = new System.Drawing.Size(1281, 769);
            this.TSDataStatus.ResumeLayout(false);
            this.TSDataStatus.PerformLayout();
            this.AllSptContainer.Panel1.ResumeLayout(false);
            this.AllSptContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.AllSptContainer)).EndInit();
            this.AllSptContainer.ResumeLayout(false);
            this.Data.Panel1.ResumeLayout(false);
            this.Data.Panel2.ResumeLayout(false);
            this.Data.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Data)).EndInit();
            this.Data.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ToolStrip TSDataStatus;
        private System.Windows.Forms.ToolStripLabel TSLblDataCount;
        private System.Windows.Forms.ToolStripLabel TSLblPageInfo;
        private System.Windows.Forms.ToolStripLabel TSlblPgUnit;
        private System.Windows.Forms.ToolStripComboBox TscmbPgIndex;
        private System.Windows.Forms.ToolStripLabel TslblGotoPg;
        private System.Windows.Forms.ToolStripSeparator FenGeFu;
        private System.Windows.Forms.ToolStripButton TsbtnLstPg;
        private System.Windows.Forms.ToolStripButton TsbtnNextPg;
        private System.Windows.Forms.ToolStripButton TsbtnPrePg;
        private System.Windows.Forms.ToolStripButton TSbtnFstPg;
        private System.Windows.Forms.SplitContainer AllSptContainer;
        private System.Windows.Forms.SplitContainer Data;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column1;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column2;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column3;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column4;
    }
}
