// 
// Copyright (C) 2021, Gem Immanuel (gemify@gmail.com)
//

using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LadderRow = NinjaTrader.Gui.SuperDom.LadderRow;

namespace Gemify.OrderFlow
{
    class OFStrength
    {
        internal double buyStrength = 0.0;
        internal double sellStrength = 0.0;
    }

    class Trade
    {
        internal TradeAggressor aggressor { get; set; }
        internal long Size { get; set; }
        internal long swCumulSize { get; set; }
        internal double Ask { get; set; }
        internal double Bid { get; set; }
        internal double AskSize { get; set; }
        internal double BidSize { get; set; }
        internal DateTime Time { get; set; }
    }

    class BidAsk
    {
        public BidAsk()
        {
            this.Size = 0;
            this.Time = new DateTime();
        }
        public BidAsk(double size, DateTime time)
        {
            this.Size = size;
            this.Time = time;
        }

        internal double Size { get; set; }
        internal DateTime Time { get; set; }
    }

    class BidAskPerc : BidAsk
    {
        internal double Perc { get; set; }
    }

    enum TradeAggressor
    {
        BUYER,
        SELLER
    }

    public enum OFSCalculationMode
    {
        COMBINED,
        IMBALANCE,
        BUY_SELL
    }

    class GemsOrderFlow
    {

        protected ITradeClassifier tradeClassifier;

        private ConcurrentDictionary<double, Trade> SlidingWindowBuys;
        private ConcurrentDictionary<double, Trade> SlidingWindowSells;
        private ConcurrentDictionary<double, long> TotalBuys;
        private ConcurrentDictionary<double, long> TotalSells;

        private ConcurrentDictionary<double, long> LastBuy;
        private ConcurrentDictionary<double, long> LastSell;
        private ConcurrentDictionary<double, long> LastBuyPrint;
        private ConcurrentDictionary<double, long> LastSellPrint;
        private ConcurrentDictionary<double, long> LastBuyPrintMax;
        private ConcurrentDictionary<double, long> LastSellPrintMax;


        private ConcurrentDictionary<double, BidAsk> CurrBid;
        private ConcurrentDictionary<double, BidAsk> CurrAsk;
        private ConcurrentDictionary<double, long> BidChange;
        private ConcurrentDictionary<double, long> AskChange;
        private ConcurrentDictionary<double, BidAskPerc> BidsPerc;
        private ConcurrentDictionary<double, BidAskPerc> AsksPerc;

        private double imbalanceFactor;
        private long imbalanceInvalidateDistance;
        private const int minSlidingWindowTrades = 1;

        // To support Print
        private Indicator ind;

        public GemsOrderFlow(ITradeClassifier tradeClassifier, double imbalanceFactor)
        {
            ind = new Indicator();

            this.tradeClassifier = tradeClassifier;

            this.imbalanceFactor = imbalanceFactor;
            this.imbalanceInvalidateDistance = 10;

            SlidingWindowBuys = new ConcurrentDictionary<double, Trade>();
            SlidingWindowSells = new ConcurrentDictionary<double, Trade>();
            TotalBuys = new ConcurrentDictionary<double, long>();
            TotalSells = new ConcurrentDictionary<double, long>();

            LastBuy = new ConcurrentDictionary<double, long>();
            LastSell = new ConcurrentDictionary<double, long>();
            LastBuyPrint = new ConcurrentDictionary<double, long>();
            LastSellPrint = new ConcurrentDictionary<double, long>();
            LastBuyPrintMax = new ConcurrentDictionary<double, long>();
            LastSellPrintMax = new ConcurrentDictionary<double, long>();

            CurrAsk = new ConcurrentDictionary<double, BidAsk>();
            CurrBid = new ConcurrentDictionary<double, BidAsk>();
            AskChange = new ConcurrentDictionary<double, long>();
            BidChange = new ConcurrentDictionary<double, long>();
            BidsPerc = new ConcurrentDictionary<double, BidAskPerc>();
            AsksPerc = new ConcurrentDictionary<double, BidAskPerc>();
        }

        internal void ClearAll()
        {

            ClearSlidingWindow();

            TotalBuys.Clear();
            TotalSells.Clear();

            CurrBid.Clear();
            CurrAsk.Clear();
            BidChange.Clear();
            AskChange.Clear();
            BidsPerc.Clear();
            AsksPerc.Clear();
        }

