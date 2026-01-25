using Device;
using Modbus.Device;
using System;
using System.ComponentModel.DataAnnotations;
using System.Formats.Asn1;

namespace Interfaces
{


    public interface IDeviceModel
    {
        public deviceTypes DeviceTypes { get; }
        /// <summary>
        /// 拼接私有属性的源数据。
        /// </summary>
        /// <remarks>
        /// 输入byte[],并自身完成拼接方法。
        /// </remarks>
        /// <param name="RawData"></param>
        /// <param name="Result"></param>
        /// <returns>
        /// Bool:是否成功拼接。对于无需实现的类，应当直接返回false。
        /// 如果存在超过一类非常规拼接值，建议重写TryJoint以保证接口的干净程度。
        /// </returns>
        bool TryJoint(byte[] RawData, out double Result);


    }
    /// <summary>
    /// 用于
    /// </summary>
    public interface IPointAtrributesCache
    {
        public int Index { get; set; }
        public int Address { get; set; }
        public pointTypes PointType { get;  }

    }

    public interface IResgisterPointCache : IPointAtrributesCache
    {
        public float A1 { get; set; }
        public float A2 { get; set; }
        public float A3 { get; set; }
        public float B { get; set; }
        public float Offset { get; set; }
        public jointTypes JointTypes { get; set; }
        public bool CustomMapping { get; set; }
        public short Length { get; set; }

    }

    public interface ICoilPointCache : IPointAtrributesCache
    {
        public bool Reverse { get; set; }
    }
}


