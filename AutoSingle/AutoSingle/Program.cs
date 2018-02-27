using log4net;
using log4net.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoSingle
{
    class Program
    {
        static void Main(string[] args)
        {
            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            ILog logger = LogManager.GetLogger("program");
            logger.Error("-------------------------- 软件开始启动 ---------------------------------");

            AccountConfig.init("lzq");
            Console.WriteLine($"{AccountConfig.accessKey}， {AccountConfig.secretKey}， {AccountConfig.sqlConfig}");
            logger.Error("-------------------------- 软件账户配置完成 ---------------------------------");

            Console.WriteLine("输入1：测试，2：正式运行");
            var choose = Console.ReadLine();
            if(choose == "1")
            {
                Test.GoTest();
            }
            else
            {
                while (true)
                {
                    Thread.Sleep(1000 * 2);

                    CoinTrade.BeginRun();
                }
            }

            Console.WriteLine("输入任意推出");
            Console.ReadLine();
        }
    }
}
