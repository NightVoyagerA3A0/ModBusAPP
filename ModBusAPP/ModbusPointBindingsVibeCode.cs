using Device;
using Interfaces;
using System;
using System.Buffers.Binary;
using System.Reflection;

namespace Components
{
    /// <summary>
    /// 表示某一个寄存器属性在运行期的绑定信息。
    /// 设计目的：把“设备实例 + 属性信息 + 特性信息”组合起来，
    /// 形成更容易执行解码、映射和回填的运行时对象。
    ///
    /// 面试问题：
    /// 1. 为什么运行期绑定对象适合持有 PropertyInfo 和 Attribute，而不是只保存地址？
    /// 2. 当设备存在 Custom 拼接时，为什么应把解释权交回设备模型？
    /// 3. 运行期绑定对象相对原始特性声明多解决了什么问题？
    /// </summary>
    /// <remarks>
    /// 行为模式：绑定型。
    /// 该类本身不负责轮询，只负责为运行时提供回填和自定义解析所需的上下文。
    /// </remarks>
    public sealed class RegisterPointBinding
    {
        private readonly IDeviceModel _device;

        /// <summary>
        /// 获取当前点位绑定的目标属性。
        /// 该属性是最终回填值的写入目标。
        /// </summary>
        public PropertyInfo Property { get; }

        /// <summary>
        /// 获取当前点位绑定对应的寄存器特性描述。
        /// </summary>
        public ModbusDevicePointAttribute Attribute { get; }

        /// <summary>
        /// 获取该点位在读取时需要占用的寄存器数量。
        /// 该数量来自拼接类型推断，供读取计划生成和快照解析使用。
        /// </summary>
        public int RegisterCount => Attribute.JointTypes switch
        {
            jointTypes.Int16 => 1,
            jointTypes.BigEndian32 => 2,
            jointTypes.LittleEndian32 => 2,
            jointTypes.Custom => Math.Max(Attribute.Length, (short)1),
            _ => Math.Max(Attribute.Length, (short)1)
        };

        /// <summary>
        /// 初始化寄存器点位绑定对象。
        /// </summary>
        /// <param name="device">
        /// 输入参数模式：目标设备实例。
        /// 该参数用于属性回填以及 Custom 拼接时的设备级解释。
        /// </param>
        /// <param name="property">
        /// 输入参数模式：目标属性元数据。
        /// 该参数必须来自可读可写公开属性。
        /// </param>
        /// <param name="attribute">
        /// 输入参数模式：寄存器特性描述。
        /// 该参数提供地址、拼接类型和映射公式等声明信息。
        /// </param>
        /// <remarks>
        /// 行为模式：构造型。
        /// 构造函数只固化绑定关系，不执行解码或通信。
        /// </remarks>
        public RegisterPointBinding(IDeviceModel device, PropertyInfo property, ModbusDevicePointAttribute attribute)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        }

        /// <summary>
        /// 将一个已经完成映射的值写回当前绑定属性。
        /// </summary>
        /// <param name="value">
        /// 输入参数模式：待写入属性的结果值。
        /// 该参数应当已经完成类型适配，与目标属性类型兼容。
        /// </param>
        /// <remarks>
        /// 行为模式：回填型。
        /// 该方法会直接修改设备实例状态。
        /// </remarks>
        public void AssignValue(object value)
        {
            Property.SetValue(_device, value);
        }

