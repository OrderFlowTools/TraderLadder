// 
// Copyright (C) 2021, Gem Immanuel (gemify@gmail.com)
//
#region Using declarations
using Gemify.OrderFlow;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.SuperDom;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using Indicator = NinjaTrader.NinjaScript.Indicators.Indicator;
using Trade = Gemify.OrderFlow.Trade;
using LadderRow = NinjaTrader.Gui.SuperDom.LadderRow;

#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
    [Gui.CategoryOrder("Order Flow Parameters", 1)]
    [Gui.CategoryOrder("Buy / Sell Columns", 2)] 
    [Gui.CategoryOrder("Bid / Ask Columns", 3)]
    [Gui.CategoryOrder("Histograms", 4)]
    [Gui.CategoryOrder("Price and Volume Columns", 5)]
    [Gui.CategoryOrder("Notes", 6)]
    [Gui.CategoryOrder("P/L Columns", 7)]
    [Gui.CategoryOrder("OFS Bar", 8)]
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

            [Description("Sess Volume")]
            SESSION_VOLUME,

            [Description("Sliding Volume")]
            SLIDING_VOLUME,

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

            [Description("Delta")]
            DELTA,

            [Description("Buys")]
            BUYS,
            
            [Description("BIce")]
            BICE,
            
            [Description("SIce")]
            SICE,
            
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
        private Pen bigBuyPen;
        private Pen bigSellPen; 
        private Pen sessionBuysHistogramPen;
        private Pen sessionSellsHistogramPen;
        private Pen totalsPen;
        private Pen totalsBuyHighlightPen;
        private Pen totalsSellHighlightPen;
        private double gridPenThickness;
        private double histogramPenThickness;
        private double gridPenHalfThickness;
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
        private long maxSessionVolume = 0;
        private List<ColumnDefinition> columns;

        private Brush GridColor;
        private Brush CurrentPriceRowColor;
        private Brush LongPositionRowColor;
        private Brush ShortPositionRowColor;
        private static Indicator ind = new Indicator();
        private static CultureInfo culture = Core.Globals.GeneralOptions.CurrentCulture;
        private double pixelsPerDip;
        private long buysInSlidingWindow = 0;
        private long sellsInSlidingWindow = 0;
        private bool SlidingWindowLastOnly;
        private bool SlidingWindowLastMaxOnly;

        private double LargeBidAskSizePercThreshold;
        private ConcurrentDictionary<double, string> notes;

        // Mouse position stuff
        private bool mouseInBid = false, mouseInAsk = false;
        private double mouseAtPrice = -1;

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
                TraderLadderVersion = "v0.4.3";
                Name = "Free Trader Ladder (gemify) " + TraderLadderVersion;
                Description = @"Traders Ladder - (c) Gem Immanuel";
                DefaultWidth = 500;
                PreviousWidth = -1;
                IsDataSeriesRequired = true;

                // Orderflow var init
                // orderFlow = new GemsOrderFlow(new SimpleTradeClassifier(), ImbalanceFactor);
                orderFlow = new GemsOrderFlow(new IceTradeClassifier(), ImbalanceFactor);

                columns = new List<ColumnDefinition>();

                BidAskRows = 10;

                ImbalanceFactor = 2;
                TradeSlidingWindowSeconds = 60;
                OrderFlowStrengthThreshold = 65;
                OFSCalcMode = OFSCalculationMode.COMBINED;

                // Sliding volume default is same as sliding buy/sell window
                SlidingVolumeWindowSeconds = TradeSlidingWindowSeconds; 

                #region Color Defaults

                DefaultTextColor = Brushes.Gray;
                GridColor = new SolidColorBrush(Color.FromRgb(38, 38, 38)); GridColor.Freeze();
                DefaultBackgroundColor = Application.Current.TryFindResource("brushPriceColumnBackground") as SolidColorBrush;
                CurrentPriceRowColor = HeaderRowColor = Application.Current.TryFindResource("GridRowHighlight") as LinearGradientBrush;
                HeadersTextColor = Application.Current.TryFindResource("FontControlBrush") as SolidColorBrush;

                // ===================================
                // Highlighting params and colors
                // ===================================

                LargeBidAskSizeHighlightFilter = 200;
                LargeBidAskSizePercThreshold = 0.80; // Highlight bid/ask if size exceeds 80% of total bid/ask sizes
                TotalsBoxColor = DefaultTextColor;

                // ===================================
                // Bid/Ask Colors
                // ===================================

                AskColumnColor = new SolidColorBrush(Color.FromRgb(30, 15, 15)); AskColumnColor.Freeze();
                BidColumnColor = new SolidColorBrush(Color.FromRgb(20, 20, 30)); BidColumnColor.Freeze();

                BidHistogramColor = Brushes.SteelBlue;
                AskHistogramColor = Brushes.Firebrick;

                BidAskRemoveColor = Brushes.Red;
                BidAskAddColor = Brushes.DodgerBlue;

                LargeAskSizeHighlightColor = Brushes.Red; 
                LargeBidSizeHighlightColor = Brushes.DodgerBlue;

                // ===================================
                // Sliding Window Colors
                // ===================================

                SellColumnColor = new SolidColorBrush(Color.FromRgb(40, 11, 11)); SellColumnColor.Freeze();
                BuyColumnColor = new SolidColorBrush(Color.FromRgb(15, 15, 30)); BuyColumnColor.Freeze();

                SIceColumnColor = new SolidColorBrush(Color.FromRgb(30, 8, 8)); SellColumnColor.Freeze();
                BIceColumnColor = new SolidColorBrush(Color.FromRgb(12, 12, 30)); BuyColumnColor.Freeze();

                LastTradeColor = Brushes.White;

                BuyTotalsTextColor = Brushes.DodgerBlue;
                SellTotalsTextColor = Brushes.Red;

                SessionBuyTextColor = Brushes.SteelBlue;
                SessionSellTextColor = Brushes.Crimson;

                SessionBuysHistogramColor = Brushes.SteelBlue;
                SessionSellsHistogramColor = Brushes.Crimson;

                SellImbalanceColor = Brushes.Magenta;
                BuyImbalanceColor = Brushes.Cyan;

                // ===================================
                // Position Colors
                // ===================================

                LongPositionRowColor = new SolidColorBrush(Color.FromRgb(10, 60, 10)); LongPositionRowColor.Freeze();
                ShortPositionRowColor = new SolidColorBrush(Color.FromRgb(70, 10, 10)); ShortPositionRowColor.Freeze();

                // ===================================
                // Volume Colors
                // ===================================

                SessionVolumeHistogramColor = Brushes.MidnightBlue;
                SessionVolumeTextColor = Brushes.SteelBlue;

                SlidingVolumeHistogramColor = new SolidColorBrush(Color.FromRgb(45, 45, 125)); GridColor.Freeze();
                SlidingVolumeTextColor = Brushes.SteelBlue;

                #endregion

                DisplaySlidingWindowBuysSells = true;
                DisplayBuySellHistogram = true; 
                DisplaySlidingWindowTotalsInSummaryRow = true;

                DisplaySessionVolumeHistogram = false;
                DisplaySessionVolumeText = false;

                DisplaySlidingVolumeHistogram = false;
                DisplaySlidingVolumeText = false;

                DisplayBidAsk = false;
                DisplayBidAskHistogram = false;
                DisplayNotes = false;
                DisplayPrice = false;
                DisplayAccountValue = false;
                DisplayPL = false;
                DisplaySessionPL = false;
                DisplayBidAskChange = false;
                DisplayIce = false;
                DisplaySessionBuysSells = false;
                DisplaySessionBuysSellsHistogram = false;
                DisplayOrderFlowStrengthBar = false;
                DisplaySlidingWindowTotalsInSlidingWindow = false;
                DisplayIceHistogram = false;
                DisplayDelta = true;

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
                if (DisplaySessionVolumeHistogram || DisplaySessionVolumeText)
                    columns.Add(new ColumnDefinition(ColumnType.SESSION_VOLUME, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateSessionVolumeText));

                if (DisplaySlidingVolumeHistogram || DisplaySlidingVolumeText)
                    columns.Add(new ColumnDefinition(ColumnType.SLIDING_VOLUME, ColumnSize.SMALL, DefaultBackgroundColor, GenerateSlidingVolumeText));
                
                if (DisplayNotes)
                    columns.Add(new ColumnDefinition(ColumnType.NOTES, ColumnSize.LARGE, DefaultBackgroundColor, GenerateNotesText));
                if (DisplayPrice)
                    columns.Add(new ColumnDefinition(ColumnType.PRICE, ColumnSize.SMALL, DefaultBackgroundColor, GetPrice));
                if (DisplayPL)
                    columns.Add(new ColumnDefinition(ColumnType.PL, ColumnSize.SMALL, DefaultBackgroundColor, CalculatePL));
                if (DisplayDelta)
                    columns.Add(new ColumnDefinition(ColumnType.DELTA, ColumnSize.SMALL, DefaultBackgroundColor, GenerateDeltaText));
                if (DisplayBidAskChange)
                    columns.Add(new ColumnDefinition(ColumnType.BID_CHANGE, ColumnSize.XSMALL, DefaultBackgroundColor, GenerateBidChangeText));
                if (DisplayBidAsk || DisplayBidAskHistogram)
                    columns.Add(new ColumnDefinition(ColumnType.BID, ColumnSize.SMALL, DefaultBackgroundColor, GenerateBidText));
                if (DisplayIce)
                    columns.Add(new ColumnDefinition(ColumnType.BICE, ColumnSize.XSMALL, BIceColumnColor, GenerateBIceText));
                if (DisplaySlidingWindowBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.SELLS, ColumnSize.SMALL, SellColumnColor, GenerateSlidingWindowSellsText));
                if (DisplaySlidingWindowBuysSells)                
                    columns.Add(new ColumnDefinition(ColumnType.BUYS, ColumnSize.SMALL, BuyColumnColor, GenerateSlidingWindowBuysText));
                if (DisplayIce)
                    columns.Add(new ColumnDefinition(ColumnType.SICE, ColumnSize.XSMALL, SIceColumnColor, GenerateSIceText));
                if (DisplayBidAsk || DisplayBidAskHistogram)
                    columns.Add(new ColumnDefinition(ColumnType.ASK, ColumnSize.SMALL, DefaultBackgroundColor, GenerateAskText));
                if (DisplayBidAskChange)
                    columns.Add(new ColumnDefinition(ColumnType.ASK_CHANGE, ColumnSize.XSMALL, DefaultBackgroundColor, GenerateAskChangeText));
                if (DisplaySessionBuysSells || DisplaySessionBuysSellsHistogram)
                {
                    columns.Add(new ColumnDefinition(ColumnType.TOTAL_SELLS, ColumnSize.SMALL, DefaultBackgroundColor, GenerateSessionSellsText));
                    columns.Add(new ColumnDefinition(ColumnType.TOTAL_BUYS, ColumnSize.SMALL, DefaultBackgroundColor, GenerateSessionBuysText));
                }

                if (DisplaySessionPL)
                    columns.Add(new ColumnDefinition(ColumnType.TOTALPL, ColumnSize.LARGE, DefaultBackgroundColor, CalculateTotalPL));
                if (DisplayAccountValue)
                    columns.Add(new ColumnDefinition(ColumnType.ACCVAL, ColumnSize.LARGE, DefaultBackgroundColor, CalculateAccValue));

                if (DisplayOrderFlowStrengthBar)
                    columns.Add(new ColumnDefinition(ColumnType.OF_STRENGTH, ColumnSize.XSMALL, DefaultBackgroundColor, CalculateOFStrength));

                #endregion

                if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
                {
                    Matrix m = PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
                    double dpiFactor = 1 / m.M11;
                    gridPenThickness = Math.Min(0.6, dpiFactor);
                    histogramPenThickness = gridPenThickness * 1.5;
                    gridPen = new Pen(GridColor, gridPenThickness);
                    gridPenHalfThickness = gridPen.Thickness * 0.5;
                    pixelsPerDip = VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

                    bidSizePen = new Pen(BidHistogramColor, histogramPenThickness);
                    askSizePen = new Pen(AskHistogramColor, histogramPenThickness);

                    bigBuyPen = new Pen(LargeBidSizeHighlightColor, histogramPenThickness * 1.5);
                    bigSellPen = new Pen(LargeAskSizeHighlightColor, histogramPenThickness * 1.5);

                    sessionBuysHistogramPen = new Pen(SessionBuysHistogramColor, histogramPenThickness);
                    sessionSellsHistogramPen = new Pen(SessionSellsHistogramColor, histogramPenThickness);

                    double totalsThickness = histogramPenThickness * 1.5;
                    totalsPen = new Pen(TotalsBoxColor, totalsThickness);
                    totalsBuyHighlightPen = new Pen(BuyTotalsTextColor, totalsThickness);
                    totalsSellHighlightPen = new Pen(SellTotalsTextColor, totalsThickness);
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
                                    double askPrice = request.Bars.BarsSeries.GetAsk(i);
                                    double bidPrice = request.Bars.BarsSeries.GetBid(i);
                                    double tradePrice = request.Bars.BarsSeries.GetClose(i);
                                    long askSize = orderFlow.GetAskSize(tradePrice);
                                    long bidSize = orderFlow.GetBidSize(tradePrice);
                                    long tradeSize = request.Bars.BarsSeries.GetVolume(i);

                                    // Classify current volume as buy/sell
                                    // and add them to the buys/sells and totalBuys/totalSells collections
                                    orderFlow.ClassifyTrade(false, askPrice, askSize, bidPrice, bidSize, tradePrice, tradeSize, time);

                                    // Calculate current max volume for session
                                    long totalVolume = orderFlow.GetVolumeAtPrice(tradePrice);
                                    maxSessionVolume = totalVolume > maxSessionVolume ? totalVolume : maxSessionVolume;
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
                WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseMove", OnMouseMove);
                WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseEnter", OnMouseEnter);
                WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseLeave", OnMouseLeave);
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
                    WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseDown", OnMouseEnter);
                    WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseDown", OnMouseLeave);
                    WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseDown", OnMouseMove);
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

            }

            OnPropertyChanged();

        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (SuperDom.MarketDepth.Asks.Count == 0 || SuperDom.MarketDepth.Bids.Count == 0)
            {
                // Only interested in Bid/Ask updates
                if (e.MarketDataType != MarketDataType.Ask && e.MarketDataType != MarketDataType.Bid) return;

                if (e.MarketDataType == MarketDataType.Ask)
                {
                    orderFlow.AddOrUpdateAsk(e.Price, e.Volume, e.Time);
                }
				else if (e.MarketDataType == MarketDataType.Bid)
                {
                    orderFlow.AddOrUpdateBid(e.Price, e.Volume, e.Time);
                }
            }
        }

        private void OnBarsUpdate(object sender, BarsUpdateEventArgs e)
        {
            try
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

                        double askPrice, bidPrice, tradePrice;
                        long tradeSize, askSize, bidSize;
                        DateTime time;

                        for (int i = lastMaxIndex + 1; i <= currentMaxIndex; i++)
                        {
                            if (barsUpdate.BarsSeries.GetIsFirstBarOfSession(i))
                            {
                                // If a new session starts, clear out the old values and start fresh
                                maxSessionVolume = 0;
                                orderFlow.ClearAll();
                            }

                            tradePrice = barsUpdate.BarsSeries.GetClose(i);
                            tradeSize = barsUpdate.BarsSeries.GetVolume(i);
                            time = barsUpdate.BarsSeries.GetTime(i);

                            // Fetch our datapoints
                            if (SuperDom.MarketDepth.Asks.Count == 0 || SuperDom.MarketDepth.Bids.Count == 0)
                            {
                                askPrice = barsUpdate.BarsSeries.GetAsk(i);
                                bidPrice = barsUpdate.BarsSeries.GetBid(i);						
                                askSize = orderFlow.GetAskSize(tradePrice);
                                bidSize = orderFlow.GetBidSize(tradePrice);
                            }
                            else
                            {
                                LadderRow askRow = SuperDom.MarketDepth.Asks[0];
                                LadderRow bidRow = SuperDom.MarketDepth.Bids[0];

                                askPrice = askRow.Price;
                                bidPrice = bidRow.Price;
                                askSize = askRow.Volume;
                                bidSize = bidRow.Volume;
                            }

                            // Clear out data in buy / sell dictionaries based on a configurable
                            // sliding window of time (in seconds)
                            orderFlow.ClearTradesOutsideSlidingWindow(time, TradeSlidingWindowSeconds);
                            orderFlow.ClearVolumeOutsideSlidingWindow(time, SlidingVolumeWindowSeconds);

                            // Classify current volume as buy/sell
                            // and add them to the buys/sells and totalBuys/totalSells collections
                            orderFlow.ClassifyTrade(true, askPrice, askSize, bidPrice, bidSize, tradePrice, tradeSize, time);

                            if (DisplaySessionVolumeHistogram)
                            {
                                // Calculate current max volume for session
                                long totalVolume = orderFlow.GetVolumeAtPrice(tradePrice);
                                maxSessionVolume = totalVolume > maxSessionVolume ? totalVolume : maxSessionVolume;
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
            catch (Exception x)
            {
                Print("An error occurred in the Free Trader Ladder SuperDOM column. Please report this error." + x.StackTrace);
            }
        }

        private List<LadderRow> GetBidLadderCopy()
        {
            List<LadderRow> ladder = null;
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
                    ladder = new List<LadderRow>(ladder);
                }
            }
            catch (Exception)
            {
                // NOP for now. 
            }
            return ladder;
        }

        private List<LadderRow> GetAskLadderCopy()
        {
            List<LadderRow> ladder = null;
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
                    ladder = new List<LadderRow>(ladder);
                }
            }
            catch (Exception)
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
                    gridPenHalfThickness = gridPen.Thickness * 0.5;
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
                    if (renderWidth - gridPenHalfThickness >= 0)
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
                Rect cellRect = new Rect(x, verticalOffset, cellWidth, SuperDom.ActualRowHeight);

                // Create a guidelines set
                GuidelineSet guidelines = new GuidelineSet();
                guidelines.GuidelinesX.Add(cellRect.Left + gridPenHalfThickness);
                guidelines.GuidelinesX.Add(cellRect.Right + gridPenHalfThickness);
                guidelines.GuidelinesY.Add(cellRect.Top + gridPenHalfThickness);
                guidelines.GuidelinesY.Add(cellRect.Bottom + gridPenHalfThickness);
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
                if (SuperDom.Position != null && row.IsEntry && colDef.ColumnType != ColumnType.OF_STRENGTH && colDef.ColumnType != ColumnType.SESSION_VOLUME && colDef.ColumnType != ColumnType.SLIDING_VOLUME)
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
                if (row.Price == SuperDom.CurrentLast && colDef.ColumnType != ColumnType.OF_STRENGTH && colDef.ColumnType != ColumnType.SESSION_VOLUME && colDef.ColumnType != ColumnType.SLIDING_VOLUME)
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

                // Detect mouse movement. 
                // Check if mouse is within current cell
                if (cellRect.Contains(Mouse.GetPosition(UiWrapper))) {
                    mouseAtPrice = row.Price;
                    switch (colDef.ColumnType)
                    {
                        case ColumnType.BID:
                            cellColor = row.Price <= SuperDom.CurrentBid ? Brushes.Blue : cellColor;                            
                            mouseInBid = true;
                            break;
                        case ColumnType.ASK:
                            cellColor = row.Price >= SuperDom.CurrentAsk ? Brushes.Maroon : cellColor;
                            mouseInAsk = true;
                            break;
                    }
                }
                else
                {
                    mouseInBid = false;
                    mouseAtPrice = -1;
                }

                // Draw grid rectangle
                dc.DrawRectangle(cellColor, null, cellRect);
                dc.DrawLine(gridPen, new Point(-gridPen.Thickness, cellRect.Bottom), new Point(renderWidth - gridPenHalfThickness, cellRect.Bottom));
                if (row.Price != SuperDom.LowerPrice && row.Price != SuperDom.CurrentLast && colDef.ColumnType != ColumnType.OF_STRENGTH && colDef.ColumnType != ColumnType.SESSION_VOLUME)
                {
                    dc.DrawLine(gridPen, new Point(cellRect.Right, verticalOffset), new Point(cellRect.Right, cellRect.Bottom));
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
                    dc.DrawText(header, new Point(cellRect.Left + 10, verticalOffset + (SuperDom.ActualRowHeight - header.Height) / 2));
                }
                // Regular data rows
                else
                {
                    // Draw Volume
                    if (row.Price != SuperDom.LowerPrice && colDef.ColumnType == ColumnType.SESSION_VOLUME && colDef.Text != null)
                    {
                        long volumeAtPrice = colDef.Text.Text == null ? 0 : long.Parse(colDef.Text.Text);
                        double totalWidth = cellWidth * ((double)volumeAtPrice / maxSessionVolume);
                        double volumeWidth = totalWidth == cellWidth ? totalWidth - gridPen.Thickness * 1.5 : totalWidth - gridPenHalfThickness;
                        volumeWidth -= 3;

                        if (volumeWidth >= 0)
                        {
                            double xc = x + (cellWidth - volumeWidth);
                            dc.DrawRectangle(SessionVolumeHistogramColor, null, new Rect(xc, verticalOffset - 1, volumeWidth, cellRect.Height));
                        }

                        if (!DisplaySessionVolumeText)
                        {
                            colDef.Text = null;
                        }
                    }
                    // Draw Sliding Volume
                    else if (row.Price != SuperDom.LowerPrice && colDef.ColumnType == ColumnType.SLIDING_VOLUME && colDef.Text != null)
                    {
                        long maxSlidingVolume = orderFlow.GetLargestSlidingVolume();
                        long volumeAtPrice = colDef.Text.Text == null ? 0 : long.Parse(colDef.Text.Text);
                        double totalWidth = cellWidth * ((double)volumeAtPrice / maxSlidingVolume);
                        double volumeWidth = totalWidth == cellWidth ? totalWidth - gridPen.Thickness * 1.5 : totalWidth - gridPenHalfThickness;
                        volumeWidth -= 3;

                        if (volumeWidth >= 0)
                        {
                            double xc = x + (cellWidth - volumeWidth);
                            dc.DrawRectangle(SlidingVolumeHistogramColor, null, new Rect(xc, verticalOffset - 1, volumeWidth, cellRect.Height));
                        }

                        if (!DisplaySlidingVolumeText)
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

                                if (orderFlow.GetAskSize(row.Price) > LargeBidAskSizeHighlightFilter && perc > LargeBidAskSizePercThreshold)
                                {
                                    pen = new Pen(LargeAskSizeHighlightColor, 2);
                                }

                                double totalWidth = cellWidth * perc;
                                double paintWidth = totalWidth == cellWidth ? totalWidth - pen.Thickness * 1.5 : totalWidth - gridPenHalfThickness;

                                if (paintWidth >= 0)
                                {
                                    double xc = x + (colDef.ColumnType == ColumnType.ASK ? 1 : (cellWidth - paintWidth));
                                    dc.DrawRectangle(null, pen, new Rect(xc, verticalOffset, paintWidth, cellRect.Height - pen.Thickness * 2));
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

                                if (orderFlow.GetBidSize(row.Price) > LargeBidAskSizeHighlightFilter && perc > LargeBidAskSizePercThreshold)
                                {
                                    pen = new Pen(LargeBidSizeHighlightColor, 2);
                                }

                                double totalWidth = cellWidth * perc;
                                double paintWidth = totalWidth == cellWidth ? totalWidth - pen.Thickness * 1.5 : totalWidth - gridPenHalfThickness;

                                if (paintWidth >= 0)
                                {
                                    double xc = x + (cellWidth - paintWidth);
                                    dc.DrawRectangle(null, pen, new Rect(xc, verticalOffset, paintWidth, cellRect.Height - pen.Thickness * 2));
                                }
                            }
                        }
                    }
                    // Draw Buy/Sell columns
                    else if ((DisplaySlidingWindowTotalsInSlidingWindow || DisplaySlidingWindowTotalsInSummaryRow) && (colDef.ColumnType == ColumnType.SELLS || colDef.ColumnType == ColumnType.BUYS))
                    {
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

                        double highestPriceInSlidingWindow = orderFlow.GetHighestBuyPriceInSlidingWindow();
                        double lowestPriceInSlidingWindow = orderFlow.GetLowestSellPriceInSlidingWindow();

                        // Calculate prices at which to display totals
                        double sellTotalsPrice = lowestPriceInSlidingWindow - SuperDom.Instrument.MasterInstrument.TickSize;
                        double buyTotalsPrice = highestPriceInSlidingWindow + SuperDom.Instrument.MasterInstrument.TickSize;

                        if (colDef.ColumnType == ColumnType.BUYS)
                        {
                            // If we're at the price where the totals should be rendered
                            if (
                                (DisplaySlidingWindowTotalsInSlidingWindow && row.Price == buyTotalsPrice) ||
                                (DisplaySlidingWindowTotalsInSummaryRow && row.Price == SuperDom.LowerPrice))
                            {

                                // Write total value
                                FormattedText text = FormatText(buyTotal.ToString(), cellWidth - 2, BuyTotalsTextColor, TextAlignment.Right);
                                dc.DrawText(text, new Point(cellRect.Left + 5, verticalOffset + (SuperDom.ActualRowHeight - text.Height) / 2));

                                // Sliding Window
                                if (DisplaySlidingWindowTotalsInSlidingWindow)
                                {
                                    dc.DrawRectangle(null, totalsPen, new Rect(x + 2, verticalOffset + totalsPen.Thickness, cellWidth - 3, cellRect.Height - totalsPen.Thickness * 2));
                                }

                                // Summary Row
                                if (DisplaySlidingWindowTotalsInSummaryRow)
                                {
                                    if (buyTotal > sellTotal)
                                    {
                                        dc.DrawRectangle(null, totalsBuyHighlightPen, new Rect(x + 2, verticalOffset + totalsBuyHighlightPen.Thickness, cellWidth - 3, cellRect.Height - totalsBuyHighlightPen.Thickness * 2));
                                    }
                                }
                            }
                        }

                        if (colDef.ColumnType == ColumnType.SELLS)
                        {
                            // If we're at the price where the totals should be rendered
                            if (
                                (DisplaySlidingWindowTotalsInSlidingWindow && row.Price == sellTotalsPrice) ||
                                (DisplaySlidingWindowTotalsInSummaryRow && row.Price == SuperDom.LowerPrice))
                            {

                                // Write total value
                                FormattedText text = FormatText(sellTotal.ToString(), cellWidth - 2, SellTotalsTextColor, TextAlignment.Right);
                                dc.DrawText(text, new Point(cellRect.Left + 5, verticalOffset + (SuperDom.ActualRowHeight - text.Height) / 2));

                                // Sliding Window
                                if (DisplaySlidingWindowTotalsInSlidingWindow)
                                {
                                    dc.DrawRectangle(null, totalsPen, new Rect(x + 2, verticalOffset + totalsPen.Thickness, cellWidth - 3, cellRect.Height - totalsPen.Thickness * 2));
                                }

                                // Summary Row
                                if (DisplaySlidingWindowTotalsInSummaryRow)
                                {
                                    if (sellTotal > buyTotal)
                                    {
                                        dc.DrawRectangle(null, totalsSellHighlightPen, new Rect(x + 2, verticalOffset + totalsSellHighlightPen.Thickness, cellWidth - 3, cellRect.Height - totalsSellHighlightPen.Thickness * 2));
                                    }
                                }

                            }
                        }
                        // Draw Sliding Window Histogram
                        if (DisplayBuySellHistogram && row.Price != SuperDom.LowerPrice)
                        {
                            if (row.Price <= highestPriceInSlidingWindow && row.Price >= lowestPriceInSlidingWindow)
                            {
                                double largestBuyInSW = 0;
                                double largestSellInSW = 0;
                                double buysSize = 0;
                                double sellsSize = 0;

                                if (SlidingWindowLastMaxOnly)
                                {
                                    largestBuyInSW = orderFlow.GetLargestMaxBuyInSlidingWindow();
                                    largestSellInSW = orderFlow.GetLargestMaxSellInSlidingWindow();
                                    buysSize = orderFlow.GetLastBuyPrintMax(row.Price);
                                    sellsSize = orderFlow.GetLastSellPrintMax(row.Price);
                                }
                                else if (SlidingWindowLastOnly)
                                {
                                    largestBuyInSW = orderFlow.GetLargestLastBuyInSlidingWindow();
                                    largestSellInSW = orderFlow.GetLargestLastSellInSlidingWindow();
                                    buysSize = orderFlow.GetLastBuyPrint(row.Price);
                                    sellsSize = orderFlow.GetLastSellPrint(row.Price);
                                }
                                else
                                {
                                    largestBuyInSW = orderFlow.GetLargestBuyInSlidingWindow();
                                    largestSellInSW = orderFlow.GetLargestSellInSlidingWindow();

                                    Trade bt = orderFlow.GetBuysInSlidingWindow(row.Price);
                                    buysSize = bt == null ? 0 : bt.swCumulSize;
                                    Trade st = orderFlow.GetSellsInSlidingWindow(row.Price);
                                    sellsSize = st == null ? 0 : st.swCumulSize;
                                }

                                double largestSize = Math.Max(largestBuyInSW, largestSellInSW);

                                if (largestSize > 0)
                                {
                                    double perc = (colDef.ColumnType == ColumnType.BUYS ? buysSize : sellsSize) / largestSize;

                                    Pen pen = colDef.ColumnType == ColumnType.BUYS ? bidSizePen : askSizePen;

                                    if (colDef.ColumnType == ColumnType.BUYS && buysSize > 2 * sellsSize)
                                    {
                                        pen = bigBuyPen;
                                    }
                                    else if (colDef.ColumnType == ColumnType.SELLS && sellsSize > 2 * buysSize)
                                    {
                                        pen = bigSellPen;
                                    }

                                    double totalWidth = cellWidth * perc;
                                    double paintWidth = (totalWidth == cellWidth ? totalWidth - pen.Thickness * 1.5 : totalWidth - gridPenHalfThickness) - 5;

                                    if (paintWidth >= 0)
                                    {
                                        double xc = x + (colDef.ColumnType == ColumnType.BUYS ? 1 : (cellWidth - paintWidth));
                                        dc.DrawRectangle(null, pen, new Rect(xc, verticalOffset, paintWidth, cellRect.Height - (pen.Thickness * 2)));
                                    }
                                }
                            }
                        }                        
                    }
                    else if (colDef.ColumnType == ColumnType.BICE || colDef.ColumnType == ColumnType.SICE)
                    {
                        if (DisplayIceHistogram && row.Price != SuperDom.LowerPrice)
                        {
                            // Draw Ice Histograms
                            double largestIceBuy = orderFlow.GetLargestBIce();
                            double largestIceSell = orderFlow.GetLargestSIce();

                            long bice = orderFlow.GetBIce(row.Price);
                            long sice = orderFlow.GetSIce(row.Price);

                            double largestSize = Math.Max(largestIceBuy, largestIceSell);

                            if (largestSize > 0)
                            {
                                double perc = (colDef.ColumnType == ColumnType.BICE ? bice : sice) / largestSize;

                                Pen pen = colDef.ColumnType == ColumnType.BICE ? bidSizePen : askSizePen;

                                double totalWidth = cellWidth * perc;
                                double paintWidth = (totalWidth == cellWidth ? totalWidth - pen.Thickness * 1.5 : totalWidth - gridPenHalfThickness) - 5;

                                if (paintWidth >= 0)
                                {
                                    double xc = x + (colDef.ColumnType == ColumnType.BICE ? 1 : (cellWidth - paintWidth));
                                    dc.DrawRectangle(null, pen, new Rect(xc, verticalOffset, paintWidth, cellRect.Height - (pen.Thickness * 2)));
                                }
                            }
                        }
                        // Summary row - display BIce/SIce totals
                        else if (DisplaySlidingWindowTotalsInSummaryRow && row.Price == SuperDom.LowerPrice)
                        {
                            // Display BIce/SIce totals
                            long totalBice = orderFlow.GetTotalBIce();
                            long totalSice = orderFlow.GetTotalSIce();
                            long total = colDef.ColumnType == ColumnType.BICE ? totalBice : totalSice;

                            if (total > 0)
                            {
                                Brush brush = colDef.ColumnType == ColumnType.BICE ? BuyTotalsTextColor : SellTotalsTextColor;

                                FormattedText text = FormatText(total.ToString(), cellWidth - 2, brush, TextAlignment.Right);
                                dc.DrawText(text, new Point(cellRect.Left + 5, verticalOffset + (SuperDom.ActualRowHeight - text.Height) / 2));

                                if (colDef.ColumnType == ColumnType.BICE && totalBice > totalSice)
                                {
                                    dc.DrawRectangle(null, totalsBuyHighlightPen, new Rect(x + 2, verticalOffset + totalsBuyHighlightPen.Thickness, cellWidth - 3, cellRect.Height - totalsBuyHighlightPen.Thickness * 2));
                                }
                                else if (colDef.ColumnType == ColumnType.SICE && totalSice > totalBice) {
                                    dc.DrawRectangle(null, totalsSellHighlightPen, new Rect(x + 2, verticalOffset + totalsSellHighlightPen.Thickness, cellWidth - 3, cellRect.Height - totalsSellHighlightPen.Thickness * 2));
                                }                                
                            }
                        }
                    }
                    else if (DisplaySessionBuysSellsHistogram && row.Price != SuperDom.LowerPrice && (colDef.ColumnType == ColumnType.TOTAL_SELLS || colDef.ColumnType == ColumnType.TOTAL_BUYS))
                    {
                        long largestSessionSize = orderFlow.GetLargestSessionSize(SuperDom.LowerPrice, SuperDom.UpperPrice);
                        if (largestSessionSize > 0)
                        {
                            long sessSize = colDef.ColumnType == ColumnType.TOTAL_BUYS ? orderFlow.GetBuyVolumeAtPrice(row.Price) : orderFlow.GetSellVolumeAtPrice(row.Price);
                            double perc = (double)sessSize / (double)largestSessionSize;

                            Pen pen = colDef.ColumnType == ColumnType.TOTAL_BUYS ? sessionBuysHistogramPen : sessionSellsHistogramPen;

                            double totalWidth = cellWidth * perc;
                            double paintWidth = (totalWidth == cellWidth ? totalWidth - pen.Thickness * 1.5 : totalWidth - gridPenHalfThickness) - 5;

                            if (paintWidth >= 0)
                            {
                                double xc = x + (colDef.ColumnType == ColumnType.TOTAL_BUYS ? 1 : (cellWidth - paintWidth));
                                Brush fill = true ? null : colDef.ColumnType == ColumnType.TOTAL_BUYS ? BIceColumnColor : SIceColumnColor;
                                dc.DrawRectangle(fill, pen, new Rect(xc, verticalOffset, paintWidth, cellRect.Height - (pen.Thickness * 2)));
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

                    // ---------------------------------
                    // Summary row (SuperDom.LowerPrice)
                    // ---------------------------------

                    if (row.Price == SuperDom.LowerPrice)
                    {

                        Brush color = DefaultTextColor;
                        String text = String.Empty;
                        TextAlignment textAlignment = TextAlignment.Right;

                        // Write summary at lowerprice row
                        if (colDef.ColumnType == ColumnType.DELTA)
                        {
                            long buyTotal = orderFlow.GetBuysInSlidingWindow();
                            long sellTotal = orderFlow.GetSellsInSlidingWindow();

                            if (buyTotal > 0 || sellTotal > 0)
                            {
                                color = buyTotal > sellTotal ? BuyTotalsTextColor : (sellTotal > buyTotal ? SellTotalsTextColor : DefaultTextColor);
                                text = (buyTotal - sellTotal).ToString();
                            }

                            textAlignment = TextAlignment.Center;
                        }
                        else if (colDef.ColumnType == ColumnType.SLIDING_VOLUME)
                        {
                            text = orderFlow.GetTotalSlidingVolume().ToString();
                        }

                        FormattedText ftext = FormatText(text, cellWidth, color, textAlignment);
                        dc.DrawText(ftext, new Point(cellRect.Left + 5, verticalOffset + (SuperDom.ActualRowHeight - ftext.Height) / 2));

                    }
                    else if (colDef.Text != null)
                    {
                        // Write the column text
                        double xp = cellRect.Left + 5;
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

        private FormattedText GenerateSessionVolumeText(double renderWidth, double price)
        {
            long totalVolume = orderFlow.GetVolumeAtPrice(price);
            return totalVolume > 0 ? FormatText(totalVolume.ToString(), renderWidth, SessionVolumeTextColor, TextAlignment.Right) : null;
        }

        private FormattedText GenerateSlidingVolumeText(double renderWidth, double price)
        {
            long slidingVolume = orderFlow.GetSlidingVolumeAtPrice(price);
            return slidingVolume > 0 ? FormatText(slidingVolume.ToString(), renderWidth, SlidingVolumeTextColor, TextAlignment.Right) : null;
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
                        color = Brushes.DodgerBlue;
                    }
                    else
                    {
                        color = Brushes.Blue;
                    }
                    text = (nBuyRows - 1 == (price - SuperDom.LowerPrice) / SuperDom.Instrument.MasterInstrument.TickSize) ? Math.Round(buyStrength, 0, MidpointRounding.AwayFromZero).ToString() : text;
                }
            }

            return FormatText(string.Format("{0}", text), renderWidth, color, TextAlignment.Center);
        }

        private FormattedText GenerateSessionBuysText(double renderWidth, double buyPrice)
        {
            if (!DisplaySessionBuysSells) return null;

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
                return FormatText(totalBuys.ToString(), renderWidth, brush, TextAlignment.Left);
            }

            return null;
        }

        private FormattedText GenerateSessionSellsText(double renderWidth, double sellPrice)
        {
            if (!DisplaySessionBuysSells) return null;

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
                long currentSize = (price >= SuperDom.CurrentAsk + BidAskCutoffTicks) ? 0 : orderFlow.GetAskSize(price);

                if (currentSize > 0)
                    return FormatText(" " + currentSize.ToString(), renderWidth, DefaultTextColor, TextAlignment.Left);
            }
            return null;
        }

        private FormattedText GenerateBidText(double renderWidth, double price)
        {
            if (DisplayBidAsk)
            {
                long currentSize = (price <= SuperDom.CurrentBid - BidAskCutoffTicks) ? 0 : orderFlow.GetBidSize(price);

                if (currentSize > 0) 
                    return FormatText(currentSize.ToString() + " ", renderWidth, DefaultTextColor, TextAlignment.Right);
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

                    return FormatText(" " + buys.swCumulSize.ToString(), renderWidth, color, TextAlignment.Left);
                }
            }
            return null;
        }

        private FormattedText GenerateSIceText(double renderWidth, double price)
        {
            long size = orderFlow.GetSIce(price);

            if (size > 0)
            {
                return FormatText(size.ToString() + " ", renderWidth, SellTotalsTextColor, TextAlignment.Right);
            }

            return null;
        }

        private FormattedText GenerateBIceText(double renderWidth, double price)
        {
            long size = orderFlow.GetBIce(price);

            if (size > 0)
            {
                return FormatText(" " + size.ToString(), renderWidth, BuyTotalsTextColor, TextAlignment.Left);
            }

            return null;
        }

        private FormattedText GenerateDeltaText(double renderWidth, double price)
        {
            Trade sells = orderFlow.GetSellsInSlidingWindow(price);
            Trade buys = orderFlow.GetBuysInSlidingWindow(price + SuperDom.Instrument.MasterInstrument.TickSize);
            long sellSize = (sells != null ? sells.swCumulSize : 0);
            long buySize = (buys != null ? buys.swCumulSize : 0);

            if (sellSize > 0 || buySize > 0)
            {
                Brush color = SuperDom.CurrentBid == price ? LastTradeColor : DefaultTextColor;

                if (buys != null && sells != null)
                {
                    if (sells.swCumulSize > buys.swCumulSize * ImbalanceFactor)
                    {
                        color = SellImbalanceColor;
                    }
                    else if (buys.swCumulSize > sells.swCumulSize * ImbalanceFactor)
                    {
                        color = BuyImbalanceColor;
                    }
                }

                return FormatText((buySize - sellSize).ToString(), renderWidth, color, TextAlignment.Center);
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

                    return FormatText(sells.swCumulSize.ToString() + " ", renderWidth, color, TextAlignment.Right);
                }
            }
            return null;
        }

        private FormattedText GenerateLastBuyPrintText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastBuyPrint(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, DefaultTextColor, TextAlignment.Left);
            }
            return null;
        }

        private FormattedText GenerateLastSellPrintText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastSellPrint(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, DefaultTextColor, TextAlignment.Right);
            }

            return null;
        }

        private FormattedText GenerateLastBuyPrintMaxText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastBuyPrintMax(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, DefaultTextColor, TextAlignment.Left);
            }
            return null;
        }

        private FormattedText GenerateLastSellPrintMaxText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastSellPrintMax(price);

            if (size > 0)
            {
                return FormatText(size.ToString(), renderWidth, DefaultTextColor, TextAlignment.Right);
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

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            OnPropertyChanged();
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            OnPropertyChanged();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            OnPropertyChanged();
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            NinjaTrader.Gui.SuperDom.ColumnWrapper wrapper = (NinjaTrader.Gui.SuperDom.ColumnWrapper)sender;

            // Print("Mouse at price " + mouseAtPrice + " on " + (mouseInBid ? " BID " : (mouseInAsk ? " ASK " : "")));

            Point p = Mouse.GetPosition(wrapper);

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
        [Display(Name = "Session Volume", Description = "Display session volume text.", Order = 2, GroupName = "Price and Volume Columns")]
        public bool DisplaySessionVolumeText
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sliding Volume", Description = "Display sliding volume text.", Order = 3, GroupName = "Price and Volume Columns")]
        public bool DisplaySlidingVolumeText
        { get; set; }



        // =========== Buy / Sell Columns

        [NinjaScriptProperty]
        [Display(Name = "Delta (Sliding Window)", Description = "Display Delta in the sliding window.", Order = 1, GroupName = "Buy / Sell Columns")]
        public bool DisplayDelta
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Buy / Sell (Sliding Window)", Description = "Display Buys/Sells in a sliding window.", Order = 2, GroupName = "Buy / Sell Columns")]
        public bool DisplaySlidingWindowBuysSells
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BIce / SIce (Experimental)", Description = "Display BIce/SIce in a sliding window.", Order = 3, GroupName = "Buy / Sell Columns")]
        public bool DisplayIce
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Buys / Sells", Description = "Display the total buys and sells columns.", Order = 4, GroupName = "Buy / Sell Columns")]
        public bool DisplaySessionBuysSells
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sliding Window Totals (Summary Row)", Description = "Display Sliding Window Totals it the summary row.", Order = 5, GroupName = "Buy / Sell Columns")]
        public bool DisplaySlidingWindowTotalsInSummaryRow
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sliding Window Totals (In Sliding Window)", Description = "Display Sliding Window Totals in the Sliding Window.", Order = 6, GroupName = "Buy / Sell Columns")]
        public bool DisplaySlidingWindowTotalsInSlidingWindow
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

        // =========== Foundational colors

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Background Color", Description = "Default background color.", Order = 1, GroupName = "Visual")]
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
        [Display(Name = "Header Row Color", Description = "Header row color.", Order = 3, GroupName = "Visual")]
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
        [Display(Name = "Header Text Color", Description = "Headers text color.", Order = 4, GroupName = "Visual")]
        public Brush HeadersTextColor
        { get; set; }

        [Browsable(false)]
        public string HeadersTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(HeadersTextColor); }
            set { HeadersTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        // =========== Summary row
        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Totals Outline Color", Description = "Totals outline color.", Order = 1, GroupName = "Colors: Summary Row")]
        public Brush TotalsBoxColor
        { get; set; }

        [Browsable(false)]
        public string TotalsBoxColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(TotalsBoxColor); }
            set { TotalsBoxColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Buy Totals Color", Description = "Buy Totals Text Color.", Order = 2, GroupName = "Colors: Summary Row")]
        public Brush BuyTotalsTextColor
        { get; set; }

        [Browsable(false)]
        public string BuyTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BuyTotalsTextColor); }
            set { BuyTotalsTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sell Totals Color", Description = "Sell Totals Text Color.", Order = 3, GroupName = "Colors: Summary Row")]
        public Brush SellTotalsTextColor
        { get; set; }

        [Browsable(false)]
        public string SellTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SellTotalsTextColor); }
            set { SellTotalsTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        // =========== Session Buy/Sell

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Buys Text Color", Description = "Session Buys Text Color.", Order = 1, GroupName = "Colors: Session Buy/Sell")]
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
        [Display(Name = "Session Sells Text Color", Description = "Session Sells Text Color.", Order = 2, GroupName = "Colors: Session Buy/Sell")]
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
        [Display(Name = "Session Buys Histogram Color", Description = "Session Buys Histogram Color.", Order = 3, GroupName = "Colors: Session Buy/Sell")]
        public Brush SessionBuysHistogramColor
        { get; set; }

        [Browsable(false)]
        public string SessionBuysHistogramColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionBuysHistogramColor); }
            set { SessionBuysHistogramColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Sells Histogram Color", Description = "Session Sells Histogram Color.", Order = 4, GroupName = "Colors: Session Buy/Sell")]
        public Brush SessionSellsHistogramColor
        { get; set; }

        [Browsable(false)]
        public string SessionSellsHistogramColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionSellsHistogramColor); }
            set { SessionSellsHistogramColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        // =========== Sliding Window Buy/Sell

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Last Trade Text Color", Description = "Last trade text color.", Order = 1, GroupName = "Colors: Sliding Window Buy/Sell")]
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
        [Display(Name = "Buy Column Color", Description = "Buy column color.", Order = 2, GroupName = "Colors: Sliding Window Buy/Sell")]
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
        [Display(Name = "Sell Column Color", Description = "Sell column color.", Order = 3, GroupName = "Colors: Sliding Window Buy/Sell")]
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
        [Display(Name = "Buy Imbalance Text Color", Description = "Buy Imbalance Text Color.", Order = 4, GroupName = "Colors: Sliding Window Buy/Sell")]
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
        [Display(Name = "Sell Imbalance Text Color", Description = "Sell Imbalance Text Color.", Order = 5, GroupName = "Colors: Sliding Window Buy/Sell")]
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
        [Display(Name = "Buy (Ice) Column Color", Description = "Buy (Ice) column color.", Order = 6, GroupName = "Colors: Sliding Window Buy/Sell")]
        public Brush BIceColumnColor
        { get; set; }

        [Browsable(false)]
        public string BIceColumnColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BIceColumnColor); }
            set { BIceColumnColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sell (Ice) Column Color", Description = "Sell (Ice) column color.", Order = 7, GroupName = "Colors: Sliding Window Buy/Sell")]
        public Brush SIceColumnColor
        { get; set; }

        [Browsable(false)]
        public string SIceColumnColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SIceColumnColor); }
            set { SIceColumnColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        // =========== Volume

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Volume Histogram Color", Description = "Session Volume Histogram Color.", Order = 1, GroupName = "Colors: Volume")]
        public Brush SessionVolumeHistogramColor
        { get; set; }

        [Browsable(false)]
        public string SessionVolumeHistogramColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionVolumeHistogramColor); }
            set { SessionVolumeHistogramColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Volume Text Color", Description = "Session Volume Text Color.", Order = 2, GroupName = "Colors: Volume")]
        public Brush SessionVolumeTextColor
        { get; set; }

        [Browsable(false)]
        public string SessionVolumeTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionVolumeTextColor); }
            set { SessionVolumeTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sliding Volume Histogram Color", Description = "Sliding Volume Histogram Color.", Order = 3, GroupName = "Colors: Volume")]
        public Brush SlidingVolumeHistogramColor
        { get; set; }

        [Browsable(false)]
        public string SlidingVolumeHistogramColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SlidingVolumeHistogramColor); }
            set { SlidingVolumeHistogramColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sliding Volume Text Color", Description = "Sliding Volume Text Color.", Order = 4, GroupName = "Colors: Volume")]
        public Brush SlidingVolumeTextColor
        { get; set; }

        [Browsable(false)]
        public string SlidingVolumeTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SlidingVolumeTextColor); }
            set { SlidingVolumeTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        // =========== Bid/Ask

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Bid Column Color", Description = "Bid column color.", Order = 1, GroupName = "Colors: Bid/Ask")]
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
        [Display(Name = "Ask Column Color", Description = "Ask column color.", Order = 2, GroupName = "Colors: Bid/Ask")]
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
        [Display(Name = "Bid/Ask (Add) Text Color", Description = "Bid/Ask orders added.", Order = 3, GroupName = "Colors: Bid/Ask")]
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
        [Display(Name = "Bid/Ask (Remove) Text Color", Description = "Bid/Ask orders removed.", Order = 4, GroupName = "Colors: Bid/Ask")]
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
        [Display(Name = "Bid Histogram Color", Description = "Bid Histogram color.", Order = 5, GroupName = "Colors: Bid/Ask")]
        public Brush BidHistogramColor
        { get; set; }

        [Browsable(false)]
        public string BidSizeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BidHistogramColor); }
            set { BidHistogramColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Ask Histogram Color", Description = "Ask Histogram color.", Order = 6, GroupName = "Colors: Bid/Ask")]
        public Brush AskHistogramColor
        { get; set; }

        [Browsable(false)]
        public string AskSizeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(AskHistogramColor); }
            set { AskHistogramColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "High Bid Size Marker Color", Description = "High Bid Size Marker.", Order = 7, GroupName = "Colors: Bid/Ask")]
        public Brush LargeBidSizeHighlightColor
        { get; set; }

        [Browsable(false)]
        public string LargeBidSizeMarkerColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(LargeBidSizeHighlightColor); }
            set { LargeBidSizeHighlightColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "High Ask Size Marker Color", Description = "High Ask Size Marker.", Order = 8, GroupName = "Colors: Bid/Ask")]
        public Brush LargeAskSizeHighlightColor
        { get; set; }

        [Browsable(false)]
        public string LargeAskSizeMarkerColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(LargeAskSizeHighlightColor); }
            set { LargeAskSizeHighlightColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }


        // =========== Notes

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Notes Text Color", Description = "Notes Text Color.", Order = 1, GroupName = "Colors: Notes")]
        public Brush NotesColor
        { get; set; }

        [Browsable(false)]
        public string NotesColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(NotesColor); }
            set { NotesColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        


        // =========== Histograms
        #region Histograms
        [NinjaScriptProperty]
        [Display(Name = "Bid / Ask Size Histogram", Description = "Draw bid/ask size Histogram.", Order = 1, GroupName = "Histograms")]
        public bool DisplayBidAskHistogram
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sliding Window Buy / Sell Histogram", Description = "Display Sliding Window Buy/Sell Histogram", Order = 2, GroupName = "Histograms")]
        public bool DisplayBuySellHistogram
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BIce / SIce Histogram (Experimental)", Description = "Display Sliding Window BIce/SIce Histogram", Order = 3, GroupName = "Histograms")]
        public bool DisplayIceHistogram
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Buys / Sells Histogram", Description = "Display the total buys and sells histogram.", Order = 4, GroupName = "Histograms")]
        public bool DisplaySessionBuysSellsHistogram
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Volume Histogram", Description = "Display session volume.", Order = 5, GroupName = "Histograms")]
        public bool DisplaySessionVolumeHistogram
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sliding Volume Histogram", Description = "Display sliding volume.", Order = 6, GroupName = "Histograms")]
        public bool DisplaySlidingVolumeHistogram
        { get; set; }

        #endregion


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
        [Display(Name = "Bid / Ask", Description = "Display the bid/ask.", Order = 1, GroupName = "Bid / Ask Columns")]
        public bool DisplayBidAsk
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Bid / Ask Rows", Description = "Bid/Ask Rows", Order = 2, GroupName = "Bid / Ask Columns")]
        public int BidAskRows
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bid / Ask Changes (Stacking / Pulling)", Description = "Display the changes in bid/ask.", Order = 3, GroupName = "Bid / Ask Columns")]
        public bool DisplayBidAskChange
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Large Bid / Ask Highlight Filter", Description = "Filter to use when highlighting large bid/ask sizes", Order = 4, GroupName = "Bid / Ask Columns")]
        public int LargeBidAskSizeHighlightFilter
        { get; set; }

        // =========== OrderFlow Parameters

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Buy / Sell Sliding Window (Seconds)", Description = "Sliding Window (in seconds) used for displaying trades.", Order = 1, GroupName = "Order Flow Parameters")]
        public int TradeSlidingWindowSeconds
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Sliding Volume Window (Seconds)", Description = "Window (in seconds) used for sliding volume.", Order = 2, GroupName = "Order Flow Parameters")]
        public int SlidingVolumeWindowSeconds
        { get; set; }

        [NinjaScriptProperty]
        [Range(1.5, double.MaxValue)]
        [Display(Name = "Imbalance Factor", Description = "Imbalance Factor", Order = 3, GroupName = "Order Flow Parameters")]
        public double ImbalanceFactor
        { get; set; }

        // =========== OrderFlow Strength Bar Parameters

        [NinjaScriptProperty]
        [Display(Name = "OrderFlow Strength (OFS) Bar", Description = "Display the overall OrderFlow strength bar, including data from imbalances.", Order = 2, GroupName = "OFS Bar")]
        public bool DisplayOrderFlowStrengthBar
        { get; set; }

        [NinjaScriptProperty]
        [Range(51, 100)]
        [Display(Name = "OrderFlow Strength Threshold", Description = "Threshold for strength bar to light up (51-100)", Order = 3, GroupName = "OFS Bar")]
        public int OrderFlowStrengthThreshold
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "OrderFlow Strength Calculation Mode", Description = "OrderFlow strength calculation mode", Order = 4, GroupName = "OFS Bar")]
        public OFSCalculationMode OFSCalcMode
        { get; set; }

        #endregion

    }
}
