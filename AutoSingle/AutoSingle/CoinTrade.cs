using AutoHuobi;
using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoSingle
{
    public class CoinTrade
    {
        static ILog logger = LogManager.GetLogger("CoinTrade");

        private static ResponseAccount accounts;
        private static Dictionary<string, AccountBalanceItem> usdtDict;
        private static Dictionary<string, DateTime> lastGetDate;

        public static AccountBalanceItem GetBlance(string accountId, string coin)
        {
            if (lastGetDate == null)
            {
                lastGetDate = new Dictionary<string, DateTime>();
            }
            if (usdtDict == null)
            {
                usdtDict = new Dictionary<string, AccountBalanceItem>();
            }

            if (lastGetDate.ContainsKey(coin))
            {
                if (lastGetDate[coin] < DateTime.Now.AddMinutes(-10))
                {
                    // 每隔n分钟刷新一次余额0缓存
                    if (usdtDict.ContainsKey(coin))
                    {
                        usdtDict.Remove(coin);
                    }
                }
            }

            if (usdtDict.ContainsKey(coin))
            {
                // 已经有了，不需要再次获取
                return usdtDict[coin];
            }

            var accountInfo = new AccountOrder().AccountBalance(accountId);
            var usdt = accountInfo.data.list.Find(it => it.currency == "usdt" && it.type == "trade");
            usdtDict.Add(coin, usdt);
            if (lastGetDate.ContainsKey(coin))
            {
                lastGetDate.Remove(coin);
            }
            lastGetDate.Add(coin, DateTime.Now);
            return usdtDict[coin];
        }

        public static bool CheckCanBuy(decimal nowOpen, decimal nearLowOpen)
        {
            return nowOpen > nearLowOpen * (decimal)1.005 && nowOpen < nearLowOpen * (decimal)1.012;
        }

        public static bool CheckCanSellByAnaylyzeData(AnaylyzeData anaylyzeData, TradeRecord tradeRecord)
        {
            decimal nearHigherOpen = new decimal(0);
            foreach (var item in anaylyzeData.OneKlineData)
            {
                if (Utils.GetDateById(item.id) <= tradeRecord.BuyDate)
                {
                    continue;
                }

                if (item.open > nearHigherOpen)
                {
                    nearHigherOpen = item.open;
                }
            }

            decimal percent = (decimal)1.04;

            if (anaylyzeData.NowPrice < tradeRecord.BuyOrderPrice * percent)
            {
                return false;
            }

            if (anaylyzeData.NowPrice * (decimal)1.005 < nearHigherOpen && anaylyzeData.NowPrice * (decimal)1.015 > nearHigherOpen)
            {
                return true;
            }

            return false;
        }

        public static decimal GetAvgBuyAmount(decimal balance, int noSellCount)
        {
            if (noSellCount > 5)
            {
                return balance / 4;
            }
            return balance / (8 - noSellCount);
        }

        public static void BeginRun()
        {
            try
            {
                BusinessRun();
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }
        }

        public static void BusinessRun()
        {
            #region 获取所有账户 并找到账户下type为margin下的账户

            if (accounts == null)
            {
                accounts = new AccountOrder().Accounts();
            }
            var accountList = accounts.data.Where(it => it.state == "working" && it.type == "margin").Select(it => it).ToList();
            
            if (accountList.Count == 0)
            {
                // 没有可操作的
                Console.WriteLine("没有可操作的账户");
                return;
            }

            #endregion

            int sleepSecond = (30 * 1000) / accountList.Count;
            foreach (var account in accountList)
            {
                Thread.Sleep(sleepSecond);
                var accountId = account.id;
                var coin = account.subtype.Substring(0, account.subtype.Length - 4);// 减去usdt字符
                var usdtBalance = GetBlance(accountId, coin);

                // 3. 对当前币做分析。找到拐点，并做交易
                decimal nowOpen;
                var flexPointList = new CoinAnalyze().Analyze(coin, "usdt",out nowOpen);
                if (flexPointList.Count == 0)
                {
                    logger.Error($"--------------> 分析结果数量为0 {coin}");
                    continue;
                }

                #region 修正数据， 如果数据没修正，则要手工修正

                try
                {
                    // 查询购买结果还没ok的数据， 去搜索一下，如果ok了，则更新下结果
                    var noSetBuySuccess = new CoinDao().ListNotSetBuySuccess(accountId, coin);
                    foreach (var item in noSetBuySuccess)
                    {
                        QueryDetailAndUpdate(item.BuyOrderId);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex.Message, ex);
                }

                try
                {
                    // 查询出售结果还没好的数据， 去搜索一下，如果ok了，则更新下结果
                    var noSetSellSuccess = new CoinDao().ListHasSellNotSetSellSuccess(accountId, coin);
                    foreach (var item in noSetSellSuccess)
                    {
                        Console.WriteLine("----------> " + JsonConvert.SerializeObject(item));
                        QuerySellDetailAndUpdate(item.SellOrderId);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex.Message, ex);
                }

                #endregion

                try
                {
                    var noSellCount = new CoinDao().GetNoSellRecordCount(account.id, coin);
                    if (usdtBalance.balance > 1 && GetAvgBuyAmount(usdtBalance.balance, noSellCount) > (decimal)0.6)
                    {
                        BusinessRunAccountForBuy(accountId, coin, account, nowOpen, flexPointList);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex.Message, ex);
                }

                try
                {
                    BusinessRunAccountForSell(accountId, coin, account, flexPointList);
                }
                catch (Exception ex)
                {
                    logger.Error(ex.Message, ex);
                }
            }
        }

        public static void BusinessRunAccountForBuy(string accountId, string coin, AccountData account, decimal nowOpen, List<FlexPoint> flexPointList)
        {
            var usdtBalance = GetBlance(accountId, coin);

            var noSellCount = new CoinDao().GetNoSellRecordCount(account.id, coin);
            // 平均推荐购买金额
            var avgBuyAmount = GetAvgBuyAmount(usdtBalance.balance, noSellCount);
            Console.WriteLine($"杠杆------->{coin.PadLeft(7, ' ')}   推荐额度:{decimal.Round(avgBuyAmount, 6).ToString().PadLeft(10)}     未售出数量 {noSellCount}");
            if (!flexPointList[0].isHigh && avgBuyAmount >= 1)
            {
                AnaylyzeData anaylyzeData = new CoinAnalyze().GetAnaylyzeData(coin, "usdt");
                // 最后一次是高位, 
                // 一定要小于5天内最高位的5%
                // 不追高????
                if (noSellCount <= 0 && CheckCanBuy(nowOpen, flexPointList[0].open) && anaylyzeData.NowPrice * (decimal)1.05 < anaylyzeData.FiveHighestPrice)
                {
                    // 可以考虑
                    decimal buyQuantity = avgBuyAmount / nowOpen;
                    buyQuantity = decimal.Round(buyQuantity, GetBuyQuantityPrecisionNumber(coin));
                    decimal orderPrice = decimal.Round(nowOpen * (decimal)1.008, getPrecisionNumber(coin));
                    ResponseOrder order = new AccountOrder().NewOrderBuy(accountId, buyQuantity, orderPrice, null, coin, "usdt");
                    if (order.status != "error")
                    {
                        new CoinDao().CreateTradeRecord(new TradeRecord()
                        {
                            Coin = coin,
                            UserName = AccountConfig.userName,
                            BuyTotalQuantity = buyQuantity,
                            BuyOrderPrice = orderPrice,
                            BuyDate = DateTime.Now,
                            HasSell = false,
                            BuyOrderResult = JsonConvert.SerializeObject(order),
                            BuyAnalyze = JsonConvert.SerializeObject(flexPointList),
                            AccountId = accountId,
                            BuySuccess = false,
                            BuyTradePrice = 0,
                            BuyOrderId = order.data,
                            BuyOrderQuery = "",
                            SellAnalyze = "",
                            SellOrderId = "",
                            SellOrderQuery = "",
                            SellOrderResult = ""
                        });
                        // 下单成功马上去查一次
                        QueryDetailAndUpdate(order.data);
                    }
                    logger.Error($"下单数据 coin：{coin} accountId:{accountId}  购买数量{buyQuantity} nowOpen{nowOpen}，orderPrice：{orderPrice}");
                    logger.Error($"下单拐点 {JsonConvert.SerializeObject(flexPointList)}");
                    logger.Error($"下单结果 {JsonConvert.SerializeObject(order)}");
                }

                if (noSellCount > 0)
                {
                    // 获取最小的那个， 如果有，
                    decimal minBuyPrice = 9999;
                    DateTime nearBuyDate = DateTime.MinValue;
                    var noSellList = new CoinDao().ListNoSellRecord(accountId, coin);
                    if (noSellList.Count == 0)
                    {
                        return;
                    }
                    foreach (var item in noSellList)
                    {
                        if (item.BuyOrderPrice < minBuyPrice)
                        {
                            minBuyPrice = item.BuyOrderPrice;
                        }
                        if(item.BuyDate > nearBuyDate)
                        {
                            nearBuyDate = item.BuyDate;
                        }
                    }

                    // 再少于1%， 并且最近购买时间要至少相差30分钟   控制下单的次数
                    decimal pecent = noSellCount >= 15 ? (decimal)1.05 : (decimal)1.05;
                    if (nearBuyDate < DateTime.Now.AddMinutes(-60 * 4) || nowOpen * pecent < minBuyPrice)
                    {
                        decimal buyQuantity = avgBuyAmount / nowOpen;
                        buyQuantity = decimal.Round(buyQuantity, GetBuyQuantityPrecisionNumber(coin));
                        decimal buyPrice = decimal.Round(nowOpen * (decimal)1.005, getPrecisionNumber(coin));
                        ResponseOrder order = new AccountOrder().NewOrderBuy(accountId, buyQuantity, buyPrice, null, coin, "usdt");
                        if (order.status != "error")
                        {
                            new CoinDao().CreateTradeRecord(new TradeRecord()
                            {
                                Coin = coin,
                                UserName = AccountConfig.userName,
                                BuyTotalQuantity = buyQuantity,
                                BuyOrderPrice = buyPrice,
                                BuyDate = DateTime.Now,
                                HasSell = false,
                                BuyOrderResult = JsonConvert.SerializeObject(order),
                                BuyAnalyze = JsonConvert.SerializeObject(flexPointList),
                                AccountId = accountId,
                                BuySuccess = false,
                                BuyTradePrice = 0,
                                BuyOrderId = order.data,
                                BuyOrderQuery = "",
                                SellAnalyze = "",
                                SellOrderId = "",
                                SellOrderQuery = "",
                                SellOrderResult = ""
                            });
                            // 下单成功马上去查一次
                            QueryDetailAndUpdate(order.data);
                        }
                        logger.Error($"下单数据 coin：{coin} accountId：{accountId}  购买数量：{buyQuantity} buyPrice：{buyPrice} nowOpen：{nowOpen},minBuyPrice:{minBuyPrice}");
                        logger.Error($"下单拐点{JsonConvert.SerializeObject(flexPointList)}");
                        logger.Error($"下单结果{JsonConvert.SerializeObject(order)}");
                    }
                }
            }
        }

        private static void QueryDetailAndUpdate(string orderId)
        {
            string orderQuery = "";
            var queryOrder = new AccountOrder().QueryOrder(orderId, out orderQuery);
            if (queryOrder.status == "ok" && queryOrder.data.state == "filled")
            {
                string orderDetail = "";
                var detail = new AccountOrder().QueryDetail(orderId, out orderDetail);
                decimal maxPrice = 0;
                foreach (var item in detail.data)
                {
                    if (maxPrice < item.price)
                    {
                        maxPrice = item.price;
                    }
                }
                if (detail.status == "ok")
                {
                    new CoinDao().UpdateTradeRecordBuySuccess(orderId, maxPrice, orderQuery);
                }
            }
        }

        /// <summary>
        /// 1. 计算每个购入价位，再最近一段时间的徘徊值，如果偏低，则出售百分比偏高
        /// 2. 计算是当前bi种是否再所有里面是最低的，最更偏高。
        /// 3. 耐性和毅力是最好结果的来源
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="coin"></param>
        /// <param name="account"></param>
        /// <param name="flexPointList"></param>
        public static void BusinessRunAccountForSell(string accountId, string coin, AccountData account, List<FlexPoint> flexPointList)
        {
            if(flexPointList.Count == 0 || !flexPointList[0].isHigh)
            {
                return;
            }

            var needSellList = new CoinDao().ListBuySuccessAndNoSellRecord(accountId, coin);
            // 查询数据库中已经下单数据，如果有，则比较之后的最高值，如果有，则出售
            foreach (var item in needSellList)
            {
                // 分析是否 大于
                //decimal itemNowOpen = 0;
                //decimal higher = new CoinAnalyze().AnalyzeNeedSell(item.BuyOrderPrice, item.BuyDate, coin, "usdt", out itemNowOpen);
                AnaylyzeData anaylyzeData = new CoinAnalyze().GetAnaylyzeData(coin, "usdt");

                //if (CheckCanSell(item.BuyOrderPrice, higher, itemNowOpen))
                if (CheckCanSellByAnaylyzeData(anaylyzeData, item))
                {
                    decimal sellQuantity = item.BuyTotalQuantity * (decimal)0.99;
                    sellQuantity = decimal.Round(sellQuantity, getSellPrecisionNumber(coin));
                    if (coin == "btc" && sellQuantity >= item.BuyTotalQuantity)
                    {
                        sellQuantity = sellQuantity - (decimal)0.0001;
                    }
                    // 出售
                    decimal sellOrderPrice = decimal.Round(anaylyzeData.NowPrice * (decimal)0.985, getPrecisionNumber(coin));
                    if (sellOrderPrice < item.BuyOrderPrice * (decimal)1.025)
                    {
                        logger.Error("-----------------算法有误--------------");
                        Console.WriteLine("-----------------算法有误--------------");
                        return;
                    }
                    ResponseOrder order = new AccountOrder().NewOrderSell(accountId, sellQuantity, sellOrderPrice, null, coin, "usdt");
                    if (order.status != "error")
                    {
                        new CoinDao().ChangeDataWhenSell(item.Id, sellQuantity, sellOrderPrice, JsonConvert.SerializeObject(order), JsonConvert.SerializeObject(flexPointList), order.data);
                        // 下单成功马上去查一次
                        QuerySellDetailAndUpdate(order.data);
                    }
                    logger.Error($"出售结果 coin{coin} accountId:{accountId}  出售数量{sellQuantity} sellOrderPrice：{sellOrderPrice}");
                    logger.Error($"出售拐点 {JsonConvert.SerializeObject(flexPointList)}");
                    logger.Error($"出售单子 {JsonConvert.SerializeObject(item)}");
                    logger.Error($"出售结果 {JsonConvert.SerializeObject(order)}");
                }
            }
        }

        private static void QuerySellDetailAndUpdate(string orderId)
        {
            string orderQuery = "";
            var queryOrder = new AccountOrder().QueryOrder(orderId, out orderQuery);
            if (queryOrder.status == "ok" && queryOrder.data.state == "filled")
            {
                string orderDetail = "";
                var detail = new AccountOrder().QueryDetail(orderId, out orderDetail);
                decimal minPrice = 99999999;
                foreach (var item in detail.data)
                {
                    if (minPrice > item.price)
                    {
                        minPrice = item.price;
                    }
                }
                // 完成
                new CoinDao().UpdateTradeRecordSellSuccess(orderId, minPrice, orderQuery);
            }
        }

        public static int getPrecisionNumber(string coin)
        {
            if (coin == "btc" || coin == "bch" || coin == "eth" || coin == "etc" || coin == "ltc" || coin == "eos" || coin == "omg" || coin == "dash" || coin == "zec" || coin == "hsr"
                 || coin == "qtum" || coin == "neo" || coin == "ven" || coin == "nas")
            {
                return 2;
            }
            return 4;
        }

        public static int getSellPrecisionNumber(string coin)
        {
            if (coin == "cvc" || coin == "ht" || coin == "xrp")
            {
                return 2;
            }
            return 4;
        }

        /// <summary>
        /// 获取购买数量的精度
        /// </summary>
        /// <param name="coin"></param>
        /// <returns></returns>
        public static int GetBuyQuantityPrecisionNumber(string coin)
        {
            if (coin == "btc")
            {
                return 4;
            }

            if (coin == "bch" || coin == "dash" || coin == "eth" || coin == "zec")
            {
                return 3;
            }

            return 2;
        }
    }
}
