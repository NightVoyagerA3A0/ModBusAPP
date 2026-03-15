# DeviceMonitorViewModelVibeCodeQuestions

## 问题 1
为什么 `DeviceMonitorViewModelVibeCode` 不直接依赖 `SerialPort`、`ModbusSerialMaster` 或寄存器地址，而是只依赖调度器和虚设备快照？

### 思考方向
- ViewModel 在 MVVM 中的职责到底是什么。
- 如果让 VM 直接操作通信层，会带来哪些测试和维护问题。
- 为什么表现层更适合消费“状态结果”而不是“底层通信对象”。

## 问题 2
为什么界面点位列表要用 `ObservableCollection<DevicePointItemVibeCode>`，而不是直接把字典丢给 XAML？

### 思考方向
- WPF 绑定里，集合变化通知和属性变化通知分别解决什么问题。
- 为什么“名称 + 文本值”结构更贴近界面展示，而不是底层业务结构。
- 当后续要加排序、分组、颜色或单位时，这种包装对象有什么好处。

## 问题 3
`AttachScheduler(...)` 这种绑定方法体现了什么设计思想？为什么 ViewModel 不在构造函数里自己 new 一个调度器？

### 思考方向
- 依赖注入和主动 new 对象之间的区别是什么。
- 为什么界面层对象最好不要自己决定底层通信怎么创建。
- 这种设计对测试替身、模拟数据和未来扩展有什么帮助。
