using OsEngine.Charts.CandleChart.Elements;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms.DataVisualization.Charting;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("RsiDivergenceStrategy")]
    public class RsiDivergenceStrategy : BotPanel
    {
        private static readonly decimal MIN_TRAIDABLE_VOLUME_USDT = 5;

        private BotTabSimple _bot;
        private bool _dealInProgress;
        private Dictionary<string, LineHorisontal> _longEntryLines;
        private Dictionary<string, LineHorisontal> _shortEntryLines;
        private LineHorisontal RsiUpLine;
        private LineHorisontal RsiDownLine;
        private decimal _lastUsedDivergenceStart;
        private decimal _lastUsedDivergenceEnd;
        private decimal _balanceMoneyOnDealStart;

        private RsiDivergence _rsiDivergence;

        private StrategyParameterInt RsiLength;
        private StrategyParameterInt RsiMinDivergenceSize;
        private StrategyParameterInt RsiMaxDivergenceSize;
        private StrategyParameterInt RsiNumCandlesLeftToFindPeak;
        private StrategyParameterInt RsiNumCandlesRightToFindPeak;

        private StrategyParameterString Regime;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal RiskPercent;
        private StrategyParameterBool LeverageAllowed;

        private ChartArea RsiArea { get { return _bot.GetChart().ChartAreas["RsiArea"]; } }
        private bool HasEntryOrdersSet { get { return _bot.PositionOpenerToStopsAll.Count > 0; } }

        public RsiDivergenceStrategy(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _longEntryLines = new Dictionary<string, LineHorisontal>();
            _shortEntryLines = new Dictionary<string, LineHorisontal>();

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in VOLUME", 0, 0, 4, 1, "Base");
            RiskPercent = CreateParameter("Risk %", 1m, 1m, 2m, 0.1m, "Base");
            LeverageAllowed = CreateParameter("Leverage allowed", false, "Base");

            RsiLength = CreateParameter("RSI - Length", 14, 14, 20, 1, "Indicator parameters");
            RsiMinDivergenceSize = CreateParameter("RSI - MIN divergence size", 5, 5, 10, 1, "Indicator parameters");
            RsiMaxDivergenceSize = CreateParameter("RSI - MAX divergence size", 60, 60, 80, 1, "Indicator parameters");
            RsiNumCandlesLeftToFindPeak = CreateParameter("RSI - Peak LEFT candles", 5, 3, 5, 1, "Indicator parameters");
            RsiNumCandlesRightToFindPeak = CreateParameter("RSI - Peak RIGHT candles", 5, 3, 5, 1, "Indicator parameters");

            RsiUpLine = new LineHorisontal("RsiUpline", "RsiArea", false) { Color = ColorTranslator.FromHtml("#9144b0"), Value = 70, TimeEnd = DateTime.Now };
            RsiDownLine = new LineHorisontal("RsiDownline", "RsiArea", false) { Color = ColorTranslator.FromHtml("#9144b0"), Value = 30, TimeEnd = DateTime.Now };
            _bot.SetChartElement(RsiUpLine);
            _bot.SetChartElement(RsiDownLine);

            _rsiDivergence = new RsiDivergence(name + "RsiDivergence", false);
            _rsiDivergence = (RsiDivergence)_bot.CreateCandleIndicator(_rsiDivergence, "RsiArea");
            _rsiDivergence.Length = RsiLength.ValueInt;
            _rsiDivergence.MinDivergenceSize = RsiMinDivergenceSize.ValueInt;
            _rsiDivergence.MaxDivergenceSize = RsiMaxDivergenceSize.ValueInt;
            _rsiDivergence.NumCandlesLeftToFindPeak = RsiNumCandlesLeftToFindPeak.ValueInt;
            _rsiDivergence.NumCandlesRightToFindPeak = RsiNumCandlesRightToFindPeak.ValueInt;
            _rsiDivergence.Save();

            _bot.CandleFinishedEvent += event_CandleClosed_DIVERGENCE_FOUND;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_CONTINUE_OR_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
            OlegUtils.LogSeparationLine();
        }

        public override string GetNameStrategyType()
        {
            return "RsiDivergenceStrategy";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void event_ParametersChangedByUser()
        {
            if (_rsiDivergence.Length != RsiLength.ValueInt || 
                _rsiDivergence.MinDivergenceSize != RsiMinDivergenceSize.ValueInt || 
                _rsiDivergence.MaxDivergenceSize != RsiMaxDivergenceSize.ValueInt || 
                _rsiDivergence.NumCandlesLeftToFindPeak != RsiNumCandlesLeftToFindPeak.ValueInt || 
                _rsiDivergence.NumCandlesRightToFindPeak != RsiNumCandlesRightToFindPeak.ValueInt
            )
            {
                _rsiDivergence.Length = RsiLength.ValueInt;
                _rsiDivergence.MinDivergenceSize = RsiMinDivergenceSize.ValueInt;
                _rsiDivergence.MaxDivergenceSize = RsiMaxDivergenceSize.ValueInt;
                _rsiDivergence.NumCandlesLeftToFindPeak = RsiNumCandlesLeftToFindPeak.ValueInt;
                _rsiDivergence.NumCandlesRightToFindPeak = RsiNumCandlesRightToFindPeak.ValueInt;
                _rsiDivergence.Reload();
                _rsiDivergence.Save();
            }
        }

        private void event_CandleClosed_DIVERGENCE_FOUND(List<Candle> candles)
        {
            if (IsEnoughDataAndEnabledToTrade())
            {
                RefreshChartHorizontalLines(candles);

                if (IsBotAboutToEnterNewDeal())
                {
                    Cancel_EP_IfDivergenceLost(candles.Last());
                }

                if (FoundNewDivergence() && Deal_ABSENT())
                {
                    SaveDivergence();
                    SaveBalanceOnDealStart();
                    Set_EP_Order();
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                _dealInProgress = true;
                p.DealGuid = Guid.NewGuid().ToString();

                Cancel_EP_Orders_OPPOSITE_DIRECTION(p);
                Cancel_EP_Order(p.SignalTypeOpen);

                Set_SL_Order(p);
                Set_TP_Order(p);

                OlegUtils.Log("New Deal time OPEN = {0}", p.TimeOpen);
            }
        }

        private void event_PositionClosed_CONTINUE_OR_FINISH_DEAL(Position p)
        {
            if (p.State == PositionStateType.Done)
            {
                LogDealStatistics(p);
                _dealInProgress = false;
                _longEntryLines = new Dictionary<string, LineHorisontal>();
                _shortEntryLines = new Dictionary<string, LineHorisontal>();
            }
        }

        private void LogDealStatistics(Position p)
        {
            int days = (p.TimeClose - p.TimeOpen).Days;
            decimal dealProfit = CalcPositionCleanProfitMoney(p.Direction, p.MaxVolume, p.EntryPrice, p.ClosePrice);
            OlegUtils.Log("Profit = {0}{1}$. days = {2}", dealProfit > 0 ? "+" : String.Empty, TruncateMoney(dealProfit), days);
        }

        private bool IsBotAboutToEnterNewDeal()
        {
            return HasEntryOrdersSet;
        }

        private bool FoundNewDivergence()
        {
            // TODO : check conditions
            // return !HasEntryOrdersSet && _rsiDivergence.Values.Last() > 0;
            return false;
        }

        private bool Deal_ABSENT()
        {
            return !_dealInProgress && HasEnoughMoney();
        }

        private void SaveDivergence()
        {
            // TODO : implement
            // _lastUsedDivergenceStart = _rsiDivergence.ValuesUp.Last();
            // _lastUsedDivergenceEnd = _rsiDivergence.ValuesDown.Last();
        }

        private void Set_EP_Order()
        {
            if (IsBotEnabled_LONG())
            {
                Set_EP_Order(Side.Buy);
            }
            if (IsBotEnabled_SHORT())
            {
                Set_EP_Order(Side.Sell);
            }
        }

        private void Set_EP_Order(Side side)
        {
            decimal availableEntryMoney = _balanceMoneyOnDealStart;

            decimal EP = Calc_EP_Price(side);
            decimal SL = Calc_SL_Price(side, EP);
            decimal TP = Calc_TP_Price(side, EP);

            decimal moneyCanLose = availableEntryMoney * RiskPercent.ValueDecimal / 100;
            decimal entryCoinsByRisk = CalcCoinsVolumeToTakeWantedProfit(side, EP, SL, -moneyCanLose);
            decimal coinsOnHands = ConvertMoneyToCoins(availableEntryMoney, EP);
            decimal entryCoins = ChooseCoinsVolume(LeverageAllowed.ValueBool, entryCoinsByRisk, coinsOnHands);
            
            // OPTIONAL params (can be used for logging only)
            decimal entryMoney = ConvertCoinsBackToMoney(entryCoins, EP);
            decimal entryMoneyInPercents = entryMoney * 100 / availableEntryMoney;
            decimal leverageUsed = entryCoins / coinsOnHands;
            decimal leverageRequired = entryCoinsByRisk / coinsOnHands;
            decimal expectedLossFromDepoInMoney = CalcPositionCleanProfitMoney(side, entryCoins, EP, SL);
            decimal expectedLossFromDepoInPercents = expectedLossFromDepoInMoney * 100 / availableEntryMoney;
            decimal expectedProfitFromDepoInMoney = CalcPositionCleanProfitMoney(side, entryCoins, EP, TP);
            decimal expectedProfitFromDepoInPercents = expectedProfitFromDepoInMoney * 100 / availableEntryMoney;

            // ************ OLD WAY ************
            // entryCoins = coinsOnHands;
            // ************ OLD WAY ************

            string entryGuid = Guid.NewGuid().ToString();
            if (side == Side.Buy)
            {
                _bot.BuyAtStop(entryCoins, EP, EP, StopActivateType.HigherOrEqual, 100000, entryGuid);
                _longEntryLines.Add(entryGuid, CreateEntryLineOnChart_LONG(entryGuid, EP));
            }
            else
            {
                _bot.SellAtStop(entryCoins, EP, EP, StopActivateType.LowerOrEqyal, 100000, entryGuid);
                _shortEntryLines.Add(entryGuid, CreateEntryLineOnChart_SHORT(entryGuid, EP));
            }
        }

        private void Set_TP_Order(Position p)
        {
            decimal TP_price = Calc_TP_Price(p);
            _bot.CloseAtProfit(p, TP_price, TP_price);
        }

        private void Set_SL_Order(Position p)
        {
            decimal SL_price = Calc_SL_Price(p);
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private void Cancel_EP_IfDivergenceLost(Candle closedCandle)
        {
            bool longOnly = Regime.ValueString == "OnlyLong";
            bool shortOnly = Regime.ValueString == "OnlyShort";
            bool priceWentOutFromDivergenceDown = false; // TODO : implement it, prototype code: closedCandle.Close < _lastUsedDivergenceEnd;
            bool priceWentOutFromDivergenceUp = false; // TODO : implement it, prototype code: closedCandle.Close > _lastUsedDivergenceStart;
            if ((longOnly && priceWentOutFromDivergenceDown) || (shortOnly && priceWentOutFromDivergenceUp)) // TODO : check condition
            {
                Cancel_EP_Orders();
            }
        }

        private void Cancel_EP_Orders()
        {
            Cancel_EP_Orders_LONG();
            Cancel_EP_Orders_SHORT();
        }

        private void Cancel_EP_Orders_OPPOSITE_DIRECTION(Position p)
        {
            if (p.Direction == Side.Buy)
            {
                Cancel_EP_Orders_SHORT();
            }
            else
            {
                Cancel_EP_Orders_LONG();
            }
        }

        private void Cancel_EP_Orders_LONG()
        {
            for (int i = _longEntryLines.Count - 1; i >= 0; i--)
            {
                Cancel_EP_Order(_longEntryLines.Keys.ElementAt(i));
            }
        }

        private void Cancel_EP_Orders_SHORT()
        {
            for (int i = _shortEntryLines.Count - 1; i >= 0; i--)
            {
                Cancel_EP_Order(_shortEntryLines.Keys.ElementAt(i));
            }
        }

        private void Cancel_EP_Order(string entryGuid)
        {
            _bot.BuyAtStopCancel(entryGuid);
            _bot.SellAtStopCancel(entryGuid);
            DeleteEntryLine(entryGuid);
        }

        private bool HasEnoughMoney()
        {
            decimal volumeMoney = _bot.Portfolio.ValueCurrent;
            bool bigEnough = volumeMoney >= MIN_TRAIDABLE_VOLUME_USDT;
            if (!bigEnough)
            {
                OlegUtils.Log("Can't perform entry. Entry volume {0}$ is too small. Min volume = {1}$", 
                    TruncateMoney(volumeMoney), TruncateMoney(MIN_TRAIDABLE_VOLUME_USDT));
            }
            return bigEnough;
        }

        private decimal Calc_EP_Price(Side side)
        {
            // TODO : re-implement this logic
            decimal divergenceStart = _lastUsedDivergenceStart;
            decimal divergenceEnd = _lastUsedDivergenceEnd;
            return side == Side.Buy ? divergenceStart : divergenceEnd;
        }

        private decimal Calc_TP_Price(Position p)
        {
            return Calc_TP_Price(p.Direction, p.EntryPrice);
        }

        private decimal Calc_TP_Price(Side side, decimal entryPrice)
        {
            // TODO : re-implement this logic
            decimal divergenceStart = _lastUsedDivergenceStart;
            decimal divergenceEnd = _lastUsedDivergenceEnd;
            decimal takeProfitSize = 0;
            return side == Side.Buy ? entryPrice + takeProfitSize : entryPrice - takeProfitSize;
        }

        private decimal Calc_SL_Price(Position p)
        {
            return Calc_SL_Price(p.Direction, p.EntryPrice);
        }

        private decimal Calc_SL_Price(Side side, decimal entryPrice)
        {
            // TODO : re-implement this logic
            decimal divergenceStart = _lastUsedDivergenceStart;
            decimal divergenceEnd = _lastUsedDivergenceEnd;
            decimal stopLossSize = 0;
            return side == Side.Buy ? entryPrice - stopLossSize : entryPrice + stopLossSize;
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
            decimal feeIn = moneyIn * _bot.ComissionValue / 100;
            decimal feeOut = moneyOut * _bot.ComissionValue / 100;
            decimal feeMoney = feeIn + feeOut;
            return profitMoney - feeMoney;
        }

        private LineHorisontal CreateEntryLineOnChart_LONG(string guid, decimal entryPrice)
        {
            return CreateLineOnChart("LONG entry line " + guid, "LONG entry", entryPrice);
        }

        private LineHorisontal CreateEntryLineOnChart_SHORT(string guid, decimal entryPrice)
        {
            return CreateLineOnChart("SHORT entry line " + guid, "SHORT entry", entryPrice);
        }

        private LineHorisontal CreateLineOnChart(string lineName, string lineLabel, decimal linePriceLevel)
        {
            LineHorisontal line = new LineHorisontal(lineName, "Prime", false);
            line.Value = linePriceLevel;
            line.TimeStart = _bot.CandlesAll[0].TimeStart;
            line.TimeEnd = _bot.CandlesAll[_bot.CandlesAll.Count - 1].TimeStart;
            line.Color = Color.White;
            line.Label = lineLabel;
            _bot.SetChartElement(line);
            return line;
        }

        private void DeleteEntryLine(string guid)
        {
            if (_longEntryLines.ContainsKey(guid))
            {
                _bot.DeleteChartElement(_longEntryLines[guid]);
                _longEntryLines.Remove(guid);
            }
            if (_shortEntryLines.ContainsKey(guid))
            {
                _bot.DeleteChartElement(_shortEntryLines[guid]);
                _shortEntryLines.Remove(guid);
            }
        }

        private void RefreshChartHorizontalLines(List<Candle> candles)
        {
            foreach (string longEntryLineGuid in _longEntryLines.Keys)
            {
                _longEntryLines[longEntryLineGuid].TimeEnd = candles[candles.Count - 1].TimeStart;
                _longEntryLines[longEntryLineGuid].Refresh();
            }
            foreach (string shortEntryLineGuid in _shortEntryLines.Keys)
            {
                _shortEntryLines[shortEntryLineGuid].TimeEnd = candles[candles.Count - 1].TimeStart;
                _shortEntryLines[shortEntryLineGuid].Refresh();
            }
            RsiUpLine.Refresh();
            RsiDownLine.Refresh();
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

        private void SaveBalanceOnDealStart()
        {
            _balanceMoneyOnDealStart = _bot.Portfolio.ValueCurrent;
        }

        private bool IsEnoughDataAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool enoughCandlesForRsi = candlesCount > RsiLength.ValueInt;
            return IsBotEnabled() && enoughCandlesForRsi;
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
            OlegUtils.Log(message);
            throw new Exception(message);
        }
    }
}
