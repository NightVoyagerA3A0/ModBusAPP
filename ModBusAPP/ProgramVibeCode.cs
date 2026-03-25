using System;
using System.Windows;

namespace ModBusAPP
{
    /// <summary>
    /// 提供当前 WPF 应用的启动入口。
    /// 设计目的：把原先偏控制台实验性质的入口与正式界面入口拆开，
    /// 让项目在学习 GUI、ViewModel 和调度链路时，能够直接启动到监控界面。
    ///
    /// 面试问题：
    /// 1. WPF 程序为什么通常要求入口线程带有 STAThread 特性？
    /// 2. 为什么把“应用入口”与“业务初始化逻辑”拆开，会更利于后续扩展？
    /// 3. 在学习型项目中，入口文件保持简单有什么价值？
    /// </summary>
    public static class ProgramVibeCode
    {
        /// <summary>
        /// 启动当前 WPF 应用并显示主窗口。
        /// </summary>
        /// <remarks>
        /// 行为模式：应用启动型。
        /// 该方法只负责创建 Application 与主窗口，不承载具体业务逻辑，
        /// 从而避免入口层和通信层、映射层直接耦合。
        /// </remarks>
        [STAThread]
        public static void Main()
        {
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };

            var window = new GUI();
            application.Run(window);
        }
    }
}
