# GuiDataSourceCoordinatorVibeCodeLesson

## 教案定位

这份教案对应本次“GUI 接入真实调度器，但仍保留分析余地”的改动。
目标不是一次学完全部 Modbus 通信细节，而是先建立一条清楚的阅读路线：

1. GUI 按钮和窗口生命周期做什么
2. GUI 如何决定走真实链路还是模拟链路
3. 真实链路如何创建调度器
4. 调度器如何一路把结果推到 ViewModel 和界面

---

## 第一课：先看 GUI，不急着看通信层

### 建议阅读文件
- `GUI.xaml`
- `GUI.xaml.cs`

### 学习目标
- 看懂窗口启动时做了哪三件事。
- 看懂“自动刷新按钮”和“手动刷新按钮”分别调用了什么。
- 建立一个非常重要的认识：`GUI.xaml.cs` 现在只负责交互，不负责串口和调度器创建。

---

## 第二课：看协调层，理解“数据源选择”

### 建议阅读文件
- `GuiDataSourceCoordinatorVibeCode.cs`

### 学习目标
- 看懂 `InitializeAsync()` 是整个接线链路的第一入口。
- 看懂协调层只做“选路”和“转发”，不直接负责创建真实调度器。
- 理解为什么 `StartAsync()`、`StopAsync()`、`RefreshOnceAsync()` 值得单独存在。

### 建议阅读顺序
1. `InitializeAsync`
2. `StartAsync`
3. `RefreshOnceAsync`
4. `Dispose`

---

## 第三课：看真实工厂，理解“真实链路怎么被造出来”

### 建议阅读文件
- `GuiRealSchedulerFactoryVibeCode.cs`
- `PollSchedulerVibeCode.cs`

### 学习目标
- 看懂串口探测与默认参数准备的最小策略。
- 看懂为什么真实工厂只“创建”，不“启动”。
- 理解工厂类在学习型项目中的意义：把复杂创建过程集中成一个可分析边界。

---

## 第四课：看 ViewModel，理解为什么真实接入后还要处理线程

### 建议阅读文件
- `DeviceMonitorViewModelVibeCode.cs`

### 学习目标
- 理解为什么真实轮询回调可能来自后台线程。
- 理解为什么 WPF 的 `ObservableCollection` 不能随便从后台线程更新。
- 看懂 `RunOnUiThread(...)` 的存在意义。

---

## 第五课：对照模拟链路和真实链路

### 建议阅读文件
- `GuiMonitorSimulationVibeCode.cs`
- `GuiDataSourceCoordinatorVibeCode.cs`
- `GuiRealSchedulerFactoryVibeCode.cs`

### 学习目标
- 看到“模拟链路”和“真实链路”其实都最终流向同一个 ViewModel。
- 理解这就是分层设计的收益：界面消费的是统一结果，而不是直接消费底层差异。
- 建立“先统一出口，再替换入口”的架构感觉。

---

## 动手练习建议

### 练习 1
把 `GuiRealSchedulerFactoryVibeCode` 中的默认 `SlaveAddress` 改成另一个值，然后观察真实设备联调时会出现什么变化。

### 练习 2
在 `GuiDataSourceCoordinatorVibeCode` 中增加一个“强制只用模拟模式”的布尔开关，并验证 GUI 是否仍能正常工作。

### 练习 3
为真实工厂增加一个“返回当前检测到的全部串口名”的方法，然后思考未来应该怎么做一个简单端口选择 UI。

### 练习 4
沿着 `RefreshOnceAsync()` 一路追踪到 `PollMissionSnapshotReaderVibeCode.ReadSnapshot(...)`，画出你自己的调用链。

---

## 本次改动最值得记住的一句话

这次不是把真实通信“塞进 GUI”，而是把 GUI 连接到了一个可继续拆、可继续学、可继续替换的真实链路入口上。
