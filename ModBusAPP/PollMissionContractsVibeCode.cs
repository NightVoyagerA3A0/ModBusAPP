using System;

namespace Components
{
    /// <summary>
    /// 表示轮询任务的状态枚举。
    /// 设计目的：用稳定的阶段标签描述当前轮询任务处于“已创建、可用、轮询中、停止中、已停止、故障、已释放”等哪一种状态，
    /// 便于虚设备、调度器和界面统一理解底层任务生命周期。
    ///
    /// 面试问题：
    /// 1. 为什么状态枚举比零散的多个 bool 标志更适合表达任务生命周期？
    /// 2. Polling、Stopping、Stopped 三者分别解决了什么语义问题？
    /// 3. 状态机设计为什么在工业通信程序里尤其重要？
    /// </summary>
    /// <remarks>
    /// 行为模式：状态描述枚举。
    /// 该枚举本身不执行行为，只负责表达任务所处阶段。
    /// </remarks>
    public enum PollMissionStateVibeCode
    {
        Created,
        Ready,
        Polling,
        Stopping,
        Stopped,
        Faulted,
        Disposed,
    }

    /// <summary>
    /// 表示轮询任务的连接与调度参数。
    /// 设计目的：把串口连接、从站地址、轮询周期和超时等配置独立出来，
    /// 避免轮询执行器把“任务行为”和“配置数据”混写在一起。
    ///
    /// 面试问题：
    /// 1. 为什么配置对象适合单独建类，而不是把参数全部塞进构造函数？
    /// 2. 连接参数和运行时状态为什么应当分开管理？
    /// 3. 工业通信配置对象通常应包含哪些最核心字段？
    /// </summary>
    /// <remarks>
    /// 行为模式：配置容器。
    /// 该类只承载连接和轮询配置，不直接执行通信行为。
    /// </remarks>
    public sealed class PollMissionOptionsVibeCode
    {
        /// <summary>
        /// 获取或设置串口名称。
        /// 该属性表示本次轮询任务绑定的物理串口，不应为空字符串。
        /// </summary>
        public string PortName { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置 Modbus 从站地址。
        /// 该属性用于标识当前任务要访问的目标从站。
        /// </summary>
        public byte SlaveAddress { get; set; } = 1;

        /// <summary>
        /// 获取或设置轮询周期。
        /// 该属性用于表达持续轮询时两次周期之间的等待时间。
        /// </summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 获取或设置串口波特率。
        /// </summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// 获取或设置读重试次数。
        /// 该属性会被传给底层 NModbus 传输层。
        /// </summary>
        public int Retries { get; set; } = 3;

        /// <summary>
        /// 获取或设置读取超时时间，单位为毫秒。
        /// </summary>
        public int ReadTimeout { get; set; } = 300;

        /// <summary>
        /// 获取或设置写入超时时间，单位为毫秒。
        /// </summary>
        public int WriteTimeout { get; set; } = 300;
    }

    /// <summary>
    /// 表示单次轮询周期完成后的结果。
    /// 设计目的：把一次轮询的时间信息、原始快照和映射摘要固定成稳定结果对象，
    /// 便于虚设备、调度器和界面消费，而不是依赖零散回调参数。
    ///
    /// 面试问题：
    /// 1. 为什么单次轮询结果值得作为独立对象存在？
    /// 2. 原始快照和应用结果为什么都要保留在结果对象中？
    /// 3. 时间戳在轮询结果里通常有哪些用途？
    /// </summary>
    /// <remarks>
    /// 行为模式：结果摘要容器。
    /// 该类不产生新的通信，只负责封装一次轮询周期的上下文信息。
    /// </remarks>
    public sealed class PollMissionCycleResultVibeCode
    {
        /// <summary>
        /// 获取本次轮询开始时间。
        /// </summary>
        public DateTimeOffset StartedAt { get; }

        /// <summary>
        /// 获取本次轮询结束时间。
        /// </summary>
        public DateTimeOffset FinishedAt { get; }

        /// <summary>
        /// 获取本次轮询使用的原始快照。
        /// </summary>
        public ModbusSourceSnapshot Snapshot { get; }

        /// <summary>
        /// 获取本次轮询的映射与回填结果。
        /// </summary>
        public ModbusApplyResult ApplyResult { get; }

        /// <summary>
        /// 初始化单次轮询结果对象。
        /// </summary>
        /// <param name="startedAt">
        /// 输入参数模式：起始时间戳。
        /// 该参数表示本次轮询开始执行时刻。
        /// </param>
        /// <param name="finishedAt">
        /// 输入参数模式：结束时间戳。
        /// 该参数表示本次轮询完成读取和映射后的时刻。
        /// </param>
        /// <param name="snapshot">
        /// 输入参数模式：原始数据快照。
        /// 该参数承载本次轮询从设备采集到的寄存器和线圈数据，不允许为 null。
        /// </param>
        /// <param name="applyResult">
        /// 输入参数模式：映射摘要结果。
        /// 该参数承载本次快照应用到设备模型后的业务结果，不允许为 null。
        /// </param>
        /// <remarks>
        /// 行为模式：结果封装型。
        /// 构造函数只负责固定结果内容，不负责轮询流程控制。
        /// </remarks>
        public PollMissionCycleResultVibeCode(
            DateTimeOffset startedAt,
            DateTimeOffset finishedAt,
            ModbusSourceSnapshot snapshot,
            ModbusApplyResult applyResult)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            ApplyResult = applyResult ?? throw new ArgumentNullException(nameof(applyResult));
            StartedAt = startedAt;
            FinishedAt = finishedAt;
        }
    }
}
