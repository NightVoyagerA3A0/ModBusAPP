using Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Device
{
    /// <summary>
    /// ModbusDeviceAttribute基类，用于统一线圈与寄存器的反射。
    /// </summary>
    /// <remarks>
    /// 抽象类。
    /// 包含基本的Index与Adress属性。其子类应当避免再次实现此字段。
    /// 没有声明构造函数的结构和返回值。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property)]
    public abstract class ModbusDeviceAttribute : Attribute
    {
        //basic class, do nothing.
        public int Index { get; protected set; }
        public int Address { get; protected set; }
        public pointTypes PointType { get; protected set; }
        /// <summary>
        /// Mapping方法，指定该实体的mapping公式
        /// </summary>
        /// <remarks>
        /// 在抽象基类中实现了所有可能的基本重载。
        /// </remarks>
        public abstract bool Mapping(bool raw);
        public abstract float Mapping(float raw);

    }
    /// <summary>
    /// 继承于ModbusDeviceAttribute，描述该寄存器中的数据从何处获取，如何处理。
    /// </summary>
    /// <remarks>
    /// F(x) = A1 * (x+Offset) + A2 * (x+Offset)^2 + A3 *(x+Offset)^3 + B
    /// 可定义A1,A2,A3,Offset,B。A1默认值为1，其余为0。
    /// </remarks>
    /// 
    [AttributeUsage(AttributeTargets.Property)]
    public class ModbusDevicePointAttribute : ModbusDeviceAttribute
    {
        //public string DeviceName { get; } = "default";
        //public deviceTypes DeviceTypes { get; } = deviceTypes.Others;
        //public jointTypes JointType { get; } 
        public float A1 { get; set; } = 1;
        public float A2 { get; set; } = 0;
        public float A3 { get; set; } = 0;
        public float B { get; set; } = 0;
        public float Offset { get; set; } = 0;
        public jointTypes JointTypes { get; set; } = jointTypes.Int16;

        public bool CustomMapping { get; set; } = false;
        public short Length
        {
            get
            {
                switch (JointTypes)
                {
                    case jointTypes.Int16: return 1;
                    case jointTypes.LittleEndian32:
                    case jointTypes.BigEndian32: return 2;
                    //case jointTypes.Double: return 4;
                    default: return 0;//means you needs a custiom joint function inside.
                }
            }
        }

        public ModbusDevicePointAttribute(int index, int startAddress, jointTypes jointTypes = jointTypes.Int16)
        {
            Index = index;
            Address = startAddress;
            JointTypes = jointTypes;
            PointType = pointTypes.Register;
        }
        public override float Mapping(float raw)
        {
            if (CustomMapping)
            {
                throw new NotSupportedException($"{nameof(ModbusDevicePointAttribute)} 要求使用设备类映射。");
            }
            double Result;
            float x;
            x = raw + Offset;
            Result = (A1 * x) +
                A2 * Math.Pow(x, 2) +
                A3 * Math.Pow(x, 3) +
                B;
            return (float)Result;//你还能算出超过±1E38?你是什么传感器？
        }
        /// <summary>
        /// bad behavior,此类无法映射bool值。
        /// </summary>
        /// <param name="raw"></param>
        /// <returns></returns>
        public override bool Mapping(bool raw)
        {
            throw new InvalidOperationException($"{nameof(ModbusDevicePointAttribute)} 只能处理数值映射，不能映射布尔值。请检查属性类型。");
        }
    }
    /// <summary>
    /// 继承于ModbusDeviceAttribute，描述该线圈中的数据从何处获取，如何处理。
    /// </summary>
    /// <remarks>当Reverse设置为True时，对输出bool值进行反转。不建议使用，仅测试功能。DataMapping检测到Reverse时，将会写log。
    /// discrete outputs in a Modbus device. The attribute defines the coil's  index and address within the Modbus
    /// device.</remarks>
    [AttributeUsage(AttributeTargets.Property)]
    public class ModbusDeviceCoilAttribute : ModbusDeviceAttribute
    {
        public bool Reverse { get; set; }
        public ModbusDeviceCoilAttribute(int index, int coilAddress, bool reverse = false)
        {
            Index = index;
            Address = coilAddress;
            Reverse = reverse;
            PointType = pointTypes.Coil;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="raw"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public override bool Mapping(bool raw)
        {
            if (Reverse)
            {
                return !raw;
            }
            return raw;
        }

        public override float Mapping(float raw)
        {
            throw new InvalidOperationException($"{nameof(ModbusDevicePointAttribute)} 只能处理布尔值映射，不能映射浮点数。请检查属性类型。");
        }
    }

    public enum deviceTypes
    {
        ForceSensor,
        Thermometer,
        Others,
    }
    /// <summary>
    /// 设备特性中的pointTypes将指明Datamapper如何将raw数据映射为真实数据。
    /// </summary>
    /// <remarks>
    /// 当为Register，Coil时，由特性类自身完成映射。为Others时，datamapper将转交给设备类进行映射(todo.)
    /// </remarks>
    public enum pointTypes
    {
        Register,
        Coil,
        Others,
    }
    public enum jointTypes
    {
        Int16,  //单寄存器
        BigEndian32, //ABCD 双寄存器
        LittleEndian32, //CDAB 双寄存器
        //Double,// 四寄存器
        Custom //映射器不进行拼接，将交由类本身拼接
    }

    public class TestDevice : IDeviceModel
    {
        public deviceTypes DeviceTypes { get; private set; } = deviceTypes.Thermometer;
        [ModbusDevicePoint(0, 0, jointTypes.Int16, A1 = 0.1f, Offset = -128.0f)]
        public float TemperaturePoint1 { get; set; }
        [ModbusDevicePoint(1, 1, jointTypes.LittleEndian32)]

        public float TemperaturePoint2 { get; set; }
        [ModbusDevicePoint(2, 3, jointTypes.BigEndian32)]
        public float TemperaturePoint3 { get; set; }
        [ModbusDeviceCoil(0, 0)]
        public bool Notification { get; set; }
        public bool TryJoint(byte[] RawData, out double Result)
        {
            Result = 0;
            return false;
        }

    }
}
