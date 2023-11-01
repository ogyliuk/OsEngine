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

namespace OsEngine.Robots.Oleg.Good
{
    /***************** RESULTS: ****************
      ADA 5m Candle-set
      01.12.2020-27.08.2023
      Надо иметь 2 бота с отдельными настроками для SHORT и LONG.  
      Profit на портфеле из 2 ботов - 501.81%
      Макс. просадка - 47%

      OnlyLong
      EP amount - 2
      EP padding in 'sq' - 0.9
      SL in 'sq' - 5
      Bollinger length - 20
      Bollinger deviation - 2
      Bollinger squeeze period - 160

      OnlyShort
      EP amount - 4
      EP padding in 'sq' - 0.7
      SL in 'sq' - 2.4
      Bollinger length - 20
      Bollinger deviation - 2
      Bollinger squeeze period - 200

      Стратегия трендовая и может приносит хороший профит в моменты тренда. 
      Однако на боковике появляются большие длительные просадки (до 50% по несколько месяцев)
      
      Были сделаны попытки выровнять эквити и уменьшить просадки благодаря 
      добавлению других ботов в портфель:
      а) боты этой же стратегии но оптимизированные на участках где был боковик. НЕУСПЕШНО, 
         потому что такие боты очень сильно сливают во время тренда и итоговая эквити сильно ухудшается.
      b) боты этой же стратегии но оптимизированные не по МАКС. ПРОФИТУ а среднему % на сделку. НЕУСПЕШНО, 
         потому что такие боты все равно сливают в моменты боковика где и есть наши просадки.
      c) боты контр тренда (MyBollingerContrTrend) которые могли бы вытягивать в моменты боковиков. НЕУСПЕШНО, 
         потому что такой бот не всегда прибыльный в боковиках, а так же потому что он работает только для LONG

      Была сделана попытка порверить робастность стратегии через walk-forward оптимизацию на 10 периодах. НЕУСПЕШНО, 
      если OutOfSample - 1 месяц, а InSample - 1 месяц, 1.5 месяца или 2 месяца, то мы зарабатываем лишь в 
      2-3 периодах из 10. Последующий поиск верных размеров InSample к OutOfSample сделан не был.

      ВЫВОД : Стратегия может работать. Но она работает только в трендах. Она дает долгие и глубокие просадки 
      в моменты боковиков, которые психологически будет очень сложно высидеть. Возможно она неплохо будет 
      работать в паре с другой стратегией которая зарабатывает на боковиках и не сильно сливает во время тренда.
    ***************** RESULTS: ****************/
    [Bot("AddToWinnerStrategy")]
    public class AddToWinnerStrategy : BotPanel
    {
        private static readonly decimal MIN_TRAIDABLE_VOLUME_USDT = 5;

        private BotTabSimple _bot;
        private string _dealGuid;
        private List<Position> _dealPositions;
        private Dictionary<string, LineHorisontal> _longEntryLines;
        private Dictionary<string, LineHorisontal> _shortEntryLines;
        private decimal _lastUsedSqueezeUpBand;
        private decimal _lastUsedSqueezeDownBand;
        private decimal _balanceMoneyOnDealStart;

        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterInt BollingerSqueezePeriod;
        private StrategyParameterDecimal BollingerDeviation;

        private StrategyParameterString Regime;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterInt EP_Amount;
        private StrategyParameterDecimal EP_PaddingInSqueezes;
        private StrategyParameterDecimal SL_SizeInSqueezes;

        private bool HasEntryOrdersSet { get { return _bot.PositionOpenerToStopsAll.Count > 0; } }

