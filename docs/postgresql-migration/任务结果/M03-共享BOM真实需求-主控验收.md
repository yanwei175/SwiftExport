# M03 共享BOM保存后真实需求读取主控验收

2026-10-06 03:00，主控串行。实际核验发现M03审查已经交回，但主清单仍写A在运行；G1只覆盖无BOM根，不能替代共享组成后的真实需求读取。本批仅补这一实际缺口，不因dv应用密码未提供而停止可独立推进的工作。

## 当前真实结果

[既有RuntimeWiring测试文件](../../../SwiftExport.Application.Tests/Database/PostgreSqlRuntimeWiringTests.cs)只新增一个方法 `RealPg_RuntimeSharedBomSaveFeedsMaterialEstablishment`。使用完整现有DI、用户指定dv真实身份、PG泛型夹具及原BomService / SalesOrderDraft / Confirmation / MaterialDemandEstablishment服务；两物理backend **7948、2160**。服务测试不冒称HTTP或密码登录。

三个明确登记的新Product作为局部主数据夹具：共享编号PGM03-36b5e4cf的两个成品根正标记1、3及无BOM的采购型客供终端。通过原BOM服务创建组成，再用原wrapper Version更新比例1/1→2/3，物理行Version1→2、原来源父及SKU保持。该物理来源父实际是R3；另一个根R1仍正确读取同一物理组成，不回写或复制BOM，不引入业务多版本。

原销售保存两行数量3、9，确认产生两个Gross；新连接原真实需求建立产生四Net，根3/9、直接客供2/6。BomLineId、各真实根ParentSku、来源basis快照正确；需求建立前后的完整BOM JSON相同，三个正式命令各一回执，事务/CommandLease/SourceDemandLease清空。

首次原生 **1通过、0失败、0跳过，1.0595062秒，exit0**。[实际命令](M03-共享BOM真实需求-实际命令.ps1)、[TRX](../../../TestResults/postgresql-migration/m03-shared-bom-demand-native.trx)、[原生身份](M03-共享BOM真实需求-原生事实.json)。没有普通全量/绿色重跑、其他窗口或独立新测试工程。

## 数据保护

[执行前登记](M03-共享BOM真实需求-执行登记.md)限定192.168.3.12:5432 / dv_next_pg_failure_tests_20261004 / public。原50表52552行已冻结；SKU/BOM/回执/日志/新SO670310号段均0，dv活动身份1、其他活动连接0。

执行后50表52572行，仅9表**20个精确新UID**：产品3、BOM1、销售头1/行2、Gross2、Net4、日志3、回执3、SO号段1。全部留存，不删除成功需求/回执、不清库或重置号段。排除20个实际UID后，**原50表52552行逐表数量/完整行MD5全部一致**；库存序列50/true不变，两backend活动及advisory锁0。最终7条实际SELECT成功。

完整摘要、20UID、预检/最终SQL、原生事实、实际源码/TRX哈希保存在[验证证据](M03-共享BOM真实需求-验证证据.json)。生成UID由原Add回写，日志/回执实际UID在执行后查询登记，没有伪称所有生成身份执行前已固定。

## 首次错误与范围限制

必要编译两次新增测试错误：首行补丁残留加号CS1519（2.07秒），Gross指纹属性误写CS1061（5.42秒）；只改本方法输入/代码，最终编译0错误、7既有MSTEST警告、6.49秒。没有生产失败或放宽断言。

保护核对有两类只读记录错误：首身份SELECT猜错sales_order物理表，应为sales_order_header；修正后错误复用三列摘要解析器处理两列UID，生成空CASE语法错误。已使用实际表名及二列UID解析，增加20身份/50表返回守卫，最终全部原摘要一致。最初零项/false不是数据损坏或PG业务失败，不据它重跑测试；错误原输出也保存在证据。

本批仅证明**保存提交后的真实需求读取**，不证明保存与需求建立交错、同组首次空范围或全部角色竞争。M03已交回审查中V1/V2/V4/V5是缺口建议，不是本轮全部执行证明；后续仅挑确需迁移风险的最小真实消费者场景，不扩业务或无限矩阵。原有4个BOM仓储/保存并发场景与本批分列，历史通过不能替代当前组合验证。

无生产/业务、契约/API、DI、连接配置、隔离/锁或数据库结构变化；无需dv_next结构同步；未写dv_next/备份，无提交/合并/推送/部署，保留全部未提交成果。G6运行库正常密码登录仍缺dv应用密码；M03保存/需求交错及P08最终、P09退出继续开放，不能说所有剩余只依凭据。

