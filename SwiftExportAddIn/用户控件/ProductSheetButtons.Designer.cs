namespace SwiftExportAddIn.用户控件
{
    partial class ProductSheetButtons
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
            this.Btn_Query = new System.Windows.Forms.Button();
            this.Btn_ClearSt = new System.Windows.Forms.Button();
            this.Btn_MatchSku = new System.Windows.Forms.Button();
            this.Btn_MatchcSKU = new System.Windows.Forms.Button();
            this.Btn_MergeToDb = new System.Windows.Forms.Button();
            this.Btn_CoverToKeGongJian = new System.Windows.Forms.Button();
            this.Btn_FindSalesDetail = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // Btn_Query
            // 
            this.Btn_Query.Location = new System.Drawing.Point(20, 36);
            this.Btn_Query.Name = "Btn_Query";
            this.Btn_Query.Size = new System.Drawing.Size(159, 94);
            this.Btn_Query.TabIndex = 0;
            this.Btn_Query.Text = "查询";
            this.Btn_Query.UseVisualStyleBackColor = true;
            // 
            // Btn_ClearSt
            // 
            this.Btn_ClearSt.Location = new System.Drawing.Point(236, 36);
            this.Btn_ClearSt.Name = "Btn_ClearSt";
            this.Btn_ClearSt.Size = new System.Drawing.Size(159, 94);
            this.Btn_ClearSt.TabIndex = 0;
            this.Btn_ClearSt.Text = "清空";
            this.Btn_ClearSt.UseVisualStyleBackColor = true;
            // 
            // Btn_MatchSku
            // 
            this.Btn_MatchSku.Location = new System.Drawing.Point(20, 168);
            this.Btn_MatchSku.Name = "Btn_MatchSku";
            this.Btn_MatchSku.Size = new System.Drawing.Size(159, 98);
            this.Btn_MatchSku.TabIndex = 0;
            this.Btn_MatchSku.Text = "按SKU匹配";
            this.Btn_MatchSku.UseVisualStyleBackColor = true;
            // 
            // Btn_MatchcSKU
            // 
            this.Btn_MatchcSKU.Location = new System.Drawing.Point(236, 168);
            this.Btn_MatchcSKU.Name = "Btn_MatchcSKU";
            this.Btn_MatchcSKU.Size = new System.Drawing.Size(159, 98);
            this.Btn_MatchcSKU.TabIndex = 0;
            this.Btn_MatchcSKU.Text = "按客户型号匹配";
            this.Btn_MatchcSKU.UseVisualStyleBackColor = true;
            // 
            // Btn_MergeToDb
            // 
            this.Btn_MergeToDb.Location = new System.Drawing.Point(20, 314);
            this.Btn_MergeToDb.Name = "Btn_MergeToDb";
            this.Btn_MergeToDb.Size = new System.Drawing.Size(159, 98);
            this.Btn_MergeToDb.TabIndex = 0;
            this.Btn_MergeToDb.Text = "合并到数据库";
            this.Btn_MergeToDb.UseVisualStyleBackColor = true;
            // 
            // Btn_CoverToKeGongJian
            // 
            this.Btn_CoverToKeGongJian.Location = new System.Drawing.Point(236, 314);
            this.Btn_CoverToKeGongJian.Name = "Btn_CoverToKeGongJian";
            this.Btn_CoverToKeGongJian.Size = new System.Drawing.Size(159, 98);
            this.Btn_CoverToKeGongJian.TabIndex = 0;
            this.Btn_CoverToKeGongJian.Text = "转客供件";
            this.Btn_CoverToKeGongJian.UseVisualStyleBackColor = true;
            // 
            // Btn_FindSalesDetail
            // 
            this.Btn_FindSalesDetail.Location = new System.Drawing.Point(20, 456);
            this.Btn_FindSalesDetail.Name = "Btn_FindSalesDetail";
            this.Btn_FindSalesDetail.Size = new System.Drawing.Size(159, 98);
            this.Btn_FindSalesDetail.TabIndex = 0;
            this.Btn_FindSalesDetail.Text = "查询销售记录";
            this.Btn_FindSalesDetail.UseVisualStyleBackColor = true;
            // 
            // ProductSheetButtons
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(14F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Btn_FindSalesDetail);
            this.Controls.Add(this.Btn_CoverToKeGongJian);
            this.Controls.Add(this.Btn_MergeToDb);
            this.Controls.Add(this.Btn_MatchcSKU);
            this.Controls.Add(this.Btn_MatchSku);
            this.Controls.Add(this.Btn_ClearSt);
            this.Controls.Add(this.Btn_Query);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "ProductSheetButtons";
            this.Size = new System.Drawing.Size(566, 1425);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button Btn_Query;
        private System.Windows.Forms.Button Btn_ClearSt;
        private System.Windows.Forms.Button Btn_MatchSku;
        private System.Windows.Forms.Button Btn_MatchcSKU;
        private System.Windows.Forms.Button Btn_MergeToDb;
        private System.Windows.Forms.Button Btn_CoverToKeGongJian;
        private System.Windows.Forms.Button Btn_FindSalesDetail;
    }
}
