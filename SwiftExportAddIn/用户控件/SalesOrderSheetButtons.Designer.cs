namespace SwiftExportAddIn.用户控件
{
    partial class SalesOrderSheetButtons
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
            this.Btn_InputSalesOrder = new System.Windows.Forms.Button();
            this.Btn_MatchCSku = new System.Windows.Forms.Button();
            this.Btn_OrderForm = new System.Windows.Forms.Button();
            this.Btn_ClearSheet = new System.Windows.Forms.Button();
            this.Btn_MergeToDb = new System.Windows.Forms.Button();
            this.Btn_SaveToNewOrder = new System.Windows.Forms.Button();
            this.Btn_ToPlanOrder = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // Btn_InputSalesOrder
            // 
            this.Btn_InputSalesOrder.Location = new System.Drawing.Point(20, 38);
            this.Btn_InputSalesOrder.Name = "Btn_InputSalesOrder";
            this.Btn_InputSalesOrder.Size = new System.Drawing.Size(236, 105);
            this.Btn_InputSalesOrder.TabIndex = 0;
            this.Btn_InputSalesOrder.Text = "导入销售订单";
            this.Btn_InputSalesOrder.UseVisualStyleBackColor = true;
            // 
            // Btn_MatchCSku
            // 
            this.Btn_MatchCSku.Location = new System.Drawing.Point(20, 180);
            this.Btn_MatchCSku.Name = "Btn_MatchCSku";
            this.Btn_MatchCSku.Size = new System.Drawing.Size(236, 105);
            this.Btn_MatchCSku.TabIndex = 0;
            this.Btn_MatchCSku.Text = "补充SKU";
            this.Btn_MatchCSku.UseVisualStyleBackColor = true;
            // 
            // Btn_OrderForm
            // 
            this.Btn_OrderForm.Location = new System.Drawing.Point(20, 321);
            this.Btn_OrderForm.Name = "Btn_OrderForm";
            this.Btn_OrderForm.Size = new System.Drawing.Size(236, 105);
            this.Btn_OrderForm.TabIndex = 0;
            this.Btn_OrderForm.Text = "查询订单";
            this.Btn_OrderForm.UseVisualStyleBackColor = true;
            // 
            // Btn_ClearSheet
            // 
            this.Btn_ClearSheet.Location = new System.Drawing.Point(20, 457);
            this.Btn_ClearSheet.Name = "Btn_ClearSheet";
            this.Btn_ClearSheet.Size = new System.Drawing.Size(236, 105);
            this.Btn_ClearSheet.TabIndex = 0;
            this.Btn_ClearSheet.Text = "清空表单";
            this.Btn_ClearSheet.UseVisualStyleBackColor = true;
            // 
            // Btn_MergeToDb
            // 
            this.Btn_MergeToDb.Location = new System.Drawing.Point(20, 603);
            this.Btn_MergeToDb.Name = "Btn_MergeToDb";
            this.Btn_MergeToDb.Size = new System.Drawing.Size(236, 105);
            this.Btn_MergeToDb.TabIndex = 0;
            this.Btn_MergeToDb.Text = "同步到数据库";
            this.Btn_MergeToDb.UseVisualStyleBackColor = true;
            // 
            // Btn_SaveToNewOrder
            // 
            this.Btn_SaveToNewOrder.Location = new System.Drawing.Point(20, 742);
            this.Btn_SaveToNewOrder.Name = "Btn_SaveToNewOrder";
            this.Btn_SaveToNewOrder.Size = new System.Drawing.Size(236, 105);
            this.Btn_SaveToNewOrder.TabIndex = 0;
            this.Btn_SaveToNewOrder.Text = "保存为新订单";
            this.Btn_SaveToNewOrder.UseVisualStyleBackColor = true;
            // 
            // Btn_ToPlanOrder
            // 
            this.Btn_ToPlanOrder.Location = new System.Drawing.Point(20, 889);
            this.Btn_ToPlanOrder.Name = "Btn_ToPlanOrder";
            this.Btn_ToPlanOrder.Size = new System.Drawing.Size(236, 105);
            this.Btn_ToPlanOrder.TabIndex = 0;
            this.Btn_ToPlanOrder.Text = "生成计划单";
            this.Btn_ToPlanOrder.UseVisualStyleBackColor = true;
            // 
            // SalesOrderSheetButtons
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(14F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Btn_ToPlanOrder);
            this.Controls.Add(this.Btn_SaveToNewOrder);
            this.Controls.Add(this.Btn_MergeToDb);
            this.Controls.Add(this.Btn_ClearSheet);
            this.Controls.Add(this.Btn_OrderForm);
            this.Controls.Add(this.Btn_MatchCSku);
            this.Controls.Add(this.Btn_InputSalesOrder);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "SalesOrderSheetButtons";
            this.Size = new System.Drawing.Size(293, 1047);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button Btn_InputSalesOrder;
        private System.Windows.Forms.Button Btn_MatchCSku;
        private System.Windows.Forms.Button Btn_OrderForm;
        private System.Windows.Forms.Button Btn_ClearSheet;
        private System.Windows.Forms.Button Btn_MergeToDb;
        private System.Windows.Forms.Button Btn_SaveToNewOrder;
        private System.Windows.Forms.Button Btn_ToPlanOrder;
    }
}
