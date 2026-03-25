using Device;
using Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace Components
{
    /// <summary>
    /// 表示一个设备模型在运行期的核心业务对象。
    /// 设计目的：把声明式设备模型转换成“可读取、可映射、可回填”的运行时结构，
    /// 让轮询层只负责读取，让界面层只负责展示。
    ///
    /// 面试问题：
    /// 1. 为什么运行时绑定对象通常要和原始设备声明对象分离？
    /// 2. 反射扫描为什么适合在构造阶段预处理，而不是每次轮询都重新执行？
    /// 3. 在工业通信项目中，映射层与通信层分离有什么好处？
    /// </summary>
    /// <typeparam name="TDevice">
    /// 输入参数模式：设备模型类型。
    /// 该泛型参数必须实现 <see cref="IDeviceModel"/>，以便运行时在 Custom 拼接场景下调用设备级解释逻辑。
    /// </typeparam>
    /// <remarks>
    /// 行为模式：构造型 + 预解析型 + 回填型。
    /// 该类会在构造阶段完成点位扫描与读取计划生成，后续通过 <see cref="ApplySnapshot"/> 执行数据映射并修改设备实例状态。
    /// 该类不直接依赖串口、主站或调度器，因此可以被命令行、WPF 和测试代码共享复用。
    /// </remarks>
    public sealed class ModbusDeviceRuntime<TDevice> where TDevice : class, IDeviceModel
    {
        private readonly TDevice _device;
        private readonly ReadOnlyCollection<RegisterPointBinding> _registerPoints;
        private readonly ReadOnlyCollection<CoilPointBinding> _coilPoints;

        /// <summary>
        /// 获取当前运行时绑定的设备实例。
        /// 该对象是本运行时后续持续回填值的目标，不是一次性输入参数。
        /// </summary>
        public TDevice Device => _device;

        /// <summary>
        /// 获取当前设备模型中的全部寄存器点位绑定集合。
        /// 该集合已经完成反射解析和地址排序，适合调试、查看和生成读取计划。
        /// </summary>
        public IReadOnlyList<RegisterPointBinding> RegisterPoints => _registerPoints;

        /// <summary>
        /// 获取当前设备模型中的全部线圈点位绑定集合。
        /// </summary>
        public IReadOnlyList<CoilPointBinding> CoilPoints => _coilPoints;

        /// <summary>
        /// 获取当前设备模型对应的读取计划。
        /// 该计划会尽量把连续地址合并，以减少上层 Modbus 读操作次数。
        /// </summary>
        public ModbusReadPlan ReadPlan { get; }

        /// <summary>
        /// 初始化一个设备运行时对象，并在构造阶段完成点位扫描与读取计划生成。
        /// </summary>
        /// <param name="device">
        /// 输入参数模式：设备模型实例。
        /// 该参数是整个运行时持续依赖的上下文对象，不允许为 null；调用方应保证其公开属性和特性定义已经完整可用。
        /// </param>
        /// <remarks>
        /// 行为模式：构造型 + 预处理型。
        /// 构造函数不仅保存设备引用，还会立刻扫描公开属性、建立运行时绑定并生成读取计划。
        /// </remarks>
        /// <exception cref="ArgumentNullException">当设备实例为空时抛出。</exception>
        public ModbusDeviceRuntime(TDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));

            // 反射扫描放在构造阶段做，目的是把高成本的元数据解析提前一次性完成，
            // 避免在每轮轮询中重复扫描属性和特性。
            (_registerPoints, _coilPoints) = BuildBindings(device);
            ReadPlan = ModbusReadPlan.Create(_registerPoints, _coilPoints);
        }

        /// <summary>
        /// 将一次采集得到的原始快照应用到当前设备实例。
        /// </summary>
        /// <param name="snapshot">
        /// 输入参数模式：原始数据快照。
        /// 该参数承载某次采集周期得到的寄存器和线圈值，不允许为 null；允许地址部分缺失，但缺失点位会生成告警而非整体失败。
        /// </param>
        /// <returns>
        /// 返回模式：映射摘要对象。
        /// 返回值同时包含成功写回的属性和值以及处理过程中产生的告警，因此允许部分成功。
        /// </returns>
        /// <remarks>
        /// 行为模式：映射型 + 回填型。
        /// 该方法会读取快照、解析点位、执行映射公式并写回设备实例，因此会修改对象状态。
        /// </remarks>
        /// <exception cref="ArgumentNullException">当快照对象为空时抛出。</exception>
        public ModbusApplyResult ApplySnapshot(ModbusSourceSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var appliedValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            var warnings = new List<string>();

            foreach (var point in _registerPoints)
            {
                if (!TryResolveRegisterValue(point, snapshot, out var value, out var warning))
                {
                    warnings.Add(warning ?? $"寄存器点位 {point.Property.Name} 映射失败。");
                    continue;
                }

                point.AssignValue(value!);
                appliedValues[point.Property.Name] = value;
            }

            foreach (var point in _coilPoints)
            {
                if (!snapshot.TryGetCoil(point.Attribute.Address, out var coilRaw))
                {
                    warnings.Add($"缺少线圈地址 {point.Attribute.Address}，属性 {point.Property.Name} 未更新。");
                    continue;
                }

                var mapped = ApplyCoilMapping(point.Attribute, coilRaw);
                point.AssignValue(mapped);
                appliedValues[point.Property.Name] = mapped;
            }

            return new ModbusApplyResult(appliedValues, warnings);
        }

        /// <summary>
        /// 获取当前设备实例上已经持有的属性值快照。
        /// </summary>
        /// <returns>
        /// 返回模式：只读状态字典。
        /// 返回值表达的是设备对象当前内存状态，而不是一次新的通信结果，适合调试、日志或界面初始化场景。
        /// </returns>
        /// <remarks>
        /// 行为模式：查询型。
        /// 该方法不修改对象状态，也不依赖外部资源。
        /// </remarks>
        public IReadOnlyDictionary<string, object?> SnapshotCurrentValues()
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var point in _registerPoints)
            {
                result[point.Property.Name] = point.Property.GetValue(_device);
            }

            foreach (var point in _coilPoints)
            {
                result[point.Property.Name] = point.Property.GetValue(_device);
            }

            return new ReadOnlyDictionary<string, object?>(result);
        }

        /// <summary>
        /// 扫描设备实例上的特性定义，并生成运行期绑定集合。
        /// </summary>
        /// <param name="device">
        /// 输入参数模式：待解析的设备实例。
        /// 该参数既是特性扫描来源，也是后续属性回填的目标对象。
        /// </param>
        /// <returns>
        /// 返回模式：双集合元组。
        /// 返回值将寄存器绑定和线圈绑定分开组织，便于后续分别规划读取和执行映射。
        /// </returns>
        /// <remarks>
        /// 行为模式：解析型 + 构建型。
        /// 该方法会反射扫描公开属性、校验属性类型、创建运行时绑定对象并按地址排序。
        /// </remarks>
        private static (ReadOnlyCollection<RegisterPointBinding>, ReadOnlyCollection<CoilPointBinding>) BuildBindings(TDevice device)
        {
            var registerPoints = new List<RegisterPointBinding>();
            var coilPoints = new List<CoilPointBinding>();

            var properties = device.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(static property => property.CanRead && property.CanWrite)
                .OrderBy(static property => property.MetadataToken);

            foreach (var property in properties)
            {
                var registerAttribute = property.GetCustomAttribute<ModbusDevicePointAttribute>();
                if (registerAttribute is not null)
                {
                    ValidateRegisterProperty(property);
                    registerPoints.Add(new RegisterPointBinding(device, property, registerAttribute));
                    continue;
                }

                var coilAttribute = property.GetCustomAttribute<ModbusDeviceCoilAttribute>();
                if (coilAttribute is not null)
                {
                    ValidateCoilProperty(property);
                    coilPoints.Add(new CoilPointBinding(device, property, coilAttribute));
                }
            }

            return
            (
                new ReadOnlyCollection<RegisterPointBinding>(registerPoints.OrderBy(static point => point.Attribute.Address).ToList()),
                new ReadOnlyCollection<CoilPointBinding>(coilPoints.OrderBy(static point => point.Attribute.Address).ToList())
            );
        }

        /// <summary>
        /// 尝试从原始快照中解析某一个寄存器点位，并将其转换成属性可接收的目标值。
        /// </summary>
        /// <param name="point">
        /// 输入参数模式：单个寄存器点位绑定对象。
        /// 该参数包含目标属性、特性定义和设备实例三类上下文，不是简单地址值。
        /// </param>
        /// <param name="snapshot">
        /// 输入参数模式：原始快照。
        /// 该参数是本方法的原始数据源，负责提供寄存器地址到原始字值的查询能力。
        /// </param>
        /// <param name="resolved">
        /// 输出参数模式：已完成类型适配的目标值。
        /// 当方法返回 true 时，该参数可直接写入目标属性。
        /// </param>
        /// <param name="warning">
        /// 输出参数模式：失败说明文本。
        /// 当方法返回 false 时，该参数描述当前点位未能更新的原因；成功时通常为空。
        /// </param>
        /// <returns>
        /// 返回模式：Try 风格状态标记。
        /// true 表示当前点位成功解析，false 表示当前点位失败，但不必然表示整个快照处理失败。
        /// </returns>
        /// <remarks>
        /// 行为模式：点位级解码型。
        /// 该方法会读取连续寄存器、判断是否需要 Custom 拼接、执行映射公式并产出待写回值。
        /// </remarks>
        private static bool TryResolveRegisterValue(
            RegisterPointBinding point,
            ModbusSourceSnapshot snapshot,
            out object? resolved,
            out string? warning)
        {
            warning = null;
            resolved = null;

            if (!snapshot.TryGetRegisters(point.Attribute.Address, point.RegisterCount, out var words))
            {
                warning = $"缺少寄存器地址 {point.Attribute.Address} 起始的 {point.RegisterCount} 个字，属性 {point.Property.Name} 未更新。";
                return false;
            }

            double rawValue;
            if (point.Attribute.CustomMapping || point.Attribute.RegisterValueType == RegisterValueType.Custom)
            {
                if (!point.TryResolveCustomValue(words, out rawValue))
                {
                    warning = $"属性 {point.Property.Name} 需要自定义拼接，但设备模型未成功处理原始字节。";
                    return false;
                }
            }
            else
            {
                rawValue = ModbusRawValueDecoder.Decode(words, point.Attribute.RegisterValueType);
            }

            var mapped = ApplyRegisterMapping(point.Attribute, rawValue);
            resolved = ConvertToPropertyType(mapped, point.Property.PropertyType);
            return true;
        }

        /// <summary>
        /// 根据线圈特性的声明规则，把原始布尔值转换成最终回填值。
        /// </summary>
        /// <param name="attribute">
        /// 输入参数模式：线圈点位的声明特性。
        /// 该参数提供反转规则，是运行时执行布尔映射时唯一需要的声明来源。
        /// </param>
        /// <param name="rawValue">
        /// 输入参数模式：快照中的原始线圈值。
        /// 该值尚未应用 Reverse 规则。
        /// </param>
        /// <returns>
        /// 返回模式：可直接回填到目标属性的布尔结果。
        /// </returns>
        /// <remarks>
        /// 行为模式：映射型。
        /// 该方法只负责应用声明层的反转规则，不访问外部资源，也不修改运行时状态。
        /// </remarks>
        private static bool ApplyCoilMapping(ModbusDeviceCoilAttribute attribute, bool rawValue)
        {
            return attribute.Reverse ? !rawValue : rawValue;
        }

        /// <summary>
        /// 根据寄存器特性的声明参数，把基础数值转换成业务值。
        /// </summary>
        /// <param name="attribute">
        /// 输入参数模式：寄存器点位的声明特性。
        /// 该参数提供偏移量、缩放系数和 Custom 标记，用于决定运行时如何执行映射。
        /// </param>
        /// <param name="rawValue">
        /// 输入参数模式：已经完成寄存器拼接后的基础数值。
        /// 该值仍属于底层解释结果，尚未应用业务公式。
        /// </param>
        /// <returns>
        /// 返回模式：业务映射后的浮点结果。
        /// 当返回成功时，结果可继续被转换为属性所需的 CLR 类型。
        /// </returns>
        /// <remarks>
        /// 行为模式：映射型。
        /// 如果声明层要求使用 Custom 映射，则不应再进入本方法，而应由设备模型自定义处理。
        /// </remarks>
        private static float ApplyRegisterMapping(ModbusDevicePointAttribute attribute, double rawValue)
        {
            if (attribute.CustomMapping)
            {
                throw new InvalidOperationException($"{nameof(ModbusDevicePointAttribute)} 已声明 CustomMapping，应由设备模型提供自定义映射结果。");
            }

            var value = rawValue + attribute.Offset;
            var mapped = (attribute.A1 * value)
                + (attribute.A2 * Math.Pow(value, 2))
                + (attribute.A3 * Math.Pow(value, 3))
                + attribute.B;

            return (float)mapped;
        }

        /// <summary>
        /// 把统一的浮点映射结果转换成目标属性真正需要的类型。
        /// </summary>
        /// <param name="mapped">
        /// 输入参数模式：映射后的浮点结果。
        /// 该参数来自寄存器公式计算之后的统一数值表示。
        /// </param>
        /// <param name="propertyType">
        /// 输入参数模式：目标属性类型。
        /// 该参数用于决定最后回填时应使用哪种 CLR 类型。
        /// </param>
        /// <returns>
        /// 返回模式：已适配的属性值对象。
        /// 返回值可被直接写入对应属性；当目标类型不在当前支持列表中时会抛出异常，而不是返回部分结果。
        /// </returns>
        /// <remarks>
        /// 行为模式：转换型。
        /// 该方法不访问外部资源，但会根据类型规则决定是否抛出不支持异常。
        /// </remarks>
        /// <exception cref="InvalidOperationException">当目标属性类型不在支持列表中时抛出。</exception>
        private static object ConvertToPropertyType(float mapped, Type propertyType)
        {
            var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (targetType == typeof(float))
            {
                return mapped;
            }

            if (targetType == typeof(double))
            {
                return (double)mapped;
            }

            if (targetType == typeof(short))
            {
                return Convert.ToInt16(mapped);
            }

            if (targetType == typeof(int))
            {
                return Convert.ToInt32(mapped);
            }

            if (targetType == typeof(long))
            {
                return Convert.ToInt64(mapped);
            }

            if (targetType == typeof(decimal))
            {
                return Convert.ToDecimal(mapped);
            }

            throw new InvalidOperationException($"暂不支持将寄存器结果写入属性类型 {propertyType.Name}。");
        }

        /// <summary>
        /// 验证某个属性是否适合作为寄存器点位承载属性。
        /// </summary>
        /// <param name="property">
        /// 输入参数模式：待验证的属性元数据。
        /// 该参数来自反射结果，必须是可读可写属性。
        /// </param>
        /// <remarks>
        /// 行为模式：校验型。
        /// 校验失败时通过异常阻止运行时继续构造，以尽早暴露错误设备定义。
        /// </remarks>
        /// <exception cref="InvalidOperationException">当属性类型不支持寄存器映射时抛出。</exception>
        private static void ValidateRegisterProperty(PropertyInfo property)
        {
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var supported = targetType == typeof(float)
                || targetType == typeof(double)
                || targetType == typeof(short)
                || targetType == typeof(int)
                || targetType == typeof(long)
                || targetType == typeof(decimal);

            if (!supported)
            {
                throw new InvalidOperationException($"属性 {property.Name} 使用寄存器点位，但类型 {property.PropertyType.Name} 不受支持。");
            }
        }

        /// <summary>
        /// 验证某个属性是否适合作为线圈点位承载属性。
        /// </summary>
        /// <param name="property">
        /// 输入参数模式：待验证的属性元数据。
        /// 该参数来自设备模型的公开属性反射结果。
        /// </param>
        /// <remarks>
        /// 行为模式：校验型。
        /// 线圈点位当前只支持布尔属性，因此校验失败时直接抛出异常。
        /// </remarks>
        /// <exception cref="InvalidOperationException">当属性类型不是 bool 时抛出。</exception>
        private static void ValidateCoilProperty(PropertyInfo property)
        {
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (targetType != typeof(bool))
            {
                throw new InvalidOperationException($"属性 {property.Name} 使用线圈点位，但类型 {property.PropertyType.Name} 不是 bool。");
            }
        }
    }
}