        /// <summary>
        /// 尝试使用设备级自定义逻辑解析寄存器原始字数据。
        /// </summary>
        /// <param name="words">
        /// 输入参数模式：寄存器原始字数组。
        /// 该参数通常来自某次连续读取区间中的局部片段。
        /// </param>
        /// <param name="result">
        /// 输出参数模式：设备模型解析后的结果。
        /// 当返回 true 时，该参数表示设备级解释后的基础数值。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格状态标记。
        /// true 表示设备模型成功完成自定义解析，false 表示当前原始字数据未能被设备模型接受。
        /// </returns>
        /// <remarks>
        /// 行为模式：委托型。
        /// 该方法不自己定义 Custom 拼接规则，而是把解释权下放到设备模型的 <see cref="IDeviceModel.TryJoint(byte[], out double)"/>。
        /// </remarks>
        public bool TryResolveCustomValue(ushort[] words, out double result)
        {
            var bytes = ModbusRawValueDecoder.ToBigEndianBytes(words);
            return _device.TryJoint(bytes, out result);
        }
    }

    /// <summary>
    /// 表示某一个线圈属性在运行期的绑定信息。
    /// 设计目的：为线圈点位提供统一的属性回填入口和声明信息承载对象。
    /// </summary>
    /// <remarks>
    /// 行为模式：绑定型。
    /// 当前线圈点位逻辑相对简单，主要承担属性回填职责。
    /// </remarks>
    public sealed class CoilPointBinding
    {
        private readonly IDeviceModel _device;

        /// <summary>
        /// 获取当前绑定的目标属性。
        /// </summary>
        public PropertyInfo Property { get; }

        /// <summary>
        /// 获取当前绑定对应的线圈特性描述。
        /// </summary>
        public ModbusDeviceCoilAttribute Attribute { get; }

        /// <summary>
        /// 初始化线圈点位绑定对象。
        /// </summary>
        /// <param name="device">
        /// 输入参数模式：目标设备实例。
        /// 该参数是线圈值最终回填的对象上下文。
        /// </param>
        /// <param name="property">
        /// 输入参数模式：目标属性元数据。
        /// </param>
        /// <param name="attribute">
        /// 输入参数模式：线圈特性描述。
        /// 该参数提供地址和反转等声明信息。
        /// </param>
        public CoilPointBinding(IDeviceModel device, PropertyInfo property, ModbusDeviceCoilAttribute attribute)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        }

        /// <summary>
        /// 将线圈值回填到设备实例。
        /// </summary>
        /// <param name="value">
        /// 输入参数模式：待写入的布尔值。
        /// 该值通常已经经过特性中的 Mapping 处理。
        /// </param>
        /// <remarks>
        /// 行为模式：回填型。
        /// 该方法会修改设备实例状态。
        /// </remarks>
        public void AssignValue(bool value)
        {
            Property.SetValue(_device, value);
        }
    }

    /// <summary>
    /// 提供寄存器原始字数据的基础解码能力。
    /// 设计目的：把通用可推断的寄存器拼接规则集中封装，避免运行时主流程反复判断字节顺序细节。
    ///
    /// 面试问题：
    /// 1. 什么情况下适合把解码逻辑提炼为独立工具类？
    /// 2. BigEndian32 和 LittleEndian32 的差异本质上是什么？
    /// 3. 为什么 Custom 类型不能继续让基础解码器“猜”下去？
    /// </summary>
    /// <remarks>
    /// 行为模式：工具型 + 解码型。
    /// 该类只负责“基础层能安全推断”的规则；一旦进入 Custom 类型，就必须回到设备模型自身处理。
    /// </remarks>
    internal static class ModbusRawValueDecoder
    {
        /// <summary>
        /// 按指定拼接类型解析寄存器原始值。
        /// </summary>
        /// <param name="words">
        /// 输入参数模式：寄存器原始字数组。
        /// 该参数不允许为空，且长度应满足目标拼接类型的最小要求。
        /// </param>
        /// <param name="jointType">
        /// 输入参数模式：拼接类型。
        /// 该参数决定如何理解字序和最终基础数值。
        /// </param>
        /// <returns>
        /// 返回模式：基础数值。
        /// 返回值是尚未经过业务映射公式处理的原始解释结果。
        /// </returns>
        /// <remarks>
        /// 行为模式：解码型。
        /// 该方法只负责通用拼接，不处理偏移、缩放或业务公式。
        /// </remarks>
        public static double Decode(ushort[] words, jointTypes jointType)
        {
            if (words is null || words.Length == 0)
            {
                throw new ArgumentException("寄存器原始数据不能为空。", nameof(words));
            }

            return jointType switch
            {
                jointTypes.Int16 => unchecked((short)words[0]),
                jointTypes.BigEndian32 => DecodeBigEndian32(words),
                jointTypes.LittleEndian32 => DecodeLittleEndian32(words),
                jointTypes.Custom => throw new NotSupportedException("Custom 类型应当由设备模型自行拼接。"),
                _ => throw new NotSupportedException($"不支持的 jointTypes: {jointType}")
            };
        }

        /// <summary>
        /// 把寄存器字数组转换为大端字节数组。
        /// </summary>
        /// <param name="words">
        /// 输入参数模式：寄存器原始字数组。
        /// 该参数通常用于设备级自定义拼接前的统一导出。
        /// </param>
        /// <returns>
        /// 返回模式：大端字节数组。
        /// 返回值按寄存器顺序依次展开为高字节在前的标准字节序列。
        /// </returns>
        public static byte[] ToBigEndianBytes(ushort[] words)
        {
            var bytes = new byte[words.Length * 2];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * 2, 2), words[index]);
            }

            return bytes;
        }

        /// <summary>
        /// 按 AB CD 的顺序把两个寄存器解析成 32 位整数。
        /// </summary>
        /// <param name="words">
        /// 输入参数模式：寄存器原始字数组。
        /// 该参数长度至少应为 2。
        /// </param>
        /// <returns>
        /// 返回模式：解析后的 32 位整数。
        /// </returns>
        private static int DecodeBigEndian32(ushort[] words)
        {
            if (words.Length < 2)
            {
                throw new ArgumentException("BigEndian32 需要两个寄存器。", nameof(words));
            }

            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(0, 2), words[0]);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(2, 2), words[1]);
            return BinaryPrimitives.ReadInt32BigEndian(bytes);
        }

        /// <summary>
        /// 按 CD AB 的顺序把两个寄存器解析成 32 位整数。
        /// </summary>
        /// <param name="words">
        /// 输入参数模式：寄存器原始字数组。
        /// 该参数长度至少应为 2。
        /// </param>
        /// <returns>
        /// 返回模式：解析后的 32 位整数。
        /// </returns>
        private static int DecodeLittleEndian32(ushort[] words)
        {
            if (words.Length < 2)
            {
                throw new ArgumentException("LittleEndian32 需要两个寄存器。", nameof(words));
            }

            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(0, 2), words[1]);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(2, 2), words[0]);
            return BinaryPrimitives.ReadInt32BigEndian(bytes);
        }
    }
}
