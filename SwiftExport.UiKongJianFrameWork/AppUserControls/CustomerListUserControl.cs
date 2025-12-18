using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    public partial class CustomerListUserControl : UserControl
    {
        public CustomerListUserControl()
        {
            InitializeComponent();
            Cbm_CustomerList.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        // 提供一个方法或属性来绑定数据
        public void BindCustomers(IEnumerable<Customers> customers)
        {
            Cbm_CustomerList.DataSource = customers.ToList();
        }

        // 暴露选中项
        public Customers SelectedCustomer => Cbm_CustomerList.SelectedItem as Customers;
        public int SelectedCustomerId => (int)Cbm_CustomerList.SelectedValue;

    }
}
