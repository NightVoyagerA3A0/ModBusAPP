using Microsoft.Windows.Themes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Interfaces;
using System.Runtime.CompilerServices;
using System.Reflection;
using Device;
namespace DeviceRuntime
{
    public class MainRuntime
    {
        protected IDeviceModel device { get; init; }
        public Dictionary<PropertyInfo, ModbusDeviceAttribute> AllPointInfos { get; private set; }
        public Dictionary<PropertyInfo, ModbusDeviceCoilAttribute> CoilInfos { get; private set; }
        public Dictionary<PropertyInfo, ModbusDevicePointAttribute> RegisterInfos { get; private set; }
        //可能还需要一个ReadChunk委托。
        //这指的是：读计划将会将点位分成几个块来读，而每个块的读取方法可能不同。比如有些块需要特殊的拼接方法，有些块则可以直接用默认的读取方法。
        //而ReadChunk则是定义了每个块的开始地址和长度。以获得原生数据流。
        //然而，注意到一点，ReadChunk，否则无法映射。
        //可以使用静态工厂方法来创建实例，隐藏构造函数的复杂性。
        public static MainRuntime Create(IDeviceModel device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device), "设备模型不能为空，无法创建运行时实例。");
            }
            return new MainRuntime(device);
        }

        //public 返回什么我还没有想好 ReadAllPoints
        private MainRuntime(IDeviceModel _deviceModel)
        {
            this.device = _deviceModel;
            Initialize();
            //CreateReadPlan();
        }

        private void Initialize()
        {
            if (device == null)
            {
                throw new InvalidOperationException("设备未初始化，无法进行运行时初始化。");
            }
            Type _type = device.GetType();
            var _propertyInfos = _type.GetProperties().Select(item => new
            {
                PropertyInfo = item,
                Attribute = item.GetCustomAttribute<ModbusDeviceAttribute>()
            }).Where(x => x.Attribute != null)
            .ToDictionary(x => x.PropertyInfo, x => x.Attribute!);
            //分别取出寄存器与线圈。
            AllPointInfos = _propertyInfos;
            var _coilInfos = _propertyInfos
                .Select(x => new
                {
                    x.Key,
                    Attr = x.Value as ModbusDeviceCoilAttribute
                })
                .Where(x => x.Attr != null)
                .ToDictionary(
                    x => x.Key,
                    x => x.Attr!
                );

            var _registerInfos = _propertyInfos
                .Select(x => new
                {
                    x.Key,
                    Attr = x.Value as ModbusDevicePointAttribute
                })
                .Where(x => x.Attr != null)
                .ToDictionary(
                    x => x.Key,
                    x => x.Attr!
                );
            CoilInfos = _coilInfos;
            RegisterInfos = _registerInfos;
            if (CoilInfos.Count == 0 && RegisterInfos.Count == 0)
            {
                throw new InvalidOperationException("设备模型中未找到任何有效的点位特性，无法进行运行时初始化。");
            }
            //检查可能的标注问题。比如寄存器与线圈的混淆。
            foreach (var coilInfo in CoilInfos)
            {
                if (coilInfo.Key.PropertyType != typeof(bool))
                {
                    throw new InvalidOperationException($"属性 {coilInfo.Key.Name} 标记为线圈，但其类型为 {coilInfo.Key.PropertyType.Name}，应为 bool 类型。");
                }
            }
            foreach (var registerInfo in RegisterInfos)
            {
                if (registerInfo.Key.PropertyType == typeof(bool))
                {
                    throw new InvalidOperationException($"属性 {registerInfo.Key.Name} 标记为寄存器，但其类型为 bool。");
                }
            }
            //检查多个标注。
            var duplicated = RegisterInfos.Keys.Intersect(CoilInfos.Keys).ToList();
            if (duplicated.Count > 0)
            {
                string duplicatedProperties = string.Join(", ", duplicated.Select(p => p.Name));
                throw new InvalidOperationException($"属性 {duplicatedProperties} 同时标记为寄存器和线圈，存在冲突。");
            }
            //检查可能的地址重叠。
            var ranges = RegisterInfos.OrderBy(x => x.Value.Address)
                .Select(x => new
                {
                    Property = x.Key,
                    Start = x.Value.Address,
                    End = x.Value.Address + x.Value.Length - 1
                })
                .ToList();
            for (int i = 1; i < ranges.Count; i++)
            {
                var prev = ranges[i - 1];
                var curr = ranges[i];

                if (curr.Start <= prev.End)
                {
                    throw new InvalidOperationException(
                        $"寄存器地址重叠：属性 {curr.Property.Name} [{curr.Start}-{curr.End}] " +
                        $"与属性 {prev.Property.Name} [{prev.Start}-{prev.End}] 重叠。");
                }
            }
        }
        public ReadPlanSnapshot CreateReadPlan()
        {
            //读所有的寄存器点位。
            var _registerList = RegisterInfos.OrderBy(x => x.Value.Address).Select(x => (Start: x.Value.Address, End: x.Value.Address + x.Value.Length - 1)).ToList();
            //读所有的线圈点位。
            var _coilList = CoilInfos.OrderBy(x => x.Value.Address).Select(x => x.Value.Address).ToList();
            //问题就在于呢，寄存器和线圈要不要连一起读。
            //最好还是不要。
            ReadPlanSnapshot readPlan = new ReadPlanSnapshot();
            if ( _registerList.Count > 0)
            {
                int _startAddr = _registerList.First().Start;
                int _endAddr = _registerList.First().End;
                //这里应该返回一个类，类里表明创建是否成功之类的。
                //先创建一个保底块。
                var newChunk = new ReadChunk(_startAddr, _endAddr);
                foreach (var item in _registerList.Skip(1))
                {
                    if (item.Start >= _endAddr + 4)
                    {
                        //舍弃旧块，创建新块。
                        newChunk = new ReadChunk(_startAddr, _endAddr);
                        //写入新块
                        readPlan.AddReadChunk(newChunk);
                        //重置起始地址
                        _startAddr = item.Start;
                    }
                    //重置末尾地址
                    _endAddr = item.End;
                }
                //创建最后一个保底块。
                newChunk = new ReadChunk(_startAddr, _endAddr);
                readPlan.AddReadChunk(newChunk);
            }
            if(_coilList.Count > 0)
            {
                //线圈的地址是单个的，所以每个地址就是一个块。
                
                foreach (var item in _coilList)
                {
                    var newChunk = new ReadChunk(item, item);
                    readPlan.AddReadChunk(newChunk,PointType.Coil);
                }
            }

            if(_coilList.Count == 0&& _registerList.Count == 0)
            {
                throw new InvalidOperationException("设备模型中未找到任何有效的点位特性，无法创建读取计划。");
            }
            return readPlan;
        }
        public void ReadAllPoints()
        {
            throw new NotImplementedException("读取所有点位的逻辑尚未实现。");
            //这里应该返回一个类，类里表明读取是否成功之类的。
        }


    }
    /// <summary>
    /// ReadPlanSnapshot用于储存一次读取计划的快照信息，包括每个块的地址和长度。
    /// </summary>
    public sealed class ReadPlanSnapshot
    {

        ///public Dictionary<int,int> ReadChunks { get; private set; }
        /// <summary>
        /// ReadList是一个列表，每个元素都是一个字典，表示一次读取计划中的所有块。每个字典的键为块的起始地址，值为块的长度。通过这个列表，运行时可以知道每次读取计划中需要读取哪些块，以及每个块的地址和长度。
        /// </summary>
        public List<ReadChunk> RegisterReadList { get; private set; }
        public List<ReadChunk> CoilReadList { get; private set; }
        /// <summary>
        /// StartAddress表示读取计划中所有块的起始地址。通过这个属性，运行时可以知道整个读取计划的起始地址，从而确定从哪个地址开始读取数据。
        /// 目前来说依然保留，不确认ReadChunk此后记录的是相对地址还是绝对地址。
        /// </summary>
        public int StartAddress { get; private set; } = 0;

        public ReadPlanSnapshot()
        {
            List<ReadChunk> coilReadList = new List<ReadChunk>();
            List<ReadChunk> registerReadList = new List<ReadChunk>();
            RegisterReadList = registerReadList;
            CoilReadList = coilReadList;
        }

        public void AddReadChunk(ReadChunk chunk,PointType pointType = PointType.Register)
        {
            List<ReadChunk> readList;
            if (pointType == PointType.Register)
            {
                readList = this.RegisterReadList;
            }
            else if (pointType == PointType.Coil)
            {
                readList = this.CoilReadList;
            }
            else
            {
                throw  new ArgumentException("无效的点位类型，无法添加读取块。", nameof(pointType));
            }
            if (readList.Count> 0 && chunk.StartAddress < readList.Last().EndAddress)
            {
                throw new ArgumentException("新增块的起始地址不能小于已有读取计划的末尾地址。", nameof(chunk));
            }
            if (chunk.Length == 0)
            {
                throw new ArgumentException("读取计划列表不能为空，至少应包含一个块。", nameof(chunk));
            }
            readList.Add(chunk);
        }
    }
    public struct ReadChunk
    {
        public int StartAddress { get; private set; }

        public int EndAddress { get; private set; }
        public int Length => EndAddress - StartAddress + 1;
        public ReadChunk(int startAddress, int endAddress)
        {
            StartAddress = startAddress;
            EndAddress = endAddress;
            if (startAddress < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startAddress), "起始地址不能为负数。");
            }
            if (Length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Length), "长度必须至少为1。");
            }

        }
    }
}



