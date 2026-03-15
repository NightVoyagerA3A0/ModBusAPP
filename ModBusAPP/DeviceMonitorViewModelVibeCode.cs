using ApplicationLayer;
using Device;
using Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModBusAPP
{
    /// <summary>
    /// 表示界面中单个点位的显示项。
    /// 设计目的：把设备属性值转换成 GUI 更容易消费的“名称 + 文本值”结构，
    /// 避免 XAML 直接绑定底层字典而增加理解成本。
    /// </summary>
    public sealed class DevicePointItemVibeCode : INotifyPropertyChanged
    {
        private string _valueText = string.Empty;

        /// <summary>
        /// 获取点位显示名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 获取或设置点位当前显示文本。
        /// </summary>
        public string ValueText
        {
            get => _valueText;
            set
            {
                if (string.Equals(_valueText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _valueText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 当属性值变化时触发。
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 初始化一个点位显示项。
        /// </summary>
        /// <param name="name">
        /// 输入参数模式：点位名称。
        /// 该参数通常来自设备属性名或后续的人类可读别名，不允许为空。
        /// </param>
        /// <param name="valueText">
        /// 输入参数模式：显示文本。
        /// 该参数是供界面直接展示的格式化结果，而不是原始通信值。
        /// </param>
        public DevicePointItemVibeCode(string name, string valueText)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("点位名称不能为空。", nameof(name));
            }

            Name = name;
            _valueText = valueText ?? string.Empty;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 表示提供给 GUI 的设备监控 ViewModel。
    /// 设计目的：把调度器和虚设备对外暴露的状态翻译成界面可绑定属性，
    /// 从而让 GUI 只关注显示，不直接操作通信或映射层对象。
    /// </summary>
    /// <remarks>
    /// 行为模式：表现层适配型。
    /// 该类不读串口、不解析寄存器，只负责接收调度器推送的状态并更新可绑定属性。
    /// </remarks>
    public sealed class DeviceMonitorViewModelVibeCode : INotifyPropertyChanged
    {
        private readonly Dictionary<string, DevicePointItemVibeCode> _pointLookup;
        private string _deviceName = "未绑定设备";
        private string _portName = "未指定串口";
        private string _schedulerStateText = "Created";
        private string _deviceStateText = "Idle";
        private string _summaryText = "尚未收到任何设备数据。";
        private string _warningsText = "无告警";
        private string _lastUpdatedText = "尚未更新";

        /// <summary>
        /// 获取用于界面展示的点位列表。
        /// </summary>
        public ObservableCollection<DevicePointItemVibeCode> Points { get; }

        /// <summary>
        /// 获取当前设备名称文本。
        /// </summary>
        public string DeviceName
        {
            get => _deviceName;
            private set => SetField(ref _deviceName, value);
        }

        /// <summary>
        /// 获取当前串口名称文本。
        /// </summary>
        public string PortName
        {
            get => _portName;
            private set => SetField(ref _portName, value);
        }

        /// <summary>
        /// 获取当前调度器状态显示文本。
        /// </summary>
        public string SchedulerStateText
        {
            get => _schedulerStateText;
            private set => SetField(ref _schedulerStateText, value);
        }

        /// <summary>
        /// 获取当前虚设备状态显示文本。
        /// </summary>
        public string DeviceStateText
        {
            get => _deviceStateText;
            private set => SetField(ref _deviceStateText, value);
        }

        /// <summary>
        /// 获取界面摘要文本。
        /// </summary>
        public string SummaryText
        {
            get => _summaryText;
            private set => SetField(ref _summaryText, value);
        }

        /// <summary>
        /// 获取最近告警文本。
        /// </summary>
        public string WarningsText
        {
            get => _warningsText;
            private set => SetField(ref _warningsText, value);
        }

        /// <summary>
        /// 获取最近更新时间文本。
        /// </summary>
        public string LastUpdatedText
        {
            get => _lastUpdatedText;
            private set => SetField(ref _lastUpdatedText, value);
        }

        /// <summary>
        /// 当可绑定属性变化时触发。
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 初始化一个空白监控 ViewModel。
        /// </summary>
        /// <remarks>
        /// 行为模式：占位初始化型。
        /// 构造函数会准备好点位集合和默认提示文本，便于界面在尚未绑定调度器时也能正常显示。
        /// </remarks>
        public DeviceMonitorViewModelVibeCode()
        {
            Points = new ObservableCollection<DevicePointItemVibeCode>();
            _pointLookup = new Dictionary<string, DevicePointItemVibeCode>(StringComparer.Ordinal);
        }

        /// <summary>
        /// 将当前 ViewModel 连接到某个调度器。
        /// </summary>
        /// <typeparam name="TDevice">
        /// 输入模式：设备模型泛型参数。
        /// 该参数用于保持 ViewModel 和调度器之间的强类型关联。
        /// </typeparam>
        /// <param name="scheduler">
        /// 输入参数模式：调度器对象。
        /// 该参数承载虚设备、轮询任务和调度状态，不允许为 null。
        /// </param>
        /// <remarks>
        /// 行为模式：绑定型。
        /// 该方法会先读取当前快照做初始化，再订阅后续状态变化事件。
        /// 当前 MVP 版本默认一个 ViewModel 只绑定一个调度器实例。
        /// </remarks>
        public void AttachScheduler<TDevice>(PollSchedulerVibeCode<TDevice> scheduler) where TDevice : class, IDeviceModel
        {
            if (scheduler is null)
            {
                throw new ArgumentNullException(nameof(scheduler));
            }

            SchedulerStateText = scheduler.State.ToString();
            ApplySnapshot(scheduler.CurrentSnapshot);

            scheduler.StateChanged += HandleSchedulerStateChanged;
            scheduler.DeviceSnapshotChanged += HandleDeviceSnapshotChanged;
            scheduler.Faulted += HandleFaulted;
        }

        /// <summary>
        /// 手动应用一个虚设备快照到当前 ViewModel。
        /// </summary>
        /// <param name="snapshot">
        /// 输入参数模式：虚设备快照。
        /// 该参数通常来自调度器事件，也可来自测试或设计期数据。
        /// </param>
        /// <remarks>
        /// 行为模式：回填型。
        /// 该方法会刷新摘要文本、告警文本和点位显示集合，因此会修改 ViewModel 内部状态。
        /// </remarks>
        public void ApplySnapshot(VirtualDeviceSnapshotVibeCode snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            DeviceName = snapshot.DeviceName;
            PortName = string.IsNullOrWhiteSpace(snapshot.PortName) ? "未指定串口" : snapshot.PortName;
            DeviceStateText = snapshot.State.ToString();
            LastUpdatedText = snapshot.LastUpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未更新";
            WarningsText = snapshot.Warnings.Count == 0 ? "无告警" : string.Join(Environment.NewLine, snapshot.Warnings);
            SummaryText = BuildSummaryText(snapshot);

            SyncPoints(snapshot.CurrentValues);
        }

        private void HandleSchedulerStateChanged(PollSchedulerStateVibeCode state)
        {
            SchedulerStateText = state.ToString();
        }

        private void HandleDeviceSnapshotChanged(VirtualDeviceSnapshotVibeCode snapshot)
        {
            ApplySnapshot(snapshot);
        }

        private void HandleFaulted(Exception exception)
        {
            SummaryText = $"调度器检测到故障：{exception.Message}";
        }

        private void SyncPoints(IReadOnlyDictionary<string, object?> values)
        {
            foreach (var pair in values)
            {
                var nextText = pair.Value?.ToString() ?? "(null)";
                if (_pointLookup.TryGetValue(pair.Key, out var item))
                {
                    item.ValueText = nextText;
                    continue;
                }

                item = new DevicePointItemVibeCode(pair.Key, nextText);
                _pointLookup.Add(pair.Key, item);
                Points.Add(item);
            }
        }

        private static string BuildSummaryText(VirtualDeviceSnapshotVibeCode snapshot)
        {
            var onlineText = snapshot.IsOnline ? "在线或可用" : "离线或未启动";
            return $"设备 {snapshot.DeviceName} 当前状态为 {snapshot.State}，端口 {snapshot.PortName}，判定为 {onlineText}。";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
        }
    }
}



