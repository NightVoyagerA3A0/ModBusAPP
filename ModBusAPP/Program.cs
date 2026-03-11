// See https://aka.ms/new-console-template for more information
//Console.WriteLine("Hello, World!");
using Components;
using Device;
using Modbus.Device;
using Modbus.IO;
using System.IO.Ports;
using System.Threading.Tasks;
namespace Receiver
{
    class StaticPrograms
    {
        public static async Task Main()
        {
            TestDevice myDevice = new TestDevice();
            DataMapper mapper = new DataMapper(myDevice);
            bool stopToken = false;
            int i = 0;
            var t = typeof(Modbus.IO.IStreamResource);
            //Modbus.IO.IStreamResource nModbusAdapter = new NModbusRTUAdapter();
            Console.WriteLine("Waiting for input.(Using async, await and task.)");
            //string? input = await Task.Run(() => Console.ReadLine());
            //Task sleeping = Task.Run(async () =>
            //{
            //    while (!stopToken)
            //    {
            //        Console.WriteLine($"Still Sleepy for {i} second(s)");
            //        i++;
            //        await Task.Delay(1000);
            //    }
            //});
            var input = Console.ReadLine();
            var displayed = string.IsNullOrEmpty(input) ? "Nothing" : input;
            Console.WriteLine($"You just input : {displayed}");
            stopToken = true;
            //await sleeping;
            SpecialDelegates awake = new SpecialDelegates();
            awake.WakeUpBird(i);
            if (!string.IsNullOrEmpty(input)) { awake.WakeUpDog(displayed); } ;
        }
        //public static  RTUPortScaner
    }

    class SpecialDelegates
    {
        public delegate void WakeItUpHandle<T>(T whatYouWantToWake);
        public SpecialDelegates()
        {
            WakeItUpHandle<string> doSomething = WakeUpCat;
            doSomething("Meow");
            return;//doNothing,a example.
        }

        public WakeItUpHandle<string> WakeUpDog = delegate (string dogName)
        {
            Console.WriteLine($"{dogName} have been woken! It barks: Woof, Woof!");
        };

        public WakeItUpHandle<int> WakeUpBird = (int howMuch) =>
        {
            Console.WriteLine($"{howMuch.ToString()} birds have been woken!");
        };

        public void WakeUpCat(string catName)
        {
            Console.WriteLine($"{catName} ignored you!");
        }
    }

    class PollMission
    {
        //来回想一下Action和Function
        //谈谈结构，这里面是打开了不关闭的action，而听到cts则立刻关掉。
        //我要学习一个cts的用法，delegate我会手写，却不会写action
        //创建了轮询任务之后应该要在全局表中显示一个占用符号，Create之后该action一直旋转，直到CancelMission为止
        //是否要保存对应的cts和sp名字pair？词典？task也应该是一个词典。
        //这就有一个问题了，MainMission是针对端口的,Mission本身只管轮询，而单端口的多设备序列管理是不由其决定的，当然，也可以由其决定，但注意，这里可以不做。
        //如果要做就会非常Fat，考虑这几点：一个端口可能有多个设备，一个设备可能有多个点位，而一个PollMission独占一个端口，实际上Mission就是所谓的虚设备类了。

        private PollMission()
        {
            //donothing;

        }

        // ===== VibeCoding 修改开始：仅为补全 TryCreate 的最小可编译返回路径与 out 赋值 =====
        public static bool TryCreate(string SPName, out PollMission? worker, int Retries = 3, int ReadTimeout = 300)
        {
            var _nameList = SerialPort.GetPortNames();
            if (!_nameList.Contains(SPName)) {
                Console.WriteLine($"There hasn't any Serial Ports named:{SPName}, do nothing.");
                worker = null;
                return false; //fast failure. 这里暂时这么写
            }
            ;

            string _serialPortName = SPName;
            SerialPort serialPort = new SerialPort(_serialPortName);
            if (!serialPort.IsOpen) { serialPort.Open(); }
            ModbusSerialMaster serialMaster = ModbusSerialMaster.CreateRtu(serialPort);
            serialMaster.Transport.Retries = Retries;
            serialMaster.Transport.ReadTimeout = ReadTimeout; //三倍字节长，9600波特率三倍字节长是多少？

            worker = new PollMission();
            return true;
            //return new MainMissionWorker(); 思索中……
        }
        // ===== VibeCoding 修改结束：仅为补全 TryCreate 的最小可编译返回路径与 out 赋值 =====

    }
    /// <summary>
    /// Mission是用于装载任务信息的属性类，包含了
    /// </summary>


    
}
