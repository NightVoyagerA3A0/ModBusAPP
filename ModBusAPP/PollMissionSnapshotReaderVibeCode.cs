using Interfaces;
using Modbus.Device;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Components
{
    /// <summary>
    /// 提供基于读取计划生成原始快照的基础能力。
    /// 设计目的：把“根据读段逐个访问 Modbus 主站并组装快照”的逻辑从轮询执行器中拆出来，
    /// 让执行器更专注于生命周期控制，而把具体读取过程收敛成一个更小的可复用部件。
    ///
    /// 面试问题：
    /// 1. 为什么“任务控制”和“按计划读取数据”适合拆成两个部件？
    /// 2. 读取器为什么依赖 ReadPlan，而不是自己重新分析设备点位？
    /// 3. 为什么快照组装逻辑值得做成独立静态帮助器？
    /// </summary>
    /// <remarks>
    /// 行为模式：读取型 + 转换型。
    /// 该类不负责轮询循环和状态切换，只负责执行单次按计划读取并组装原始快照。
    /// </remarks>
    internal static class PollMissionSnapshotReaderVibeCode
    {
        /// <summary>
        /// 按运行时读取计划执行一次读取，并返回原始快照。
        /// </summary>
        /// <typeparam name="TDevice">
        /// 输入参数模式：设备模型类型。
        /// 该泛型参数仅用于与运行时类型保持一致，不影响读取过程本身。
        /// </typeparam>
        /// <param name="runtime">
        /// 输入参数模式：设备运行时。
        /// 该参数提供读取计划，不允许为 null。
        /// </param>
        /// <param name="serialMaster">
        /// 输入参数模式：Modbus 主站对象。
        /// 该参数负责实际执行寄存器和线圈读取，不允许为 null。
        /// </param>
        /// <param name="options">
        /// 输入参数模式：轮询配置。
        /// 该参数提供从站地址等读取上下文，不允许为 null。
        /// </param>
        /// <param name="cancellationToken">
        /// 输入参数模式：取消信号。
        /// 该参数允许调用方在读取尚未完成前中止本次快照构造。
        /// </param>
        /// <returns>
        /// 返回模式：原始快照对象。
        /// 返回值表达本次按计划读取后得到的地址值集合。
        /// </returns>
        /// <remarks>
        /// 行为模式：查询型 + 组装型。
        /// 该方法会访问外部设备资源，但不修改任务状态。
        /// </remarks>
        public static ModbusSourceSnapshot ReadSnapshot<TDevice>(
            ModbusDeviceRuntime<TDevice> runtime,
            ModbusSerialMaster serialMaster,
            PollMissionOptionsVibeCode options,
            CancellationToken cancellationToken) where TDevice : class, IDeviceModel
        {
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (serialMaster is null)
            {
                throw new ArgumentNullException(nameof(serialMaster));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var registers = new Dictionary<int, ushort>();
            var coils = new Dictionary<int, bool>();
            var plan = runtime.ReadPlan;

            foreach (var segment in plan.RegisterSegments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = serialMaster.ReadHoldingRegisters(
                    options.SlaveAddress,
                    checked((ushort)segment.StartAddress),
                    checked((ushort)segment.Count));

                for (var index = 0; index < values.Length; index++)
                {
                    registers[segment.StartAddress + index] = values[index];
                }
            }

            foreach (var segment in plan.CoilSegments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = serialMaster.ReadCoils(
                    options.SlaveAddress,
                    checked((ushort)segment.StartAddress),
                    checked((ushort)segment.Count));

                for (var index = 0; index < values.Length; index++)
                {
                    coils[segment.StartAddress + index] = values[index];
                }
            }

            return new ModbusSourceSnapshot(registers, coils);
        }
    }
}
