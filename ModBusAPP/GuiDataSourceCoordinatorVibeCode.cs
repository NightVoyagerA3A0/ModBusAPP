using ApplicationLayer;
using Device;
using System;
using System.Threading.Tasks;

namespace ModBusAPP
{
    /// <summary>
    /// 负责在 GUI 中协调“模拟数据源”和“真实轮询调度器”两条链路。
    /// 设计目的：让窗口层只处理按钮交互，而把“如何选择数据源、失败后如何回退、启动后走哪条刷新路径”集中到一个较薄的协调对象中。
    /// 这样既完成了真实接入，也保留出很清晰的学习入口。
    ///
    /// 面试问题：
    /// 1. 为什么界面层常常需要一个比 ViewModel 更靠近基础设施、但又不直接放进 Window 代码后的协调对象？
    /// 2. “真实链路失败时回退到模拟链路”体现了什么渐进式集成思路？
    /// 3. 为什么把数据源切换逻辑集中到一个类里，会更利于后续替换成配置驱动或正式连接面板？
    /// </summary>
    /// <remarks>
    /// 行为模式：协调型 + 回退型。
    /// 当前实现刻意保持最小化，只做一件事：优先尝试真实串口调度器，失败时退回模拟链路。
    /// 这样既完成了“先接入”，又保留了清晰的分析边界，便于后续继续拆解。
    /// </remarks>
    public sealed class GuiDataSourceCoordinatorVibeCode : IDisposable
    {
        private readonly DeviceMonitorViewModelVibeCode _viewModel;
        private readonly GuiMonitorSimulationVibeCode _simulation;
        private PollSchedulerVibeCode<TestDevice>? _scheduler;
        private bool _disposed;
        private bool _initialized;

        /// <summary>
        /// 获取当前界面共享使用的 ViewModel。
        /// </summary>
        public DeviceMonitorViewModelVibeCode ViewModel => _viewModel;

        /// <summary>
        /// 获取当前是否处于自动刷新或自动轮询状态。
        /// </summary>
        public bool IsRunning
        {
            get
            {
                if (_scheduler is not null)
                {
                    return _scheduler.State == PollSchedulerStateVibeCode.Running;
                }

                return _simulation.IsRunning;
            }
        }

        /// <summary>
        /// 初始化界面数据源协调器。
        /// </summary>
        /// <remarks>
        /// 行为模式：构造型。
        /// 构造函数只准备共享 ViewModel 和模拟链路，不立即打开串口；真实串口接入延迟到 <see cref="InitializeAsync"/>。
        /// </remarks>
        public GuiDataSourceCoordinatorVibeCode()
        {
            _viewModel = new DeviceMonitorViewModelVibeCode();
            _simulation = new GuiMonitorSimulationVibeCode(_viewModel);
        }

        /// <summary>
        /// 初始化当前 GUI 的实际数据源。
        /// </summary>
        /// <returns>
        /// 返回模式：初始化完成任务。
        /// 返回值在真实链路尝试完成、并且必要时已经退回模拟链路后完成。
        /// </returns>
        /// <remarks>
        /// 行为模式：接线型 + 回退型。
        /// 当前方法是整个 GUI 接入真实调度器的第一阅读入口。
        /// 若你后续要分析整个启动链路，建议从这里开始，再分别跟进真实工厂和模拟器。
        /// </remarks>
        public Task InitializeAsync()
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return Task.CompletedTask;
            }

            _initialized = true;

            if (GuiRealSchedulerFactoryVibeCode.TryCreateDefault(out var scheduler, out var detail) && scheduler is not null)
            {
                _scheduler = scheduler;
                _viewModel.AttachScheduler(_scheduler);
                _viewModel.UpdateLearningContext(
                    $"当前数据源：真实串口调度器。{detail}",
                    "分析建议：先看 GuiDataSourceCoordinatorVibeCode.InitializeAsync，再看 GuiRealSchedulerFactoryVibeCode.TryCreateDefault，最后顺着 PollScheduler -> VirtualDevice -> PollMission 往下读。");
                return Task.CompletedTask;
            }

            _viewModel.UpdateLearningContext(
                "当前数据源：模拟快照驱动。",
                "分析建议：先看为什么真实工厂没有创建成功，再对照 GuiMonitorSimulationVibeCode 理解模拟链路如何复用同一套 ViewModel。");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 启动当前选中数据源的自动更新流程。
        /// </summary>
        /// <returns>
        /// 返回模式：启动完成任务。
        /// 若当前为真实调度器，则表示轮询请求已发出；若为模拟链路，则表示演示计时器已启动。
        /// </returns>
        /// <remarks>
        /// 行为模式：启动分发型。
        /// 该方法只负责把启动意图转发给当前数据源，不重新决定模式。
        /// </remarks>
        public Task StartAsync()
        {
            ThrowIfDisposed();
            EnsureInitialized();

            if (_scheduler is not null)
            {
                return _scheduler.StartAsync();
            }

            _simulation.Start();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止当前选中数据源的自动更新流程。
        /// </summary>
        /// <returns>
        /// 返回模式：停止完成任务。
        /// 返回值在底层轮询停止或模拟计时器停止后完成。
        /// </returns>
        /// <remarks>
        /// 行为模式：停止分发型。
        /// 该方法适合作为窗口关闭和按钮暂停的统一入口。
        /// </remarks>
        public Task StopAsync()
        {
            ThrowIfDisposed();
            if (!_initialized)
            {
                return Task.CompletedTask;
            }

            if (_scheduler is not null)
            {
                return _scheduler.StopAsync();
            }

            _simulation.Stop();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 手动执行一次刷新。
        /// </summary>
        /// <returns>
        /// 返回模式：单次刷新完成任务。
        /// 当前为真实模式时，会执行一次真实轮询；当前为模拟模式时，会执行一次模拟快照推进。
        /// </returns>
        /// <remarks>
        /// 行为模式：单次触发型。
        /// 这也是最适合分析“手动刷新按钮到底沿着哪条链路走下去”的入口方法。
        /// </remarks>
        public async Task RefreshOnceAsync()
        {
            ThrowIfDisposed();
            EnsureInitialized();

            if (_scheduler is not null)
            {
                await _scheduler.PollOnceAsync().ConfigureAwait(true);
                return;
            }

            _simulation.RefreshOnce();
        }

        /// <summary>
        /// 释放协调器持有的模拟器与调度器资源。
        /// </summary>
        /// <remarks>
        /// 行为模式：资源清理型。
        /// 当前类统一拥有 GUI 接线阶段创建出的对象，因此关闭窗口时由该类集中收尾最清晰。
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
                if (_scheduler is not null)
                {
                    _scheduler.StopAsync().GetAwaiter().GetResult();
                    _scheduler.Dispose();
                }
            }
            catch
            {
                // GUI 关闭阶段优先保证收尾，不再把停止异常继续抛给窗口层。
            }
            finally
            {
                _simulation.Dispose();
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("请先调用 InitializeAsync 完成 GUI 数据源接线。");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GuiDataSourceCoordinatorVibeCode));
            }
        }
    }
}
