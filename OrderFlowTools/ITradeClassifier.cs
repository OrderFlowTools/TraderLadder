using System;

namespace Gemify.OrderFlow
{
    internal interface ITradeClassifier
    {
        /*
         * Classifies given trade as either buyer or seller initiated.
         */
        TradeAggressor ClassifyTrade(double askPrice, double bidPrice, double tradePrice, long tradeSize, DateTime time);

        /*
         * Classifies given trade as either buyer or seller initiated.
         */
        TradeAggressor ClassifyTrade(double askPrice, long askSize, double bidPrice, long bidSize, double tradePrice, long tradeSize, DateTime time);
    }
}