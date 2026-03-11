using Device;
using Interfaces;
using Modbus.Device;
using Modbus.IO;
using System;
using System.IO.Ports;
using System.Reflection;
using System.Threading.Tasks;
namespace Components
{
    /// <summary>
    /// 封装串口,仅作为导入成分。
    /// 依赖实现不完全，目前不建议使用。
    /// 
    /// </summary>

    /// <summary>
    /// 封装串口，实现Modbus.IO.IStreamResource接口。
    /// </summary>
    public class NModbusRTUAdapter : Modbus.IO.IStreamResource
    {
        public SerialPort Port { get; private set; }
        public int InfiniteTimeout { get => SerialPort.InfiniteTimeout; }
        public int ReadTimeout
        {
            get => Port.ReadTimeout;
            set
            {
                Port.ReadTimeout = value < 0 ? InfiniteTimeout : value;
            }
        }
        public int WriteTimeout
        {
            get => Port.WriteTimeout;
            set
            {
                Port.WriteTimeout = value < 0 ? InfiniteTimeout : value;
            }
        }

        public NModbusRTUAdapter(SerialPort port
            , int writeTimeOut = 1000, int readTimeOut = 1000)
        {
            Port = port ??
                throw new ArgumentNullException("No valid serial port, check.");
            WriteTimeout = writeTimeOut;
            ReadTimeout = readTimeOut;
        }

        public void DiscardInBuffer() => Port.DiscardInBuffer(); //如果我想连到逻辑Buffer上可以试试看，但注意了，其实没有必要，似乎更复杂。

        public int Read(byte[] buffer, int offset, int count) => Port.Read(buffer, offset, count);

        public void Write(byte[] buffer, int offset, int count) => Port.Write(buffer, offset, count);
        public void Dispose() => Port.Dispose();
    }
    /// <summary>
    /// 核心数据映射器：负责将通讯层获取的原始字节数组（Raw Data）转换为上层可读的业务对象。
    /// </summary>
    /// <remarks>
    /// 在此类中拉取解释器和暂存内容，Datamapper允许多个实例同时运行，不允许改变注入的设备类IDeviceModel。
    /// 当持续运行时，DataMapper与设备类IDeviceModel应当是一一对应的。
    /// 
    /// </remarks>
    //对于调度器来说，调度器只希望知道Device里有几个点位，
    class DataMapper
    {
        public IDeviceModel DeviceModel { get; private set; }
        public DataMapper(IDeviceModel deviceModel)
        {
            DeviceModel = deviceModel ?? throw new ArgumentException(nameof(deviceModel), "Unexist DeviceModel, check.");
            Type type = deviceModel.GetType();
            PropertyInfo[] propertyInfos = type.GetProperties();
            Dictionary<string, IPointAtrributesCache> PointPropertys = new Dictionary<string, IPointAtrributesCache>();
            foreach (PropertyInfo p in propertyInfos)
            {
                var attribute = p.GetCustomAttribute<ModbusDeviceAttribute>();

                if (attribute != null)
                {
                    // 如果找到了标签，就可以读出你在标签里写的参数
                    Console.WriteLine($"找到标记属性: {p.Name}");
                    Console.WriteLine($"对应的寄存器地址: {attribute.Address}");
                    //PointPropertys.Add(p.Name, attribute);
                    //foreach(PropertyAttributes props in attribute)
                    var cache = AttrCacheBuilder(attribute);
                    PointPropertys.Add(p.Name, cache);
                }
                else
                {
                    Console.WriteLine("没有标签？");
                }
            }
            foreach (string key in PointPropertys.Keys)
            {
                IPointAtrributesCache thiscache;
                if (PointPropertys.TryGetValue(key, out thiscache))
                {
                    Console.WriteLine($"{key},类型为{thiscache.ToString()}");
                }
                Console.WriteLine();
            }

        }

        /*
         接下来要完成datamapper，datamapper从deviceModel的特性之中取得特性。
         */