        internal void ClearSlidingWindow()
        {
            SlidingWindowBuys.Clear();
            SlidingWindowSells.Clear();

            LastBuy.Clear();
            LastSell.Clear();
            LastBuyPrint.Clear();
            LastSellPrint.Clear();
            LastBuyPrintMax.Clear();
            LastSellPrintMax.Clear();
        }

        private void Print(string s)
        {
            ind.Print(s);
        }

        internal void SetBidLadder(List<LadderRow> newBidLadder)
        {
            try
            {
                CurrBid.Clear();
                BidChange.Clear();
                if (newBidLadder != null)
                {
                    foreach (LadderRow row in newBidLadder)
                    {
                        BidAsk entry = new BidAsk(row.Volume, row.Time);
                        if (CurrBid != null) CurrBid.TryAdd(row.Price, entry);                        
                    }
                }
            }
            catch (Exception ex)
            {
                // NOP for now. 
            }
        }

        internal void SetAskLadder(List<LadderRow> newAskLadder)
        {
            try
            {
                CurrAsk.Clear();
                AskChange.Clear();
                if (newAskLadder != null)
                {
                    foreach (LadderRow row in newAskLadder)
                    {
                        BidAsk entry = new BidAsk(row.Volume, row.Time);
                        if (CurrAsk != null) CurrAsk.TryAdd(row.Price, entry);                        
                    }
                }
            }
            catch (Exception ex)
            {
                // NOP for now. 
            }
        }        

        internal BidAsk AddOrUpdateBid(double price, long size, DateTime time)
        {
            BidAsk currBidAsk = null;
            CurrBid.TryGetValue(price, out currBidAsk);

            // Change in bid size
            long change = Convert.ToInt64(size - (currBidAsk == null ? 0 : currBidAsk.Size));
            if (size > 0)
            {
                BidChange.AddOrUpdate(price, change, (key, value) => change);
            }
            else
            {
                BidChange.AddOrUpdate(price, 0, (key, value) => 0);
            }

            // Add or replace current entry
            BidAsk newBidAsk = new BidAsk(size, time);
            return CurrBid.AddOrUpdate(price, newBidAsk, (key, existing) => newBidAsk);
        }

        internal BidAsk AddOrUpdateAsk(double price, long size, DateTime time)
        {
            BidAsk currBidAsk = null;
            CurrAsk.TryGetValue(price, out currBidAsk);

            // Change in ask size
            long change = Convert.ToInt64(size - (currBidAsk == null ? 0 : currBidAsk.Size));
            if (size > 0)
            {
                AskChange.AddOrUpdate(price, change, (key, value) => change);
            }
            else
            {
                AskChange.AddOrUpdate(price, 0, (key, value) => 0);
            }

            // Add or replace current entry
            BidAsk newBidAsk = new BidAsk(size, time);
            return CurrAsk.AddOrUpdate(price, newBidAsk, (key, existing) => newBidAsk);
        }

        /*
         * Classifies given trade as either buyer or seller initiated based on configured classifier.
         */
        internal void ClassifyTrade(bool updateSlidingWindow, double ask, double askSize, double bid, double bidSize, double close, long volume, DateTime time)
        {
            TradeAggressor aggressor = tradeClassifier.ClassifyTrade(ask, bid, close, volume, time);

            // Classification - buyers vs. sellers
            if (aggressor == TradeAggressor.BUYER)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowBuys.TryGetValue(close, out oldTrade);

                Trade trade = new Trade();
                trade.aggressor = aggressor;
                trade.Ask = ask;
                trade.AskSize = askSize;
                trade.Time = time;
                trade.Size = volume;

                if (gotOldTrade)
                {
                    trade.swCumulSize = oldTrade.swCumulSize + volume;
                }
                else
                {
                    trade.swCumulSize = volume;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowBuys.AddOrUpdate(close, trade, (price, existingTrade) => existingTrade = trade);
                    // Update last buy
                    LastBuy.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    LastBuyPrint.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    long lastMax = 0;
                    LastBuyPrintMax.TryGetValue(close, out lastMax);
                    if (volume > lastMax)
                    {
                        LastBuyPrintMax.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    }
                }
                TotalBuys.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
            }
            else if (aggressor == TradeAggressor.SELLER)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowSells.TryGetValue(close, out oldTrade);

