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
            //nowOpen > flexPointList[0].open * (decimal)1.005 && nowOpen < flexPointList[0].open * (decimal)1.01
            return nowOpen > nearLowOpen * (decimal)1.005 && nowOpen < nearLowOpen * (decimal)1.01;
        }

        public static bool CheckCanSell(decimal buyPrice, decimal nearHigherOpen, decimal nowOpen)
        {
            //item.BuyPrice, higher, itemNowOpen
            // if (item.BuyPrice * (decimal)1.05 < higher && itemNowOpen * (decimal)1.005 < higher)
            if (nowOpen < buyPrice * (decimal)1.03)
            {
                // 如果不高于 3% 没有意义
                return false;
            }

            if (nowOpen * (decimal)1.005 < nearHigherOpen)
            {
                // 表示回头趋势， 暂时定为 0.5% 就有回头趋势
                return true;
            }

            return false;
            // buyPrice * (decimal)1.05 < nearHigherOpen && 
        }


        public static decimal GetAvgBuyAmount(decimal balance, int noSellCount)
        {
            if (noSellCount > 6)
            {
                return balance / 5;
            }
            return balance / (10 - noSellCount);
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
            // 1. 获取所有账户
            if (accounts == null)
            {
                accounts = new AccountOrder().Accounts();
            }
            // 2. 分析账户下type为margin下的账户， 找到余额大于6， 并且平均大于1的账户
            var accountList = accounts.data.Where(it => it.state == "working" && it.type == "margin").Select(it => it).ToList();
            for (var i = accountList.Count - 1; i >= 0; i--)
            {
                var account = accountList[i];
                var coin = account.subtype.Substring(0, account.subtype.Length - 4);// 减去usdt字符
                var usdt = GetBlance(account.id, coin);
                var noSellCount = new CoinDao().GetNoSellRecordCount(account.id, coin);
                if (usdt.balance < 6 || GetAvgBuyAmount(usdt.balance, noSellCount) < 1)
                {
                    accountList.RemoveAt(i);
                    continue;
                }
            }

            if (accountList.Count == 0)
            {
                // 没有可操作的
                Console.WriteLine("没有可操作的账户");
                return;
            }

            int sleepSecond = (30 * 1000) / accountList.Count;
            foreach (var account in accountList)
            {
                Thread.Sleep(sleepSecond);
                var coin = account.subtype.Substring(0, account.subtype.Length - 4);// 减去usdt字符

                var accountId = account.id;
                var usdtBalance = GetBlance(accountId, coin);
                Console.WriteLine($"------------- 开始操作 {coin} {JsonConvert.SerializeObject(usdtBalance)} ----------------------");

                // 3. 对当前币做分析。找到拐点，并做交易
                decimal lastLow;
                decimal nowOpen;
                var flexPointList = new CoinAnalyze().Analyze(coin, "usdt", out lastLow, out nowOpen);
                if (flexPointList.Count == 0)
                {
                    logger.Error($"--------------> 分析结果数量为0 {coin}");
                    return;
                }

                try
                {
                    // 查询出结果还没好的数据， 去搜索一下
                    var noSetBuySuccess = new CoinDao().ListNotSetBuySuccess(accountId, coin);
                    foreach (var item in noSetBuySuccess)
                    {
                        string orderQuery = "";
                        var queryOrder = new AccountOrder().QueryOrder(item.BuyOrderId, out orderQuery);
                        if (queryOrder.status == "ok" && queryOrder.data.state == "filled")
                        {
                            // 完成
                            new CoinDao().UpdateTradeRecordBuySuccess(item.BuyOrderId, queryOrder.data.price, orderQuery);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex.Message, ex);
                }

                try
                {
                    BusinessRunAccountForBuy(accountId, coin, account, lastLow, nowOpen, flexPointList);
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

        public static void BusinessRunAccountForBuy(string accountId, string coin, AccountData account, decimal lastLow, decimal nowOpen, List<FlexPoint> flexPointList)
        {
            var usdtBalance = GetBlance(accountId, coin);

            // 4. 对购入的成交价 和 出售的成交价做记录
            var noSellCount = new CoinDao().GetNoSellRecordCount(account.id, coin);
            // 平均推荐购买金额
            var avgBuyAmount = GetAvgBuyAmount(usdtBalance.balance, noSellCount);
            Console.WriteLine($"-------------> avgBuyAmount {avgBuyAmount}");
            Console.WriteLine($"-------------> noSellCount {noSellCount}");
            if (!flexPointList[0].isHigh && avgBuyAmount > 1)
            {
                // 最后一次是高位
                if (noSellCount <= 0 && CheckCanBuy(nowOpen, flexPointList[0].open))
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
                    else
                    {
                        logger.Error($"下单结果 {coin} accountId:{accountId}  购买数量{buyQuantity} nowOpen{nowOpen} {JsonConvert.SerializeObject(order)}");
                        logger.Error($"下单结果 拐点分析 {JsonConvert.SerializeObject(flexPointList)}");
                    }
                }

                if (noSellCount > 0)
                {
                    // 获取最小的那个， 如果有，
                    decimal minBuyPrice = 9999;
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
                    }

                    // 再少于5%， 
                    decimal pecent = noSellCount >= 15 ? (decimal)1.03 : (decimal)1.025;
                    if (nowOpen * pecent < minBuyPrice)
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
                        else
                        {
                            logger.Error($"下单结果 coin{coin} accountId:{accountId}  购买数量{buyQuantity} nowOpen{nowOpen} {JsonConvert.SerializeObject(order)}");
                            logger.Error($"下单结果 分析 {JsonConvert.SerializeObject(flexPointList)}");
                        }
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
                if(detail.status == "ok")
                {
                    new CoinDao().UpdateTradeRecordBuySuccess(orderId, detail.data.price, orderQuery);
                }
            }
        }

        public static void BusinessRunAccountForSell(string accountId, string coin, AccountData account, List<FlexPoint> flexPointList)
        {
            var needSellList = new CoinDao().ListBuySuccessAndNoSellRecord(accountId, coin);
            // 查询数据库中已经下单数据，如果有，则比较之后的最高值，如果有，则出售
            foreach (var item in needSellList)
            {
                // 分析是否 大于
                decimal itemNowOpen = 0;
                decimal higher = new CoinAnalyze().AnalyzeNeedSell(item.BuyOrderPrice, item.BuyDate, coin, "usdt", out itemNowOpen);

                if (CheckCanSell(item.BuyOrderPrice, higher, itemNowOpen))
                {
                    decimal sellQuantity = item.BuyTotalQuantity * (decimal)0.99;
                    sellQuantity = decimal.Round(sellQuantity, getSellPrecisionNumber(coin));
                    // 出售
                    decimal sellOrderPrice = decimal.Round(itemNowOpen * (decimal)0.985, getPrecisionNumber(coin));
                    ResponseOrder order = new AccountOrder().NewOrderSell(accountId, sellQuantity, sellOrderPrice, null, coin, "usdt");
                    if (order.status != "error")
                    {
                        new CoinDao().ChangeDataWhenSell(item.Id, sellQuantity, sellOrderPrice, JsonConvert.SerializeObject(order), JsonConvert.SerializeObject(flexPointList), order.data);
                        // 下单成功马上去查一次
                        string orderQuery = "";
                        var queryOrder = new AccountOrder().QueryOrder(order.data, out orderQuery);
                        if (queryOrder.status == "ok" && queryOrder.data.state == "filled")
                        {
                            // 完成
                            new CoinDao().UpdateTradeRecordSellSuccess(order.data, queryOrder.data.price, orderQuery);
                        }
                    }
                    else
                    {
                        logger.Error($"出售结果 coin{coin} accountId:{accountId}  出售数量{sellQuantity} itemNowOpen{itemNowOpen} higher{higher} {JsonConvert.SerializeObject(order)}");
                        logger.Error($"出售结果 分析 {JsonConvert.SerializeObject(flexPointList)}");
                    }
                }
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
