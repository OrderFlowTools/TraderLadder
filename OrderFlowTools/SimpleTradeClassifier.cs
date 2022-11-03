using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gemify.OrderFlow
{
    class SimpleTradeClassifier : ITradeClassifier
    {

        /// <summary>
        /// Simple trade classifier implementation. No ICE implementation.
        /// </summary>
        /// <param name="askPrice"></param>
        /// <param name="askSize"></param>
        /// <param name="bidPrice"></param>
        /// <param name="bidSize"></param>
        /// <param name="tradePrice"></param>
        /// <param name="tradeSize"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public TradeAggressor ClassifyTrade(double askPrice, long askSize, double bidPrice, long bidSize, double tradePrice, long tradeSize, DateTime time)
        {
            throw new NotImplementedException("Not implemented");
        }

        /// <summary>
        /// Simple trade classifier implementation. 
        /// </summary>
        /// <param name="askPrice"></param>
        /// <param name="bidPrice"></param>
        /// <param name="tradePrice"></param>
        /// <param name="tradeSize"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public TradeAggressor ClassifyTrade(double askPrice, double bidPrice, double tradePrice, long tradeSize, DateTime time)
        {
            TradeAggressor aggressor;

            double midpoint = (askPrice + bidPrice) / 2.0;

            if (askPrice == bidPrice)
            {
                if (tradePrice > askPrice)
                {
                    aggressor = TradeAggressor.BUYER;
                }
                else
                {
                    aggressor = TradeAggressor.SELLER;
                }
            }
            else
            {
                if (tradePrice > midpoint)
                {
                    aggressor = TradeAggressor.BUYER;
                }
                else
                {
                    aggressor = TradeAggressor.SELLER;
                }
            }

            return aggressor;
        }
    }
}
