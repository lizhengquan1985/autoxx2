using AutoHuobi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSingle
{
    public class AnaylyzeData
    {
        // 目前值， 5分钟最高值， 5分钟最低值，目前值得5分钟偏向（以最低值为准）,5分钟列表，1分钟列表
        public decimal NowPrice { get; set; }
        public decimal FiveHighestPrice { get; set; }
        public decimal FiveLowestPrice { get; set; }
        /// <summary>
        /// 目前值得5分钟偏向（以最低值为准）
        /// </summary>
        public decimal NowLeanPercent { get; set; }
        public List<KlineData> FiveKlineData { get; set; }
        public List<KlineData> OneKlineData { get; set; }
    }
}
