namespace Components
{
    /*
     设计说明：
     PollMission 这一层已经按“最小实现部件”拆分为以下文件：

     - PollMissionContractsVibeCode.cs
       负责状态枚举、连接配置和单周期结果模型。
     - PollMissionSnapshotReaderVibeCode.cs
       负责按照 ReadPlan 访问 Modbus 主站并组装原始快照。
     - PollMissionExecutorVibeCode.cs
       负责串口生命周期、轮询循环、状态切换与事件分发。

     保留本文件的原因：
     1. 维持项目演进路径的连续性；
     2. 避免后续阅读时找不到 PollMission 这一层的入口名称；
     3. 明确告诉维护者：PollMission 仍是主线，只是已经拆小。
    */
}
