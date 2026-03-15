using Components;
using Device;
using Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApplicationLayer
{
    /// <summary>
    /// 表示应用层调度器的整体状态。
    /// 设计目的：让 GUI 和 ViewModel 不必直接理解底层任务细节，
    /// 而是通过更稳定的应用层状态来判断当前是否已创建、正在运行、正在停止或已故障。
    /// </summary>
    public enum PollSchedulerStateVibeCode
    {
        Created,
        Ready,
        Running,
        Stopping,
        Stopped,
        Faulted,
        Disposed,
    }

    /// <summary>
    /// 表示面向上层应用的单设备调度器。
    /// 设计目的：把虚设备、轮询任务和启动停止控制集中到一个对象中，
    /// 让 ViewModel 不必直接管理串口和任务生命周期。
    /// </summary>
    /// <typeparam name="TDevice">
    /// 设备模型类型参数。
    /// 该参数必须实现 <see cref="IDeviceModel"/>，以便调度器能够创建虚设备并驱动底层轮询任务。
    /// </typeparam>
    /// <remarks>
    /// 行为模式：协调型 + 调度型。
    /// 当前实现是 MVP 版本，只管理一个虚设备；后续如果需要多端口或多设备，可以在此基础上扩展设备表和端口占用表。
    /// </remarks>
    public sealed class PollSchedulerVibeCode<TDevice> : IDisposable where TDevice : class, IDeviceModel
    {
        private readonly object _gate = new object();
        private readonly VirtualDeviceVibeCode<TDevice> _virtualDevice;
        private bool _disposed;
        private PollSchedulerStateVibeCode _state;

        private PollSchedulerVibeCode(VirtualDeviceVibeCode<TDevice> virtualDevice)
        {
            _virtualDevice = virtualDevice ?? throw new ArgumentNullException(nameof(virtualDevice));
            _state = PollSchedulerStateVibeCode.Ready;

            // 调度器只做上层协调，因此主要监听虚设备的快照和故障事件。
            _virtualDevice.SnapshotChanged += HandleSnapshotChanged;
            _virtualDevice.Faulted += HandleVirtualDeviceFaulted;
        }

        /// <summary>
        /// 获取当前调度器状态。
        /// </summary>
        public PollSchedulerStateVibeCode State => _state;

        /// <summary>
        /// 获取当前被调度的虚设备对象。
        /// </summary>
        public VirtualDeviceVibeCode<TDevice> VirtualDevice => _virtualDevice;

        /// <summary>
        /// 获取当前设备对应的底层轮询任务对象。
        /// </summary>
        public PollMissionVibeCode<TDevice> Mission => _virtualDevice.Mission;

        /// <summary>
        /// 获取当前调度器维护的最新设备快照。
        /// </summary>
        public VirtualDeviceSnapshotVibeCode CurrentSnapshot => _virtualDevice.CurrentSnapshot;

        /// <summary>
        /// 当调度器状态发生变化时触发。
        /// </summary>
        public event Action<PollSchedulerStateVibeCode>? StateChanged;

        /// <summary>
        /// 当虚设备快照发生变化时触发。
        /// </summary>
        public event Action<VirtualDeviceSnapshotVibeCode>? DeviceSnapshotChanged;

        /// <summary>
        /// 当调度器观察到设备故障时触发。
        /// </summary>
        public event Action<Exception>? Faulted;

        /// <summary>
        /// 尝试创建一个单设备调度器。
        /// </summary>
        /// <param name="deviceName">
        /// 输入参数模式：设备显示名称。
        /// 该参数用于界面和日志层标识当前调度对象，不允许为空。
        /// </param>
        /// <param name="device">
        /// 输入参数模式：设备模型实例。
        /// 该对象承载设备点位声明和运行时回填目标，不允许为 null。
        /// </param>
        /// <param name="options">
        /// 输入参数模式：连接与轮询配置。
        /// 该参数定义串口、波特率、轮询周期和从站地址等通信前置条件。
        /// </param>
        /// <param name="scheduler">
        /// 输出参数模式：调度器输出位。
        /// 当方法返回 true 时，该参数持有已创建的调度器；失败时为 null。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格创建标记。
        /// true 表示底层轮询任务已经成功建立，false 表示前置条件不满足或连接创建失败。
        /// </returns>
        /// <remarks>
        /// 行为模式：工厂型。
        /// 该方法负责把“轮询任务 -> 虚设备 -> 调度器”三层一次性串起来，但不会自动启动轮询。
        /// </remarks>
        public static bool TryCreate(
            string deviceName,
            TDevice device,
            PollMissionOptionsVibeCode options,
            out PollSchedulerVibeCode<TDevice>? scheduler)
        {
            scheduler = null;

            if (!PollMissionVibeCode<TDevice>.TryCreate(device, options, out var mission) || mission is null)
            {
                return false;
            }

            var virtualDevice = new VirtualDeviceVibeCode<TDevice>(deviceName, mission);
            scheduler = new PollSchedulerVibeCode<TDevice>(virtualDevice);
            scheduler.ChangeState(PollSchedulerStateVibeCode.Ready);
            return true;
        }

        /// <summary>
        /// 启动调度器管理的轮询流程。
        /// </summary>
        /// <returns>
        /// 返回模式：启动完成任务。
        /// 返回值在启动请求成功转发到底层轮询任务后完成。
        /// </returns>
        /// <remarks>
        /// 行为模式：调度启动型。
        /// 当前版本只管理一个虚设备，因此启动动作会直接作用于该虚设备的轮询任务。
        /// </remarks>
        public async Task StartAsync()
        {
            ThrowIfDisposed();
            ChangeState(PollSchedulerStateVibeCode.Running);
            await _virtualDevice.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 停止调度器管理的轮询流程。
        /// </summary>
        /// <returns>
        /// 返回模式：停止完成任务。
        /// 返回值在底层轮询流程真正停止后完成。
        /// </returns>
        /// <remarks>
        /// 行为模式：调度停止型。
        /// 该方法适用于应用关闭、界面断开按钮或切换端口等需要安全收尾的场景。
        /// </remarks>
        public async Task StopAsync()
        {
            ThrowIfDisposed();
            ChangeState(PollSchedulerStateVibeCode.Stopping);
            await _virtualDevice.StopAsync().ConfigureAwait(false);
            ChangeState(PollSchedulerStateVibeCode.Stopped);
        }

        /// <summary>
        /// 手动执行一次单周期轮询。
        /// </summary>
        /// <param name="cancellationToken">
        /// 输入参数模式：取消令牌。
        /// 该参数允许调用方在等待单次轮询时主动终止当前周期。
        /// </param>
        /// <returns>
        /// 返回模式：单周期结果对象。
        /// 返回值承载本次轮询的原始快照与映射摘要，可用于调试、教学或“刷新一次”按钮场景。
        /// </returns>
        /// <remarks>
        /// 行为模式：查询型 + 单次调度型。
        /// 该方法不进入持续循环，只执行一次读取，因此特别适合作为 GUI 的手动刷新入口。
        /// </remarks>
        public Task<PollMissionCycleResultVibeCode> PollOnceAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return Mission.PollOnceAsync(cancellationToken);
        }

        /// <summary>
        /// 向当前虚设备应用一份模拟快照。
        /// </summary>
        /// <param name="snapshot">
        /// 输入参数模式：模拟原始快照。
        /// 该参数通常来自测试、教学或串口尚未接通时的模拟输入。
        /// </param>
        /// <returns>
        /// 返回模式：映射摘要对象。
        /// 返回值表示本次模拟更新后有哪些属性被刷新、有哪些告警被保留。
        /// </returns>
        /// <remarks>
        /// 行为模式：教学辅助型。
        /// 该方法允许应用层在没有真实设备时先学习整条映射链路。
        /// </remarks>
        public ModbusApplyResult ApplySimulatedSnapshot(ModbusSourceSnapshot snapshot)
        {
            ThrowIfDisposed();
            return _virtualDevice.ApplySimulatedSnapshot(snapshot);
        }

        /// <summary>
        /// 释放调度器和其持有的虚设备、轮询任务资源。
        /// </summary>
        /// <remarks>
        /// 行为模式：资源清理型。
        /// 当前调度器拥有虚设备及底层轮询任务的生命周期，因此释放时会一并停止轮询并释放底层资源。
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _virtualDevice.SnapshotChanged -= HandleSnapshotChanged;
            _virtualDevice.Faulted -= HandleVirtualDeviceFaulted;
            _virtualDevice.Dispose();
            Mission.Dispose();
            ChangeState(PollSchedulerStateVibeCode.Disposed);
        }

        private void HandleSnapshotChanged(VirtualDeviceSnapshotVibeCode snapshot)
        {
            DeviceSnapshotChanged?.Invoke(snapshot);
        }

        private void HandleVirtualDeviceFaulted(Exception exception)
        {
            ChangeState(PollSchedulerStateVibeCode.Faulted);
            Faulted?.Invoke(exception);
        }

        private void ChangeState(PollSchedulerStateVibeCode state)
        {
            lock (_gate)
            {
                _state = state;
            }

            StateChanged?.Invoke(state);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PollSchedulerVibeCode<TDevice>));
            }
        }
    }
}



