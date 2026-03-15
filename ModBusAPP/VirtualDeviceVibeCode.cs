using Components;
using Device;
using Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ApplicationLayer
{
    /// <summary>
    /// 表示虚设备在应用层中的运行状态。
    /// 设计目的：把底层轮询任务状态翻译成更接近业务含义的设备状态，
    /// 便于调度器、ViewModel 和界面统一理解“当前设备是否在线、是否在轮询、是否出现故障”。
    /// </summary>
    public enum VirtualDeviceStateVibeCode
    {
        Idle,
        Ready,
        Polling,
        Warning,
        Faulted,
        Stopped,
        Disposed,
    }

    /// <summary>
    /// 表示虚设备在某个时刻对外暴露的状态快照。
    /// 设计目的：把当前值、告警、更新时间和状态聚合为一个稳定对象，
    /// 避免上层直接拼装多个零散字段。
    /// </summary>
    public sealed class VirtualDeviceSnapshotVibeCode
    {
        /// <summary>
        /// 获取虚设备显示名称。
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// 获取虚设备当前关联的串口名称。
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// 获取虚设备当前业务状态。
        /// </summary>
        public VirtualDeviceStateVibeCode State { get; }

        /// <summary>
        /// 获取当前虚设备视角下的属性值快照。
        /// 键为属性名，值为当前显示值。
        /// </summary>
        public IReadOnlyDictionary<string, object?> CurrentValues { get; }

        /// <summary>
        /// 获取最近一次处理后保留下来的告警集合。
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// 获取最近一次成功更新状态的时间。
        /// 如果仍未有任何结果，则该值为 null。
        /// </summary>
        public DateTimeOffset? LastUpdatedAt { get; }

        /// <summary>
        /// 获取当前设备是否可视为在线。
        /// </summary>
        public bool IsOnline { get; }

        /// <summary>
        /// 初始化一个虚设备状态快照对象。
        /// </summary>
        /// <param name="deviceName">
        /// 输入参数模式：显示名称。
        /// 该参数是界面和日志使用的业务标识，不应为空字符串。
        /// </param>
        /// <param name="portName">
        /// 输入参数模式：串口名称。
        /// 该参数用于表达当前虚设备关联的物理通信端口。
        /// </param>
        /// <param name="state">
        /// 输入参数模式：业务状态枚举。
        /// 该参数表示当前虚设备所处的运行阶段。
        /// </param>
        /// <param name="currentValues">
        /// 输入参数模式：当前值字典。
        /// 该参数承载已经映射好的属性值快照，不允许为 null。
        /// </param>
        /// <param name="warnings">
        /// 输入参数模式：告警集合。
        /// 该参数用于表达最近一次更新留下的局部失败或提示信息，不允许为 null。
        /// </param>
        /// <param name="lastUpdatedAt">
        /// 输入参数模式：最近更新时间。
        /// 该参数为 null 表示设备尚未收到任何有效周期结果。
        /// </param>
        /// <remarks>
        /// 行为模式：状态封装型。
        /// 该构造函数不触发通信，只负责把虚设备当前状态整理成只读快照。
        /// </remarks>
        public VirtualDeviceSnapshotVibeCode(
            string deviceName,
            string portName,
            VirtualDeviceStateVibeCode state,
            IDictionary<string, object?> currentValues,
            IList<string> warnings,
            DateTimeOffset? lastUpdatedAt)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new ArgumentException("虚设备名称不能为空。", nameof(deviceName));
            }

            if (currentValues is null)
            {
                throw new ArgumentNullException(nameof(currentValues));
            }

            if (warnings is null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            DeviceName = deviceName;
            PortName = portName;
            State = state;
            CurrentValues = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(currentValues));
            Warnings = new ReadOnlyCollection<string>(warnings.ToList());
            LastUpdatedAt = lastUpdatedAt;
            IsOnline = state is VirtualDeviceStateVibeCode.Ready
                or VirtualDeviceStateVibeCode.Polling
                or VirtualDeviceStateVibeCode.Warning;
        }
    }

    /// <summary>
    /// 表示单个设备在应用层中的虚设备对象。
    /// 设计目的：把“设备声明模型”“运行时映射器”“轮询任务结果”整合成一个单设备上下文，
    /// 让调度器和 ViewModel 不需要直接面对底层反射与 Modbus 细节。
    /// </summary>
    /// <typeparam name="TDevice">
    /// 设备模型类型。
    /// 该泛型参数必须实现 <see cref="IDeviceModel"/>，以便虚设备能够承载真实设备实例和其自定义拼接逻辑。
    /// </typeparam>
    /// <remarks>
    /// 行为模式：聚合型 + 回填监听型。
    /// 该类自身不直接发起串口读命令，而是订阅轮询任务结果，并维护当前值、最近告警与在线状态。
    /// </remarks>
    public sealed class VirtualDeviceVibeCode<TDevice> : IDisposable where TDevice : class, IDeviceModel
    {
        private readonly object _gate = new object();
        private readonly PollMissionVibeCode<TDevice> _mission;
        private IReadOnlyDictionary<string, object?> _currentValues;
        private IReadOnlyList<string> _warnings;
        private bool _disposed;
        private VirtualDeviceStateVibeCode _state;
        private DateTimeOffset? _lastUpdatedAt;

        /// <summary>
        /// 获取虚设备显示名称。
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// 获取底层设备实例。
        /// 该对象承载声明式点位定义以及当前已回填的属性值。
        /// </summary>
        public TDevice Device => _mission.Runtime.Device;

        /// <summary>
        /// 获取底层轮询任务对象。
        /// 该属性主要用于调试和进阶扩展；一般上层更推荐通过虚设备或调度器间接使用。
        /// </summary>
        public PollMissionVibeCode<TDevice> Mission => _mission;

        /// <summary>
        /// 获取当前虚设备状态。
        /// </summary>
        public VirtualDeviceStateVibeCode State => _state;

        /// <summary>
        /// 获取最近一次成功整理出来的当前值字典。
        /// </summary>
        public IReadOnlyDictionary<string, object?> CurrentValues => _currentValues;

        /// <summary>
        /// 获取最近一次处理留下的告警集合。
        /// </summary>
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>
        /// 获取最近一次成功更新虚设备状态的时间。
        /// </summary>
        public DateTimeOffset? LastUpdatedAt => _lastUpdatedAt;

        /// <summary>
        /// 获取当前虚设备快照。
        /// 该属性适合被 ViewModel 拉取，用于初始化界面状态。
        /// </summary>
        public VirtualDeviceSnapshotVibeCode CurrentSnapshot => BuildSnapshot();

        /// <summary>
        /// 当虚设备快照发生变化时触发。
        /// </summary>
        public event Action<VirtualDeviceSnapshotVibeCode>? SnapshotChanged;

        /// <summary>
        /// 当虚设备进入故障状态时触发。
        /// </summary>
        public event Action<Exception>? Faulted;

        /// <summary>
        /// 初始化一个虚设备对象，并订阅底层轮询任务事件。
        /// </summary>
        /// <param name="deviceName">
        /// 输入参数模式：业务显示名称。
        /// 该参数用于区分多个虚设备，建议传入稳定且可读的设备别名。
        /// </param>
        /// <param name="mission">
        /// 输入参数模式：已创建完成的轮询任务。
        /// 该参数承载串口、Modbus 主站和运行时映射器，不允许为 null。
        /// </param>
        /// <remarks>
        /// 行为模式：聚合构造型。
        /// 构造函数会建立事件订阅并初始化首个空快照，但不会自动启动轮询。
        /// </remarks>
        public VirtualDeviceVibeCode(string deviceName, PollMissionVibeCode<TDevice> mission)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new ArgumentException("虚设备名称不能为空。", nameof(deviceName));
            }

            DeviceName = deviceName;
            _mission = mission ?? throw new ArgumentNullException(nameof(mission));
            _currentValues = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));
            _warnings = Array.Empty<string>();
            _state = VirtualDeviceStateVibeCode.Ready;

            // 虚设备不主动轮询，而是监听轮询层把结果推上来。
            _mission.CycleCompleted += HandleCycleCompleted;
            _mission.StateChanged += HandleMissionStateChanged;
            _mission.PollingFailed += HandlePollingFailed;

            PublishSnapshot();
        }

        /// <summary>
        /// 启动当前虚设备关联的轮询任务。
        /// </summary>
        /// <returns>
        /// 返回模式：启动完成任务。
        /// 返回值表示启动请求已转发到底层轮询任务，不代表已经产生首个采集结果。
        /// </returns>
        /// <remarks>
        /// 行为模式：转发型调度方法。
        /// 该方法不直接读设备，只负责通知底层轮询器开始循环。
        /// </remarks>
        public Task StartAsync()
        {
            ThrowIfDisposed();
            return _mission.StartPollingAsync();
        }

        /// <summary>
        /// 停止当前虚设备关联的轮询任务。
        /// </summary>
        /// <returns>
        /// 返回模式：停止完成任务。
        /// 返回值在底层轮询循环真正退出后完成。
        /// </returns>
        /// <remarks>
        /// 行为模式：转发型停止方法。
        /// 该方法把停止控制权交给轮询层，自身负责在轮询层回调时同步状态。
        /// </remarks>
        public Task StopAsync()
        {
            ThrowIfDisposed();
            return _mission.StopPollingAsync();
        }

        /// <summary>
        /// 手动应用一份模拟快照到当前虚设备。
        /// </summary>
        /// <param name="snapshot">
        /// 输入参数模式：模拟原始快照。
        /// 该参数通常来自测试、教学或无硬件联调场景，用于验证映射链路。
        /// </param>
        /// <returns>
        /// 返回模式：应用结果摘要。
        /// 返回值表示这次模拟快照回填后有哪些属性被更新、有哪些告警被记录。
        /// </returns>
        /// <remarks>
        /// 行为模式：教学型/调试型回填方法。
        /// 该方法不会访问串口，而是直接复用运行时映射器，帮助学习者先理解“快照 -> 设备值”的过程。
        /// </remarks>
        public ModbusApplyResult ApplySimulatedSnapshot(ModbusSourceSnapshot snapshot)
        {
            ThrowIfDisposed();
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var applyResult = _mission.Runtime.ApplySnapshot(snapshot);
            UpdateState(
                applyResult.HasWarnings ? VirtualDeviceStateVibeCode.Warning : VirtualDeviceStateVibeCode.Ready,
                _mission.Runtime.SnapshotCurrentValues(),
                applyResult.Warnings,
                DateTimeOffset.Now);

            return applyResult;
        }

        /// <summary>
        /// 释放虚设备持有的事件订阅。
        /// </summary>
        /// <remarks>
        /// 行为模式：资源解绑型。
        /// 该方法不会主动释放底层串口资源；底层轮询任务对象的生命周期应由调度器统一管理。
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mission.CycleCompleted -= HandleCycleCompleted;
            _mission.StateChanged -= HandleMissionStateChanged;
            _mission.PollingFailed -= HandlePollingFailed;
            _state = VirtualDeviceStateVibeCode.Disposed;
            PublishSnapshot();
        }

        private void HandleCycleCompleted(PollMissionCycleResultVibeCode result)
        {
            var nextState = result.ApplyResult.HasWarnings
                ? VirtualDeviceStateVibeCode.Warning
                : VirtualDeviceStateVibeCode.Polling;

            UpdateState(
                nextState,
                _mission.Runtime.SnapshotCurrentValues(),
                result.ApplyResult.Warnings,
                result.FinishedAt);
        }

        private void HandleMissionStateChanged(PollMissionStateVibeCode state)
        {
            var mapped = state switch
            {
                PollMissionStateVibeCode.Ready => VirtualDeviceStateVibeCode.Ready,
                PollMissionStateVibeCode.Polling => VirtualDeviceStateVibeCode.Polling,
                PollMissionStateVibeCode.Stopping => VirtualDeviceStateVibeCode.Warning,
                PollMissionStateVibeCode.Stopped => VirtualDeviceStateVibeCode.Stopped,
                PollMissionStateVibeCode.Faulted => VirtualDeviceStateVibeCode.Faulted,
                PollMissionStateVibeCode.Disposed => VirtualDeviceStateVibeCode.Disposed,
                _ => VirtualDeviceStateVibeCode.Idle,
            };

            UpdateState(mapped, _currentValues, _warnings, _lastUpdatedAt);
        }

        private void HandlePollingFailed(Exception exception)
        {
            UpdateState(VirtualDeviceStateVibeCode.Faulted, _currentValues, _warnings, _lastUpdatedAt);
            Faulted?.Invoke(exception);
        }

        private void UpdateState(
            VirtualDeviceStateVibeCode state,
            IReadOnlyDictionary<string, object?> currentValues,
            IReadOnlyList<string> warnings,
            DateTimeOffset? lastUpdatedAt)
        {
            lock (_gate)
            {
                _state = state;
                _currentValues = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(currentValues));
                _warnings = new ReadOnlyCollection<string>(warnings.ToList());
                _lastUpdatedAt = lastUpdatedAt;
            }

            PublishSnapshot();
        }

        private VirtualDeviceSnapshotVibeCode BuildSnapshot()
        {
            lock (_gate)
            {
                return new VirtualDeviceSnapshotVibeCode(
                    DeviceName,
                    _mission.Options.PortName,
                    _state,
                    new Dictionary<string, object?>(_currentValues),
                    _warnings.ToList(),
                    _lastUpdatedAt);
            }
        }

        private void PublishSnapshot()
        {
            SnapshotChanged?.Invoke(BuildSnapshot());
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VirtualDeviceVibeCode<TDevice>));
            }
        }
    }
}



