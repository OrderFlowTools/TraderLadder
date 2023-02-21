// 
// Copyright (C) 2021, Gem Immanuel (gemify@gmail.com)
//

using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections;
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

    class TimeStamped
    {
        internal long Size;
        internal DateTime Time { get; set; }
    }

    class Trade : TimeStamped
    {
        internal TradeAggressor aggressor { get; set; }
        internal long swCumulSize { get; set; }
        internal double Ask { get; set; }
        internal double Bid { get; set; }
        internal double AskSize { get; set; }
        internal double BidSize { get; set; }
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
        SELLER,
        BICE,
        SICE
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
        private ConcurrentDictionary<double, TimeStamped> BIce;
        private ConcurrentDictionary<double, TimeStamped> SIce;
        private ConcurrentDictionary<double, long> SessionBuys;
        private ConcurrentDictionary<double, long> SessionSells;

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

        private ConcurrentDictionary<double, TimeStamped> SlidingVolume;

        private double imbalanceFactor;
        private long imbalanceInvalidateDistance;
        private const int minSlidingWindowTrades = 1;

        private struct Totals
        {
            public long sessionBuys;
            public long sessionSells;
            public long largestSessionSize;
            public long bice;
            public long sice;
            public long totalSlidingVolume;
        } 
        
        private Totals DataTotals;

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
            BIce = new ConcurrentDictionary<double, TimeStamped>();
            SIce = new ConcurrentDictionary<double, TimeStamped>();
            SessionBuys = new ConcurrentDictionary<double, long>();
            SessionSells = new ConcurrentDictionary<double, long>();

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

            SlidingVolume = new ConcurrentDictionary<double, TimeStamped>();

            DataTotals = new Totals();
        }

        internal void ClearAll()
        {

            ClearSlidingWindow();

            SessionBuys.Clear();
            SessionSells.Clear();
            DataTotals.sessionSells = 0;
            DataTotals.sessionBuys = 0;
            DataTotals.largestSessionSize = 0;
            DataTotals.bice = 0;
            DataTotals.sice = 0;

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
            BIce.Clear();
            SIce.Clear();
            DataTotals.bice = 0;
            DataTotals.sice = 0;

            LastBuy.Clear();
            LastSell.Clear();
            LastBuyPrint.Clear();
            LastSellPrint.Clear();
            LastBuyPrintMax.Clear();
            LastSellPrintMax.Clear();

            SlidingVolume.Clear();
            DataTotals.totalSlidingVolume = 0;
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
            catch (Exception)
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
            catch (Exception)
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
        internal void ClassifyTrade(bool updateSlidingWindow, double askPrice, long askSize, double bidPrice, long bidSize, double tradePrice, long tradeSize, DateTime time)
        {
            TradeAggressor aggressor = tradeClassifier.ClassifyTrade(askPrice, askSize, bidPrice, bidSize, tradePrice, tradeSize, time);

            // Classification - buyers vs. sellers
            if (aggressor == TradeAggressor.BUYER || aggressor == TradeAggressor.BICE)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowBuys.TryGetValue(tradePrice, out oldTrade);

                Trade trade = new Trade();
                trade.aggressor = aggressor;
                trade.Ask = askPrice;
                trade.AskSize = askSize;
                trade.Time = time;
                trade.Size = tradeSize;

                if (gotOldTrade)
                {
                    trade.swCumulSize = oldTrade.swCumulSize + tradeSize;
                }
                else
                {
                    trade.swCumulSize = tradeSize;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowBuys.AddOrUpdate(tradePrice, trade, (price, existingTrade) => existingTrade = trade);

                    if (aggressor == TradeAggressor.BICE)
                    {
                        BIce.AddOrUpdate(tradePrice, new Trade() { Size = tradeSize, Time = time }, (price, oldItem) => new Trade() { Size = (oldItem.Size + tradeSize), Time = time });
                        DataTotals.bice += tradeSize;
                    }

                    // Update last buy
                    LastBuy.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => tradeSize);
                    LastBuyPrint.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => tradeSize);
                    long lastMax = 0;
                    LastBuyPrintMax.TryGetValue(tradePrice, out lastMax);
                    if (tradeSize > lastMax)
                    {
                        LastBuyPrintMax.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => tradeSize);
                    }
                }
                SessionBuys.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => oldVolume + tradeSize);
                DataTotals.sessionBuys += tradeSize;

                // Calculate largest session buy/sell so far
                long sessBuy;
                if (SessionBuys.TryGetValue(tradePrice, out sessBuy))
                {
                    DataTotals.largestSessionSize = Math.Max(DataTotals.largestSessionSize, sessBuy);
                }
            }
            else if (aggressor == TradeAggressor.SELLER || aggressor == TradeAggressor.SICE)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowSells.TryGetValue(tradePrice, out oldTrade);

                Trade trade = new Trade();
                trade.aggressor = aggressor;
                trade.Bid = bidPrice;
                trade.BidSize = bidSize;
                trade.Time = time;
                trade.Size = tradeSize;

                if (gotOldTrade)
                {
                    trade.swCumulSize = oldTrade.swCumulSize + tradeSize;
                }
                else
                {
                    trade.swCumulSize = tradeSize;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowSells.AddOrUpdate(tradePrice, trade, (price, existingTrade) => existingTrade = trade);

                    if (aggressor == TradeAggressor.SICE)
                    {
                        SIce.AddOrUpdate(tradePrice, new Trade() { Size = tradeSize, Time = time }, (price, oldItem) => new Trade() { Size = (oldItem.Size + tradeSize), Time = time });
                        DataTotals.sice += tradeSize;
                    }

                    // Update last sell
                    LastSell.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => tradeSize);
                    LastSellPrint.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => tradeSize);
                    long lastMax = 0;
                    LastSellPrintMax.TryGetValue(tradePrice, out lastMax);
                    if (tradeSize > lastMax)
                    {
                        LastSellPrintMax.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => tradeSize);
                    }
                }
                SessionSells.AddOrUpdate(tradePrice, tradeSize, (price, oldVolume) => oldVolume + tradeSize);
                DataTotals.sessionSells += tradeSize;

                // Calculate largest session buy/sell so far
                long sessSell;
                if (SessionSells.TryGetValue(tradePrice, out sessSell))
                {
                    DataTotals.largestSessionSize = Math.Max(DataTotals.largestSessionSize, sessSell);
                }

            }

            if (updateSlidingWindow)
            {
                SlidingVolume.AddOrUpdate(tradePrice, new TimeStamped() { Size = tradeSize, Time = time }, (price, oldItem) => new TimeStamped() { Size = (oldItem.Size + tradeSize), Time = time });
                DataTotals.totalSlidingVolume += tradeSize;
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
            SessionBuys.TryGetValue(price, out buyVolume);
            SessionSells.TryGetValue(price, out sellVolume);
            long totalVolume = buyVolume + sellVolume;
            return totalVolume;
        }

        internal long GetSlidingVolumeAtPrice(double price)
        {
            long slidingVolume = 0;
            TimeStamped ts;
            if (SlidingVolume.TryGetValue(price, out ts))
            {
                slidingVolume = ts.Size;
            }
            return slidingVolume;
        }


        internal void ClearVolumeOutsideSlidingWindow(DateTime time, int VolumeSlidingWindowSeconds)
        {
            foreach (double price in SlidingVolume.Keys)
            {
                TimeStamped item;
                if (SlidingVolume.TryGetValue(price, out item))
                {
                    TimeSpan diff = time - item.Time;
                    if (diff.TotalSeconds > VolumeSlidingWindowSeconds)
                    {
                        DataTotals.totalSlidingVolume -= item.Size;
                        SlidingVolume.TryRemove(price, out item);
                    }
                }
            }
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

            foreach (double price in BIce.Keys)
            {
                TimeStamped item;
                if (BIce.TryGetValue(price, out item))
                {
                    TimeSpan diff = time - item.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        DataTotals.bice -= item.Size;
                        BIce.TryRemove(price, out item);                        
                    }
                }
            }
            foreach (double price in SIce.Keys)
            {
                TimeStamped item;
                if (SIce.TryGetValue(price, out item))
                {
                    TimeSpan diff = time - item.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        DataTotals.sice -= item.Size;
                        SIce.TryRemove(price, out item);
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
            SessionBuys.TryGetValue(price, out volume);
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
            SessionSells.TryGetValue(price, out volume);
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

        internal long GetBidSize(double price)
        {
            BidAsk entry;
            if (CurrBid.TryGetValue(price, out entry))
            {
                return Convert.ToInt64(entry.Size);
            }
            return 0;
        }

        internal long GetAskSize(double price)
        {
            BidAsk entry;
            if (CurrAsk.TryGetValue(price, out entry))
            {
                return Convert.ToInt64(entry.Size);
            }
            return 0;
        }

        internal long GetTotalSessionSells()
        {
            return DataTotals.sessionSells;
        }

        internal long GetTotalSessionBuys()
        {
            return DataTotals.sessionBuys;
        }

        internal long GetLargestSessionSize()
        {
            return DataTotals.largestSessionSize;
        }

        internal long GetLargestSessionSize(double lowerPrice, double upperPrice)
        {
            var bSizes = SessionBuys.Where(kvp => kvp.Key > lowerPrice && kvp.Key < upperPrice);
            long largestSessionBuy = bSizes.OrderByDescending(i => (i.Value)).FirstOrDefault().Value;
            var sSizes = SessionSells.Where(kvp => kvp.Key > lowerPrice && kvp.Key < upperPrice);
            long largestSessionSell = sSizes.OrderByDescending(i => (i.Value)).FirstOrDefault().Value;
            return Math.Max(largestSessionBuy, largestSessionSell);
        }

        /*
        * Gets largest buy volume in the sliding window
        */
        internal long GetLargestBuyInSlidingWindow()
        {
            IOrderedEnumerable<Trade> trades = SlidingWindowBuys.Values.OrderByDescending(i => ((Trade)i).swCumulSize);
            Trade item = trades.FirstOrDefault();
            return item == null ? 0 : item.swCumulSize;
        }

        /*
        * Gets largest sell volume in the sliding window
        */
        internal long GetLargestSellInSlidingWindow()
        {
            IOrderedEnumerable<Trade> trades = SlidingWindowSells.Values.OrderByDescending(i => ((Trade)i).swCumulSize);
            Trade item = trades.FirstOrDefault();
            return item == null ? 0 : item.swCumulSize;
        }

        internal long GetLargestMaxBuyInSlidingWindow()
        {
            IOrderedEnumerable<long> items = LastBuyPrintMax.Values.OrderByDescending(i => (long)i);
            return items.FirstOrDefault();
        }
        internal long GetLargestMaxSellInSlidingWindow()
        {
            IOrderedEnumerable<long> items = LastSellPrintMax.Values.OrderByDescending(i => (long)i);
            return items.FirstOrDefault();
        }
        internal long GetLargestLastBuyInSlidingWindow()
        {
            IOrderedEnumerable<long> items = LastBuyPrint.Values.OrderByDescending(i => (long)i);
            return items.FirstOrDefault();
        }
        internal long GetLargestLastSellInSlidingWindow()
        {
            IOrderedEnumerable<long> items = LastSellPrint.Values.OrderByDescending(i => (long)i);
            return items.FirstOrDefault();
        }

        internal long GetBIce(double price)
        {
            TimeStamped item;
            if (BIce.TryGetValue(price, out item))
                return item.Size;
            else
                return 0;
        }

        internal long GetSIce(double price)
        {
            TimeStamped item;
            if (SIce.TryGetValue(price, out item))
                return item.Size;
            else
                return 0;
        }

        internal double GetLargestBIce()
        {
            IOrderedEnumerable<TimeStamped> ices = BIce.Values.OrderByDescending(i => ((TimeStamped)i).Size);
            TimeStamped item = ices.FirstOrDefault();
            return item == null ? 0 : item.Size;
        }

        internal double GetLargestSIce()
        {
            IOrderedEnumerable<TimeStamped> ices = SIce.Values.OrderByDescending(i => ((TimeStamped)i).Size);
            TimeStamped item = ices.FirstOrDefault();
            return item == null ? 0 : item.Size;
        }

        internal long GetTotalBIce()
        {
            return DataTotals.bice;
        }

        internal long GetTotalSIce()
        {
            return DataTotals.sice;
        }

        internal long GetTotalSlidingVolume()
        {
            return DataTotals.totalSlidingVolume;
        }

        internal long GetLargestSlidingVolume()
        {
            IOrderedEnumerable<TimeStamped> volumes = SlidingVolume.Values.OrderByDescending(i => ((TimeStamped)i).Size);
            TimeStamped item = volumes.FirstOrDefault();
            return item == null ? 0 : item.Size;
        }
    }
}