        // TODO : think about volume:
        // a) use different value to risk same money in each position
        // b) increase volume in each position to cut loses on 1st attempt failures (70% cases)
        public AddToWinnerStrategy(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _dealGuid = String.Empty;
            _dealPositions = new List<Position>();
            _longEntryLines = new Dictionary<string, LineHorisontal>();
            _shortEntryLines = new Dictionary<string, LineHorisontal>();

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in VOLUME", 0, 0, 4, 1, "Base");

            EP_Amount = CreateParameter("EP amount", 3, 2, 3, 1, "Robot parameters");
            EP_PaddingInSqueezes = CreateParameter("EP padding in 'sq'", 0m, 0m, 1m, 0.1m, "Robot parameters");
            SL_SizeInSqueezes = CreateParameter("SL in 'sq'", 2m, 2m, 5m, 0.2m, "Robot parameters");

            BollingerLength = CreateParameter("BOLLINGER - Length", 20, 20, 50, 2, "Indicator parameters");
            BollingerDeviation = CreateParameter("BOLLINGER - Deviation", 2m, 2m, 3m, 0.1m, "Indicator parameters");
            BollingerSqueezePeriod = CreateParameter("BOLLINGER - Squeeze period", 130, 130, 600, 5, "Indicator parameters");

            _bollingerWithSqueeze = new BollingerWithSqueeze(name + "BollingerWithSqueeze", false);
            _bollingerWithSqueeze = (BollingerWithSqueeze)_bot.CreateCandleIndicator(_bollingerWithSqueeze, "Prime");
            _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
            _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
            _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezePeriod.ValueInt;
            _bollingerWithSqueeze.Save();

            _bot.CandleFinishedEvent += event_CandleClosed_SQUEEZE_FOUND;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_CONTINUE_OR_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
            OlegUtils.LogSeparationLine();
        }

