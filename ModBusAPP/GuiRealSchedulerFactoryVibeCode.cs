using ApplicationLayer;
using Components;
using Device;
using System;
using System.IO.Ports;

namespace ModBusAPP
{
    /// <summary>
    /// 负责为 GUI 侧创建“最小可用”的真实调度器。
    /// 设计目的：把串口探测、默认参数准备和 <see cref="PollSchedulerVibeCode{TDevice}"/> 创建逻辑从窗口与协调层中拆开，
    /// 让学习者在阅读真实接入时，能先专注看“真实调度器是怎么被造出来的”。
    ///
    /// 面试问题：
    /// 1. 为什么“创建对象”本身也值得单独形成一个工厂类，而不一定放在调用方里直接 new？
    /// 2. 工厂类返回 Try 风格结果，在通信类项目里有什么实际价值？
    /// 3. 为什么串口探测、参数设定、调度器组装这三件事最好聚在一起，而不是散落在多个按钮事件里？
    /// </summary>
    /// <remarks>
    /// 行为模式：工厂型 + 探测型。
    /// 当前实现故意只提供“首个串口 + 默认参数”的最小方案，用来先跑通主链路。
    /// 后续若要加入正式配置面板，应优先在本类扩展，而不是把复杂度重新塞回 GUI。
    /// </remarks>
    public static class GuiRealSchedulerFactoryVibeCode
    {
        /// <summary>
        /// 尝试创建 GUI 可直接使用的真实调度器。
        /// </summary>
        /// <param name="scheduler">
        /// 输出参数模式：真实调度器输出位。
        /// 创建成功时，该参数承载一个已经完成串口连接准备的调度器实例；失败时为 null。
        /// </param>
        /// <param name="detail">
        /// 输出参数模式：创建过程说明文本。
        /// 该文本用于向 GUI 或学习者解释本次为什么成功、失败，或者采用了什么默认策略。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格创建标记。
        /// true 表示真实调度器已成功建立，false 表示当前环境还不满足最小真实接入条件。
        /// </returns>
        /// <remarks>
        /// 行为模式：创建型。
        /// 该方法只负责创建，不自动启动轮询；这能让 GUI 层把“创建”和“开始运行”分成两个更清晰的步骤。
        /// </remarks>
        public static bool TryCreateDefault(
            out PollSchedulerVibeCode<TestDevice>? scheduler,
            out string detail)
        {
            scheduler = null;

            var portNames = SerialPort.GetPortNames();
            if (portNames.Length == 0)
            {
                detail = "当前没有检测到串口，因此无法建立真实调度器。";
                return false;
            }

            var options = new PollMissionOptionsVibeCode
            {
                PortName = portNames[0],
                SlaveAddress = 1,
                BaudRate = 9600,
                PollInterval = TimeSpan.FromSeconds(1),
                ReadTimeout = 300,
                WriteTimeout = 300,
                Retries = 3,
            };

            if (!PollSchedulerVibeCode<TestDevice>.TryCreate("TestDevice-Real", new TestDevice(), options, out scheduler) || scheduler is null)
            {
                detail = $"检测到串口 {options.PortName}，但无法按默认参数创建真实调度器。";
                return false;
            }

            detail = $"已按最小默认策略接入真实调度器，当前使用串口 {options.PortName}。";
            return true;
        }
    }
}
