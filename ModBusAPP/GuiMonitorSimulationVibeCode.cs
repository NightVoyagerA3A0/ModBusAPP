using ApplicationLayer;
using Components;
using Device;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace ModBusAPP
{
    /// <summary>
    /// 为 GUI 提供一个基于模拟快照的监控数据驱动器。
    /// 设计目的：在真实串口和真实设备尚未接入前，
    /// 先把“模拟原始数据 -> 运行时映射 -> 虚设备快照 -> ViewModel -> XAML”这条链路跑通。
    ///
    /// 面试问题：
    /// 1. 为什么在 GUI 早期联调阶段，常常先做一个模拟数据驱动器，而不是直接连接硬件？
    /// 2. `DispatcherTimer` 和普通后台线程定时器在 WPF 中最关键的区别是什么？
    /// 3. 为什么这里要让模拟器产出“原始快照”，而不是直接改 ViewModel 属性？
    /// </summary>
    /// <remarks>
    /// 行为模式：教学驱动型 + 演示调度型。
    /// 该类会周期性构造模拟寄存器和线圈数据，再复用运行时映射器生成界面可消费的快照。
    /// 当前版本支持注入共享 ViewModel，从而让 GUI 可以在“模拟链路”和“真实链路”之间共用同一套界面对象。
    /// </remarks>
    public sealed class GuiMonitorSimulationVibeCode : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly TestDevice _device;
        private readonly ModbusDeviceRuntime<TestDevice> _runtime;
        private bool _disposed;
        private int _cycleIndex;

        /// <summary>
        /// 获取当前界面绑定使用的 ViewModel。
        /// </summary>
        public DeviceMonitorViewModelVibeCode ViewModel { get; }

        /// <summary>
        /// 获取当前模拟器是否处于自动演示状态。
        /// </summary>
        public bool IsRunning => _timer.IsEnabled;

        /// <summary>
        /// 初始化一个 GUI 模拟器。
        /// </summary>
        /// <param name="viewModel">
        /// 输入参数模式：外部传入的 ViewModel。
        /// 该参数用于让模拟链路与真实链路共享同一个界面对象；传入 null 时会在内部创建默认 ViewModel。
        /// </param>
        /// <remarks>
        /// 行为模式：构造型。
        /// 构造函数会准备计时器、设备运行时和初始快照，但不会自动开始循环刷新。
        /// </remarks>
        public GuiMonitorSimulationVibeCode(DeviceMonitorViewModelVibeCode? viewModel = null)
        {
            ViewModel = viewModel ?? new DeviceMonitorViewModelVibeCode();
            _device = new TestDevice();
            _runtime = new ModbusDeviceRuntime<TestDevice>(_device);
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.2)
            };

            _timer.Tick += HandleTimerTick;

            // 先推送一次初始快照，确保窗口打开时就有可学习的示例数据。
            RefreshOnce();
        }

        /// <summary>
        /// 启动自动演示循环。
        /// </summary>
        /// <remarks>
        /// 行为模式：调度启动型。
        /// 启动后会按固定间隔持续刷新模拟快照，方便观察界面状态变化。
        /// </remarks>
        public void Start()
        {
            ThrowIfDisposed();
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        /// <summary>
        /// 停止自动演示循环。
        /// </summary>
        /// <remarks>
        /// 行为模式：调度停止型。
        /// 停止后保留当前界面显示内容，不会清空已经生成的快照结果。
        /// </remarks>
        public void Stop()
        {
            if (_disposed)
            {
                return;
            }

            _timer.Stop();
        }

        /// <summary>
        /// 手动生成并应用一次新的模拟快照。
        /// </summary>
        /// <remarks>
        /// 行为模式：单次刷新型。
        /// 该方法适合被 GUI 的“刷新一次”按钮调用，用于教学演示单周期更新。
        /// </remarks>
        public void RefreshOnce()
        {
            ThrowIfDisposed();

            var snapshot = BuildNextSnapshot();
            var applyResult = _runtime.ApplySnapshot(snapshot);
            var viewSnapshot = new VirtualDeviceSnapshotVibeCode(
                deviceName: "TestDevice-Simulated",
                portName: "SIM-DEMO",
                state: applyResult.HasWarnings ? VirtualDeviceStateVibeCode.Warning : VirtualDeviceStateVibeCode.Polling,
                currentValues: new Dictionary<string, object?>(_runtime.SnapshotCurrentValues()),
                warnings: new List<string>(applyResult.Warnings),
                lastUpdatedAt: DateTimeOffset.Now);

            ViewModel.ApplySnapshot(viewSnapshot);
            _cycleIndex++;
        }

        /// <summary>
        /// 释放模拟器占用的计时器资源。
        /// </summary>
        /// <remarks>
        /// 行为模式：资源清理型。
        /// 当前类只持有托管资源，但仍建议显式解绑事件，避免窗口关闭后残留引用。
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= HandleTimerTick;
        }

        private void HandleTimerTick(object? sender, EventArgs e)
        {
            RefreshOnce();
        }

        private ModbusSourceSnapshot BuildNextSnapshot()
        {
            var rawTemperature1 = (ushort)(128 + ((_cycleIndex * 7) % 60));
            var temperature2Value = 100 + (_cycleIndex * 8);
            var temperature3Value = 300 + (_cycleIndex * 5);
            var notification = _cycleIndex % 2 == 0;

            var registers = new Dictionary<int, ushort>
            {
                [0] = rawTemperature1,
                [1] = unchecked((ushort)(temperature2Value & 0xFFFF)),
                [2] = unchecked((ushort)((temperature2Value >> 16) & 0xFFFF)),
                [3] = unchecked((ushort)((temperature3Value >> 16) & 0xFFFF)),
                [4] = unchecked((ushort)(temperature3Value & 0xFFFF)),
            };

            var coils = new Dictionary<int, bool>
            {
                [0] = notification
            };

            // 每第五个周期故意缺一个寄存器，帮助界面演示 Warning 状态与局部失败提示。
            if ((_cycleIndex + 1) % 5 == 0)
            {
                registers.Remove(4);
            }

            return new ModbusSourceSnapshot(registers, coils);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GuiMonitorSimulationVibeCode));
            }
        }
    }
}
