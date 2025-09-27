using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Timers;
using System.Windows.Forms.DataVisualization.Charting;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("RsiDivergenceStrategy")]
    public class RsiDivergenceStrategy : BotPanel
    {
        // --==== VOLUME ANSAMBLING ====--
        // ETH L  - (dd5.16)  - 30.73%
        // BNB L  - (dd11.31) - 14.02%
        // BNB S  - (dd5.83)  - 27.20%
        // XRP S  - (dd10.85) - 14.62%
        // DOGE L - (dd11.81) - 13.43%

        // --==== VOLUME DECIMALS ====--
        // BTC - 5  | futures = 3
        // ETH - 4  | futures = 2
        // BNB - 3  | futures = 1
        // SOL - 3  | futures = 1
        // ADA - 1  | futures = 0
        // XRP - 1  | futures = 0
        // DOGE - 0 | futures = 0

        // --==== BINANCE-FUTURES ====--
        //          <coin>usdT
        // MARKET : 0.05% ~BNB~> 0.045%
        // LIMIT  : 0.02% ~BNB~> 0.018%
        // -----------------------------
        //          <coin>usdC
        // MARKET : 0.04% ~BNB~> 0.036%
        // LIMIT  : 0%    ~BNB~> 0%
        // -----------------------------
        // Keep in mind FUNDING! It is better to trade short deals to avoid funding!
        // Otherwise these small fees + funding can be bigger than SPOT fees!
        // SUMMARY :
        // 5m ----> FUTURES
        // 30m and higher TF ----> SPOT
        // --=========================--

        private static readonly bool LOG_STATS = false;

        private static readonly decimal EP_FeePercent = 0.036m; //0.06m;
        private static readonly decimal TP_FeePercent = 0m;
        private static readonly decimal SL_FeePercent = 0.036m; //0.06m;
        private static readonly decimal MAX_BORROW_LEVERAGE_POSSIBLE = 5m;
        private static readonly decimal MIN_TRAIDABLE_VOLUME_USDT = 5m;
        private static readonly decimal MIN_TRAIDABLE_CLEAN_PROFIT_PERCENTS = 0m;
        private static readonly DateTime Jan1St1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly object _rsiDivergenceLock = new object();

        private string STATISTICS_STRING = String.Empty;

        private BotTabSimple _bot;
        private bool _dealInProgress;
        private decimal _balanceMoneyOnDealStart;
        private long _lastLostDivergenceId;
        private RsiDivergenceObject _enteredDivergence;

        private RsiDivergence _rsiDivergence;
        private MovingAverage _ema;

        private StrategyParameterInt RsiLength;
        private StrategyParameterInt RsiMinDivergenceSize;
        private StrategyParameterInt RsiMaxDivergenceSize;
        private StrategyParameterInt RsiNumCandlesLeftToFindPeak;
        private StrategyParameterInt RsiNumCandlesRightToFindPeak;
        private StrategyParameterBool RsiDisplayDivergences;
        private StrategyParameterInt RsiNumberUpDivergencesToStoreAndDisplay;
        private StrategyParameterInt RsiNumberDownDivergencesToStoreAndDisplay;
        private StrategyParameterInt EmaLength;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal Leverage;
        private StrategyParameterDecimal DepoPercent;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal RiskPercent;
        private StrategyParameterBool BorrowingAllowed;
        private StrategyParameterDecimal TP_SizeIn_SLs;
        private StrategyParameterDecimal DivergenceMinStrength;

        private ChartArea RsiArea { get { return _bot.GetChart().ChartAreas["RsiArea"]; } }

        private System.Timers.Timer inactivityTimer;

        public RsiDivergenceStrategy(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in VOLUME (filterType[LOT_SIZE].stepSize)", 0, 0, 4, 1, "Base");
            Leverage = CreateParameter("Leverage", 1m, 1m, 1m, 1m, "Base");
            DepoPercent = CreateParameter("Depo %", 100m, 1m, 100m, 1m, "Base");
            RiskPercent = CreateParameter("Risk %", 1m, 1m, 2m, 0.1m, "Base");
            BorrowingAllowed = CreateParameter("Borrowing allowed", false, "Base");
            TP_SizeIn_SLs = CreateParameter("TP in SLs", 1m, 1m, 3, 0.2m, "Robot parameters");
            DivergenceMinStrength = CreateParameter("Divergence MIN Strength [0.0 - 10.0]", 0.1m, 0.1m, 10m, 0.01m, "Robot parameters");

            RsiLength = CreateParameter("RSI - Length", 14, 14, 20, 1, "Indicator parameters");
            RsiMinDivergenceSize = CreateParameter("RSI - MIN divergence size", 5, 5, 10, 1, "Indicator parameters");
            RsiMaxDivergenceSize = CreateParameter("RSI - MAX divergence size", 60, 60, 80, 1, "Indicator parameters");
            RsiNumCandlesLeftToFindPeak = CreateParameter("RSI - Peak LEFT candles", 5, 3, 5, 1, "Indicator parameters");
            RsiNumCandlesRightToFindPeak = CreateParameter("RSI - Peak RIGHT candles", 5, 3, 5, 1, "Indicator parameters");
            RsiDisplayDivergences = CreateParameter("Display divergences", false, "Indicator parameters");
            RsiNumberUpDivergencesToStoreAndDisplay = CreateParameter("Number UP divergences to store and display", 10, 10, 10, 10, "Indicator parameters");
            RsiNumberDownDivergencesToStoreAndDisplay = CreateParameter("Number DOWN divergences to store and display", 10, 10, 10, 10, "Indicator parameters");
            EmaLength = CreateParameter("EMA - Length", 200, 100, 300, 5, "Indicator parameters");

            _rsiDivergence = new RsiDivergence(name + "RsiDivergence", false);
            _rsiDivergence = (RsiDivergence)_bot.CreateCandleIndicator(_rsiDivergence, "RsiArea");
            _rsiDivergence.Regime = Regime.ValueString;
            _rsiDivergence.Length = RsiLength.ValueInt;
            _rsiDivergence.MinDivergenceSize = RsiMinDivergenceSize.ValueInt;
            _rsiDivergence.MaxDivergenceSize = RsiMaxDivergenceSize.ValueInt;
            _rsiDivergence.NumCandlesLeftToFindPeak = RsiNumCandlesLeftToFindPeak.ValueInt;
            _rsiDivergence.NumCandlesRightToFindPeak = RsiNumCandlesRightToFindPeak.ValueInt;
            _rsiDivergence.DisplayDivergences = RsiDisplayDivergences.ValueBool;
            _rsiDivergence.NumberUpDivergencesToStoreAndDisplay = RsiNumberUpDivergencesToStoreAndDisplay.ValueInt;
            _rsiDivergence.NumberDownDivergencesToStoreAndDisplay = RsiNumberDownDivergencesToStoreAndDisplay.ValueInt;
            _rsiDivergence.Save();

            _ema = new MovingAverage(name + "EMA", false);
            _ema = _bot.CreateIndicator(_ema);
            _ema.Lenght = EmaLength.ValueInt;
            _ema.Save();

            _bot.CandleFinishedEvent += event_CandleClosed_ENTER_AFTER_DIVERGENCE;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
            InitInactivityTimer();
            OlegUtils.LogSeparationLine();
        }

        public override string GetNameStrategyType()
        {
            return "RsiDivergenceStrategy";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void event_ParametersChangedByUser()
        {
            if (_rsiDivergence.Regime != Regime.ValueString ||
                _rsiDivergence.Length != RsiLength.ValueInt ||
                _rsiDivergence.MinDivergenceSize != RsiMinDivergenceSize.ValueInt || 
                _rsiDivergence.MaxDivergenceSize != RsiMaxDivergenceSize.ValueInt || 
                _rsiDivergence.NumCandlesLeftToFindPeak != RsiNumCandlesLeftToFindPeak.ValueInt || 
                _rsiDivergence.NumCandlesRightToFindPeak != RsiNumCandlesRightToFindPeak.ValueInt ||
                _rsiDivergence.DisplayDivergences != RsiDisplayDivergences.ValueBool ||
                _rsiDivergence.NumberUpDivergencesToStoreAndDisplay != RsiNumberUpDivergencesToStoreAndDisplay.ValueInt ||
                _rsiDivergence.NumberDownDivergencesToStoreAndDisplay != RsiNumberDownDivergencesToStoreAndDisplay.ValueInt
            )
            {
                _rsiDivergence.Regime = Regime.ValueString;
                _rsiDivergence.Length = RsiLength.ValueInt;
                _rsiDivergence.MinDivergenceSize = RsiMinDivergenceSize.ValueInt;
                _rsiDivergence.MaxDivergenceSize = RsiMaxDivergenceSize.ValueInt;
                _rsiDivergence.NumCandlesLeftToFindPeak = RsiNumCandlesLeftToFindPeak.ValueInt;
                _rsiDivergence.NumCandlesRightToFindPeak = RsiNumCandlesRightToFindPeak.ValueInt;
                _rsiDivergence.DisplayDivergences = RsiDisplayDivergences.ValueBool;
                _rsiDivergence.NumberUpDivergencesToStoreAndDisplay = RsiNumberUpDivergencesToStoreAndDisplay.ValueInt;
                _rsiDivergence.NumberDownDivergencesToStoreAndDisplay = RsiNumberDownDivergencesToStoreAndDisplay.ValueInt;
                _rsiDivergence.Reload();
                _rsiDivergence.Save();
            }
            if (_ema.Lenght != EmaLength.ValueInt)
            {
                _ema.Lenght = EmaLength.ValueInt;
                _ema.Reload();
                _ema.Save();
            }
        }

        private void event_CandleClosed_ENTER_AFTER_DIVERGENCE(List<Candle> candles)
        {
            if (IsEnoughDataAndEnabledToTrade())
            {
                RsiDivergenceObject lastUpDivergence;
                RsiDivergenceObject lastDownDivergence;

                lock (_rsiDivergenceLock)
                {
                    lastUpDivergence = _rsiDivergence.UpDivergences.LastOrDefault();
                    lastDownDivergence = _rsiDivergence.DownDivergences.LastOrDefault();
                }

                bool lastUpDivergenceIsNotLatestOnChart = lastUpDivergence != null && lastDownDivergence != null && lastDownDivergence.Id > lastUpDivergence.Id;
                if (lastUpDivergenceIsNotLatestOnChart)
                {
                    lastUpDivergence = null;
                }
                bool lastDownDivergenceIsNotLatestOnChart = lastUpDivergence != null && lastDownDivergence != null && lastUpDivergence.Id > lastDownDivergence.Id;
                if (lastDownDivergenceIsNotLatestOnChart)
                {
                    lastDownDivergence = null;
                }
                if (Deal_ABSENT() && IsBotEnabled_LONG())
                {
                    TryEnterOrLooseDivergence(candles, lastUpDivergence);
                }
                if (Deal_ABSENT() && IsBotEnabled_SHORT())
                {
                    TryEnterOrLooseDivergence(candles, lastDownDivergence);
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                _dealInProgress = true;
                p.EP_FeePercent = EP_FeePercent;
                p.SL_FeePercent = SL_FeePercent;
                p.TP_FeePercent = TP_FeePercent;
                p.DealGuid = Guid.NewGuid().ToString();
                Set_SL_Order(p);
                Set_TP_Order(p);
                ResetInactivityTimer();
            }
        }

        private void event_PositionClosed_FINISH_DEAL(Position p)
        {
            if (p.State == PositionStateType.Done)
            {
                CollectClosedDealStatistics(p);
                _dealInProgress = false;
                if (LOG_STATS && !inactivityTimer.Enabled) inactivityTimer.Start();
            }
        }

        private void TryEnterOrLooseDivergence(List<Candle> candles, RsiDivergenceObject lastDivergence)
        {
            bool lastDivergenceActive = lastDivergence != null && lastDivergence.Enabled && lastDivergence.Id != _lastLostDivergenceId;
            bool lastDivergenceWasAlreadyTraded = _enteredDivergence != null && lastDivergence != null && lastDivergence.Id == _enteredDivergence.Id;
            RsiDivergenceObject activeDivergence = lastDivergenceActive && !lastDivergenceWasAlreadyTraded ? lastDivergence : null;
            if (activeDivergence != null)
            {
                Candle curCandle = candles.Last();
                decimal currentPrice = curCandle.Close;
                bool isLong = activeDivergence.Type == DivergenceType.UP;
                bool lossLevelBreached = isLong ? curCandle.Low < activeDivergence.LossLevel : curCandle.High > activeDivergence.LossLevel;
                decimal expectedCleanProfitPercents = CalculateExpectedCleanProfitSizePercents(currentPrice, activeDivergence);
                bool profitTooSmall = expectedCleanProfitPercents < MIN_TRAIDABLE_CLEAN_PROFIT_PERCENTS;
                bool divergenceLost = lossLevelBreached || profitTooSmall;
                bool entryLevelCrossed = isLong ? curCandle.Close > activeDivergence.EntryLevel : curCandle.Close < activeDivergence.EntryLevel;
                bool newCandleClosedAfterDivergence = activeDivergence.IndexTo != candles.Count - 1;
                if (divergenceLost)
                {
                    _lastLostDivergenceId = activeDivergence.Id;
                }
                else if (newCandleClosedAfterDivergence && entryLevelCrossed)
                {
                    bool followTrend = isLong ? currentPrice > _ema.Values.Last() : currentPrice < _ema.Values.Last();
                    bool divergenceAngleBigEnough = activeDivergence.CalculateAngleStrength() >= this.DivergenceMinStrength.ValueDecimal;
                    bool entryCandleVolumeHigherThanDivergenceVolume = candles.Last().Volume > candles[activeDivergence.IndexTo].Volume;
                    bool divergenceVolumeIsRising = candles[activeDivergence.IndexTo].Volume > candles[activeDivergence.IndexFrom].Volume;

                    bool divergenceIsGood = 
                        divergenceAngleBigEnough
                        // && followTrend - WE DON'T USE. IT CAN DO SOME IMPROVEMENT BUT IT REDUCES NUMBER OF ENTRIES TOO MUCH.
                        // && entryCandleVolumeHigherThanDivergenceVolume - WE DON'T USE. IT CAN DO SOME IMPROVEMENT BUT IT REDUCES NUMBER OF ENTRIES TOO MUCH.
                        // && divergenceVolumeIsRising - WE DON'T USE. IT KIND OF A USELESS, ENTRY CANDLE VOLUME IS MUCH BETTER
                        ;
                    if (divergenceIsGood)
                    {
                        SaveBalanceOnDealStart();
                        SaveEnteredDivergence(activeDivergence);
                        decimal entryCoins = CalcEntryVolume(activeDivergence, currentPrice);
                        if (isLong)
                        {
                            _bot.BuyAtLimit(entryCoins, currentPrice); // NOTE : it must be MARKET but to keep price expected in back testing we made it be LIMIT
                        }
                        else
                        {
                            _bot.SellAtLimit(entryCoins, currentPrice); // NOTE : it must be MARKET but to keep price expected in back testing we made it be LIMIT
                        }
                    }
                    else
                    {
                        _lastLostDivergenceId = activeDivergence.Id;
                    }
                }
            }
        }

        private void CollectClosedDealStatistics(Position p)
        {
            if (LOG_STATS)
            {
                decimal dealProfit = CalcPositionCleanProfitMoney(p.Direction, p.MaxVolume, p.EntryPrice, p.ClosePrice);
                decimal profitPercentFromDepo = dealProfit * 100 / _balanceMoneyOnDealStart;
                if (String.IsNullOrEmpty(STATISTICS_STRING))
                {
                    STATISTICS_STRING = "\n --=== " + _bot.TabName + " ===--";
                }

                string timestamp = ToMillis(p.TimeCreate).ToString();
                while (STATISTICS_STRING.Contains(timestamp))
                {
                    timestamp = (ToMillis(p.TimeCreate) + 1).ToString();
                }
                string percent = String.Format("{0}", TruncateDecimal(profitPercentFromDepo, 2)).Replace(",", ".");

                STATISTICS_STRING += String.Format("\n{0},{1}", timestamp, percent);
            }
        }

        private void InitInactivityTimer()
        {
            if (LOG_STATS)
            {
                inactivityTimer = new System.Timers.Timer(10000);
                inactivityTimer.Elapsed += inactivityTimer_OnInactivity;
                inactivityTimer.AutoReset = false;
            }
        }

        private void ResetInactivityTimer()
        {
            if (LOG_STATS)
            {
                inactivityTimer.Stop();
                inactivityTimer.Start();
            }
        }

        private void inactivityTimer_OnInactivity(object sender, ElapsedEventArgs e)
        {
            Console.WriteLine(STATISTICS_STRING);
        }

        private bool Deal_ABSENT()
        {
            return !_dealInProgress && HasEnoughMoney();
        }

        // --------------------------------------------------------------------------------------------------------------------- old[2.0] : 207.98% - 15.23% ===> 13.66
        private static readonly List<decimal> RISK_PORTIONS_1 = new List<decimal>() { 1.0m, 1.5m, 2.0m, 2.3m, 2.6m, 3.0m }; // 1[1.0-3.0] : 133.58% - 13.66% ===>  9.77
        private static readonly List<decimal> RISK_PORTIONS_2 = new List<decimal>() { 1.2m, 1.6m, 2.0m, 2.2m, 2.4m, 2.6m }; // 2[1.2-2.6] : 145.40% - 13.86% ===> 10.49
        private static readonly List<decimal> RISK_PORTIONS = new List<decimal>() { 1.6m, 2.0m, 2.5m, 2.8m, 3.0m, 3.5m };   // 3[1.6-3.5] : 212.83% - 17.68% ===> 11.91
        private static readonly Dictionary<string, List<decimal>> DIVS_STRENGTH_BUCKETS = new Dictionary<string, List<decimal>>()
        {
            { "ETHLong30mtab0",  new List<decimal>() { 1.61m, 2.09m, 2.95m, 3.80m, 4.48m } },
            { "BNBLong30mtab0",  new List<decimal>() { 0.54m, 0.99m, 1.78m, 2.94m, 3.86m } },
            { "BNBShort30mtab0", new List<decimal>() { 1.07m, 1.54m, 2.30m, 3.46m, 4.29m } },
            { "ADAShort30mtab0", new List<decimal>() { 0.92m, 1.39m, 2.03m, 2.90m, 3.60m } },
            { "XRPShort30mtab0", new List<decimal>() { 0.48m, 0.85m, 1.61m, 2.54m, 3.63m } }
        };

        private decimal CalcEntryVolume(RsiDivergenceObject entryDivergence, decimal currentPrice)
        {
            Side direction = entryDivergence.Type == DivergenceType.UP ? Side.Buy : Side.Sell;
            decimal EP = NormalizePrice(currentPrice);
            decimal SL = Calc_SL_Price(entryDivergence);
            decimal allowedPercentFromDepo = DepoPercent.ValueDecimal;
            allowedPercentFromDepo *= Leverage.ValueDecimal;
            decimal availableEntryMoney = allowedPercentFromDepo * _balanceMoneyOnDealStart / 100;
            decimal riskPercent = RiskPercent.ValueDecimal;

            // -----------------------------------------------------------------
            bool dynamicRisksingEnabled = false;
            if (dynamicRisksingEnabled)
            {
                riskPercent = RISK_PORTIONS.Last();
                decimal divergenceStrength = entryDivergence.CalculateAngleStrength();
                List<decimal> strengthBuckets = DIVS_STRENGTH_BUCKETS[_bot.TabName];
                for (int i = 0; i < strengthBuckets.Count; i++)
                {
                    if (divergenceStrength <= strengthBuckets[i])
                    {
                        riskPercent = RISK_PORTIONS[i];
                        break;
                    }
                }
            }
            // -----------------------------------------------------------------

            decimal moneyCanLose = availableEntryMoney * riskPercent / 100;
            decimal entryCoinsByRisk = CalcCoinsVolumeToTakeWantedProfit(direction, EP, SL, -moneyCanLose);
            decimal coinsOnHands = ConvertMoneyToCoins(availableEntryMoney, EP);
            decimal maxPossibleVolumeCoins = coinsOnHands * MAX_BORROW_LEVERAGE_POSSIBLE;
            entryCoinsByRisk = entryCoinsByRisk > maxPossibleVolumeCoins ? maxPossibleVolumeCoins : entryCoinsByRisk;
            decimal entryCoins = ChooseCoinsVolume(BorrowingAllowed.ValueBool, entryCoinsByRisk, coinsOnHands);

            // OPTIONAL params (can be used for logging only)
            // decimal TP = Calc_TP_Price(EP);
            // decimal entryMoney = ConvertCoinsBackToMoney(entryCoins, EP);
            // decimal entryMoneyInPercents = entryMoney * 100 / availableEntryMoney;
            // decimal leverageUsed = entryCoins / coinsOnHands;
            // decimal leverageRequired = entryCoinsByRisk / coinsOnHands;
            // decimal expectedLossFromDepoInMoney = CalcPositionCleanProfitMoney(direction, entryCoins, EP, SL);
            // decimal expectedLossFromDepoInPercents = expectedLossFromDepoInMoney * 100 / availableEntryMoney;
            // decimal expectedProfitFromDepoInMoney = CalcPositionCleanProfitMoney(direction, entryCoins, EP, TP);
            // decimal expectedProfitFromDepoInPercents = expectedProfitFromDepoInMoney * 100 / availableEntryMoney;

            return entryCoins;
        }

        private void Set_TP_Order(Position p)
        {
            decimal TP = Calc_TP_Price(p.EntryPrice);
            _bot.CloseAtProfit(p, TP, TP);
        }

        private void Set_SL_Order(Position p)
        {
            decimal SL = Calc_SL_Price();
            _bot.CloseAtStop(p, SL, SL);
        }

        private bool HasEnoughMoney()
        {
            decimal volumeMoney = _bot.Portfolio.ValueCurrent;
            bool bigEnough = volumeMoney >= MIN_TRAIDABLE_VOLUME_USDT;
            if (!bigEnough)
            {
                Console.WriteLine("Can't perform entry. Entry volume {0}$ is too small. Min volume = {1}$", 
                    TruncateMoney(volumeMoney), TruncateMoney(MIN_TRAIDABLE_VOLUME_USDT));
            }
            return bigEnough;
        }

        private decimal CalculateExpectedCleanProfitSizePercents(decimal EP, RsiDivergenceObject entryDivergence)
        {
            decimal feeInPercents = _bot.ComissionValue;
            decimal SL = Calc_SL_Price(entryDivergence);
            decimal SL_SizePercents = entryDivergence.Type == DivergenceType.UP ? 100 - (SL * 100 / EP) : (SL * 100 / EP) - 100;
            decimal TP_SizePercents = SL_SizePercents * TP_SizeIn_SLs.ValueDecimal;
            return TP_SizePercents - (feeInPercents * 2);
        }

        private decimal Calc_SL_Price()
        {
            return Calc_SL_Price(_enteredDivergence);
        }

        private decimal Calc_SL_Price(RsiDivergenceObject signalDivergence)
        {
            decimal priceStep = _bot.Securiti.PriceStep;
            Side direction = signalDivergence.Type == DivergenceType.UP ? Side.Buy : Side.Sell;
            return NormalizePrice(direction == Side.Buy ? signalDivergence.LossLevel - priceStep : signalDivergence.LossLevel + priceStep);
        }

        private decimal Calc_TP_Price(decimal EP)
        {
            return Calc_TP_Price(EP, Calc_SL_Price());
        }

        private decimal Calc_TP_Price(decimal EP, decimal SL)
        {
            Side direction = _enteredDivergence.Type == DivergenceType.UP ? Side.Buy : Side.Sell;
            decimal TP_Size = Math.Abs(EP - SL) * TP_SizeIn_SLs.ValueDecimal;
            return NormalizePrice(direction == Side.Buy ? EP + TP_Size : EP - TP_Size);
        }

        public decimal CalcCoinsVolumeToTakeWantedProfit(Side side, decimal EP, decimal TP, decimal wantedCleanProfit)
        {
            decimal feeInPercents = _bot.ComissionValue;
            decimal coinsVolume = side == Side.Buy ?
                CalcCoinsVolumeToTakeWantedProfit_LONG(EP, TP, wantedCleanProfit, feeInPercents) :
                CalcCoinsVolumeToTakeWantedProfit_SHORT(EP, TP, wantedCleanProfit, feeInPercents);
            return TruncateDecimal(coinsVolume, VolumeDecimals.ValueInt);
        }

        private decimal CalcCoinsVolumeToTakeWantedProfit_LONG(decimal EP, decimal TP, decimal wantedCleanProfit, decimal feeInPercents)
        {
            return (100 * wantedCleanProfit) / (100 * (TP - EP) - feeInPercents * (EP + TP));
        }

        private decimal CalcCoinsVolumeToTakeWantedProfit_SHORT(decimal EP, decimal TP, decimal wantedCleanProfit, decimal feeInPercents)
        {
            return (100 * wantedCleanProfit) / (100 * (EP - TP) - feeInPercents * (EP + TP));
        }

        private decimal CalcPositionCleanProfitMoney(Side positionDirection, decimal volumeCoins, decimal entryPrice, decimal closePrice)
        {
            decimal moneyIn = volumeCoins * entryPrice;
            decimal moneyOut = volumeCoins * closePrice;
            decimal profitMoney = positionDirection == Side.Buy ? moneyOut - moneyIn : moneyIn - moneyOut;
            decimal feeIn = moneyIn * EP_FeePercent / 100;
            decimal feeOut = moneyOut * (profitMoney > 0 ? TP_FeePercent : SL_FeePercent) / 100;
            decimal feeMoney = feeIn + feeOut;
            return profitMoney - feeMoney;
        }

        private decimal ConvertMoneyToCoins(decimal money, decimal price)
        {
            decimal moneyNeededForFee = money / 100 * _bot.ComissionValue;
            decimal moneyLeftForCoins = money - moneyNeededForFee;
            decimal coinsVolumeDirty = moneyLeftForCoins / price;
            return TruncateDecimal(coinsVolumeDirty, VolumeDecimals.ValueInt);
        }

        private decimal ConvertCoinsBackToMoney(decimal coins, decimal price)
        {
            bool entryButNotExitAction = true;
            decimal feePercents = _bot.ComissionValue;
            decimal moneySpentForCoins = coins * price;
            decimal moneySpentForFee = moneySpentForCoins * feePercents / 100;
            return entryButNotExitAction ? moneySpentForCoins + moneySpentForFee : moneySpentForCoins - moneySpentForFee;
        }

        private decimal ChooseCoinsVolume(bool canBorrow, decimal entryCoinsByRisk, decimal coinsOnHands)
        {
            if (entryCoinsByRisk <= coinsOnHands)
            {
                return entryCoinsByRisk;
            }
            else
            {
                return canBorrow ? entryCoinsByRisk : coinsOnHands;
            }
        }

        private void SaveEnteredDivergence(RsiDivergenceObject enteredDivergence)
        {
            _enteredDivergence = enteredDivergence;
        }

        private void SaveBalanceOnDealStart()
        {
            _balanceMoneyOnDealStart = _bot.Portfolio.ValueCurrent;
        }

        private bool IsEnoughDataAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool enoughCandlesForRsi = candlesCount > RsiLength.ValueInt;
            bool enoughCandlesForEma = candlesCount > EmaLength.ValueInt;
            return IsBotEnabled() && enoughCandlesForRsi && enoughCandlesForEma;
        }

        private bool IsBotEnabled()
        {
            return IsBotEnabled_LONG() || IsBotEnabled_SHORT();
        }

        private bool IsBotEnabled_LONG()
        {
            return Regime.ValueString == "On" || Regime.ValueString == "OnlyLong";
        }

        private bool IsBotEnabled_SHORT()
        {
            return Regime.ValueString == "On" || Regime.ValueString == "OnlyShort";
        }

        private decimal NormalizePrice(decimal price)
        {
            return price - price % _bot.Securiti.PriceStep;
        }

        private decimal TruncateMoney(decimal money)
        {
            return TruncateDecimal(money, 2);
        }

        private decimal TruncateDecimal(decimal decimalNumber, int decimalDigitsCount)
        {
            if (decimalDigitsCount < 0)
            {
                ThrowException("DecimalDigitsCount cannot be less than zero");
            }
            string decimalNumberAsText = decimalNumber.ToString();
            int decimalPointIndex = decimalNumberAsText.IndexOf(Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            if (decimalPointIndex < 0)
            {
                return decimalNumber;
            }
            int newLength = decimalPointIndex + decimalDigitsCount + 1;
            while (newLength > decimalNumberAsText.Length)
            {
                newLength--;
            }
            return Convert.ToDecimal(decimalNumberAsText.Substring(0, newLength).TrimEnd('0'));
        }

        private void ThrowException(string messageTemplate, params object[] messageArgs)
        {
            string message = "ERROR : " + String.Format(messageTemplate, messageArgs);
            Console.WriteLine(message);
            throw new Exception(message);
        }

        private long ToMillis(DateTime date)
        {
            return (long)(date - Jan1St1970).TotalMilliseconds;
        }
    }
}
