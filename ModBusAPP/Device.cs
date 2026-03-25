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
        // 声明层只承载元数据，不直接执行映射逻辑。
        public int Index { get; protected set; }
        public int Address { get; protected set; }
        public PointType PointType { get; protected set; }
    }
    /// <summary>
    /// 继承于ModbusDeviceAttribute，描述该寄存器点位的声明信息。
    /// </summary>
    /// <remarks>
    /// 该特性只负责声明寄存器地址、拼接类型和默认缩放参数。
    /// 真正的运行时映射公式由运行时层执行，而不是由特性对象直接承担。
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
        public RegisterValueType RegisterValueType { get; set; } = RegisterValueType.Int16;

        public bool CustomMapping { get; set; } = false;
        public short Length
        {
            get
            {
                switch (RegisterValueType)
                {
                    case RegisterValueType.Int16: return 1;
                    case RegisterValueType.LittleEndian32:
                    case RegisterValueType.BigEndian32: return 2;
                    //case jointTypes.Double: return 4;
                    default: return 0;//means you needs a custiom joint function inside.
                }
            }
        }

        public ModbusDevicePointAttribute(int index, int startAddress, RegisterValueType registerValueType = RegisterValueType.Int16)
        {
            Index = index;
            Address = startAddress;
            RegisterValueType = registerValueType;
            PointType = PointType.Register;
        }
    }
    /// <summary>
    /// 继承于ModbusDeviceAttribute，描述该线圈点位的声明信息。
    /// </summary>
    /// <remarks>
    /// 当Reverse设置为True时，表示运行时回填前需要对布尔值进行反转。
    /// 该特性只声明规则，不直接执行布尔映射。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property)]
    public class ModbusDeviceCoilAttribute : ModbusDeviceAttribute
    {
        public bool Reverse { get; set; }
        public ModbusDeviceCoilAttribute(int index, int coilAddress, bool reverse = false)
        {
            Index = index;
            Address = coilAddress;
            Reverse = reverse;
            PointType = PointType.Coil;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    class TestAttributeList : Attribute
    {
        public int Counts { get; set; }

        public int DuplicateCounts => Counts * 2;

        public TestAttributeList(int count)
        {
            Counts = count;
        }
    }

    public enum DeviceType
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
    public enum PointType
    {
        Register,
        Coil,
        Others,
    }
    public enum RegisterValueType
    {
        Int16,  //单寄存器
        BigEndian32, //ABCD 双寄存器
        LittleEndian32, //CDAB 双寄存器
        //Double,// 四寄存器
        Custom //映射器不进行拼接，将交由类本身拼接
    }

    public class TestDevice: IDeviceModel
    {
        public DeviceType DeviceType { get; private set; } = DeviceType.Thermometer;
        [ModbusDevicePoint(0, 0, RegisterValueType.Int16, A1 = 0.1f, Offset = -128.0f)]
        public float TemperaturePoint1 { get; set; }
        [ModbusDevicePoint(1, 1, RegisterValueType.LittleEndian32)]
        [TestAttributeList(3)]
        public float TemperaturePoint2 { get; set; }
        [ModbusDevicePoint(2, 3, RegisterValueType.BigEndian32)]
        public float TemperaturePoint3 { get; set; }
        [ModbusDeviceCoil(0, 0)]
        public bool Notification { get; set; }
        public bool TryMapCustom(string pointName, byte[] rawData, out double? result) {
            result = null;
            return false;
        }

    }
}
