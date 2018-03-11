﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSingle
{
    public class ResponseKline
    {
        public string status { get; set; }
        public string ch { get; set; }
        public string ts { get; set; }
        public List<KlineData> data { get; set; }
    }

    public class KlineData
    {
        public long id { get; set; }
        public decimal amount { get; set; }
        public decimal count { get; set; }
        public decimal open { get; set; }
        public decimal close { get; set; }
        public decimal low { get; set; }
        public decimal high { get; set; }
        public decimal vol { get; set; }
    }

    public class ResponseAccount
    {
        public string status { get; set; }
        public List<AccountData> data { get; set; }
    }

    public class AccountData
    {
        public string id { get; set; }
        public string type { get; set; }
        public string subtype { get; set; }
        /// <summary>
        /// working正常
        /// </summary>
        public string state { get; set; }
    }

    public class ResponseOrder
    {
        public string status { get; set; }
        public string data { get; set; }
    }

    public class ResponseQueryOrder
    {
        public string status { get; set; }
        public OrderData data { get; set; }
    }

    public class OrderData
    {
        public string id { get; set; }
        // 如 gntusdt
        public string symbol { get; set; }
        public decimal amount { get; set; }
        public decimal price { get; set; }
        public string type { get; set; }
        public string state { get; set; }
        public string source { get; set; }
    }

    public class ResponseOrderDetail
    {
        public string status { get; set; }
        public List<OrderDetailData> data { get; set; }
    }

    public class OrderDetailData
    {
        public string id { get; set; }
        // 如 gntusdt
        public string symbol { get; set; }
        public string type { get; set; }
        public decimal price { get; set; }
        public string source { get; set; }
    }

    public class AccountBalance
    {
        public string status { get; set; }
        public AccountBalanceData data { get; set; }
    }

    public class AccountBalanceData
    {
        public long id { get; set; }
        public string type { get; set; }
        public string state { get; set; }
        public List<AccountBalanceItem> list { get; set; }
    }

    public class AccountBalanceItem
    {
        public string currency { get; set; }
        public decimal balance { get; set; }
        public string type { get; set; }
    }

    #region

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
    #endregion

}
