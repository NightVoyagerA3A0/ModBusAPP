using System;
using System.Windows;

namespace ModBusAPP
{
    /// <summary>
    /// GUI.xaml 的交互层。
    /// 设计目的：当前文件只负责窗口生命周期和按钮事件，不直接承载数据源选择、串口创建或轮询调度逻辑。
    /// 这样做的原因是，学习者在阅读界面层时，可以先看清楚“按钮做了什么”，再去追踪协调层如何决定真实链路或模拟链路。
    /// </summary>
    public partial class GUI : Window
    {
        private readonly GuiDataSourceCoordinatorVibeCode _coordinator;

        /// <summary>
        /// 初始化主窗口，并准备界面数据源协调器。
        /// </summary>
        public GUI()
        {
            InitializeComponent();

            _coordinator = new GuiDataSourceCoordinatorVibeCode();
            DataContext = _coordinator.ViewModel;

            Loaded += HandleLoaded;
            Closed += HandleClosed;
        }

        private async void HandleLoaded(object sender, RoutedEventArgs e)
        {
            await _coordinator.InitializeAsync();
            await _coordinator.StartAsync();
            UpdateAutoToggleButtonContent();
        }

        private void HandleClosed(object? sender, EventArgs e)
        {
            _coordinator.Dispose();
        }

        private async void AutoToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_coordinator.IsRunning)
            {
                await _coordinator.StopAsync();
                UpdateAutoToggleButtonContent();
                return;
            }

            await _coordinator.StartAsync();
            UpdateAutoToggleButtonContent();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await _coordinator.RefreshOnceAsync();
        }

        private void UpdateAutoToggleButtonContent()
        {
            AutoToggleButton.Content = _coordinator.IsRunning ? "暂停自动刷新" : "启动自动刷新";
        }
    }
}
