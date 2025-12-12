using System.Drawing;
using System.Windows.Forms;

namespace SwiftExportVSTO
{
    partial class LoginFrm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.txt_UserName = new System.Windows.Forms.TextBox();
            this.txt_passwd = new System.Windows.Forms.TextBox();
            this.SignIn = new System.Windows.Forms.Button();
            this.SignUp = new System.Windows.Forms.Button();
            this.tableLayoutPanelInnet = new System.Windows.Forms.TableLayoutPanel();
            this.Panel_pswd = new System.Windows.Forms.Panel();
            this.lbl_Pswd = new System.Windows.Forms.Label();
            this.PanelUsers = new System.Windows.Forms.Panel();
            this.Lbl_Users = new System.Windows.Forms.Label();
            this.tableLayoutPanelOutlet = new System.Windows.Forms.TableLayoutPanel();
            this.tableLayoutPanelInnet.SuspendLayout();
            this.Panel_pswd.SuspendLayout();
            this.PanelUsers.SuspendLayout();
            this.tableLayoutPanelOutlet.SuspendLayout();
            this.SuspendLayout();
            // 
            // txt_UserName
            // 
            this.txt_UserName.Dock = System.Windows.Forms.DockStyle.Top;
            this.txt_UserName.Location = new System.Drawing.Point(0, 31);
            this.txt_UserName.Margin = new System.Windows.Forms.Padding(4);
            this.txt_UserName.Name = "txt_UserName";
            this.txt_UserName.Size = new System.Drawing.Size(482, 39);
            this.txt_UserName.TabIndex = 1;
            // 
            // txt_passwd
            // 
            this.txt_passwd.Dock = System.Windows.Forms.DockStyle.Top;
            this.txt_passwd.Location = new System.Drawing.Point(0, 31);
            this.txt_passwd.Margin = new System.Windows.Forms.Padding(4);
            this.txt_passwd.Name = "txt_passwd";
            this.txt_passwd.Size = new System.Drawing.Size(482, 39);
            this.txt_passwd.TabIndex = 2;
            // 
            // SignIn
            // 
            this.SignIn.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SignIn.Font = new System.Drawing.Font("HarmonyOS Sans", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SignIn.Location = new System.Drawing.Point(4, 186);
            this.SignIn.Margin = new System.Windows.Forms.Padding(4);
            this.SignIn.Name = "SignIn";
            this.SignIn.Size = new System.Drawing.Size(480, 83);
            this.SignIn.TabIndex = 3;
            this.SignIn.Text = "登录";
            this.SignIn.UseVisualStyleBackColor = true;
            this.SignIn.Click += new System.EventHandler(this.SignIn_Click);
            // 
            // SignUp
            // 
            this.SignUp.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.SignUp.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SignUp.Font = new System.Drawing.Font("HarmonyOS Sans", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SignUp.Location = new System.Drawing.Point(4, 277);
            this.SignUp.Margin = new System.Windows.Forms.Padding(4);
            this.SignUp.Name = "SignUp";
            this.SignUp.Size = new System.Drawing.Size(480, 83);
            this.SignUp.TabIndex = 4;
            this.SignUp.Text = "退出";
            this.SignUp.UseVisualStyleBackColor = true;
            this.SignUp.Click += new System.EventHandler(this.SignUp_Click);
            // 
            // tableLayoutPanelInnet
            // 
            this.tableLayoutPanelInnet.ColumnCount = 1;
            this.tableLayoutPanelInnet.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelInnet.Controls.Add(this.Panel_pswd, 0, 1);
            this.tableLayoutPanelInnet.Controls.Add(this.SignUp, 0, 3);
            this.tableLayoutPanelInnet.Controls.Add(this.PanelUsers, 0, 0);
            this.tableLayoutPanelInnet.Controls.Add(this.SignIn, 0, 2);
            this.tableLayoutPanelInnet.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanelInnet.Location = new System.Drawing.Point(167, 82);
            this.tableLayoutPanelInnet.Name = "tableLayoutPanelInnet";
            this.tableLayoutPanelInnet.RowCount = 4;
            this.tableLayoutPanelInnet.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanelInnet.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanelInnet.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanelInnet.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanelInnet.Size = new System.Drawing.Size(488, 364);
            this.tableLayoutPanelInnet.TabIndex = 2;
            // 
            // Panel_pswd
            // 
            this.Panel_pswd.Controls.Add(this.txt_passwd);
            this.Panel_pswd.Controls.Add(this.lbl_Pswd);
            this.Panel_pswd.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Panel_pswd.Location = new System.Drawing.Point(3, 94);
            this.Panel_pswd.Name = "Panel_pswd";
            this.Panel_pswd.Size = new System.Drawing.Size(482, 85);
            this.Panel_pswd.TabIndex = 5;
            // 
            // lbl_Pswd
            // 
            this.lbl_Pswd.AutoSize = true;
            this.lbl_Pswd.Dock = System.Windows.Forms.DockStyle.Top;
            this.lbl_Pswd.Location = new System.Drawing.Point(0, 0);
            this.lbl_Pswd.Name = "lbl_Pswd";
            this.lbl_Pswd.Size = new System.Drawing.Size(62, 31);
            this.lbl_Pswd.TabIndex = 1;
            this.lbl_Pswd.Text = "密码";
            // 
            // PanelUsers
            // 
            this.PanelUsers.Controls.Add(this.txt_UserName);
            this.PanelUsers.Controls.Add(this.Lbl_Users);
            this.PanelUsers.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PanelUsers.Location = new System.Drawing.Point(3, 3);
            this.PanelUsers.Name = "PanelUsers";
            this.PanelUsers.Size = new System.Drawing.Size(482, 85);
            this.PanelUsers.TabIndex = 4;
            // 
            // Lbl_Users
            // 
            this.Lbl_Users.AutoSize = true;
            this.Lbl_Users.Dock = System.Windows.Forms.DockStyle.Top;
            this.Lbl_Users.Location = new System.Drawing.Point(0, 0);
            this.Lbl_Users.Name = "Lbl_Users";
            this.Lbl_Users.Size = new System.Drawing.Size(86, 31);
            this.Lbl_Users.TabIndex = 1;
            this.Lbl_Users.Text = "用户名";
            // 
            // tableLayoutPanelOutlet
            // 
            this.tableLayoutPanelOutlet.ColumnCount = 3;
            this.tableLayoutPanelOutlet.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableLayoutPanelOutlet.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.tableLayoutPanelOutlet.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableLayoutPanelOutlet.Controls.Add(this.tableLayoutPanelInnet, 1, 1);
            this.tableLayoutPanelOutlet.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanelOutlet.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanelOutlet.Name = "tableLayoutPanelOutlet";
            this.tableLayoutPanelOutlet.RowCount = 3;
            this.tableLayoutPanelOutlet.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 15F));
            this.tableLayoutPanelOutlet.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 70F));
            this.tableLayoutPanelOutlet.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 15F));
            this.tableLayoutPanelOutlet.Size = new System.Drawing.Size(824, 529);
            this.tableLayoutPanelOutlet.TabIndex = 2;
            // 
            // LoginFrm
            // 
            this.AcceptButton = this.SignIn;
            this.AutoScaleDimensions = new System.Drawing.SizeF(14F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.SignUp;
            this.ClientSize = new System.Drawing.Size(824, 529);
            this.Controls.Add(this.tableLayoutPanelOutlet);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(800, 500);
            this.Name = "LoginFrm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "UserLogin";
            this.TopMost = true;
            this.tableLayoutPanelInnet.ResumeLayout(false);
            this.Panel_pswd.ResumeLayout(false);
            this.Panel_pswd.PerformLayout();
            this.PanelUsers.ResumeLayout(false);
            this.PanelUsers.PerformLayout();
            this.tableLayoutPanelOutlet.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TextBox txt_UserName;
        private System.Windows.Forms.TextBox txt_passwd;
        private System.Windows.Forms.Button SignIn;
        private System.Windows.Forms.Button SignUp;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelInnet;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelOutlet;
        private Panel PanelUsers;
        private Label Lbl_Users;
        private Panel Panel_pswd;
        private Label lbl_Pswd;
    }
}