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




    
}