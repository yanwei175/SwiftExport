# Shipping当前页面与调用边界

核对日期：2026-10-06。当前箱单管理、详情、来源选择、导入草稿关联、过账/反向预览与Trace位于PackingLists，API为Contracts的IPackingListApi。NavigationRouteRegistry注册管理及详情入口，写动作调用同一后端权威命令，不在ViewModel写库存规则。

箱单正式过账及反向已由Application服务编排；当前非标/CR来源走仅交付路径，标准库存路径使用活动物料拆解和PO Link。导入尚有待关联行不得过账，来源须明确数量和稳定身份，Preview不是资源预留。

旧文档目录的阶段计划、重复交接和未实施声明已被当前说明替代。现行规则见[箱单地图](../../../.agents/references/模块能力地图/PackingList模块能力地图.md)、[导入与关联](../../../docs/PackingList导入与来源关联实施说明.md)、[API索引](../../../docs/业务API索引.md)，页面模型见[PackingLists](PackingLists)。本轮未操作WPF或修改界面。