        public override string GetNameStrategyType()
        {
            return "AddToWinnerStrategy";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void event_ParametersChangedByUser()
        {
            if (_bollingerWithSqueeze.Lenght != BollingerLength.ValueInt ||
                _bollingerWithSqueeze.Deviation != BollingerDeviation.ValueDecimal ||
                _bollingerWithSqueeze.SqueezePeriod != BollingerSqueezePeriod.ValueInt)
            {
                _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
                _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
                _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezePeriod.ValueInt;
                _bollingerWithSqueeze.Reload();
                _bollingerWithSqueeze.Save();
            }
        }

        private void event_CandleClosed_SQUEEZE_FOUND(List<Candle> candles)
        {
            if (IsEnoughDataAndEnabledToTrade())
            {
                RefreshEntryLines(candles);

                if (IsBotAboutToEnterNewDeal())
                {
                    Cancel_EP_IfSqueezeLost(candles.Last());
                }

                if (FoundNewSqueeze())
                {
                    if (Deal_ABSENT())
                    {
                        SaveSqueeze();
                        SaveBalanceOnDealStart();
                        Set_EP_Order();
                    }
                    else if (Deal_INPROGRESS())
                    {
                        decimal SL_Price_NewSqueeze = Calc_SL_Price_ForNewlyFoundSqueeze();
                        if (IsNewSqueezeOnGoodDistanceFromLastEntry(SL_Price_NewSqueeze))
                        {
                            if (NeedMoreEntries())
                            {
                                SaveSqueeze();
                                Set_EP_Order();
                            }
                            else if (AllEntriesDone() && IsNewSqueezeMoreBenefitialForAllPoses(SL_Price_NewSqueeze))
                            {
                                Trail_SLs_ForAllPositions(SL_Price_NewSqueeze);
                            }
                        }
                    }
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && !_dealPositions.Contains(p) && p.State == PositionStateType.Open)
            {
                bool firstPosition = ProcessNewPosition(p);
                if (firstPosition)
                {
                    Set_SL_Order(p);
                }
                else
                {
                    decimal BE_Price_PreviousPosition = Calc_BE_Price(_dealPositions[_dealPositions.Count - 2]);
                    Trail_SLs_ForAllPositions(BE_Price_PreviousPosition);
                }
            }
        }

        private void event_PositionClosed_CONTINUE_OR_FINISH_DEAL(Position p)
        {
            bool allPositionsClosed = _dealPositions.All(p => p.State == PositionStateType.Done);
            if (allPositionsClosed)
            {
                FinishDeal();
            }
        }

        private bool ProcessNewPosition(Position p)
        {
            bool newDeal = _dealPositions.Count == 0;
            if (newDeal)
            {
                _dealGuid = Guid.NewGuid().ToString();
                Cancel_EP_Orders_OPPOSITE_DIRECTION(p);
                OlegUtils.Log("New Deal time OPEN = {0}", p.TimeOpen);
            }

            _dealPositions.Add(p);
            p.DealGuid = _dealGuid;
            Cancel_EP_Order(p.SignalTypeOpen);

            return newDeal;
        }

        private void FinishDeal()
        {
            if (HasEntryOrdersSet)
            {
                Cancel_EP_Orders();
            }

            LogDealStatistics();

            _dealGuid = String.Empty;
            _longEntryLines = new Dictionary<string, LineHorisontal>();
            _shortEntryLines = new Dictionary<string, LineHorisontal>();
            _dealPositions = new List<Position>();
        }

        private void LogDealStatistics()
        {
            int BE_Closed = 0;
            int LOSS_Closed = 0;
            int PROFIT_Closed = 0;
            decimal dealProfit = 0;
            foreach (Position p in _dealPositions)
            {
                if (TruncateDecimal(Calc_BE_Price(p), 4) == TruncateDecimal(p.ClosePrice, 4))
                {
                    BE_Closed++;
                }
                else
                {
                    if (p.ProfitOperationPunkt > 0)
                    {
                        PROFIT_Closed++;
                    }
                    else
                    {
                        LOSS_Closed++;
                    }
                }
                dealProfit += CalcPositionCleanProfitMoney(p.Direction, p.MaxVolume, p.EntryPrice, p.ClosePrice);
            }

            int days = (_dealPositions.Last().TimeClose - _dealPositions.First().TimeOpen).Days;
            OlegUtils.Log("Profit = {0}{1}$. Poses = {2}/{3}; PR = {4}; LS = {5}; BE = {6}; days = {7}",
                dealProfit > 0 ? "+" : String.Empty, TruncateMoney(dealProfit), _dealPositions.Count, 
                EP_Amount.ValueInt, PROFIT_Closed, LOSS_Closed, BE_Closed, days);
        }

        private Side GetDealDirection()
        {
            if (_dealPositions.Count == 0)
            {
                ThrowException("Can't get deal direction! Deal has no positions!");
            }
            return _dealPositions.First().Direction;
        }

        private bool IsBotAboutToEnterNewDeal()
        {
            return _dealPositions.Count == 0 && HasEntryOrdersSet;
        }

        private bool FoundNewSqueeze()
        {
            return !HasEntryOrdersSet && _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
        }

        private bool Deal_ABSENT()
        {
            return _dealPositions.Count == 0 && HasEnoughMoney();
        }

        private bool Deal_INPROGRESS()
        {
            return _dealPositions.Count > 0;
        }

        private bool NeedMoreEntries()
        {
            return _dealPositions.Count < EP_Amount.ValueInt;
        }

        private bool AllEntriesDone()
        {
            return _dealPositions.Count == EP_Amount.ValueInt;
        }

        private void SaveSqueeze()
        {
            _lastUsedSqueezeUpBand = _bollingerWithSqueeze.ValuesUp.Last();
            _lastUsedSqueezeDownBand = _bollingerWithSqueeze.ValuesDown.Last();
        }

        private void Set_EP_Order()
        {
            if (_dealPositions.Count == 0)
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
            else
            {
                Set_EP_Order(GetDealDirection());
            }
        }

        private void Set_EP_Order(Side side)
        {
            string entryGuid = Guid.NewGuid().ToString();
            decimal entryPrice = Calc_EP_Price(side);
            decimal volumeMoney = _balanceMoneyOnDealStart / EP_Amount.ValueInt;
            decimal volumeCoins = ConvertMoneyToCoins(volumeMoney, entryPrice);
            if (side == Side.Buy)
            {
                _bot.BuyAtStop(volumeCoins, entryPrice, entryPrice, StopActivateType.HigherOrEqual, 100000, entryGuid);
                _longEntryLines.Add(entryGuid, CreateEntryLineOnChart_LONG(entryGuid, entryPrice));
            }
            else
            {
                _bot.SellAtStop(volumeCoins, entryPrice, entryPrice, StopActivateType.LowerOrEqyal, 100000, entryGuid);
                _shortEntryLines.Add(entryGuid, CreateEntryLineOnChart_SHORT(entryGuid, entryPrice));
            }
        }

        private void Set_SL_Order(Position p)
        {
            Set_SL_Order(p, Calc_SL_Price(p));
        }

        private void Set_SL_Order(Position p, decimal SL_price)
        {
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private bool IsNewSqueezeOnGoodDistanceFromLastEntry(decimal SL_Price_NewSqueeze)
        {
            decimal BE_Price_PreviousPosition = Calc_BE_Price(_dealPositions.Last());
            return Is_EXIT_PriceBetterThan(SL_Price_NewSqueeze, BE_Price_PreviousPosition);
        }

        private bool IsNewSqueezeMoreBenefitialForAllPoses(decimal SL_Price_NewSqueeze)
        {
            decimal SL_Price_AllPoses = _dealPositions.Last().StopOrderRedLine;
            return Is_EXIT_PriceBetterThan(SL_Price_NewSqueeze, SL_Price_AllPoses);
        }

        private void Trail_SLs_ForAllPositions(decimal new_SL_Price)
        {
            foreach (Position p in _dealPositions)
            {
                Set_SL_Order(p, new_SL_Price);
            }
        }

        private void Cancel_EP_IfSqueezeLost(Candle closedCandle)
        {
            bool longOnly = Regime.ValueString == "OnlyLong";
            bool shortOnly = Regime.ValueString == "OnlyShort";
            bool priceWentOutFromSqueezeToShort = closedCandle.Low < _lastUsedSqueezeDownBand;
            bool priceWentOutFromSqueezeToLong = closedCandle.High > _lastUsedSqueezeUpBand;
            if ((longOnly && priceWentOutFromSqueezeToShort) || (shortOnly && priceWentOutFromSqueezeToLong))
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
            decimal volumeMoney = _bot.Portfolio.ValueCurrent / EP_Amount.ValueInt;
            bool bigEnough = volumeMoney >= MIN_TRAIDABLE_VOLUME_USDT;
            if (!bigEnough)
            {
                OlegUtils.Log("Can't perform entry. Entry volume {0}$ is too small. Min volume = {1}$", 
                    TruncateMoney(volumeMoney), TruncateMoney(MIN_TRAIDABLE_VOLUME_USDT));
            }
            return bigEnough;
        }

        private decimal Calc_BE_Price(Position p)
        {
            decimal wantedCleanProfitMoney = 0;
            return p.Direction == Side.Buy ?
                Calc_EXIT_Price_LONG_TakeWantedCleanProfit_MONEY(p.MaxVolume, p.EntryPrice, wantedCleanProfitMoney) :
                Calc_EXIT_Price_SHORT_TakeWantedCleanProfit_MONEY(p.MaxVolume, p.EntryPrice, wantedCleanProfitMoney);
        }

        private decimal Calc_EP_Price(Side side)
        {
            return Calc_EP_Price(side, _lastUsedSqueezeUpBand, _lastUsedSqueezeDownBand);
        }

        private decimal Calc_EP_Price(Side side, decimal squeezeUpBand, decimal squeezeDownBand)
        {
            decimal pricePadding = CalcSqueezeSize(squeezeUpBand, squeezeDownBand) * EP_PaddingInSqueezes.ValueDecimal;
            return side == Side.Buy ? squeezeUpBand + pricePadding : squeezeDownBand - pricePadding;
        }

        private decimal Calc_SL_Price(Position p)
        {
            return Calc_SL_Price(p.Direction, _lastUsedSqueezeUpBand, _lastUsedSqueezeDownBand, p.EntryPrice);
        }

        private decimal Calc_SL_Price(Side side, decimal squeezeUpBand, decimal squeezeDownBand, decimal entryPrice)
        {
            decimal stopLossSize = CalcSqueezeSize(squeezeUpBand, squeezeDownBand) * SL_SizeInSqueezes.ValueDecimal;
            return side == Side.Buy ? entryPrice - stopLossSize : entryPrice + stopLossSize;
        }

        private decimal Calc_SL_Price_ForNewlyFoundSqueeze()
        {
            Side dealDirection = GetDealDirection();
            decimal currentSqueezeUpBand = _bollingerWithSqueeze.ValuesUp.Last();
            decimal currentSqueezeDownBand = _bollingerWithSqueeze.ValuesDown.Last();
            decimal potentialEntryPrice = Calc_EP_Price(dealDirection, currentSqueezeUpBand, currentSqueezeDownBand);
            return Calc_SL_Price(dealDirection, currentSqueezeUpBand, currentSqueezeDownBand, potentialEntryPrice);
        }

        private decimal Calc_EXIT_Price_LONG_TakeWantedCleanProfit_MONEY(decimal volumeCoins, decimal entryPrice, decimal wantedCleanProfitMoney)
        {
            // Profit will be a bit SMALLER than expected because of price rouding to SMALLER size
            decimal feeInPercents = _bot.ComissionValue;
            decimal exitPrice = (volumeCoins * entryPrice * (100 + feeInPercents) + 100 * wantedCleanProfitMoney) / (volumeCoins * (100 - feeInPercents));
            return NormalizePrice(exitPrice);
        }

        private decimal Calc_EXIT_Price_SHORT_TakeWantedCleanProfit_MONEY(decimal volumeCoins, decimal entryPrice, decimal wantedCleanProfitMoney)
        {
            // Profit will be a bit BIGGER than expected because of price rouding to SMALLER size
            decimal feeInPercents = _bot.ComissionValue;
            decimal exitPrice = (volumeCoins * entryPrice * (100 - feeInPercents) - 100 * wantedCleanProfitMoney) / (volumeCoins * (100 + feeInPercents));
            return NormalizePrice(exitPrice);
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

        private decimal CalcSqueezeSize(decimal squeezeUpBand, decimal squeezeDownBand)
        {
            decimal squeezeSize = squeezeUpBand - squeezeDownBand;
            return squeezeSize > 0 ? squeezeSize : 0;
        }

        private bool Is_EXIT_PriceBetterThan(decimal exitPriceToBeBetter, decimal exitPriceToBeWorse)
        {
            return GetDealDirection() == Side.Buy ? exitPriceToBeBetter > exitPriceToBeWorse : exitPriceToBeBetter < exitPriceToBeWorse;
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

        private void RefreshEntryLines(List<Candle> candles)
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
        }

        private decimal ConvertMoneyToCoins(decimal money, decimal price)
        {
            decimal moneyNeededForFee = money / 100 * _bot.ComissionValue;
            decimal moneyLeftForCoins = money - moneyNeededForFee;
            decimal coinsVolumeDirty = moneyLeftForCoins / price;
            return TruncateDecimal(coinsVolumeDirty, VolumeDecimals.ValueInt);
        }

        private void SaveBalanceOnDealStart()
        {
            _balanceMoneyOnDealStart = _bot.Portfolio.ValueCurrent;
        }

        private bool IsEnoughDataAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool enoughCandlesForBollinger = candlesCount > BollingerLength.ValueInt;
            bool enoughCandlesForBollingerSqueeze = candlesCount > BollingerSqueezePeriod.ValueInt;
            return IsBotEnabled() && enoughCandlesForBollinger && enoughCandlesForBollingerSqueeze;
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

        private decimal NormalizePrice(decimal price)
        {
            return price - price % _bot.Securiti.PriceStep;
        }
    }
}
