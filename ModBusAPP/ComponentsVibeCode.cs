using Device;
using Interfaces;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace Components
{
    /// <summary>
    /// 表示一个设备模型在运行期的核心业务对象。
    /// 该类负责完成三件事情：
    /// 1. 扫描设备模型上的 Modbus 特性，建立点位绑定；
    /// 2. 根据点位地址生成批量读取计划，方便上层轮询器复用；
    /// 3. 接收一次采集快照，并把寄存器或线圈结果映射回设备实例。
    /// </summary>
    /// <typeparam name="TDevice">
    /// 设备模型类型。该类型必须实现 <see cref="IDeviceModel"/>，
    /// 以便在 Custom 拼接场景下提供设备级的原始数据处理能力。
    /// </typeparam>
    /// <remarks>
    /// 设计意图：
    /// 该类并不直接负责串口打开、关闭、重试或轮询调度。
    /// 它更像是“映射运行时”，负责把声明式模型转成可执行的业务结构。
    /// 这样后续无论接控制台轮询、WPF 绑定还是后台任务，都可以共享这一层。
    /// </remarks>
    public sealed class ModbusDeviceRuntime<TDevice> where TDevice : class, IDeviceModel
    {
        /// <summary>
        /// 当前运行时绑定的设备实例。
        /// 所有映射成功的值最终都会写回到这个对象中。
        /// </summary>
        private readonly TDevice _device;

        /// <summary>
        /// 寄存器点位绑定缓存。
        /// 这里缓存的是运行期结构，而不是原始特性本身，便于后续快速处理。
        /// </summary>
        private readonly ReadOnlyCollection<RegisterPointBinding> _registerPoints;

        /// <summary>
        /// 线圈点位绑定缓存。
        /// </summary>
        private readonly ReadOnlyCollection<CoilPointBinding> _coilPoints;

        /// <summary>
        /// 获取当前运行时绑定的设备实例。
        /// </summary>
        public TDevice Device => _device;

        /// <summary>
        /// 获取当前设备模型中的全部寄存器点位绑定信息。
        /// 上层可通过它查看每个寄存器属性在运行期的解析结果。
        /// </summary>
        public IReadOnlyList<RegisterPointBinding> RegisterPoints => _registerPoints;

        /// <summary>
        /// 获取当前设备模型中的全部线圈点位绑定信息。
        /// </summary>
        public IReadOnlyList<CoilPointBinding> CoilPoints => _coilPoints;

        /// <summary>
        /// 获取当前设备模型对应的读取计划。
        /// 读取计划会把连续地址合并成段，方便上层减少 Modbus 读操作次数。
        /// </summary>
        public ModbusReadPlan ReadPlan { get; }

        /// <summary>
        /// 初始化一个设备运行时对象，并在构造阶段完成点位扫描与读取计划生成。
        /// </summary>
        /// <param name="device">要绑定的设备模型实例。</param>
        /// <exception cref="ArgumentNullException">当传入设备实例为空时抛出。</exception>
        public ModbusDeviceRuntime(TDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));

            // 构造时就完成反射扫描，目的是把运行期需要反复使用的数据提前准备好，
            // 避免在每次轮询时重复读取特性和属性信息。
            (_registerPoints, _coilPoints) = BuildBindings(device);
            ReadPlan = ModbusReadPlan.Create(_registerPoints, _coilPoints);
        }

        /// <summary>
        /// 将一次采集到的 Modbus 原始快照应用到当前设备实例。
        /// </summary>
        /// <param name="snapshot">一次采集周期内得到的寄存器和线圈原始数据。</param>
        /// <returns>
        /// 返回本次应用结果。
        /// 其中包含已成功回填的属性，以及未能回填时产生的告警信息。
        /// </returns>
        /// <exception cref="ArgumentNullException">当快照对象为空时抛出。</exception>
        public ModbusApplyResult ApplySnapshot(ModbusSourceSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var appliedValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            var warnings = new List<string>();

            // 先处理寄存器点位，因为寄存器通常存在拼接、缩放、偏移等数值映射逻辑。
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

            // 线圈点位处理相对简单，通常只涉及布尔值读取与反转逻辑。
            foreach (var point in _coilPoints)
            {
                if (!snapshot.TryGetCoil(point.Attribute.Address, out var coilRaw))
                {
                    warnings.Add($"缺少线圈地址 {point.Attribute.Address}，属性 {point.Property.Name} 未更新。");
                    continue;
                }

                var mapped = point.Attribute.Mapping(coilRaw);
                point.AssignValue(mapped);
                appliedValues[point.Property.Name] = mapped;
            }

            return new ModbusApplyResult(appliedValues, warnings);
        }

        /// <summary>
        /// 获取当前设备实例上已经存在的属性值快照。
        /// </summary>
        /// <returns>
        /// 返回以属性名为键、当前属性值为值的只读字典。
        /// 该方法更偏向调试、日志或界面展示使用。
        /// </returns>
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
        /// <param name="device">需要解析的设备实例。</param>
        /// <returns>返回寄存器绑定集合与线圈绑定集合。</returns>
        private static (ReadOnlyCollection<RegisterPointBinding>, ReadOnlyCollection<CoilPointBinding>) BuildBindings(TDevice device)
        {
            var registerPoints = new List<RegisterPointBinding>();
            var coilPoints = new List<CoilPointBinding>();

            // 这里要求属性既能读又能写。
            // 读是为了调试或界面展示，写则是为了把映射结果回填到模型。
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

            // 按地址排序能够让后续生成读取计划时更自然，
            // 也便于开发阶段核对“属性顺序”和“物理地址顺序”是否一致。
            return
            (
                new ReadOnlyCollection<RegisterPointBinding>(registerPoints.OrderBy(static point => point.Attribute.Address).ToList()),
                new ReadOnlyCollection<CoilPointBinding>(coilPoints.OrderBy(static point => point.Attribute.Address).ToList())
            );
        }

        /// <summary>
        /// 尝试从原始快照中解析某一个寄存器点位，并将其转换成属性可接收的目标值。
        /// </summary>
        /// <param name="point">目标寄存器点位绑定。</param>
        /// <param name="snapshot">原始数据快照。</param>
        /// <param name="resolved">解析成功后的目标值。</param>
        /// <param name="warning">当解析失败时给出的说明信息。</param>
        /// <returns>若解析成功则返回 true，否则返回 false。</returns>
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

            // Custom 或 CustomMapping 的设计意图是：
            // 通用映射器不再猜测拼接规则，而是把原始字节交给设备模型自己解释。
            if (point.Attribute.CustomMapping || point.Attribute.JointTypes == jointTypes.Custom)
            {
                if (!point.TryResolveCustomValue(words, out rawValue))
                {
                    warning = $"属性 {point.Property.Name} 需要自定义拼接，但设备模型未成功处理原始字节。";
                    return false;
                }
            }
            else
            {
                rawValue = ModbusRawValueDecoder.Decode(words, point.Attribute.JointTypes);
            }

            // 统一通过特性中的 Mapping 公式完成缩放和偏移，
            // 这样设备描述和映射规则就仍然集中在设备模型声明层中。
            var mapped = point.Attribute.Mapping((float)rawValue);
            resolved = ConvertToPropertyType(mapped, point.Property.PropertyType);
            return true;
        }

        /// <summary>
        /// 把统一的浮点映射结果转换成目标属性真正需要的类型。
        /// </summary>
        /// <param name="mapped">特性公式计算后的映射值。</param>
        /// <param name="propertyType">目标属性类型。</param>
        /// <returns>适配后的属性值对象。</returns>
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
        /// 验证某个属性是否适合作为寄存器点位的承载属性。
        /// </summary>
        /// <param name="property">待验证的属性。</param>
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
        /// 验证某个属性是否适合作为线圈点位的承载属性。
        /// </summary>
        /// <param name="property">待验证的属性。</param>
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

    /// <summary>
    /// 表示一次原始快照应用到设备实例后的结果。
    /// </summary>
    /// <remarks>
    /// 该类的作用不是替代异常，而是用于表达“本次有哪些属性成功更新、哪些属性因为缺少数据而被跳过”。
    /// 在轮询场景中，这种结果对象比直接写控制台更容易接日志、界面状态栏或调试面板。
    /// </remarks>
    public sealed class ModbusApplyResult
    {
        /// <summary>
        /// 获取本次成功写入设备实例的属性值集合。
        /// 键为属性名，值为最终写入后的对象值。
        /// </summary>
        public IReadOnlyDictionary<string, object?> AppliedValues { get; }

        /// <summary>
        /// 获取本次应用过程中产生的告警信息。
        /// 告警通常表示某个点位未更新，但不一定意味着整个周期失败。
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// 指示本次应用是否产生了告警。
        /// </summary>
        public bool HasWarnings => Warnings.Count > 0;

        /// <summary>
        /// 初始化一个结果对象。
        /// </summary>
        /// <param name="appliedValues">成功写入的属性值集合。</param>
        /// <param name="warnings">本次处理产生的告警集合。</param>
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

            AppliedValues = new ReadOnlyDictionary<string, object?>(appliedValues);
            Warnings = new ReadOnlyCollection<string>(warnings);
        }
    }

    /// <summary>
    /// 表示一次轮询或一次通信调用中采集到的原始 Modbus 数据快照。
    /// </summary>
    /// <remarks>
    /// 这里的寄存器和线圈数据使用“地址 -> 值”的字典组织，
    /// 好处是：
    /// 1. 上层可以先分段读取，再组装为统一快照；
    /// 2. 下层映射时不需要知道数据最初来自哪一次具体的读命令；
    /// 3. 对缺失地址的判断更直接。
    /// </remarks>
    public sealed class ModbusSourceSnapshot
    {
        /// <summary>
        /// 寄存器原始值表。
        /// 键是寄存器地址，值是一个 16 位无符号字。
        /// </summary>
        private readonly Dictionary<int, ushort> _registers;

        /// <summary>
        /// 线圈原始值表。
        /// 键是线圈地址，值是布尔值。
        /// </summary>
        private readonly Dictionary<int, bool> _coils;

        /// <summary>
        /// 获取寄存器原始值字典的只读视图。
        /// </summary>
        public IReadOnlyDictionary<int, ushort> Registers => _registers;

        /// <summary>
        /// 获取线圈原始值字典的只读视图。
        /// </summary>
        public IReadOnlyDictionary<int, bool> Coils => _coils;

        /// <summary>
        /// 初始化一个原始快照对象。
        /// </summary>
        /// <param name="registers">寄存器原始值集合，可以为空。</param>
        /// <param name="coils">线圈原始值集合，可以为空。</param>
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
        /// <param name="registerStartAddress">寄存器数组对应的起始地址。</param>
        /// <param name="registers">寄存器数组。</param>
        /// <param name="coilStartAddress">线圈数组对应的起始地址。</param>
        /// <param name="coils">线圈数组。</param>
        /// <returns>构造完成的快照对象。</returns>
        public static ModbusSourceSnapshot FromArrays(
            int registerStartAddress,
            ushort[]? registers,
            int coilStartAddress = 0,
            bool[]? coils = null)
        {
            var registerMap = new Dictionary<int, ushort>();
            var coilMap = new Dictionary<int, bool>();

            // 这里把数组转换成地址字典，是为了让后续处理统一走“按地址查值”的逻辑。
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
        /// <param name="address">目标寄存器地址。</param>
        /// <param name="value">输出寄存器值。</param>
        /// <returns>若存在该地址则返回 true。</returns>
        public bool TryGetRegister(int address, out ushort value) => _registers.TryGetValue(address, out value);

        /// <summary>
        /// 尝试读取某一个线圈地址的值。
        /// </summary>
        /// <param name="address">目标线圈地址。</param>
        /// <param name="value">输出线圈值。</param>
        /// <returns>若存在该地址则返回 true。</returns>
        public bool TryGetCoil(int address, out bool value) => _coils.TryGetValue(address, out value);

        /// <summary>
        /// 尝试连续读取一段寄存器值。
        /// </summary>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="count">需要连续读取的数量。</param>
        /// <param name="values">输出的寄存器数组。</param>
        /// <returns>当所有地址都存在时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 表示当前设备模型在通信层面上的读取计划。
    /// </summary>
    /// <remarks>
    /// 读取计划的核心价值是减少冗余读操作。
    /// 如果多个点位地址连续，那么把它们合并成一个读取段，
    /// 通常会比逐点读取更接近真实工业通信场景的需要。
    /// </remarks>
    public sealed class ModbusReadPlan
    {
        /// <summary>
        /// 获取寄存器读取段集合。
        /// </summary>
        public IReadOnlyList<ModbusReadSegment> RegisterSegments { get; }

        /// <summary>
        /// 获取线圈读取段集合。
        /// </summary>
        public IReadOnlyList<ModbusReadSegment> CoilSegments { get; }

        /// <summary>
        /// 初始化读取计划对象。
        /// </summary>
        /// <param name="registerSegments">寄存器读取段集合。</param>
        /// <param name="coilSegments">线圈读取段集合。</param>
        private ModbusReadPlan(IReadOnlyList<ModbusReadSegment> registerSegments, IReadOnlyList<ModbusReadSegment> coilSegments)
        {
            RegisterSegments = registerSegments;
            CoilSegments = coilSegments;
        }

        /// <summary>
        /// 根据运行期点位绑定创建读取计划。
        /// </summary>
        /// <param name="registerPoints">寄存器点位集合。</param>
        /// <param name="coilPoints">线圈点位集合。</param>
        /// <returns>合并完成后的读取计划。</returns>
        public static ModbusReadPlan Create(IEnumerable<RegisterPointBinding> registerPoints, IEnumerable<CoilPointBinding> coilPoints)
        {
            var registerSegments = BuildSegments(registerPoints.Select(static point => (point.Attribute.Address, point.RegisterCount)));
            var coilSegments = BuildSegments(coilPoints.Select(static point => (point.Attribute.Address, 1)));
            return new ModbusReadPlan(registerSegments, coilSegments);
        }

        /// <summary>
        /// 把离散的点位地址信息合并为连续读取段。
        /// </summary>
        /// <param name="points">地址和长度的集合。</param>
        /// <returns>合并后的读取段列表。</returns>
        private static IReadOnlyList<ModbusReadSegment> BuildSegments(IEnumerable<(int Address, int Count)> points)
        {
            var ordered = points
                .OrderBy(static point => point.Address)
                .ThenBy(static point => point.Count)
                .ToList();

            if (ordered.Count == 0)
            {
                return Array.Empty<ModbusReadSegment>();
            }

            var segments = new List<ModbusReadSegment>();
            var currentStart = ordered[0].Address;
            var currentEnd = ordered[0].Address + ordered[0].Count - 1;

            for (var index = 1; index < ordered.Count; index++)
            {
                var nextStart = ordered[index].Address;
                var nextEnd = ordered[index].Address + ordered[index].Count - 1;

                // 只要下一个点位与当前段相连或重叠，就继续并入当前段。
                if (nextStart <= currentEnd + 1)
                {
                    currentEnd = Math.Max(currentEnd, nextEnd);
                    continue;
                }

                segments.Add(new ModbusReadSegment(currentStart, currentEnd - currentStart + 1));
                currentStart = nextStart;
                currentEnd = nextEnd;
            }

            segments.Add(new ModbusReadSegment(currentStart, currentEnd - currentStart + 1));
            return new ReadOnlyCollection<ModbusReadSegment>(segments);
        }
    }

    /// <summary>
    /// 表示一个连续的 Modbus 读取区间。
    /// </summary>
    /// <param name="StartAddress">读取起始地址。</param>
    /// <param name="Count">连续读取的数量。</param>
    public readonly record struct ModbusReadSegment(int StartAddress, int Count);

    /// <summary>
    /// 表示某一个寄存器属性在运行期的绑定信息。
    /// </summary>
    /// <remarks>
    /// 它的职责是把“设备实例 + 属性信息 + 特性信息”组合起来，
    /// 形成一个更容易执行映射和回填的对象。
    /// </remarks>
    public sealed class RegisterPointBinding
    {
        /// <summary>
        /// 当前绑定对应的设备实例。
        /// 当存在 Custom 拼接时，会调用该实例上的 <see cref="IDeviceModel.TryJoint(byte[], out double)"/>。
        /// </summary>
        private readonly IDeviceModel _device;

        /// <summary>
        /// 获取当前点位绑定的目标属性。
        /// </summary>
        public PropertyInfo Property { get; }

        /// <summary>
        /// 获取当前点位绑定对应的寄存器特性描述。
        /// </summary>
        public ModbusDevicePointAttribute Attribute { get; }

        /// <summary>
        /// 获取该点位在读取时需要占用的寄存器数量。
        /// </summary>
        /// <remarks>
        /// Int16 只需 1 个寄存器，32 位拼接需要 2 个寄存器。
        /// 对于 Custom 类型，如果特性长度没有给出有效值，则默认按 1 处理，避免读零长度。
        /// </remarks>
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
        /// <param name="device">目标设备实例。</param>
        /// <param name="property">目标属性信息。</param>
        /// <param name="attribute">寄存器特性信息。</param>
        public RegisterPointBinding(IDeviceModel device, PropertyInfo property, ModbusDevicePointAttribute attribute)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        }

        /// <summary>
        /// 将一个已经完成映射的值写回当前绑定属性。
        /// </summary>
        /// <param name="value">需要写入属性的值。</param>
        public void AssignValue(object value)
        {
            Property.SetValue(_device, value);
        }

        /// <summary>
        /// 尝试使用设备级自定义逻辑解析寄存器原始字数据。
        /// </summary>
        /// <param name="words">寄存器原始字数组。</param>
        /// <param name="result">设备模型解析后的结果。</param>
        /// <returns>若设备模型成功完成解析则返回 true。</returns>
        public bool TryResolveCustomValue(ushort[] words, out double result)
        {
            // 这里固定导出为大端字节序，是因为 words 本身代表标准寄存器序列，
            // 后续如果设备要做更复杂的拼接，应当在 TryJoint 内部自行解释。
            var bytes = ModbusRawValueDecoder.ToBigEndianBytes(words);
            return _device.TryJoint(bytes, out result);
        }
    }

    /// <summary>
    /// 表示某一个线圈属性在运行期的绑定信息。
    /// </summary>
    public sealed class CoilPointBinding
    {
        /// <summary>
        /// 当前绑定对应的设备实例。
        /// </summary>
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
        /// <param name="device">目标设备实例。</param>
        /// <param name="property">目标属性信息。</param>
        /// <param name="attribute">线圈特性信息。</param>
        public CoilPointBinding(IDeviceModel device, PropertyInfo property, ModbusDeviceCoilAttribute attribute)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        }

        /// <summary>
        /// 将线圈值回填到设备实例。
        /// </summary>
        /// <param name="value">待写入的布尔值。</param>
        public void AssignValue(bool value)
        {
            Property.SetValue(_device, value);
        }
    }

    /// <summary>
    /// 提供寄存器原始字数据的基础解码能力。
    /// </summary>
    /// <remarks>
    /// 该类只负责“通用可推断”的拼接规则。
    /// 一旦进入 Custom 类型，就意味着该规则已经不能由基础层安全推断，
    /// 此时必须回到设备模型自身的 TryJoint 方法中处理。
    /// </remarks>
    internal static class ModbusRawValueDecoder
    {
        /// <summary>
        /// 按指定拼接类型解析寄存器原始值。
        /// </summary>
        /// <param name="words">寄存器原始字数组。</param>
        /// <param name="jointType">拼接类型。</param>
        /// <returns>解码后的基础数值。</returns>
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
        /// <param name="words">原始寄存器字数组。</param>
        /// <returns>转换后的大端字节数组。</returns>
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
        /// <param name="words">寄存器原始字数组。</param>
        /// <returns>解析后的 32 位整数。</returns>
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
        /// <param name="words">寄存器原始字数组。</param>
        /// <returns>解析后的 32 位整数。</returns>
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
