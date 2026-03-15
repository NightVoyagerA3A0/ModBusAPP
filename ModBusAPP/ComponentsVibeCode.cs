namespace Components
{
    /*
     设计说明：
     这个文件当前保留为运行时组件层的入口说明文件。
     原先聚合在 ComponentsVibeCode.cs 中的最小实现部件，已经按职责拆分到以下文件：

     - ModbusDeviceRuntimeVibeCode.cs
       负责设备运行时、点位扫描、属性校验与快照回填。
     - ModbusSnapshotModelsVibeCode.cs
       负责原始快照和映射摘要结果的数据模型。
     - ModbusReadPlanVibeCode.cs
       负责连续地址读取计划与读取段模型。
     - ModbusPointBindingsVibeCode.cs
       负责寄存器/线圈运行时绑定以及基础解码工具。

     保留该文件的原因：
     1. 让项目历史演进更容易回看；
     2. 避免原文件名突然消失后造成阅读断层；
     3. 明确告诉后续维护者：“组件层仍是主线，但已拆小阅读粒度”。
    */
}
