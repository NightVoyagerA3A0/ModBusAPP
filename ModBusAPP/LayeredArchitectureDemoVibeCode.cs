using ApplicationLayer;
using Components;
using Device;
using ModBusAPP;
using System;

namespace Receiver
{
    /// <summary>
    /// 提供基于“GUI -> VM -> 调度器 -> 虚设备 -> 轮询器 -> NModbus4”分层的最小教学示例。
    /// 设计目的：在不强制连接真实串口的前提下，先把架构中的数据流跑通，
    /// 便于学习者理解每一层分别负责什么。
    /// </summary>
    public static class LayeredArchitectureDemoVibeCode
    {
        /// <summary>
        /// 创建一个仅依赖模拟快照的示例 ViewModel。
        /// </summary>
        /// <returns>
        /// 返回模式：已完成一次模拟回填的 ViewModel。
        /// 返回值可直接用于命令行打印或未来 GUI 设计期预览。
        /// </returns>
        /// <remarks>
        /// 行为模式：教学工厂方法。
        /// 该方法不会访问串口，而是用一份手工构造的快照演示“虚设备 -> VM”的数据流。
        /// </remarks>
        public static DeviceMonitorViewModelVibeCode CreateDesignTimeViewModel()
        {
            var viewModel = new DeviceMonitorViewModelVibeCode();
            var snapshot = ModbusSourceSnapshot.FromArrays(
                registerStartAddress: 0,
                registers: new ushort[] { 256, 1, 0, 0, 42 },
                coilStartAddress: 0,
                coils: new[] { true });

            var device = new TestDevice();
            var runtime = new ModbusDeviceRuntime<TestDevice>(device);
            var applyResult = runtime.ApplySnapshot(snapshot);

            var deviceSnapshot = new VirtualDeviceSnapshotVibeCode(
                deviceName: nameof(TestDevice),
                portName: "SIMULATED",
                state: applyResult.HasWarnings ? VirtualDeviceStateVibeCode.Warning : VirtualDeviceStateVibeCode.Ready,
                currentValues: new System.Collections.Generic.Dictionary<string, object?>(runtime.SnapshotCurrentValues()),
                warnings: new System.Collections.Generic.List<string>(applyResult.Warnings),
                lastUpdatedAt: DateTimeOffset.Now);

            viewModel.ApplySnapshot(deviceSnapshot);
            return viewModel;
        }
    }
}




