# GuiMonitorSimulationVibeCodeQuestions

## 问题 1
为什么 `GuiMonitorSimulationVibeCode` 要生成 `ModbusSourceSnapshot` 再交给 `ModbusDeviceRuntime`，而不是直接改 `DeviceMonitorViewModelVibeCode`？

### 思考方向
- 这样做是否真正复用了项目里的映射链路。
- 如果未来改了设备点位定义，哪种方式更不容易和真实业务脱节。
- “演示数据”是否也应该走真实业务边界对象。

## 问题 2
`DispatcherTimer` 为什么适合当前这个 GUI 演示场景？

### 思考方向
- 它和 `Task.Delay` + 后台线程更新 UI 的差别是什么。
- 为什么 WPF 控件更新通常更希望发生在 UI 线程。
- 当前这个学习型演示对“精确计时”和“线程简单性”哪一个更重要。

## 问题 3
为什么模拟器每隔几个周期故意制造一次缺失寄存器的快照？

### 思考方向
- 正常路径和异常路径都需要被界面提前看见。
- 工业通信程序里，“部分成功 + 告警”为什么很常见。
- 这种教学型模拟如何帮助后续联调真实设备时更快定位问题。
