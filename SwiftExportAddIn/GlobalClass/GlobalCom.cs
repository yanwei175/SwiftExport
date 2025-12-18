using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExportVSTO.GlobalClass
{
    public static class GlobalCom
    {
        // 当前允许使用的 Workbook
        public static Microsoft.Office.Interop.Excel.Workbook CurrentWorkbook { get; private set; }

        // 白名单列表
        public static List<string> WhiteList { get; set; } = new List<string>();

        /// <summary>
        /// 检查并设置全局 Workbook
        /// </summary>
        public static bool TrySetWorkbook(Microsoft.Office.Interop.Excel.Workbook wb)
        {
            if (wb == null) return false;

            string fileName = wb.FullName;

            if (WhiteList.Contains(fileName))
            {
                CurrentWorkbook = wb;
                return true;
            }

            CurrentWorkbook = null;
            return false;
        }

        /// <summary>
        /// 判断当前 Workbook 是否在白名单内
        /// </summary>
        public static bool IsAllowedWorkbook(Microsoft.Office.Interop.Excel.Workbook wb)
        {
            if (wb == null) return false;
            return WhiteList.Contains(wb.FullName);
        }
    }

}