                Trade trade = new Trade();
                trade.aggressor = aggressor;
                trade.Bid = bid;
                trade.BidSize = bidSize;
                trade.Time = time;
                trade.Size = volume;

                if (gotOldTrade)
                {
                    trade.swCumulSize = oldTrade.swCumulSize + volume;
                }
                else
                {
                    trade.swCumulSize = volume;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowSells.AddOrUpdate(close, trade, (price, existingTrade) => existingTrade = trade);
                    // Update last sell
                    LastSell.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    LastSellPrint.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    long lastMax = 0;
                    LastSellPrintMax.TryGetValue(close, out lastMax);
                    if (volume > lastMax)
                    {
                        LastSellPrintMax.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    }
                }
                TotalSells.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
            }
        }

        /*
        * Gets total buy volume in the sliding window
        */
        internal long GetBuysInSlidingWindow()
        {
            long total = 0;
            foreach (Trade trade in SlidingWindowBuys.Values)
            {
                total += trade.swCumulSize;
            }
            return total;
        }

        /*
        * Gets total sell volume in the sliding window
        */
        internal long GetSellsInSlidingWindow()
        {
            long total = 0;
            foreach (Trade trade in SlidingWindowSells.Values)
            {
                total += trade.swCumulSize;
            }
            return total;
        }

        /*
        * Gets total buy (large) volume in the sliding window
        */
        internal long GetTotalLargeBuysInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastBuyPrintMax.Values)
            {
                total += size;
            }
            return total;
        }

        /*
        * Gets total sell (large) volume in the sliding window
        */
        internal long GetTotalLargeSellsInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastSellPrintMax.Values)
            {
                total += size;
            }
            return total;
        }

        /*
        * Gets sum of current buy prints in the sliding window
        */
        internal long GetTotalBuyPrintsInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastBuyPrint.Values)
            {
                total += size;
            }
            return total;
        }

        /*
        * Gets sum of current sell prints in the sliding window
        */
        internal long GetTotalSellPrintsInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastSellPrint.Values)
            {
                total += size;
            }
            return total;
        }

        internal double GetHighestBuyPriceInSlidingWindow()
        {
            IOrderedEnumerable<double> prices = SlidingWindowBuys.Keys.OrderByDescending(i => ((float)i));
            return prices.FirstOrDefault();
        }

        internal double GetLowestSellPriceInSlidingWindow()
        {
            IOrderedEnumerable<double> prices = SlidingWindowSells.Keys.OrderByDescending(i => ((float)i));
            return prices.LastOrDefault();

        }

        /*
         * Gets total volume transacted (buyers + sellers) at given price.
         */
        internal long GetVolumeAtPrice(double price)
        {
            long buyVolume = 0, sellVolume = 0;
            TotalBuys.TryGetValue(price, out buyVolume);
            TotalSells.TryGetValue(price, out sellVolume);
            long totalVolume = buyVolume + sellVolume;
            return totalVolume;
        }

        /* 
         * Clear out trades from the buys and sells collection if the trade entries 
         * fall outside (older trades) of a sliding time window (seconds), 
         * thus preserving only the latest trades based on the time window.
         */
        internal void ClearTradesOutsideSlidingWindow(DateTime time, int TradeSlidingWindowSeconds)
        {
            foreach (double price in SlidingWindowBuys.Keys)
            {
                Trade trade;
                bool gotTrade = SlidingWindowBuys.TryGetValue(price, out trade);
                if (gotTrade)
                {
                    TimeSpan diff = time - trade.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowBuys.TryRemove(price, out trade);
                        long oldVolume;
                        LastBuy.TryRemove(price, out oldVolume);
                        LastBuyPrint.TryRemove(price, out oldVolume);
                        LastBuyPrintMax.TryRemove(price, out oldVolume);
                    }
                }
            }

            foreach (double price in SlidingWindowSells.Keys)
            {
                Trade trade;
                bool gotTrade = SlidingWindowSells.TryGetValue(price, out trade);
                if (gotTrade)
                {
                    TimeSpan diff = time - trade.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowSells.TryRemove(price, out trade);
                        long oldVolume;
                        LastSell.TryRemove(price, out oldVolume);
                        LastSellPrint.TryRemove(price, out oldVolume);
                        LastSellPrintMax.TryRemove(price, out oldVolume);
                    }
                }
            }
        }

        internal long GetImbalancedBuys(double currentPrice, double tickSize)
        {
            long buyImbalance = 0;

            foreach (double buyPrice in SlidingWindowBuys.Keys)
            {
                // If we've blown past "imbalance", the imbalance did not hold up. It does not indicate strength.
                if (currentPrice < buyPrice - (imbalanceInvalidateDistance * tickSize))
                {
                    continue;
                }

                Trade buyTrade;
                bool gotBuy = SlidingWindowBuys.TryGetValue(buyPrice, out buyTrade);
                long buySize = gotBuy ? buyTrade.swCumulSize : 0;

                Trade sellTrade;
                bool gotSell = SlidingWindowSells.TryGetValue(buyPrice - tickSize, out sellTrade);
                long sellSize = gotSell ? sellTrade.swCumulSize : 0;

                if (gotSell && buySize >= sellSize * imbalanceFactor)
                    buyImbalance += buySize;
            }

            return buyImbalance;
        }

        internal long GetImbalancedSells(double currentPrice, double tickSize)
        {
            long sellImbalance = 0;

            foreach (double sellPrice in SlidingWindowSells.Keys)
            {
                // If we've blown past "imbalance", the imbalance did not hold up. It does not indicate strength.
                if (currentPrice > sellPrice + (imbalanceInvalidateDistance * tickSize))
                {
                    continue;
                }

                Trade sellTrade;
                bool gotSell = SlidingWindowSells.TryGetValue(sellPrice, out sellTrade);
                long sellSize = gotSell ? sellTrade.swCumulSize : 0;

                Trade buyTrade;
                bool gotBuy = SlidingWindowBuys.TryGetValue(sellPrice + tickSize, out buyTrade);
                long buySize = gotBuy ? buyTrade.swCumulSize : 0;

                if (gotBuy && sellSize >= buySize * imbalanceFactor)
                    sellImbalance += sellSize;
            }

            return sellImbalance;
        }

        internal BidAskPerc GetBidPerc(double price)
        {
            BidAskPerc bidAskPerc = null;
            this.BidsPerc.TryGetValue(price, out bidAskPerc);
            return bidAskPerc;
        }

        internal BidAskPerc GetAskPerc(double price)
        {
            BidAskPerc bidAskPerc = null;
            this.AsksPerc.TryGetValue(price, out bidAskPerc);
            return bidAskPerc;
        }

        internal OFStrength CalculateOrderFlowStrength(OFSCalculationMode mode, double price, double tickSize)
        {
            OFStrength orderFlowStrength = new OFStrength();

            // Short-circuit if there's not enough data
            if ((SlidingWindowBuys.Count + SlidingWindowSells.Count) < minSlidingWindowTrades)
            {
                return orderFlowStrength;
            }

            long buyImbalance = 0, sellImbalance = 0, totalImbalance = 0, buysInSlidingWindow = 0, sellsInSlidingWindow = 0, totalVolume = 0;

            if (mode == OFSCalculationMode.COMBINED || mode == OFSCalculationMode.IMBALANCE)
            {
                // Imbalance data
                buyImbalance = GetImbalancedBuys(price, tickSize);
                sellImbalance = GetImbalancedSells(price, tickSize);

                if (buyImbalance + sellImbalance == 0)
                {
                    buyImbalance = sellImbalance = 1;
                }

                totalImbalance = buyImbalance + sellImbalance;
            }

            if (mode == OFSCalculationMode.COMBINED || mode == OFSCalculationMode.BUY_SELL)
            {
                // Buy/Sell data in sliding window
                buysInSlidingWindow = GetBuysInSlidingWindow();
                sellsInSlidingWindow = GetSellsInSlidingWindow();

                if (buysInSlidingWindow + sellsInSlidingWindow == 0)
                {
                    buysInSlidingWindow = sellsInSlidingWindow = 1;
                }

                totalVolume = sellsInSlidingWindow + buysInSlidingWindow;
            }

            orderFlowStrength.buyStrength = (Convert.ToDouble(buysInSlidingWindow + buyImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.00;
            orderFlowStrength.sellStrength = (Convert.ToDouble(sellsInSlidingWindow + sellImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.8;

            return orderFlowStrength;
        }

        internal long GetBuyVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalBuys.TryGetValue(price, out volume);
            return volume;
        }

        internal void CalculateBidAskPerc(double tickSize, double currentBidPrice, double currentAskPrice, double lowerBidCutOffPrice, double upperAskCutOffPrice)
        {
            AsksPerc.Clear();
            BidsPerc.Clear();

            // Calculate largest Bid/Ask Size (for histogram drawing)
            double largestBidAskSize = GetLargestBidAskSize(tickSize, currentBidPrice, currentAskPrice, lowerBidCutOffPrice, upperAskCutOffPrice);

            for (double price = currentAskPrice; price < upperAskCutOffPrice; price += tickSize)
            {
                BidAsk currentAsk;
                if (!CurrAsk.TryGetValue(price, out currentAsk)) continue;

                // Calculate percentage of current ask volume relative to total bid/ask volume
                BidAskPerc perc = new BidAskPerc();
                perc.Size = currentAsk.Size;
                perc.Perc = currentAsk.Size / largestBidAskSize;
                AsksPerc.AddOrUpdate(price, perc, (key, existing) => existing = perc);
            }

            for (double price = currentBidPrice; price > lowerBidCutOffPrice; price -= tickSize)
            {
                BidAsk currentBid;
                if (!CurrBid.TryGetValue(price, out currentBid)) continue;

                // Calculate percentage of current bid volume relative to total bid/ask volume
                BidAskPerc perc = new BidAskPerc();
                perc.Size = currentBid.Size;
                perc.Perc = currentBid.Size / largestBidAskSize;
                BidsPerc.AddOrUpdate(price, perc, (key, existing) => existing = perc);
            }
        }

        private double GetLargestBidAskSize(double tickSize, double currentBidPrice, double currentAskPrice, double lowerBidCutOffPrice, double upperAskCutOffPrice)
        {
            double maxBidAsk = 0;
            for (double price = currentAskPrice; price < upperAskCutOffPrice; price += tickSize)
            {
                BidAsk bidAsk;
                if (CurrAsk.TryGetValue(price, out bidAsk))
                {
                    maxBidAsk = bidAsk.Size > maxBidAsk ? bidAsk.Size : maxBidAsk;
                }
            }
            for (double price = currentBidPrice; price > lowerBidCutOffPrice; price -= tickSize)
            {
                BidAsk bidAsk;
                if (CurrBid.TryGetValue(price, out bidAsk))
                {
                    maxBidAsk = bidAsk.Size > maxBidAsk ? bidAsk.Size : maxBidAsk;
                }
            }

            return maxBidAsk;
        }

        internal long GetSellVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalSells.TryGetValue(price, out volume);
            return volume;
        }

        internal long GetAskChange(double price)
        {
            long value = 0;
            AskChange.TryGetValue(price, out value);
            return value;
        }

        internal long GetBidChange(double price)
        {
            long value = 0;
            BidChange.TryGetValue(price, out value);
            return value;
        }

        internal Trade GetBuysInSlidingWindow(double price)
        {
            Trade trade = null;
            SlidingWindowBuys.TryGetValue(price, out trade);
            return trade;
        }

        internal Trade GetSellsInSlidingWindow(double price)
        {
            Trade trade = null;
            SlidingWindowSells.TryGetValue(price, out trade);
            return trade;
        }

        internal long GetLastBuySize(double price)
        {
            long lastSize = 0;
            LastBuy.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastSellSize(double price)
        {
            long lastSize = 0;
            LastSell.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastBuyPrint(double price)
        {
            long lastSize = 0;
            LastBuyPrint.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastSellPrint(double price)
        {
            long lastSize = 0;
            LastSellPrint.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastBuyPrintMax(double price)
        {
            long lastSize = 0;
            LastBuyPrintMax.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastSellPrintMax(double price)
        {
            long lastSize = 0;
            LastSellPrintMax.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal void RemoveLastBuy(double price)
        {
            long lastSize;
            LastBuy.TryRemove(price, out lastSize);
        }

        internal void RemoveLastSell(double price)
        {
            long lastSize;
            LastSell.TryRemove(price, out lastSize);
        }

        internal long GetBid(double price)
        {
            BidAsk entry;
            if (CurrBid.TryGetValue(price, out entry))
            {
                return Convert.ToInt64(entry.Size);
            }
            return 0;
        }

        internal long GetAsk(double price)
        {
            BidAsk entry;
            if (CurrAsk.TryGetValue(price, out entry))
            {
                return Convert.ToInt64(entry.Size);
            }
            return 0;
        }
    }
}
