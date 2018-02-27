using MySql.Data.MySqlClient;
using SharpDapper;
using SharpDapper.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSingle
{
    public class CoinDao
    {
        public CoinDao()
        {
            string connectionString = AccountConfig.sqlConfig;
            var connection = new MySqlConnection(connectionString);
            Database = new DapperConnection(connection);

        }
        protected IDapperConnection Database { get; private set; }

        public void InsertLog(BuyRecord buyRecord)
        {
            if (buyRecord.BuyAnalyze.Length > 4000)
            {
                buyRecord.BuyAnalyze = buyRecord.BuyAnalyze.Substring(0, 4000);
            }
            if (buyRecord.BuyOrderResult.Length > 500)
            {
                buyRecord.BuyOrderResult = buyRecord.BuyOrderResult.Substring(0, 500);
            }

            using (var tx = Database.BeginTransaction())
            {
                Database.Insert(buyRecord);
                tx.Commit();
            }
        }

        public List<TradeRecord> ListNoSellRecord(string buyCoin)
        {
            var sql = $"select * from t_buy_record where BuyCoin = '{buyCoin}' and HasSell=0 and UserName='{AccountConfig.userName}'";
            return Database.Query<TradeRecord>(sql).ToList();
        }

        public int GetAllNoSellRecordCount()
        {
            var sql = $"select count(1) from t_buy_record where HasSell=0 and UserName='{AccountConfig.userName}'";
            return Database.Query<int>(sql).FirstOrDefault();
        }

        public void SetHasSell(long id, decimal sellAmount, string sellOrderResult, string sellAnalyze)
        {
            if (sellAnalyze.Length > 4000)
            {
                sellAnalyze = sellAnalyze.Substring(0, 4000);
            }
            if (sellOrderResult.Length > 500)
            {
                sellOrderResult = sellOrderResult.Substring(0, 500);
            }

            using (var tx = Database.BeginTransaction())
            {
                var sql = $"update t_buy_record set HasSell=1, SellAmount={sellAmount}, SellDate=now(), SellAnalyze='{sellAnalyze}', SellOrderResult='{sellOrderResult}' where Id = {id}";
                Database.Execute(sql);
                tx.Commit();
            }
        }
    }

    [Table("t_trade_record")]
    public class TradeRecord
    {
        public long Id { get; set; }
        public string Coin { get; set; }
        public string AccountId { get; set; }
        public bool HasSell { get; set; }
        public string UserName { get; set; }


        public decimal BuyTotalQuantity { get; set; }
        public decimal BuyOrderPrice { get; set; }
        public decimal BuyTradePrice { get; set; }
        public DateTime BuyDate { get; set; }
        public string BuyOrderResult { get; set; }
        public bool BuySuccess { get; set; }


        public decimal SellTotalQuantity { get; set; }
        public decimal SellOrderPrice { get; set; }
        public decimal SellTradePrice { get; set; }
        public DateTime SellDate { get; set; }
        public string SellOrderResult { get; set; }
        public bool SellSuccess { get; set; }

        public string BuyAnalyze { get; set; }
        public string SellAnalyze { get; set; }
    }
}
