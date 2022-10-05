// 
// Copyright (C) 2021, Gem Immanuel (gemify@gmail.com)
//
#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using System.Globalization;
using Gemify.OrderFlow;
using Trade = Gemify.OrderFlow.Trade;
#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
    public class GemsTraderLadder : SuperDomColumn
    {
        public enum PLType
        {
            Ticks,
            Currency
        }

        class ColumnDefinition
        {
            public ColumnDefinition(ColumnType columnType, ColumnSize columnSize, Brush backgroundColor, Func<double, double, FormattedText> calculate)
            {
                ColumnType = columnType;
                ColumnSize = columnSize;
                BackgroundColor = backgroundColor;
                Calculate = calculate;
            }
            public ColumnType ColumnType { get; set; }
            public ColumnSize ColumnSize { get; set; }
            public Brush BackgroundColor { get; set; }
            public Func<double, double, FormattedText> Calculate { get; set; }
            public FormattedText Text { get; set; }
            public void GenerateText(double renderWidth, double price)
            {
                Text = Calculate(renderWidth, price);
            }
        }

        enum ColumnType
        {
            [Description("Notes")]
            NOTES,
            [Description("Volume")]
            VOLUME,
            [Description("Acc Val")]
            ACCVAL,
            [Description("Sess P/L")]
            TOTALPL,
            [Description("P/L")]
            PL,
            [Description("Price")]
            PRICE,
            [Description("Sells")]
            SELLS,
            [Description("Buys")]
            BUYS,
            [Description("Last")]
            SELL_SIZE,
            [Description("Last")]
            BUY_SIZE,
            [Description("Sess Sells")]
            TOTAL_SELLS,
            [Description("Sess Buys")]
            TOTAL_BUYS,
            [Description("Bid")]
            BID,
            [Description("Ask")]
            ASK,
            [Description("B+/-")]
            BID_CHANGE,
            [Description("A+/-")]
            ASK_CHANGE,
            [Description("OFS")]
            OF_STRENGTH
        }

        enum ColumnSize
        {
            XSMALL, SMALL, MEDIUM, LARGE, XLARGE
        }

        #region Variable Decls
        // VERSION
        private string TraderLadderVersion;

        // UI variables
        private bool clearLoadingSent;
        private FontFamily fontFamily;
        private FontStyle fontStyle;
        private FontWeight fontWeight;
        private Pen gridPen;
        private Pen bidSizePen;
        private Pen askSizePen;
        private Pen highlightPen;
        private Pen buyHighlightPen;
        private Pen sellHighlightPen;
        private double halfPenWidth;
        private bool heightUpdateNeeded;
        private double textHeight;
        private Point textPosition = new Point(10, 0);
        private static Typeface typeFace;

        // plumbing
        private readonly object barsSync = new object();
        private readonly double ImbalanceInvalidationThreshold = 5;
        private string tradingHoursData = TradingHours.UseInstrumentSettings;
        private bool mouseEventsSubscribed;
        private bool marketDepthSubscribed;
        private int lastMaxIndex = -1;

        // Orderflow variables
        private GemsOrderFlow orderFlow;

        private double commissionRT = 0.00;

        // Number of rows to display bid/ask size changes
        private long maxVolume = 0;
        private List<ColumnDefinition> columns;

        private Brush CurrentPriceRowColor;
        private Brush LongPositionRowColor = new SolidColorBrush(Color.FromRgb(10, 60, 10));
        private Brush ShortPositionRowColor = new SolidColorBrush(Color.FromRgb(70, 10, 10));
        private static Indicator ind = new Indicator();
        private static CultureInfo culture = Core.Globals.GeneralOptions.CurrentCulture;
        private double pixelsPerDip;
        private long buysInSlidingWindow = 0;
        private long sellsInSlidingWindow = 0;
        private bool SlidingWindowLastOnly;
        private bool SlidingWindowLastMaxOnly;

        private double LargeBidAskSizePercThreshold;
        private ConcurrentDictionary<double, string> notes;

        Dictionary<int, string> globexCodes = new Dictionary<int, string>()
            {
                { 1,"F" },
                { 2,"G" },
                { 3,"H" },
                { 4,"J" },
                { 5,"K" },
                { 6,"M" },
                { 7,"N" },
                { 8,"Q" },
                { 9,"U" },
                { 10,"V" },
                { 11,"X" },
                { 12,"Z" }
            };
        private double BidAskCutoffTicks;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                TraderLadderVersion = "v0.4.0";
                Name = "Free Trader Ladder (gemify) " + TraderLadderVersion;
                Description = @"Traders Ladder - (c) Gem Immanuel";
                DefaultWidth = 500;
                PreviousWidth = -1;
                IsDataSeriesRequired = true;

                // Orderflow var init
                orderFlow = new GemsOrderFlow(new SimpleTradeClassifier(), ImbalanceFactor);

                columns = new List<ColumnDefinition>();

                BidAskRows = 10;

                ImbalanceFactor = 2;
                TradeSlidingWindowSeconds = 60;
                OrderFlowStrengthThreshold = 65;
                OFSCalcMode = OFSCalculationMode.COMBINED;

                DefaultTextColor = Brushes.Gray;
                VolumeColor = Brushes.MidnightBlue;
                VolumeTextColor = DefaultTextColor;
                BuyTextColor = Brushes.Lime;
                SellTextColor = Brushes.Red;
                SessionBuyTextColor = Brushes.Green;
                SessionSellTextColor = Brushes.Firebrick;
                BidAskRemoveColor = Brushes.Firebrick;
                BidAskAddColor = Brushes.Green;
                SellImbalanceColor = Brushes.Magenta;
                BuyImbalanceColor = Brushes.Cyan;
                LastTradeColor = Brushes.White;
                DefaultBackgroundColor = Application.Current.TryFindResource("brushPriceColumnBackground") as SolidColorBrush;
                CurrentPriceRowColor = HeaderRowColor = Application.Current.TryFindResource("GridRowHighlight") as LinearGradientBrush;
                HeadersTextColor = Application.Current.TryFindResource("FontControlBrush") as SolidColorBrush;

                SellColumnColor = new SolidColorBrush(Color.FromRgb(40, 15, 15));
                AskColumnColor = new SolidColorBrush(Color.FromRgb(30, 15, 15));

                BuyColumnColor = new SolidColorBrush(Color.FromRgb(15, 15, 30));
                BidColumnColor = new SolidColorBrush(Color.FromRgb(20, 20, 30));

                BidSizeColor = Brushes.MediumBlue;
                AskSizeColor = Brushes.Firebrick;

                LargeBidAskSizeHighlightFilter = 200;
                LargeBidAskSizePercThreshold = 0.80; // Highlight bid/ask if size exceeds 80% of total bid/ask sizes

                LargeBidSizeHighlightColor = Brushes.DeepSkyBlue;
                LargeAskSizeHighlightColor = Brushes.Red;

                HighlightColor = DefaultTextColor;
                DisplayNotes = true;
                DisplayVolume = true;
                DisplayVolumeText = true;
                DisplayPrice = true;
                DisplayAccountValue = false;
                DisplayPL = false;
                DisplaySessionPL = false;
                DisplayBidAsk = true;
                DisplayBidAskHistogram = true;
                DisplayBidAskChange = false;
                DisplayLastSize = false;
                DisplaySlidingWindowBuysSells = true;
                DisplaySessionBuysSells = true;
                DisplayOrderFlowStrengthBar = false;
                DisplaySlidingWindowTotals = true;

                NotesURL = string.Empty;
                NotesDelimiter = ',';
                NotesColor = Brushes.RoyalBlue;

                // This can be toggled - ie, display last size at price instead of cumulative buy/sell.
                SlidingWindowLastOnly = false;
                SlidingWindowLastMaxOnly = false;

                ProfitLossType = PLType.Ticks;
                SelectedCurrency = Currency.UsDollar;

                marketDepthSubscribed = false;
            }
            else if (State == State.Configure)
            {

                // Set the cutoff value where Bid/Ask rows will stop
                BidAskCutoffTicks = BidAskRows* SuperDom.Instrument.MasterInstrument.TickSize;

                #region Add Requested Columns
                // Add requested columns
                if (DisplayVolume)
                    columns.Add(new ColumnDefinition(ColumnType.VOLUME, ColumnSize.LARGE, DefaultBackgroundColor, GenerateVolumeText));
                if (DisplayNotes)
                    columns.Add(new ColumnDefinition(ColumnType.NOTES, ColumnSize.LARGE, DefaultBackgroundColor, GenerateNotesText));
                if (DisplayPrice)
                    columns.Add(new ColumnDefinition(ColumnType.PRICE, ColumnSize.MEDIUM, DefaultBackgroundColor, GetPrice));
                if (DisplayPL)
                    columns.Add(new ColumnDefinition(ColumnType.PL, ColumnSize.MEDIUM, DefaultBackgroundColor, CalculatePL));
                if (DisplaySessionBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.TOTAL_SELLS, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateSessionSellsText));
                if (DisplayBidAskChange)
                    columns.Add(new ColumnDefinition(ColumnType.BID_CHANGE, ColumnSize.SMALL, DefaultBackgroundColor, GenerateBidChangeText));
                if (DisplayBidAsk || DisplayBidAskHistogram)
                    columns.Add(new ColumnDefinition(ColumnType.BID, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateBidText));
                if (DisplaySlidingWindowBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.SELLS, ColumnSize.SMALL, SellColumnColor, GenerateSlidingWindowSellsText));
                if (DisplayLastSize)
                {
                    columns.Add(new ColumnDefinition(ColumnType.SELL_SIZE, ColumnSize.XSMALL, SellColumnColor, GenerateLastSellText));
                    columns.Add(new ColumnDefinition(ColumnType.BUY_SIZE, ColumnSize.XSMALL, BuyColumnColor, GenerateLastBuyText));
                }
                if (DisplaySlidingWindowBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.BUYS, ColumnSize.SMALL, BuyColumnColor, GenerateSlidingWindowBuysText));
                if (DisplayBidAsk || DisplayBidAskHistogram)
                    columns.Add(new ColumnDefinition(ColumnType.ASK, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateAskText));
                if (DisplayBidAskChange)
                    columns.Add(new ColumnDefinition(ColumnType.ASK_CHANGE, ColumnSize.SMALL, DefaultBackgroundColor, GenerateAskChangeText));
                if (DisplaySessionBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.TOTAL_BUYS, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateSessionBuysText));

                if (DisplaySessionPL)
                    columns.Add(new ColumnDefinition(ColumnType.TOTALPL, ColumnSize.LARGE, DefaultBackgroundColor, CalculateTotalPL));
                if (DisplayAccountValue)
                    columns.Add(new ColumnDefinition(ColumnType.ACCVAL, ColumnSize.LARGE, DefaultBackgroundColor, CalculateAccValue));

                if (DisplayOrderFlowStrengthBar)
                    columns.Add(new ColumnDefinition(ColumnType.OF_STRENGTH, ColumnSize.SMALL, DefaultBackgroundColor, CalculateOFStrength));

                #endregion

                if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
                {
                    Matrix m = PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
                    double dpiFactor = 1 / m.M11;
                    gridPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), dpiFactor);
                    halfPenWidth = gridPen.Thickness * 0.5;
                    pixelsPerDip = VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

                    bidSizePen = new Pen(BidSizeColor, gridPen.Thickness);
                    askSizePen = new Pen(AskSizeColor, gridPen.Thickness);

                    highlightPen = new Pen(HighlightColor, gridPen.Thickness);
                    buyHighlightPen = new Pen(BuyTextColor, gridPen.Thickness);
                    sellHighlightPen = new Pen(SellTextColor, gridPen.Thickness);
                }

                if (SuperDom.Instrument != null && SuperDom.IsConnected)
                {

                    lastMaxIndex = 0;
                    orderFlow.ClearAll();

                    if (DisplayBidAsk || DisplayBidAskChange || DisplayBidAskHistogram)
                    {
                        // Get initial snapshots of the ask and bid ladders
                        // Don't like this much due to dependency on the SuperDOM.
                        orderFlow.SetAskLadder(GetAskLadderCopy());
                        orderFlow.SetBidLadder(GetBidLadderCopy());
                    }

                    BarsPeriod bp = new BarsPeriod
                    {
                        MarketDataType = MarketDataType.Last,
                        BarsPeriodType = BarsPeriodType.Tick,
                        Value = 1
                    };

                    SuperDom.Dispatcher.InvokeAsync(() => SuperDom.SetLoadingString());
                    clearLoadingSent = false;

                    if (BarsRequest != null)
                    {
                        BarsRequest.Update -= OnBarsUpdate;
                        BarsRequest = null;
                    }

                    BarsRequest = new BarsRequest(SuperDom.Instrument,
                        Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now,
                        Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now);

                    BarsRequest.BarsPeriod = bp;
                    BarsRequest.Update += OnBarsUpdate;

                    BarsRequest.Request((request, errorCode, errorMessage) =>
                    {
                        // Make sure this isn't a bars callback from another column instance
                        if (request != BarsRequest)
                        {
                            return;
                        }

                        if (State >= NinjaTrader.NinjaScript.State.Terminated)
                        {
                            return;
                        }

                        if (errorCode == Cbi.ErrorCode.UserAbort)
                        {
                            if (State <= NinjaTrader.NinjaScript.State.Terminated)
                                if (SuperDom != null && !clearLoadingSent)
                                {
                                    SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                                    clearLoadingSent = true;
                                }

                            request.Update -= OnBarsUpdate;
                            request.Dispose();
                            request = null;
                            return;
                        }

                        if (errorCode != Cbi.ErrorCode.NoError)
                        {
                            request.Update -= OnBarsUpdate;
                            request.Dispose();
                            request = null;
                            if (SuperDom != null && !clearLoadingSent)
                            {
                                SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                                clearLoadingSent = true;
                            }
                        }
                        else if (errorCode == Cbi.ErrorCode.NoError)
                        {

                            SessionIterator superDomSessionIt = new SessionIterator(request.Bars);
                            bool includesEndTimeStamp = request.Bars.BarsType.IncludesEndTimeStamp(false);

                            if (superDomSessionIt.IsInSession(Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now, includesEndTimeStamp, request.Bars.BarsType.IsIntraday))
                            {

                                for (int i = 0; i < request.Bars.Count; i++)
                                {
                                    DateTime time = request.Bars.BarsSeries.GetTime(i);
                                    if ((includesEndTimeStamp && time <= superDomSessionIt.ActualSessionBegin) || (!includesEndTimeStamp && time < superDomSessionIt.ActualSessionBegin))
                                        continue;

                                    // Get our datapoints
                                    double ask = request.Bars.BarsSeries.GetAsk(i);
                                    double bid = request.Bars.BarsSeries.GetBid(i);
                                    double close = request.Bars.BarsSeries.GetClose(i);
                                    double askSize = orderFlow.GetAsk(close);
                                    double bidSize = orderFlow.GetBid(close);
                                    long volume = request.Bars.BarsSeries.GetVolume(i);

                                    // Classify current volume as buy/sell
                                    // and add them to the buys/sells and totalBuys/totalSells collections
                                    orderFlow.ClassifyTrade(false, ask, askSize, bid, bidSize, close, volume, time);

                                    // Calculate current max volume for session
                                    long totalVolume = orderFlow.GetVolumeAtPrice(close);
                                    maxVolume = totalVolume > maxVolume ? totalVolume : maxVolume;
                                }

                                lastMaxIndex = request.Bars.Count - 1;

                                // Repaint the column on the SuperDOM
                                OnPropertyChanged();
                            }

                            if (SuperDom != null && !clearLoadingSent)
                            {
                                SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                                clearLoadingSent = true;
                            }

                        }
                    });

                    if (DisplayNotes && !string.IsNullOrWhiteSpace(NotesURL))
                    {
                        // Read notes for this instrument
                        string instrumentName = SuperDom.Instrument.MasterInstrument.Name;
                        string contractCode = instrumentName + GetGlobexCode(SuperDom.Instrument.Expiry.Month, SuperDom.Instrument.Expiry.Year);
                        LadderNotesReader notesReader = new LadderNotesReader(NotesDelimiter, instrumentName, contractCode, SuperDom.Instrument.MasterInstrument.TickSize);
                        notes = notesReader.ReadCSVNotes(NotesURL);
                    }

                    // Repaint the column on the SuperDOM
                    OnPropertyChanged();

                }

            }
            else if (State == State.Active)
            {
                WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseDown", OnMouseClick);
                mouseEventsSubscribed = true;

                if (SuperDom.MarketDepth != null)
                {
                    WeakEventManager<Data.MarketDepth<LadderRow>, Data.MarketDepthEventArgs>.AddHandler(SuperDom.MarketDepth, "Update", OnMarketDepthUpdate);
                    marketDepthSubscribed = true;
                }

            }
            else if (State == State.DataLoaded)
            {
                AccountItemEventArgs commissionAccountItem = SuperDom.Account.GetAccountItem(AccountItem.Commission, SelectedCurrency);
                if (commissionAccountItem != null)
                {
                    commissionRT = 2 * commissionAccountItem.Value;
                }

            }
            else if (State == State.Terminated)
            {
                if (BarsRequest != null)
                {
                    BarsRequest.Update -= OnBarsUpdate;
                    BarsRequest.Dispose();
                }

                if (marketDepthSubscribed && SuperDom.MarketDepth != null)
                {
                    WeakEventManager<Data.MarketDepth<LadderRow>, Data.MarketDepthEventArgs>.RemoveHandler(SuperDom.MarketDepth, "Update", OnMarketDepthUpdate);
                    marketDepthSubscribed = false;
                }

                BarsRequest = null;

                if (SuperDom != null && !clearLoadingSent)
                {
                    SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                    clearLoadingSent = true;
                }

                if (mouseEventsSubscribed)
                {
                    WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseDown", OnMouseClick);
                    mouseEventsSubscribed = false;
                }

                lastMaxIndex = 0;
                orderFlow.ClearAll();
            }
        }

        private string GetGlobexCode(int month, int year)
        {
            return globexCodes[month] + (year % 10);
        }

        // Subscribed to SuperDOM
        private void OnMarketDepthUpdate(object sender, Data.MarketDepthEventArgs e)
        {
            if (DisplayBidAsk || DisplayBidAskChange || DisplayBidAskHistogram)
            {

                // Only interested in Bid/Ask updates
                if (e.MarketDataType != MarketDataType.Ask && e.MarketDataType != MarketDataType.Bid) return;

                if (e.MarketDataType == MarketDataType.Ask && (e.Operation == Operation.Add || e.Operation == Operation.Update))
                {
                    orderFlow.AddOrUpdateAsk(e.Price, e.Volume, e.Time);
                }
                else if (e.MarketDataType == MarketDataType.Ask && e.Operation == Operation.Remove)
                {
                    orderFlow.AddOrUpdateAsk(e.Price, 0, e.Time);
                }
                else if (e.MarketDataType == MarketDataType.Bid && (e.Operation == Operation.Add || e.Operation == Operation.Update))
                {
                    orderFlow.AddOrUpdateBid(e.Price, e.Volume, e.Time);
                }
                else if (e.MarketDataType == MarketDataType.Bid && e.Operation == Operation.Remove)
                {
                    orderFlow.AddOrUpdateBid(e.Price, 0, e.Time);
                }

                double currentAsk = SuperDom.CurrentAsk;
                double upperAskCutOff = currentAsk + BidAskCutoffTicks;
                double currentBid = SuperDom.CurrentBid;
                double lowerBidCutOff = currentBid - BidAskCutoffTicks;

                // Calculate bid/ask size percentages in terms of total bid/ask volume 
                if (DisplayBidAskHistogram) orderFlow.CalculateBidAskPerc(SuperDom.Instrument.MasterInstrument.TickSize, currentBid, currentAsk, lowerBidCutOff, upperAskCutOff);

                OnPropertyChanged();
            }

        }

        private void OnBarsUpdate(object sender, BarsUpdateEventArgs e)
        {
            if (State == State.Active && SuperDom != null && SuperDom.IsConnected)
            {
                if (SuperDom.IsReloading)
                {
                    OnPropertyChanged();
                    return;
                }
                
                BarsUpdateEventArgs barsUpdate = e;
                lock (barsSync)
                {

                    int currentMaxIndex = barsUpdate.MaxIndex;

                    for (int i = lastMaxIndex + 1; i <= currentMaxIndex; i++)
                    {
                        if (barsUpdate.BarsSeries.GetIsFirstBarOfSession(i))
                        {
                            // If a new session starts, clear out the old values and start fresh
                            maxVolume = 0;
                            orderFlow.ClearAll();
                        }

                        // Fetch our datapoints
                        double ask = barsUpdate.BarsSeries.GetAsk(i);
                        double bid = barsUpdate.BarsSeries.GetBid(i);
                        double close = barsUpdate.BarsSeries.GetClose(i);
                        long volume = barsUpdate.BarsSeries.GetVolume(i);
                        DateTime time = barsUpdate.BarsSeries.GetTime(i);

                        // TODO: This may not be accurate.
                        // Sizes need to correspond to when the trade occurred
                        double askSize = orderFlow.GetAsk(close);
                        double bidSize = orderFlow.GetBid(close);
                        
                        // Clear out data in buy / sell dictionaries based on a configurable
                        // sliding window of time (in seconds)
                        orderFlow.ClearTradesOutsideSlidingWindow(time, TradeSlidingWindowSeconds);

                        // Classify current volume as buy/sell
                        // and add them to the buys/sells and totalBuys/totalSells collections
                        orderFlow.ClassifyTrade(true, ask, askSize, bid, bidSize, close, volume, time);

                        if (DisplayVolume)
                        {
                            // Calculate current max volume for session
                            long totalVolume = orderFlow.GetVolumeAtPrice(close);
                            maxVolume = totalVolume > maxVolume ? totalVolume : maxVolume;
                        }
                    }

                    lastMaxIndex = barsUpdate.MaxIndex;
                    if (!clearLoadingSent)
                    {
                        SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                        clearLoadingSent = true;
                    }
                }
            }
        }

        private List<NinjaTrader.Gui.SuperDom.LadderRow> GetBidLadderCopy()
        {
            List<NinjaTrader.Gui.SuperDom.LadderRow> ladder = null;
            try
            {
                if (SuperDom.MarketDepth.Bids.Count > 0)
                {
                    if (SuperDom.MarketDepth.Bids.Count > BidAskRows)
                    {
                        lock (SuperDom.MarketDepth.Bids)
                        {
                            ladder = SuperDom.MarketDepth.Bids.GetRange(0, BidAskRows);
                        }
                    }
                    else
                    {
                        ladder = SuperDom.MarketDepth.Bids;
                    }
                    ladder = new List<NinjaTrader.Gui.SuperDom.LadderRow>(ladder);
                }
            }
            catch (Exception e)
            {
                // NOP for now. 
            }
            return ladder;
        }

        private List<NinjaTrader.Gui.SuperDom.LadderRow> GetAskLadderCopy()
        {
            List<NinjaTrader.Gui.SuperDom.LadderRow> ladder = null;
            try
            {
                if (SuperDom.MarketDepth.Asks.Count > 0)
                {
                    if (SuperDom.MarketDepth.Asks.Count > BidAskRows)
                    {
                        lock (SuperDom.MarketDepth.Asks)
                        {
                            ladder = SuperDom.MarketDepth.Asks.GetRange(0, BidAskRows);
                        }
                    }
                    else
                    {
                        ladder = SuperDom.MarketDepth.Asks;
                    }
                    ladder = new List<NinjaTrader.Gui.SuperDom.LadderRow>(ladder);
                }
            }
            catch (Exception e)
            {
                // NOP for now. 
            }
            return ladder;
        }

        protected override void OnRender(DrawingContext dc, double renderWidth)
        {

            // This may be true if the UI for a column hasn't been loaded yet (e.g., restoring multiple tabs from workspace won't load each tab until it's clicked by the user)
            if (gridPen == null)
            {
                if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
                {
                    Matrix m = PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
                    double dpiFactor = 1 / m.M11;
                    gridPen = new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush, 1 * dpiFactor);
                    halfPenWidth = gridPen.Thickness * 0.5;
                }
            }

            double verticalOffset = -gridPen.Thickness;
            pixelsPerDip = VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

            if (fontFamily != SuperDom.Font.Family
                || (SuperDom.Font.Italic && fontStyle != FontStyles.Italic)
                || (!SuperDom.Font.Italic && fontStyle == FontStyles.Italic)
                || (SuperDom.Font.Bold && fontWeight != FontWeights.Bold)
                || (!SuperDom.Font.Bold && fontWeight == FontWeights.Bold))
            {
                // Only update this if something has changed
                fontFamily = SuperDom.Font.Family;
                fontStyle = SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal;
                fontWeight = SuperDom.Font.Bold ? FontWeights.Bold : FontWeights.Normal;
                typeFace = new Typeface(fontFamily, fontStyle, fontWeight, FontStretches.Normal);
                heightUpdateNeeded = true;
            }

            lock (SuperDom.Rows)
            {
                foreach (PriceRow row in SuperDom.Rows)
                {
                    if (renderWidth - halfPenWidth >= 0)
                    {
                        if (SuperDom.IsConnected && !SuperDom.IsReloading && State == NinjaTrader.NinjaScript.State.Active)
                        {
                            // Generate cell text
                            for (int i = 0; i < columns.Count; i++)
                            {
                                double cellWidth = CalculateCellWidth(columns[i].ColumnSize, renderWidth);
                                columns[i].GenerateText(cellWidth, row.Price);
                            }

                            // Render the grid
                            DrawGrid(dc, renderWidth, verticalOffset, row);

                            verticalOffset += SuperDom.ActualRowHeight;
                        }
                    }
                }
            }
        }

        private double CalculateCellWidth(ColumnSize columnSize, double renderWidth)
        {
            double cellWidth = 0;
            int factor = 0;
            foreach (ColumnDefinition colDef in columns)
            {
                switch (colDef.ColumnSize)
                {
                    case ColumnSize.XSMALL: factor += 1; break;
                    case ColumnSize.SMALL: factor += 2; break;
                    case ColumnSize.MEDIUM: factor += 3; break;
                    case ColumnSize.LARGE: factor += 4; break;
                    case ColumnSize.XLARGE: factor += 5; break;
                }
            }
            double unitCellWidth = renderWidth / factor;
            switch (columnSize)
            {
                case ColumnSize.XLARGE: cellWidth = 5 * unitCellWidth; break;
                case ColumnSize.LARGE: cellWidth = 4 * unitCellWidth; break;
                case ColumnSize.MEDIUM: cellWidth = 3 * unitCellWidth; break;
                case ColumnSize.SMALL: cellWidth = 2 * unitCellWidth; break;
                default: cellWidth = unitCellWidth; break;
            }
            return cellWidth;
        }

        private void DrawGrid(DrawingContext dc, double renderWidth, double verticalOffset, PriceRow row)
        {
            double x = 0;

            for (int i = 0; i < columns.Count; i++)
            {
                ColumnDefinition colDef = columns[i];
                double cellWidth = CalculateCellWidth(colDef.ColumnSize, renderWidth);
                Brush cellColor = colDef.BackgroundColor;
                Rect rect = new Rect(x, verticalOffset, cellWidth, SuperDom.ActualRowHeight);

                // Create a guidelines set
                GuidelineSet guidelines = new GuidelineSet();
                guidelines.GuidelinesX.Add(rect.Left + halfPenWidth);
                guidelines.GuidelinesX.Add(rect.Right + halfPenWidth);
                guidelines.GuidelinesY.Add(rect.Top + halfPenWidth);
                guidelines.GuidelinesY.Add(rect.Bottom + halfPenWidth);
                dc.PushGuidelineSet(guidelines);

                // BID column color
                if ((colDef.ColumnType == ColumnType.BID ||
                    colDef.ColumnType == ColumnType.BID_CHANGE) &&
                    row.Price < SuperDom.CurrentLast)
                {
                    cellColor = BidColumnColor;
                }

                // ASK column color
                if ((colDef.ColumnType == ColumnType.ASK ||
                    colDef.ColumnType == ColumnType.ASK_CHANGE) &&
                    row.Price > SuperDom.CurrentLast)
                {
                    cellColor = AskColumnColor;
                }

                // Position based row color
                if (SuperDom.Position != null && row.IsEntry && colDef.ColumnType != ColumnType.OF_STRENGTH && colDef.ColumnType != ColumnType.VOLUME)
                {
                    if (SuperDom.Position.MarketPosition == MarketPosition.Long)
                    {
                        cellColor = LongPositionRowColor;
                    }
                    else
                    {
                        cellColor = ShortPositionRowColor;
                    }
                }

                // Indicate current price
                if (row.Price == SuperDom.CurrentLast && colDef.ColumnType != ColumnType.OF_STRENGTH && colDef.ColumnType != ColumnType.VOLUME)
                {
                    cellColor = CurrentPriceRowColor;
                }

                // Headers row
                if (row.Price == SuperDom.UpperPrice)
                {
                    cellColor = HeaderRowColor;
                }

                // Summary/Bottom row
                if (row.Price == SuperDom.LowerPrice)
                {
                    cellColor = HeaderRowColor;
                }

                // Draw grid rectangle
                dc.DrawRectangle(cellColor, null, rect);
                dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(renderWidth - halfPenWidth, rect.Bottom));
                if (row.Price != SuperDom.LowerPrice && row.Price != SuperDom.CurrentLast && colDef.ColumnType != ColumnType.OF_STRENGTH && colDef.ColumnType != ColumnType.VOLUME)
                {
                    dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));
                }

                // Write Header Row
                if (row.Price == SuperDom.UpperPrice)
                {
                    Brush headerColor = HeadersTextColor;
                    string headerText = GetEnumDescription(colDef.ColumnType);
                    if (colDef.ColumnType == ColumnType.SELLS || colDef.ColumnType == ColumnType.BUYS)
                    {
                        if (SlidingWindowLastMaxOnly)
                        {
                            headerText = "* MAX";
                            headerColor = Brushes.Yellow;
                        }
                        else if (SlidingWindowLastOnly)
                        {
                            headerText = "* PRINT";
                            headerColor = Brushes.Yellow;
                        }
                    }

                    if (colDef.ColumnType == ColumnType.PL)
                    {
                        if (ProfitLossType == PLType.Ticks) { 
                            headerText += " (t)";
                        }
                    }

                    FormattedText header = FormatText(headerText, renderWidth, headerColor, TextAlignment.Left);
                    dc.DrawText(header, new Point(rect.Left + 10, verticalOffset + (SuperDom.ActualRowHeight - header.Height) / 2));
                }
                // Regular data rows
                else
                {
                    // Draw Volume
                    if (row.Price != SuperDom.LowerPrice && colDef.ColumnType == ColumnType.VOLUME && colDef.Text != null)
                    {
                        long volumeAtPrice = colDef.Text.Text == null ? 0 : long.Parse(colDef.Text.Text);
                        double totalWidth = cellWidth * ((double)volumeAtPrice / maxVolume);
                        double volumeWidth = totalWidth == cellWidth ? totalWidth - gridPen.Thickness * 1.5 : totalWidth - halfPenWidth;

                        if (volumeWidth >= 0)
                        {
                            double xc = x + (cellWidth - volumeWidth);
                            dc.DrawRectangle(VolumeColor, null, new Rect(xc, verticalOffset + halfPenWidth, volumeWidth, rect.Height - gridPen.Thickness));
                        }

                        if (!DisplayVolumeText)
                        {
                            colDef.Text = null;
                        }
                    }
                    // Draw ASK Histogram
                    else if (row.Price != SuperDom.LowerPrice && DisplayBidAskHistogram && colDef.ColumnType == ColumnType.ASK)
                    {
                        if (DisplayBidAskHistogram)
                        {
                            if (row.Price < SuperDom.CurrentAsk + BidAskCutoffTicks)
                            {
                                BidAskPerc bidAskPerc = orderFlow.GetAskPerc(row.Price);
                                double perc = bidAskPerc == null ? 0 : bidAskPerc.Perc;

                                Pen pen = askSizePen;

                                if (orderFlow.GetAsk(row.Price) > LargeBidAskSizeHighlightFilter && perc > LargeBidAskSizePercThreshold)
                                {
                                    pen = new Pen(LargeAskSizeHighlightColor, 2);
                                }

                                double totalWidth = cellWidth * perc;
                                double paintWidth = totalWidth == cellWidth ? totalWidth - pen.Thickness * 1.5 : totalWidth - halfPenWidth;

                                if (paintWidth >= 0)
                                {
                                    double xc = x + (cellWidth - paintWidth);
                                    dc.DrawRectangle(null, pen, new Rect(xc, verticalOffset + halfPenWidth, paintWidth, rect.Height - pen.Thickness));
                                }
                            }
                        }
                    }
                    // Draw BID Histogram
                    else if (row.Price != SuperDom.LowerPrice && DisplayBidAskHistogram && colDef.ColumnType == ColumnType.BID)
                    {
                        if (DisplayBidAskHistogram)
                        {
                            if (row.Price > SuperDom.CurrentBid - BidAskCutoffTicks)
                            {
                                BidAskPerc bidAskPerc = orderFlow.GetBidPerc(row.Price);
                                double perc = bidAskPerc == null ? 0 : bidAskPerc.Perc;

                                Pen pen = bidSizePen;

                                if (orderFlow.GetBid(row.Price) > LargeBidAskSizeHighlightFilter && perc > LargeBidAskSizePercThreshold)
                                {
                                    pen = new Pen(LargeBidSizeHighlightColor, 2);
                                }

                                double totalWidth = cellWidth * perc;
                                double paintWidth = totalWidth == cellWidth ? totalWidth - pen.Thickness * 1.5 : totalWidth - halfPenWidth;

                                if (paintWidth >= 0)
                                {
                                    double xc = x + (cellWidth - paintWidth);
                                    dc.DrawRectangle(null, pen, new Rect(xc, verticalOffset + halfPenWidth, paintWidth, rect.Height - pen.Thickness));
                                }
                            }
                        }
                    }
                    // Draw Buy/Sell columns
                    else if (DisplaySlidingWindowTotals && (colDef.ColumnType == ColumnType.SELLS || colDef.ColumnType == ColumnType.BUYS))
                    {

                        double highestPriceInSlidingWindow = orderFlow.GetHighestBuyPriceInSlidingWindow();
                        double lowestPriceInSlidingWindow = orderFlow.GetLowestSellPriceInSlidingWindow();

                        // Calculate prices at which to display totals
                        double sellTotalsPrice = lowestPriceInSlidingWindow - SuperDom.Instrument.MasterInstrument.TickSize;
                        double buyTotalsPrice = highestPriceInSlidingWindow + SuperDom.Instrument.MasterInstrument.TickSize;

                        double buyTotal = 0;
                        double sellTotal = 0;

                        if (SlidingWindowLastMaxOnly)
                        {
                            buyTotal = orderFlow.GetTotalLargeBuysInSlidingWindow();
                            sellTotal = orderFlow.GetTotalLargeSellsInSlidingWindow();
                        }
                        else if (SlidingWindowLastOnly)
                        {
                            buyTotal = orderFlow.GetTotalBuyPrintsInSlidingWindow();
                            sellTotal = orderFlow.GetTotalSellPrintsInSlidingWindow();
                        }
                        else
                        {
                            buyTotal = orderFlow.GetBuysInSlidingWindow();
                            sellTotal = orderFlow.GetSellsInSlidingWindow();
                        }

                        if (colDef.ColumnType == ColumnType.BUYS && highestPriceInSlidingWindow > 0)
                        {
                            // If we're at the price where the totals should be rendered
                            if (row.Price == buyTotalsPrice || row.Price == SuperDom.LowerPrice)
                            {

                                FormattedText text = FormatText(buyTotal.ToString(), cellWidth - 2, BuyTextColor, TextAlignment.Right);

                                dc.DrawText(text, new Point(rect.Left + 5, verticalOffset + (SuperDom.ActualRowHeight - text.Height) / 2));
                                if (row.Price != SuperDom.LowerPrice)
                                {
                                    dc.DrawRectangle(null, highlightPen, new Rect(x + 2, verticalOffset + halfPenWidth - 1, cellWidth - 3, rect.Height - highlightPen.Thickness));
                                }
                                else
                                {
                                    if (buyTotal > sellTotal)
                                    {
                                        dc.DrawRectangle(null, buyHighlightPen, new Rect(x + 2, verticalOffset + halfPenWidth - 1, cellWidth - 3, rect.Height - buyHighlightPen.Thickness));
                                    }
                                }
                            }
                        }

                        if (colDef.ColumnType == ColumnType.SELLS && lowestPriceInSlidingWindow > 0)
                        {

                            // If we're at the price where the totals should be rendered
                            if (row.Price == sellTotalsPrice || row.Price == SuperDom.LowerPrice)
                            {

                                FormattedText text = FormatText(sellTotal.ToString(), cellWidth - 2, SellTextColor, TextAlignment.Right);

                                dc.DrawText(text, new Point(rect.Left + 5, verticalOffset + (SuperDom.ActualRowHeight - text.Height) / 2));
                                if (row.Price != SuperDom.LowerPrice)
                                {
                                    dc.DrawRectangle(null, highlightPen, new Rect(x + 2, verticalOffset + halfPenWidth - 1, cellWidth - 3, rect.Height - highlightPen.Thickness));
                                }
                                else
                                {
                                    if (sellTotal > buyTotal)
                                    {
                                        dc.DrawRectangle(null, sellHighlightPen, new Rect(x + 2, verticalOffset + halfPenWidth - 1, cellWidth - 3, rect.Height - sellHighlightPen.Thickness));
                                    }
                                }

                            }
                        }
                    }
                    else if (DisplayPrice && DisplayNotes && colDef.ColumnType == ColumnType.PRICE)
                    {
                        string notesText = null;
                        if (notes != null && notes.TryGetValue(row.Price, out notesText))
                        {
                            colDef.Text.SetForegroundBrush(NotesColor);
                        }
                    }

                    if (row.Price == SuperDom.LowerPrice)
                    {
                        // Write summary at lowerprice row
                        // NOP
                    }
                    else if (colDef.Text != null)
                    {
                        // Write the column text
                        double xp = rect.Left + 5;
                        double yp = verticalOffset + (SuperDom.ActualRowHeight - colDef.Text.Height) / 2;
                        dc.DrawText(colDef.Text, new Point(xp, yp));
                    }
                }

                dc.Pop();

                x += cellWidth;
            }
        }

        #region Text utils
        private FormattedText FormatText(string text, double renderWidth, Brush color, TextAlignment alignment)
        {
            return new FormattedText(text.ToString(culture), culture, FlowDirection.LeftToRight, typeFace, SuperDom.Font.Size, color, pixelsPerDip) { MaxLineCount = 1, MaxTextWidth = (renderWidth < 11 ? 1 : renderWidth - 10), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = alignment };
        }

        private void Print(string s)
        {
            ind.Print(s);
        }

        public string GetEnumDescription(Enum enumValue)
        {
            var fieldInfo = enumValue.GetType().GetField(enumValue.ToString());

            var descriptionAttributes = (DescriptionAttribute[])fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);

            return descriptionAttributes.Length > 0 ? descriptionAttributes[0].Description : enumValue.ToString();
        }

        #endregion        

        #region Column Text Calculation

        private FormattedText GenerateVolumeText(double renderWidth, double price)
        {
            long totalVolume = orderFlow.GetVolumeAtPrice(price);
            return totalVolume > 0 ? FormatText(totalVolume.ToString(), renderWidth, VolumeTextColor, TextAlignment.Right) : null;
        }

        private FormattedText CalculateOFStrength(double renderWidth, double price)
        {
            string text = "██";
            Brush color = Brushes.Transparent;

            OFStrength ofStrength = orderFlow.CalculateOrderFlowStrength(OFSCalcMode, SuperDom.CurrentLast, SuperDom.Instrument.MasterInstrument.TickSize);

            double buyStrength = ofStrength.buyStrength;
            double sellStrength = ofStrength.sellStrength;

            double totalRows = Convert.ToDouble(SuperDom.Rows.Count);
            int nBuyRows = Convert.ToInt16(totalRows * (buyStrength / 100.00));
            int nSellRows = Convert.ToInt16(totalRows - nBuyRows);

            if (buyStrength + sellStrength > 0)
            {
                if ((SuperDom.UpperPrice - price) < nSellRows * SuperDom.Instrument.MasterInstrument.TickSize)
                {
                    if (sellStrength >= OrderFlowStrengthThreshold)
                    {
                        color = Brushes.Red;
                    }
                    else
                    {
                        color = Brushes.Maroon;
                    }

                    text = (nSellRows - 1 == (SuperDom.UpperPrice - price) / SuperDom.Instrument.MasterInstrument.TickSize) ? Math.Round(sellStrength, 0, MidpointRounding.AwayFromZero).ToString() : text;
                }
                else
                {
                    if (buyStrength >= OrderFlowStrengthThreshold)
                    {
                        color = Brushes.Lime;
                    }
                    else
                    {
                        color = Brushes.DarkGreen;
                    }
                    text = (nBuyRows - 1 == (price - SuperDom.LowerPrice) / SuperDom.Instrument.MasterInstrument.TickSize) ? Math.Round(buyStrength, 0, MidpointRounding.AwayFromZero).ToString() : text;
                }
            }

            return FormatText(string.Format("{0}", text), renderWidth, color, TextAlignment.Center);
        }

        private FormattedText GenerateSessionBuysText(double renderWidth, double buyPrice)
        {
            Brush brush = SessionBuyTextColor;

            double sellPrice = buyPrice - SuperDom.Instrument.MasterInstrument.TickSize;

            long totalBuys = orderFlow.GetBuyVolumeAtPrice(buyPrice);
            long totalSells = orderFlow.GetSellVolumeAtPrice(sellPrice);

            if (totalBuys > 0 && totalSells > 0 && totalBuys > totalSells * ImbalanceFactor)
            {
                brush = BuyImbalanceColor;
            }

            if (totalBuys != 0)
            {
                return FormatText(totalBuys.ToString(), renderWidth, brush, TextAlignment.Right);
            }

            return null;
        }

        private FormattedText GenerateSessionSellsText(double renderWidth, double sellPrice)
        {
            Brush brush = SessionSellTextColor;

            double buyPrice = sellPrice + SuperDom.Instrument.MasterInstrument.TickSize;

            long totalBuys = orderFlow.GetBuyVolumeAtPrice(buyPrice);
            long totalSells = orderFlow.GetSellVolumeAtPrice(sellPrice);

            if (totalBuys > 0 && totalSells > 0 && totalSells > totalBuys * ImbalanceFactor)
            {
                brush = SellImbalanceColor;
            }

            if (totalSells != 0)
            {
                return FormatText(totalSells.ToString(), renderWidth, brush, TextAlignment.Right);
            }

            return null;
        }

        private FormattedText GenerateAskChangeText(double renderWidth, double price)
        {
            long change = (price >= SuperDom.CurrentAsk + BidAskCutoffTicks) ? 0 : orderFlow.GetAskChange(price);

            if (change != 0)
            {
                Brush color = change > 0 ? BidAskAddColor : BidAskRemoveColor;
                return FormatText(change.ToString(), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateBidChangeText(double renderWidth, double price)
        {
            long change = (price <= SuperDom.CurrentBid - BidAskCutoffTicks) ? 0 : orderFlow.GetBidChange(price);

            if (change != 0)
            {
                Brush color = change > 0 ? BidAskAddColor : BidAskRemoveColor;
                return FormatText(change.ToString(), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateAskText(double renderWidth, double price)
        {
            if (DisplayBidAsk)
            {
                long currentSize = (price >= SuperDom.CurrentAsk + BidAskCutoffTicks) ? 0 : orderFlow.GetAsk(price);

                if (currentSize > 0)
                    return FormatText(currentSize.ToString(), renderWidth, DefaultTextColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateBidText(double renderWidth, double price)
        {
            if (DisplayBidAsk)
            {
                long currentSize = (price <= SuperDom.CurrentBid - BidAskCutoffTicks) ? 0 : orderFlow.GetBid(price);

                if (currentSize > 0) 
                    return FormatText(currentSize.ToString(), renderWidth, DefaultTextColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateSlidingWindowBuysText(double renderWidth, double price)
        {
            // If requested to ONLY display last size (and not cumulative value)
            if (SlidingWindowLastOnly)
            {
                return GenerateLastBuyPrintText(renderWidth, price);
            }
            else if (SlidingWindowLastMaxOnly)
            {
                return GenerateLastBuyPrintMaxText(renderWidth, price);
            }
            else
            {
                double sellPrice = price - SuperDom.Instrument.MasterInstrument.TickSize;

                Trade buys = orderFlow.GetBuysInSlidingWindow(price);
                if (buys != null)
                {
                    Brush color = SuperDom.CurrentAsk == price ? LastTradeColor : DefaultTextColor;

                    Trade sells = orderFlow.GetSellsInSlidingWindow(sellPrice);
                    if (sells != null && buys.swCumulSize > sells.swCumulSize * ImbalanceFactor)
                    {
                        color = BuyImbalanceColor;
                    }

                    return FormatText(buys.swCumulSize.ToString(), renderWidth, color, TextAlignment.Right);
                }
            }
            return null;
        }

        private FormattedText GenerateSlidingWindowSellsText(double renderWidth, double price)
        {
            // If requested to ONLY display last size (and not cumulative value)
            if (SlidingWindowLastOnly)
            {
                return GenerateLastSellPrintText(renderWidth, price);
            }
            else if (SlidingWindowLastMaxOnly)
            {
                return GenerateLastSellPrintMaxText(renderWidth, price);
            }
            else
            {
                double buyPrice = price + SuperDom.Instrument.MasterInstrument.TickSize;

                Trade sells = orderFlow.GetSellsInSlidingWindow(price);
                if (sells != null)
                {
                    Brush color = SuperDom.CurrentBid == price ? LastTradeColor : DefaultTextColor;

                    Trade buys = orderFlow.GetBuysInSlidingWindow(buyPrice);
                    if (buys != null && sells.swCumulSize > buys.swCumulSize * ImbalanceFactor)
                    {
                        color = SellImbalanceColor;
                    }

                    return FormatText(sells.swCumulSize.ToString(), renderWidth, color, TextAlignment.Right);
                }
            }
            return null;
        }

        private FormattedText GenerateLastBuyText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastBuySize(price);

            if (size > 0)
            {
                orderFlow.RemoveLastBuy(price);
                return FormatText(size.ToString(), renderWidth, BuyTextColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateLastSellText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastSellSize(price);
            if (size > 0)
            {
                orderFlow.RemoveLastSell(price);
                return FormatText(size.ToString(), renderWidth, SellTextColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateLastBuyPrintText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastBuyPrint(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, BuyTextColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateLastSellPrintText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastSellPrint(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, SellTextColor, TextAlignment.Right);
            }

            return null;
        }

        private FormattedText GenerateLastBuyPrintMaxText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastBuyPrintMax(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, BuyTextColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateLastSellPrintMaxText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastSellPrintMax(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, SellTextColor, TextAlignment.Right);
            }

            return null;
        }

        private FormattedText GenerateNotesText(double renderWidth, double price)
        {
            string text = null;
            if (notes != null && notes.TryGetValue(price, out text))
            {
                return FormatText(text, renderWidth, NotesColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GetPrice(double renderWidth, double price)
        {
            return FormatText(SuperDom.Instrument.MasterInstrument.FormatPrice(price), renderWidth, Brushes.Gray, TextAlignment.Right);
        }

        private FormattedText CalculatePL(double renderWidth, double price)
        {
            FormattedText fpl = null;

            // Print P/L if position is open
            if (SuperDom.Position != null)
            {
                double pl = 0;

                if (ProfitLossType == PLType.Currency)
                {
                    pl = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price) - (commissionRT * SuperDom.Position.Quantity);
                    Brush color = (pl > 0 ? (price == SuperDom.CurrentLast ? Brushes.Lime : Brushes.Green) : (pl < 0 ? (price == SuperDom.CurrentLast ? Brushes.Red : Brushes.Firebrick) : Brushes.DimGray));
                    fpl = FormatText(string.Format("{0:0.00}", pl), renderWidth, color, TextAlignment.Right);
                }
                else
                {
                    pl = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Ticks, price);
                    Brush color = (pl > 0 ? (price == SuperDom.CurrentLast ? Brushes.Lime : Brushes.Green) : (pl < 0 ? (price == SuperDom.CurrentLast ? Brushes.Red : Brushes.Firebrick) : Brushes.DimGray));
                    fpl = FormatText(string.Format("{0}", Convert.ToInt32(pl)), renderWidth, color, TextAlignment.Right);
                }

                return fpl;
            }
            return fpl;
        }

        private FormattedText CalculateTotalPL(double renderWidth, double price)
        {
            // Print Total P/L if position is open
            if (SuperDom.Position != null)
            {
                double pl = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price) - (commissionRT * SuperDom.Position.Quantity) + SuperDom.Account.Get(AccountItem.RealizedProfitLoss, SelectedCurrency);
                Brush color = (pl > 0 ? (price == SuperDom.CurrentLast ? Brushes.Lime : Brushes.Green) : (pl < 0 ? (price == SuperDom.CurrentLast ? Brushes.Red : Brushes.Firebrick) : Brushes.DimGray));
                return FormatText(string.Format("{0:0.00}", pl), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }


        private FormattedText CalculateAccValue(double renderWidth, double price)
        {
            // Print Account Value if position is open
            if (SuperDom.Position != null)
            {
                double accVal = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price) - (commissionRT * SuperDom.Position.Quantity) + SuperDom.Account.Get(AccountItem.CashValue, SelectedCurrency);
                Brush color = Brushes.DimGray;
                return FormatText(string.Format("{0:0.00}", accVal), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }

        #endregion

        #region Event Handlers
        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            NinjaTrader.Gui.SuperDom.ColumnWrapper wrapper = (NinjaTrader.Gui.SuperDom.ColumnWrapper)sender;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (System.Windows.Forms.UserControl.ModifierKeys == System.Windows.Forms.Keys.Control)
                {
                    // Toggle display between last at price only vs. cumulative at price in Sliding Window
                    this.SlidingWindowLastMaxOnly = false;
                    this.SlidingWindowLastOnly = this.SlidingWindowLastOnly ? false : true;
                    OnPropertyChanged();
                }
                else if (System.Windows.Forms.UserControl.ModifierKeys == System.Windows.Forms.Keys.Shift)
                {
                    // Toggle display between last (MAX) at price only vs. cumulative at price in Sliding Window
                    this.SlidingWindowLastOnly = false;
                    this.SlidingWindowLastMaxOnly = this.SlidingWindowLastMaxOnly ? false : true;
                    OnPropertyChanged();
                }
            }

            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                orderFlow.ClearSlidingWindow();

                OnPropertyChanged();
            }
        }
        #endregion

        #region Properties

        #region Notes column
        [NinjaScriptProperty]
        [Display(Name = "Notes", Description = "Display notes.", Order = 1, GroupName = "Notes")]
        public bool DisplayNotes
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Notes Location / URL", Description = "File path or URL that contains notes CSV file.", Order = 2, GroupName = "Notes")]
        public string NotesURL
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Notes Delimiter", Description = "CSV delimiter.", Order = 3, GroupName = "Notes")]
        public char NotesDelimiter
        { get; set; }

        #endregion

        // =========== Price Column

        [NinjaScriptProperty]
        [Display(Name = "Price", Description = "Display price.", Order = 1, GroupName = "Price and Volume Columns")]
        public bool DisplayPrice
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Histogram", Description = "Display volume.", Order = 2, GroupName = "Price and Volume Columns")]
        public bool DisplayVolume
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Histogram Text", Description = "Display volume text.", Order = 3, GroupName = "Price and Volume Columns")]
        public bool DisplayVolumeText
        { get; set; }


        // =========== Buy / Sell Columns

        [NinjaScriptProperty]
        [Display(Name = "Trades (Sliding Window)", Description = "Display trades in a sliding window.", Order = 1, GroupName = "Buy / Sell Columns")]
        public bool DisplayLastSize
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Buy/Sell (Sliding Window)", Description = "Display Buys/Sells in a sliding window.", Order = 2, GroupName = "Buy / Sell Columns")]
        public bool DisplaySlidingWindowBuysSells
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Buy/Sell Sliding Window (Seconds)", Description = "Sliding Window (in seconds) used for displaying trades.", Order = 3, GroupName = "Buy / Sell Columns")]
        public int TradeSlidingWindowSeconds
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Buys / Sells", Description = "Display the total buys and sells columns.", Order = 4, GroupName = "Buy / Sell Columns")]
        public bool DisplaySessionBuysSells
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sliding Window Totals", Description = "Display Sliding Window Totals.", Order = 5, GroupName = "Buy / Sell Columns")]
        public bool DisplaySlidingWindowTotals
        { get; set; }


        [Browsable(false)]
        public string SlidingWindowLastMaxOnlySerialize
        {
            get { return SlidingWindowLastMaxOnly.ToString(); }
            set { SlidingWindowLastMaxOnly = Convert.ToBoolean(value); }
        }

        [Browsable(false)]
        public string SlidingWindowLastOnlySerialize
        {
            get { return SlidingWindowLastOnly.ToString(); }
            set { SlidingWindowLastOnly = Convert.ToBoolean(value); }
        }

        // =========== Visual

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Background Color", Description = "Default background color.", Order = 2, GroupName = "Visual")]
        public Brush DefaultBackgroundColor
        { get; set; }

        [Browsable(false)]
        public string DefaultBackgroundColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(DefaultBackgroundColor); }
            set { DefaultBackgroundColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Default Text Color", Description = "Default text color.", Order = 2, GroupName = "Visual")]
        public Brush DefaultTextColor
        { get; set; }

        [Browsable(false)]
        public string DefaultTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(DefaultTextColor); }
            set { DefaultTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Buys Text Color", Description = "Buys Text Color.", Order = 3, GroupName = "Visual")]
        public Brush BuyTextColor
        { get; set; }

        [Browsable(false)]
        public string BuyTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BuyTextColor); }
            set { BuyTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sells Text Color", Description = "Sells Text Color.", Order = 4, GroupName = "Visual")]
        public Brush SellTextColor
        { get; set; }

        [Browsable(false)]
        public string SellTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SellTextColor); }
            set { SellTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Buys Text Color", Description = "Session Buys Text Color.", Order = 5, GroupName = "Visual")]
        public Brush SessionBuyTextColor
        { get; set; }

        [Browsable(false)]
        public string SessionBuyTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionBuyTextColor); }
            set { SessionBuyTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Sells Text Color", Description = "Session Sells Text Color.", Order = 6, GroupName = "Visual")]
        public Brush SessionSellTextColor
        { get; set; }

        [Browsable(false)]
        public string SessionSellTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionSellTextColor); }
            set { SessionSellTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Buy Imbalance Text Color", Description = "Buy Imbalance Text Color.", Order = 7, GroupName = "Visual")]
        public Brush BuyImbalanceColor
        { get; set; }

        [Browsable(false)]
        public string BuyImbalanceColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BuyImbalanceColor); }
            set { BuyImbalanceColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sell Imbalance Text Color", Description = "Sell Imbalance Text Color.", Order = 8, GroupName = "Visual")]
        public Brush SellImbalanceColor
        { get; set; }

        [Browsable(false)]
        public string SellImbalanceColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SellImbalanceColor); }
            set { SellImbalanceColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Volume Histogram Color", Description = "Volume Histogram Color.", Order = 9, GroupName = "Visual")]
        public Brush VolumeColor
        { get; set; }

        [Browsable(false)]
        public string VolumeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(VolumeColor); }
            set { VolumeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Volume Text Color", Description = "Volume Text Color.", Order = 10, GroupName = "Visual")]
        public Brush VolumeTextColor
        { get; set; }

        [Browsable(false)]
        public string VolumeTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(VolumeTextColor); }
            set { VolumeTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask (Add) Text Color", Description = "Bid/Ask orders added.", Order = 11, GroupName = "Visual")]
        public Brush BidAskAddColor
        { get; set; }

        [Browsable(false)]
        public string BidAskAddColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BidAskAddColor); }
            set { BidAskAddColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask (Remove) Text Color", Description = "Bid/Ask orders removed.", Order = 12, GroupName = "Visual")]
        public Brush BidAskRemoveColor
        { get; set; }

        [Browsable(false)]
        public string BidAskRemoveColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BidAskRemoveColor); }
            set { BidAskRemoveColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Last Trade Text Color", Description = "Last trade text color.", Order = 13, GroupName = "Visual")]
        public Brush LastTradeColor
        { get; set; }

        [Browsable(false)]
        public string LastTradeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(LastTradeColor); }
            set { LastTradeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Header Row Color", Description = "Header row color.", Order = 14, GroupName = "Visual")]
        public Brush HeaderRowColor
        { get; set; }

        [Browsable(false)]
        public string HeaderRowColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(HeaderRowColor); }
            set { HeaderRowColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Header Text Color", Description = "Headers text color.", Order = 15, GroupName = "Visual")]
        public Brush HeadersTextColor
        { get; set; }

        [Browsable(false)]
        public string HeadersTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(HeadersTextColor); }
            set { HeadersTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Bid Column Color", Description = "Bid column color.", Order = 16, GroupName = "Visual")]
        public Brush BidColumnColor
        { get; set; }

        [Browsable(false)]
        public string BidColumnColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BidColumnColor); }
            set { BidColumnColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Ask Column Color", Description = "Ask column color.", Order = 16, GroupName = "Visual")]
        public Brush AskColumnColor
        { get; set; }

        [Browsable(false)]
        public string AskColumnColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(AskColumnColor); }
            set { AskColumnColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Buy Column Color", Description = "Buy column color.", Order = 17, GroupName = "Visual")]
        public Brush BuyColumnColor
        { get; set; }

        [Browsable(false)]
        public string BuyColumnColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BuyColumnColor); }
            set { BuyColumnColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sell Column Color", Description = "Sell column color.", Order = 18, GroupName = "Visual")]
        public Brush SellColumnColor
        { get; set; }

        [Browsable(false)]
        public string SellColumnColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SellColumnColor); }
            set { SellColumnColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Bid Histogram Color", Description = "Bid Histogram color.", Order = 19, GroupName = "Visual")]
        public Brush BidSizeColor
        { get; set; }

        [Browsable(false)]
        public string BidSizeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BidSizeColor); }
            set { BidSizeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Ask Histogram Color", Description = "Ask Histogram color.", Order = 20, GroupName = "Visual")]
        public Brush AskSizeColor
        { get; set; }

        [Browsable(false)]
        public string AskSizeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(AskSizeColor); }
            set { AskSizeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Highlight Color", Description = "Highlight color.", Order = 21, GroupName = "Visual")]
        public Brush HighlightColor
        { get; set; }

        [Browsable(false)]
        public string HighlightColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(HighlightColor); }
            set { HighlightColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "High Bid Size Marker Color", Description = "High Bid Size Marker.", Order = 23, GroupName = "Visual")]
        public Brush LargeBidSizeHighlightColor
        { get; set; }

        [Browsable(false)]
        public string HighBidSizeMarkerColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(LargeBidSizeHighlightColor); }
            set { LargeBidSizeHighlightColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "High Ask Size Marker Color", Description = "High Ask Size Marker.", Order = 24, GroupName = "Visual")]
        public Brush LargeAskSizeHighlightColor
        { get; set; }

        [Browsable(false)]
        public string HighAskSizeMarkerColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(LargeAskSizeHighlightColor); }
            set { LargeAskSizeHighlightColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Notes Text Color", Description = "Notes Text Color.", Order = 25, GroupName = "Visual")]
        public Brush NotesColor
        { get; set; }

        [Browsable(false)]
        public string NotesColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(NotesColor); }
            set { NotesColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        // =========== P/L Columns


        [NinjaScriptProperty]
        [Display(Name = "P/L", Description = "Display P/L.", Order = 1, GroupName = "P/L Columns")]
        public bool DisplayPL
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "P/L display type", Description = "P/L display type.", Order = 2, GroupName = "P/L Columns")]
        public PLType ProfitLossType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "P/L display currency", Description = "P/L display currency.", Order = 3, GroupName = "P/L Columns")]
        public Currency SelectedCurrency
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session P/L", Description = "Display session P/L.", Order = 4, GroupName = "P/L Columns")]
        public bool DisplaySessionPL
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Account Cash Value", Description = "Display account value.", Order = 5, GroupName = "P/L Columns")]
        public bool DisplayAccountValue
        { get; set; }

        // =========== Bid / Ask Columns

        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask", Description = "Display the bid/ask.", Order = 1, GroupName = "Bid / Ask Columns")]
        public bool DisplayBidAsk
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask Size Histogram", Description = "Draw bid/ask size Histogram.", Order = 2, GroupName = "Bid / Ask Columns")]
        public bool DisplayBidAskHistogram
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Bid/Ask Rows", Description = "Bid/Ask Rows", Order = 3, GroupName = "Bid / Ask Columns")]
        public int BidAskRows
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask Changes", Description = "Display the changes in bid/ask.", Order = 4, GroupName = "Bid / Ask Columns")]
        public bool DisplayBidAskChange
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Large Bid/Ask Highlight Filter", Description = "Filter to use when highlighting large bid/ask sizes", Order = 5, GroupName = "Bid / Ask Columns")]
        public int LargeBidAskSizeHighlightFilter
        { get; set; }

        // =========== OrderFlow Parameters

        [NinjaScriptProperty]
        [Range(1.5, double.MaxValue)]
        [Display(Name = "Imbalance Factor", Description = "Imbalance Factor", Order = 1, GroupName = "Order Flow Parameters")]
        public double ImbalanceFactor
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Overall OrderFlow Strength Bar", Description = "Display the overall OrderFlow strength bar, including data from imbalances.", Order = 2, GroupName = "Order Flow Parameters")]
        public bool DisplayOrderFlowStrengthBar
        { get; set; }

        [NinjaScriptProperty]
        [Range(51, 100)]
        [Display(Name = "OrderFlow Strength Threshold", Description = "Threshold for strength bar to light up (51-100)", Order = 3, GroupName = "Order Flow Parameters")]
        public int OrderFlowStrengthThreshold
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "OrderFlow Strength Calculation Mode", Description = "OrderFlow strength calculation mode", Order = 4, GroupName = "Order Flow Parameters")]
        public OFSCalculationMode OFSCalcMode
        { get; set; }

        #endregion

    }
}
