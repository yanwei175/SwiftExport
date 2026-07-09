namespace SwiftExport.Contracts.Apis.BaseApi
{
    /// <summary>
    /// 标准化 RESTful 动态路由生成器
    /// 只要后端遵循 api/{实体名}Action 的规范，前端就无需再手写任何路由字符串。
    /// </summary>
    public static class ApiRouteBuilder
    {
        // ================= 基础 RESTful 路由 =================

        // 用于: GET (获取列表), POST (新建单体)
        public static string Resource(string entityName) => $"api/{entityName}";

        // 用于: GET (获取单个), PUT (更新单体), DELETE (删除单体)
        public static string Resource(string entityName, object id) => $"api/{entityName}/{id}";

    }
}
