using SwiftExport.AppLayer.Services;
using SwiftExportAddIn.DI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExportVSTO.GlobalClass
{
    public static class GlobalCom
    {

        // 白名单列表
        public static HashSet<string> WhitelistCache { get; private set; }

        // 提供一个初始化方法
        public static async Task InitializeAsync()
        {
            // 如果已经加载过，就不重复加载
            if (WhitelistCache != null) return;

            try
            {
                var svr = await DI_VstoHost.GetService<WhiteListServices>().GetVstoWhiteListAsync();
                WhitelistCache = svr.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // 处理异常，或者赋值一个空的 HashSet 防止后续报错
                WhitelistCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

    }

}
