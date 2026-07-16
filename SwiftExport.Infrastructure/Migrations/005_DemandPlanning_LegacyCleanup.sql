/*
    Demand Planning Phase 6（延后草案）

    本文件故意不可执行。只有以下门槛全部由专项评审确认后，才能用新的正式迁移
    替换本文件，而不是删除下方保护：

    1. 新 WPF 客户端已完整切换到 GrossDemand/NetDemand API。
    2. SwiftExport.View.FW、VSTO、DevApp 等旧客户端无部署依赖。
    3. 到货、发货和齐套查询已切换到正式单据与库存流水。
    4. 旧 IndependentDemand/DependentDemand API 已确认无流量。
    5. 历史数据库已备份，并完成恢复和回滚演练。

    评审后的候选动作：
    - 删除旧万能 OrderStatus、ConfirmationStatus 和执行冗余列。
    - 删除旧 DTO/API/Service/Repository。
    - 评估将 IndependentDemand/DependentDemand 物理改名为 GrossDemand/NetDemand。
    - 同步更新全部 SQL、索引、约束和运维脚本。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

THROW 51500, N'005 is a protected legacy-cleanup draft and must not be executed.', 1;