        public IPointAtrributesCache AttrCacheBuilder(ModbusDeviceAttribute meta)
        {
            //此后的将会导入写log方式，而不是控制台
            IPointAtrributesCache cache;
            switch (meta.PointType)
            {
                case pointTypes.Register:
                    {
                        RegisterCache _cache = new RegisterCache();
                        cache = _cache;
                        break;
                    }
                case pointTypes.Coil:
                    {

                        CoilCache _cache = new CoilCache();
                        if (_cache.PointType != pointTypes.Coil)
                        { Console.WriteLine($"{nameof(cache)}缓存点位类型错误的初始化为Register。应当为Coil"); }
                        cache = _cache;
                        break;

                    }
                    break;

                case pointTypes.Others:
                    //这意味着数据映射器无法映射，也不存在合理的元信息猜测。这里预定一个通用缓存类。只写标准信息。
                    //是否考虑改为TryBuild方法？待定。
                    throw new NotImplementedException();
                    break;
                default:
                    throw new NotImplementedException();
                    break;
            }
            Type cacheType = cache.GetType();
            Type metaType = meta.GetType();
            foreach (var p in cacheType.GetProperties())
            {
                if (metaType.GetProperty(p.Name) == null) { throw new ArgumentException(nameof(meta), "设备特性类标签与缓存类不符合，请检查"); }
                var source = metaType.GetProperty(p.Name);
                var value = source.GetValue(meta);
                if (p.CanWrite) { p.SetValue(cache, value); }
                else { Console.WriteLine($"{p.Name}不允许写，检查行为是否正确。值为:{p.GetValue(cache)}"); }

            }
            Console.WriteLine($"缓存:good");
            return cache;
        }

        public bool TryMap(bool raw, ICoilPointCache attr,out bool result)
        {
            if (attr.Reverse)
            {
                result = (!raw);
                Console.WriteLine($"{nameof(attr)}的Reverse为真，确认是否为默认行为。");
                /*
                 即将改为写log。
                 */
            }
            else
            {
                result = raw;
            }
            return true;
        }
        /// <summary>
        /// 映射数据到真实值。有内部数值类型混乱问题.注意特性中商定的数值类型为float。
        /// </summary>
        /// <param name="raw"></param>
        /// <param name="attr"></param>
        /// <returns></returns>
        public bool TryMap(double raw, IResgisterPointCache attr,out double result)
        {
            raw += attr.Offset;
            result = (attr.A1 * raw) +
                attr.A2 * Math.Pow(raw, 2) +
                attr.A3 * Math.Pow(raw, 3) +
                attr.B;
            return (!double.IsNaN(result) && !double.IsInfinity(result));
        }
    }



    /// <summary>
    /// MappingHelper用于重排与连接数据。
    /// </summary>
    static class MappingHelper
    {
        static public byte[] Joint(byte[] raw, int addr, jointTypes types)
        {
            throw new NotImplementedException();
            //以下皆在商讨中，在MVP环节中只考虑单字节映射，而不joint。
            int rawaddr = addr * 2;
            int length = (types == jointTypes.Int16) ? 2 : 4;
            if (raw.Length < rawaddr + length)
            {
                throw new ArgumentException();
            }
            byte[] result = new byte[length];
            switch (types)
            {
                case jointTypes.Int16:
                    for (int i = 0; i < 2; i++) { result[i] = raw[rawaddr + i]; }
                    break;
                case jointTypes.BigEndian32:
                    for (int i = 0; i < 4; i++) { result[i] = raw[rawaddr + i]; }
                    break;
                case jointTypes.LittleEndian32:
                    result[3] = raw[rawaddr + 0];
                    result[2] = raw[rawaddr + 1];
                    result[1] = raw[rawaddr + 2];
                    result[0] = raw[rawaddr + 3];
                    break;
                default:
                    throw new NotSupportedException();
            }
            return result;

        }

    }

    struct RegisterCache : IResgisterPointCache
    {
        public float A1 { get; set; }
        public float A2 { get; set; }
        public float A3 { get; set; }
        public float B { get; set; }
        public float Offset { get; set; }
        public jointTypes JointTypes { get; set; }
        public bool CustomMapping { get; set; }
        public short Length { get; set; }
        public int Index { get; set; }
        public int Address { get; set; }
        public pointTypes PointType { get { return pointTypes.Register; } }

    }

    struct CoilCache : ICoilPointCache
    {
        public bool Reverse { get; set; }
        public int Index { get; set; }
        public int Address { get; set; }
        public pointTypes PointType { get { return pointTypes.Coil; } }
    }
}
