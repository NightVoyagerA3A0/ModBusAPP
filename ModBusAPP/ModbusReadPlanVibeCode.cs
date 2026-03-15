using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Components
{
    /// <summary>
    /// 表示当前设备模型在通信层面上的读取计划。
    /// 设计目的：把离散点位提前合并成连续读取段，减少轮询器逐点读取的冗余操作，
    /// 让通信层更接近真实的 Modbus 批量读取方式。
    ///
    /// 面试问题：
    /// 1. 为什么批量读取计划适合在运行时初始化阶段预先生成？
    /// 2. 连续地址合并能带来哪些收益，又可能带来哪些边界问题？
    /// 3. 为什么读取计划属于运行时层，而不是调度器层？
    /// </summary>
    /// <remarks>
    /// 行为模式：规划型数据对象。
    /// 该类不做实际通信，只负责描述“应该按哪些区间读取”。
    /// </remarks>
    public sealed class ModbusReadPlan
    {
        /// <summary>
        /// 获取寄存器读取段集合。
        /// 每一段都表示一个连续地址区间。
        /// </summary>
        public IReadOnlyList<ModbusReadSegment> RegisterSegments { get; }

        /// <summary>
        /// 获取线圈读取段集合。
        /// </summary>
        public IReadOnlyList<ModbusReadSegment> CoilSegments { get; }

        /// <summary>
        /// 初始化读取计划对象。
        /// </summary>
        /// <param name="registerSegments">
        /// 输入参数模式：寄存器读取段集合。
        /// 该参数通常来自点位绑定的地址合并结果。
        /// </param>
        /// <param name="coilSegments">
        /// 输入参数模式：线圈读取段集合。
        /// </param>
        /// <remarks>
        /// 行为模式：构造型。
        /// 构造函数只承载已经规划好的结果，不重复执行地址合并逻辑。
        /// </remarks>
        private ModbusReadPlan(IReadOnlyList<ModbusReadSegment> registerSegments, IReadOnlyList<ModbusReadSegment> coilSegments)
        {
            RegisterSegments = registerSegments;
            CoilSegments = coilSegments;
        }

        /// <summary>
        /// 根据运行期点位绑定创建读取计划。
        /// </summary>
        /// <param name="registerPoints">
        /// 输入参数模式：寄存器点位绑定集合。
        /// 该参数提供各寄存器属性的起始地址与占用长度信息。
        /// </param>
        /// <param name="coilPoints">
        /// 输入参数模式：线圈点位绑定集合。
        /// 该参数提供线圈属性的地址信息。
        /// </param>
        /// <returns>
        /// 返回模式：读取规划对象。
        /// 返回值会把离散点位转换成按类型分组的连续读取段集合。
        /// </returns>
        /// <remarks>
        /// 行为模式：规划型工厂方法。
        /// 该方法不执行通信，只根据运行时绑定的地址布局预先生成最适合批量读取的区间结构。
        /// </remarks>
        public static ModbusReadPlan Create(IEnumerable<RegisterPointBinding> registerPoints, IEnumerable<CoilPointBinding> coilPoints)
        {
            var registerSegments = BuildSegments(registerPoints.Select(static point => (point.Attribute.Address, point.RegisterCount)));
            var coilSegments = BuildSegments(coilPoints.Select(static point => (point.Attribute.Address, 1)));
            return new ModbusReadPlan(registerSegments, coilSegments);
        }

        /// <summary>
        /// 把离散的点位地址信息合并为连续读取段。
        /// </summary>
        /// <param name="points">
        /// 输入参数模式：地址和长度集合。
        /// 每一项表示一个点位的起始地址及其占用长度。
        /// </param>
        /// <returns>
        /// 返回模式：合并后的连续读取段列表。
        /// 返回值为空时表示当前没有任何可读取点位。
        /// </returns>
        /// <remarks>
        /// 行为模式：规划型。
        /// 该方法只负责地址段合并；只要下一个点位与当前段相连或重叠，就会并入同一读取段。
        /// </remarks>
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
    /// 设计目的：把“起始地址 + 连续数量”封装成稳定值对象，方便轮询层逐段执行。
    /// </summary>
    /// <param name="StartAddress">
    /// 输入参数模式：读取起始地址。
    /// 该参数表示逻辑地址区间的起点。
    /// </param>
    /// <param name="Count">
    /// 输入参数模式：连续读取数量。
    /// 该参数表示本段包含的寄存器或线圈数量，应大于零。
    /// </param>
    public readonly record struct ModbusReadSegment(int StartAddress, int Count);
}
