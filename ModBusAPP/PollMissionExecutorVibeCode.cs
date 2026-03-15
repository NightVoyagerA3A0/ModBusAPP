using Interfaces;
using Modbus.Device;
using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace Components
{
    /// <summary>
    /// 表示可被命令行与 GUI 共用的核心轮询任务执行器。
    /// 设计目的：把串口连接、主站对象、轮询循环和状态事件集中到一个对象中，
    /// 让上层虚设备和调度器不必直接管理串口生命周期。
    ///
    /// 面试问题：
    /// 1. 为什么轮询执行器适合只负责生命周期和事件，而不直接承担界面逻辑？
    /// 2. 创建连接、启动轮询、停止轮询为什么应当是三个不同动作？
    /// 3. 在异步轮询场景中，CancellationTokenSource 通常扮演什么角色？
    /// </summary>
    /// <typeparam name="TDevice">
    /// 输入模式：设备模型类型参数。
    /// 该泛型参数要求实现 <see cref="IDeviceModel"/>，以便轮询结果能被映射回设备实例。
    /// </typeparam>
    /// <remarks>
    /// 行为模式：连接型 + 调度型 + 轮询型。
    /// 该类负责建立串口连接、创建 Modbus 主站、启动后台轮询循环，并把结果通过事件抛给上层。
    /// 上层命令行或 GUI 不需要自己管理串口生命周期，只需要调用创建、启动和停止方法。
    /// </remarks>
    public sealed class PollMissionVibeCode<TDevice> : IDisposable where TDevice : class, IDeviceModel
    {
        private readonly object _gate = new object();
        private readonly PollMissionOptionsVibeCode _options;
        private readonly SerialPort _serialPort;
        private readonly ModbusSerialMaster _serialMaster;
        private readonly ModbusDeviceRuntime<TDevice> _runtime;
        private CancellationTokenSource? _pollingCts;
        private Task? _pollingTask;
        private bool _disposed;
        private PollMissionStateVibeCode _state;

        private PollMissionVibeCode(
            TDevice device,
            PollMissionOptionsVibeCode options,
            SerialPort serialPort,
            ModbusSerialMaster serialMaster)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
            _serialMaster = serialMaster ?? throw new ArgumentNullException(nameof(serialMaster));
            _runtime = new ModbusDeviceRuntime<TDevice>(device ?? throw new ArgumentNullException(nameof(device)));
            _state = PollMissionStateVibeCode.Ready;
        }

        /// <summary>
        /// 获取当前绑定的设备运行时。
        /// 该属性向上层暴露“读取计划 + 映射回填”的运行时上下文。
        /// </summary>
        public ModbusDeviceRuntime<TDevice> Runtime => _runtime;

        /// <summary>
        /// 获取当前轮询状态。
        /// </summary>
        public PollMissionStateVibeCode State => _state;

        /// <summary>
        /// 获取当前任务使用的配置。
        /// </summary>
        public PollMissionOptionsVibeCode Options => _options;

        /// <summary>
        /// 当轮询状态发生变化时触发。
        /// </summary>
        public event Action<PollMissionStateVibeCode>? StateChanged;

        /// <summary>
        /// 当单次轮询完成时触发。
        /// </summary>
        public event Action<PollMissionCycleResultVibeCode>? CycleCompleted;

        /// <summary>
        /// 当轮询过程中发生异常时触发。
        /// </summary>
        public event Action<Exception>? PollingFailed;

        /// <summary>
        /// 尝试创建一个轮询任务实例，并在创建阶段建立串口连接。
        /// </summary>
        /// <param name="device">
        /// 输入参数模式：设备模型实例。
        /// 该参数表示轮询结果最终要写回的目标模型对象，不允许为 null。
        /// </param>
        /// <param name="options">
        /// 输入参数模式：连接与轮询配置对象。
        /// 该参数提供串口名、从站地址、轮询周期和超时等配置，不允许为 null。
        /// </param>
        /// <param name="worker">
        /// 输出参数模式：任务实例输出位。
        /// 当方法返回 true 时，该参数会承载已建立连接的轮询任务实例；失败时为 null。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格创建标记。
        /// true 表示串口存在且连接创建成功，false 表示前置条件不满足或创建过程中出现异常。
        /// </returns>
        /// <remarks>
        /// 行为模式：工厂型 + 连接型。
        /// 该方法会检查串口是否存在、尝试打开串口并创建 Modbus 主站，因此不是纯对象构造。
        /// </remarks>
        public static bool TryCreate(
            TDevice device,
            PollMissionOptionsVibeCode options,
            out PollMissionVibeCode<TDevice>? worker)
        {
            worker = null;

            if (device is null || options is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.PortName))
            {
                return false;
            }

            var portNames = SerialPort.GetPortNames();
            if (Array.IndexOf(portNames, options.PortName) < 0)
            {
                return false;
            }

            try
            {
                var serialPort = new SerialPort(options.PortName, options.BaudRate)
                {
                    ReadTimeout = options.ReadTimeout,
                    WriteTimeout = options.WriteTimeout
                };

                if (!serialPort.IsOpen)
                {
                    serialPort.Open();
                }

                var serialMaster = ModbusSerialMaster.CreateRtu(serialPort);
                serialMaster.Transport.Retries = options.Retries;
                serialMaster.Transport.ReadTimeout = options.ReadTimeout;
                serialMaster.Transport.WriteTimeout = options.WriteTimeout;

                worker = new PollMissionVibeCode<TDevice>(device, options, serialPort, serialMaster);
                return true;
            }
            catch
            {
                worker = null;
                return false;
            }
        }

        /// <summary>
        /// 启动后台轮询任务。
        /// </summary>
        /// <returns>
        /// 返回模式：启动完成任务。
        /// 返回值表示后台轮询循环已经被安排启动；调用方通常不需要等待其自然结束。
        /// </returns>
        /// <remarks>
        /// 行为模式：调度启动型。
        /// 该方法会创建后台任务并开始周期性轮询；如果已经处于轮询状态，则不会重复启动。
        /// </remarks>
        public Task StartPollingAsync()
        {
            ThrowIfDisposed();

            lock (_gate)
            {
                if (_pollingTask is not null && !_pollingTask.IsCompleted)
                {
                    return Task.CompletedTask;
                }

                _pollingCts = new CancellationTokenSource();
                ChangeState(PollMissionStateVibeCode.Polling);
                _pollingTask = RunPollingLoopAsync(_pollingCts.Token);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 停止后台轮询任务。
        /// </summary>
        /// <returns>
        /// 返回模式：停止完成任务。
        /// 返回值在轮询循环真正退出后完成，便于上层在关闭界面或断开连接时安全等待。
        /// </returns>
        /// <remarks>
        /// 行为模式：停止型。
        /// 该方法会发出取消信号并等待后台任务退出；如果当前未启动轮询，则直接返回。
        /// </remarks>
        public async Task StopPollingAsync()
        {
            ThrowIfDisposed();

            Task? pollingTask;
            lock (_gate)
            {
                if (_pollingTask is null)
                {
                    ChangeState(PollMissionStateVibeCode.Stopped);
                    return;
                }

                ChangeState(PollMissionStateVibeCode.Stopping);
                _pollingCts?.Cancel();
                pollingTask = _pollingTask;
            }

            try
            {
                await pollingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 轮询停止属于预期行为，因此在这里吞掉取消异常。
            }
            finally
            {
                lock (_gate)
                {
                    _pollingTask = null;
                    _pollingCts?.Dispose();
                    _pollingCts = null;
                    ChangeState(PollMissionStateVibeCode.Stopped);
                }
            }
        }

        /// <summary>
        /// 手动执行一次轮询周期。
        /// </summary>
        /// <param name="cancellationToken">
        /// 输入参数模式：取消信号。
        /// 该参数用于让调用方在等待单次轮询完成前主动中止本次操作。
        /// </param>
        /// <returns>
        /// 返回模式：单周期结果对象。
        /// 返回值包含本次采集到的原始快照以及应用到设备模型后的摘要结果。
        /// </returns>
        /// <remarks>
        /// 行为模式：单次轮询型。
        /// 该方法适合命令行手动触发、调试或未来 GUI 的“刷新一次”场景。
        /// </remarks>
        public Task<PollMissionCycleResultVibeCode> PollOnceAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return ExecuteSingleCycleAsync(cancellationToken);
        }

        /// <summary>
        /// 释放轮询任务占用的串口和主站资源。
        /// </summary>
        /// <remarks>
        /// 行为模式：资源清理型。
        /// 释放时若仍在轮询，会同步请求停止并关闭底层串口资源。
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                StopPollingAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Dispose 阶段以尽量释放资源为主，不再向外抛出停止过程中的异常。
            }

            _serialMaster.Dispose();
            _serialPort.Dispose();
            ChangeState(PollMissionStateVibeCode.Disposed);
        }

        private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await ExecuteSingleCycleAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 由停止流程统一收尾即可。
            }
            catch (Exception ex)
            {
                ChangeState(PollMissionStateVibeCode.Faulted);
                PollingFailed?.Invoke(ex);
                throw;
            }
        }

        private Task<PollMissionCycleResultVibeCode> ExecuteSingleCycleAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var startedAt = DateTimeOffset.Now;
                var snapshot = PollMissionSnapshotReaderVibeCode.ReadSnapshot(_runtime, _serialMaster, _options, cancellationToken);
                var applyResult = _runtime.ApplySnapshot(snapshot);
                var finishedAt = DateTimeOffset.Now;
                var result = new PollMissionCycleResultVibeCode(startedAt, finishedAt, snapshot, applyResult);
                CycleCompleted?.Invoke(result);
                return result;
            }, cancellationToken);
        }

        private void ChangeState(PollMissionStateVibeCode state)
        {
            _state = state;
            StateChanged?.Invoke(state);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PollMissionVibeCode<TDevice>));
            }
        }
    }
}
