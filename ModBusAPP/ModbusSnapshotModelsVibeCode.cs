using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Components
{
    /// <summary>
    /// 表示一次原始快照应用到设备实例后的结果。
    /// 设计目的：把“成功写回了哪些属性”和“处理过程中产生了哪些告警”统一封装为一个稳定结果对象，
    /// 便于界面、日志和调试代码复用。
    ///
    /// 面试问题：
    /// 1. 为什么工业通信中的一次处理结果通常不适合只用 bool 表示？
    /// 2. 结果对象和异常分别适合表达哪些信息？
    /// 3. 什么叫“允许部分成功”的返回模型？
    /// </summary>
    /// <remarks>
    /// 行为模式：结果封装型。
    /// 该类不替代异常，而是用于表达本次应用过程的业务摘要，尤其适合局部成功、局部失败的场景。
    /// </remarks>
    public sealed class ModbusApplyResult
    {
        /// <summary>
        /// 获取本次成功写入设备实例的属性值集合。
        /// 键为属性名，值为最终写入的对象值。
        /// </summary>
        public IReadOnlyDictionary<string, object?> AppliedValues { get; }

        /// <summary>
        /// 获取本次应用过程中产生的告警信息集合。
        /// 告警通常表示某个点位未更新，但不一定意味着整个周期失败。
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// 指示本次应用是否产生了告警。
        /// </summary>
        public bool HasWarnings => Warnings.Count > 0;

        /// <summary>
        /// 初始化一个映射摘要结果对象。
        /// </summary>
        /// <param name="appliedValues">
        /// 输入参数模式：成功写回的属性值集合。
        /// 该参数不允许为 null，键应为属性名，值为最终写入值。
        /// </param>
        /// <param name="warnings">
        /// 输入参数模式：告警集合。
        /// 该参数不允许为 null；当没有告警时应传入空集合，而不是 null。
        /// </param>
        /// <remarks>
        /// 行为模式：构造型。
        /// 构造函数只负责固定结果内容，不执行额外业务逻辑。
        /// </remarks>
        public ModbusApplyResult(IDictionary<string, object?> appliedValues, IList<string> warnings)
        {
            if (appliedValues is null)
            {
                throw new ArgumentNullException(nameof(appliedValues));
            }

            if (warnings is null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            AppliedValues = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(appliedValues));
            Warnings = new ReadOnlyCollection<string>(new List<string>(warnings));
        }
    }

    /// <summary>
    /// 表示一次轮询或通信调用中采集到的原始 Modbus 数据快照。
    /// 设计目的：用统一的“地址 -> 值”结构承载寄存器和线圈结果，
    /// 让上层读取过程与下层映射过程解耦。
    ///
    /// 面试问题：
    /// 1. 为什么快照对象适合作为通信层与映射层之间的边界对象？
    /// 2. 地址字典结构相对原始数组有哪些优缺点？
    /// 3. 什么情况下应当把“读取结果”和“设备对象当前状态”分开保存？
    /// </summary>
    /// <remarks>
    /// 行为模式：数据容器型 + 查询型。
    /// 该类不负责读取设备，只负责保存某一次读取已经拿到的原始数据，并提供按地址查询的方法。
    /// </remarks>
    public sealed class ModbusSourceSnapshot
    {
        private readonly Dictionary<int, ushort> _registers;
        private readonly Dictionary<int, bool> _coils;

        /// <summary>
        /// 获取寄存器原始值字典的只读视图。
        /// 键为寄存器地址，值为 16 位无符号字。
        /// </summary>
        public IReadOnlyDictionary<int, ushort> Registers => _registers;

        /// <summary>
        /// 获取线圈原始值字典的只读视图。
        /// 键为线圈地址，值为布尔量。
        /// </summary>
        public IReadOnlyDictionary<int, bool> Coils => _coils;

        /// <summary>
        /// 初始化一个原始快照对象。
        /// </summary>
        /// <param name="registers">
        /// 输入参数模式：寄存器地址值集合。
        /// 该参数允许为空；为空时表示本次快照不包含寄存器数据。
        /// </param>
        /// <param name="coils">
        /// 输入参数模式：线圈地址值集合。
        /// 该参数允许为空；为空时表示本次快照不包含线圈数据。
        /// </param>
        /// <remarks>
        /// 行为模式：构造型。
        /// 构造函数会复制输入集合，避免外部继续修改传入对象后污染当前快照。
        /// </remarks>
        public ModbusSourceSnapshot(IDictionary<int, ushort>? registers = null, IDictionary<int, bool>? coils = null)
        {
            _registers = registers is null
                ? new Dictionary<int, ushort>()
                : new Dictionary<int, ushort>(registers);

            _coils = coils is null
                ? new Dictionary<int, bool>()
                : new Dictionary<int, bool>(coils);
        }

        /// <summary>
        /// 根据连续数组创建一个快照对象。
        /// </summary>
        /// <param name="registerStartAddress">
        /// 输入参数模式：寄存器起始地址。
        /// 该参数用于解释寄存器数组的逻辑地址区间。
        /// </param>
        /// <param name="registers">
        /// 输入参数模式：连续寄存器数组。
        /// 该参数允许为 null；为 null 表示本次没有寄存器数组数据。
        /// </param>
        /// <param name="coilStartAddress">
        /// 输入参数模式：线圈起始地址。
        /// 该参数用于解释线圈数组的逻辑地址区间。
        /// </param>
        /// <param name="coils">
        /// 输入参数模式：连续线圈数组。
        /// 该参数允许为 null；为 null 表示本次没有线圈数组数据。
        /// </param>
        /// <returns>
        /// 返回模式：地址字典化后的快照对象。
        /// 返回值是统一数据容器，而不是原数组本身。
        /// </returns>
        /// <remarks>
        /// 行为模式：转换型工厂方法。
        /// 该方法不会访问设备，只负责把已取得的连续数组转换成当前组件统一使用的快照结构。
        /// </remarks>
        public static ModbusSourceSnapshot FromArrays(
            int registerStartAddress,
            ushort[]? registers,
            int coilStartAddress = 0,
            bool[]? coils = null)
        {
            var registerMap = new Dictionary<int, ushort>();
            var coilMap = new Dictionary<int, bool>();

            if (registers is not null)
            {
                for (var index = 0; index < registers.Length; index++)
                {
                    registerMap[registerStartAddress + index] = registers[index];
                }
            }

            if (coils is not null)
            {
                for (var index = 0; index < coils.Length; index++)
                {
                    coilMap[coilStartAddress + index] = coils[index];
                }
            }

            return new ModbusSourceSnapshot(registerMap, coilMap);
        }

        /// <summary>
        /// 尝试读取某一个寄存器地址的值。
        /// </summary>
        /// <param name="address">
        /// 输入参数模式：目标寄存器地址。
        /// 该参数是快照中的逻辑地址，不是数组偏移。
        /// </param>
        /// <param name="value">
        /// 输出参数模式：对应地址的寄存器值。
        /// 当返回 true 时，该参数承载实际字值。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格状态标记。
        /// true 表示快照中存在该地址，false 表示该地址缺失。
        /// </returns>
        public bool TryGetRegister(int address, out ushort value) => _registers.TryGetValue(address, out value);

        /// <summary>
        /// 尝试读取某一个线圈地址的值。
        /// </summary>
        /// <param name="address">
        /// 输入参数模式：目标线圈地址。
        /// </param>
        /// <param name="value">
        /// 输出参数模式：对应线圈值。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格状态标记。
        /// true 表示该地址存在，false 表示该地址缺失。
        /// </returns>
        public bool TryGetCoil(int address, out bool value) => _coils.TryGetValue(address, out value);

        /// <summary>
        /// 尝试连续读取一段寄存器值。
        /// </summary>
        /// <param name="startAddress">
        /// 输入参数模式：起始地址。
        /// 该参数表示连续区间的逻辑起点。
        /// </param>
        /// <param name="count">
        /// 输入参数模式：连续读取数量。
        /// 该参数必须大于零，表示期望完整覆盖的字数量。
        /// </param>
        /// <param name="values">
        /// 输出参数模式：连续寄存器数组。
        /// 当方法返回 true 时，该参数按地址顺序给出全部结果；失败时为空数组。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格状态标记。
        /// true 表示所有地址都存在，false 表示存在缺口，因此不返回部分连续数组。
        /// </returns>
        /// <remarks>
        /// 行为模式：查询型。
        /// 该方法要求地址区间完整存在，适合寄存器拼接和批量解码场景。
        /// </remarks>
        public bool TryGetRegisters(int startAddress, int count, out ushort[] values)
        {
            values = Array.Empty<ushort>();
            if (count <= 0)
            {
                return false;
            }

            var buffer = new ushort[count];
            for (var index = 0; index < count; index++)
            {
                if (!_registers.TryGetValue(startAddress + index, out buffer[index]))
                {
                    return false;
                }
            }

            values = buffer;
            return true;
        }
    }
}
