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

/***************** RESULTS: ****************
  ADA 5m Candle-set
  01.12.2020-27.08.2023
  OnlyLong
  EP amount = 3
  TP = 2.5 | SL = 2.5
  Common TP = True | Trailing SLs = False

  Профитов в 2 раза меньше чем лоссов : [ P=20% | L=40% | BE=40% ]
  В лоссах 40% это - [ 14%(0 по BE) | 13%(1 по BE) | 11%(2 по BE) ]
  В профитах 20% это - [ 9,5%(1 из 3) | 6,5%(2 из 3) | 3,2%(3 из 3) ]
  
  ВЫВОД : Лоссы происходят в 2 раза чаще, чем профиты. Много EP не помогает, 
    потому что профиты сразу по нескольким EP бывают редко.
***************** RESULTS: ****************/
namespace OsEngine.Robots.Oleg.Good
{
    [Bot("LetProfitGrow")]
    public class LetProfitGrow : BotPanel
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
        private StrategyParameterInt EPs_ZoneSizeFromProfitSizeInPercents;
        private StrategyParameterDecimal EP_PaddingInSqueezes;
        private StrategyParameterDecimal TP_SizeInSqueezes;
        private StrategyParameterDecimal SL_SizeInSqueezes;
        private StrategyParameterBool KeepCommon_TP;
        private StrategyParameterBool UseTrailing_SLs;

        private bool HasEntryOrdersSet { get { return _bot.PositionOpenerToStopsAll.Count > 0; } }

        private decimal LastUsedSqueezeSize
        {
            get
            {
                decimal squeezeSize = _lastUsedSqueezeUpBand - _lastUsedSqueezeDownBand;
                return squeezeSize > 0 ? squeezeSize : 0;
            }
        }

        public LetProfitGrow(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _dealGuid = String.Empty;
            _dealPositions = new List<Position>();
            _longEntryLines = new Dictionary<string, LineHorisontal>();
            _shortEntryLines = new Dictionary<string, LineHorisontal>();

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in VOLUME", 0, 0, 4, 1, "Base");

            EP_Amount = CreateParameter("EP amount", 1, 1, 5, 1, "Robot parameters");
            EPs_ZoneSizeFromProfitSizeInPercents = CreateParameter("EP(s)_ZONE size % from profit size", 50, 20, 100, 10, "Robot parameters");
            EP_PaddingInSqueezes = CreateParameter("EP padding in 'sq'", 0m, 0m, 1m, 0.1m, "Robot parameters");
            TP_SizeInSqueezes = CreateParameter("TP in 'sq'", 2.5m, 2.5m, 5, 0.1m, "Robot parameters");
            SL_SizeInSqueezes = CreateParameter("SL in 'sq'", 2.5m, 2.5m, 5, 0.1m, "Robot parameters");
            KeepCommon_TP = CreateParameter("Common TP", true, "Robot parameters");
            UseTrailing_SLs = CreateParameter("Use trailing SLs", false, "Robot parameters");

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
            _bot.CandleUpdateEvent += event_CandleUpdated_LOOSE_SQUEEZE_OR_TAIL_SL;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_CONTINUE_OR_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
            OlegUtils.LogSeparationLine();            
        }

        public override string GetNameStrategyType()
        {
            return "LetProfitGrow";
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
                    Cancel_EP_OrdersIfSqueezeLost(candles.Last());
                }

                if (IsFreeBotFoundNewSqueeze())
                {
                    SaveBalanceOnDealStart();
                    SaveNewSqueezeInfo();
                    if (IsVolumePortionBigEnough() && Is_EP_DistancesBigEnough() && Is_TP_SizeBigEnough())
                    {
                        Set_EP_Orders();
                    }
                    else
                    {
                        OlegUtils.Log("Ignoring found SQEEZE : IsVolumeValid = {0}; Is_EP_DistancesBigEnough = {1}; && Is_TP_SizeBigEnough = {2}",
                            IsVolumePortionBigEnough(), Is_EP_DistancesBigEnough(), Is_TP_SizeBigEnough());
                    }
                }

                if (IsDealInProgress())
                {
                    Move_SL_ForLastPositionTo_BE_Price_WhenProfitBigEnough(candles.Last());
                }
            }
        }

        private void event_CandleUpdated_LOOSE_SQUEEZE_OR_TAIL_SL(List<Candle> candles)
        {
            if (IsBotAboutToEnterNewDeal())
            {
                Cancel_EP_OrdersIfSqueezeLost(candles.Last());
            }

            if (IsDealInProgress())
            {
                Move_SL_ForLastPositionTo_BE_Price_WhenProfitBigEnough(candles.Last());
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && !_dealPositions.Contains(p) && p.State == PositionStateType.Open)
            {
                bool newDealStarted = InitNewDealIfNeeded(p);
                SavePosition(p);
                Cancel_EP_Order(p.SignalTypeOpen);
                if (newDealStarted)
                {
                    Cancel_EP_Orders_OPPOSITE_DIRECTION(p);
                }
                
                // TODO : what if BE is close or equal to current price and will be immediately triggered?
                // Change validation rules for new squeeze?
                Move_SL_ForPreLastPositionTo_BE_Price();

                Move_SL_ForSomePositionsToBetterPlace();
                Set_SL_Order(p);
                Set_TP_Order(p);
            }
        }

        private void event_PositionClosed_CONTINUE_OR_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
                bool allPositionsClosed = _dealPositions.All(p => p.State == PositionStateType.Done);
                if (allPositionsClosed)
                {
                    FinishDeal();
                }
            }
        }

        private bool InitNewDealIfNeeded(Position p)
        {
            bool newDeal = _dealPositions.Count == 0;
            if (newDeal)
            {
                _dealGuid = Guid.NewGuid().ToString();
                OlegUtils.Log("New Deal time OPEN = {0}", p.TimeOpen);
            }
            return newDeal;
        }

        private void FinishDeal()
        {
            if (HasEntryOrdersSet)
            {
                Cancel_EP_Orders();
            }

            _dealGuid = String.Empty;
            _longEntryLines = new Dictionary<string, LineHorisontal>();
            _shortEntryLines = new Dictionary<string, LineHorisontal>();

            int PROFIT_Closed = 0;
            int BE_Closed = 0;
            int LOSS_Closed = 0;
            foreach (Position p in _dealPositions)
            {
                decimal distanceToBE = Math.Abs(p.ClosePrice - Calc_BE_Price(p));
                decimal distanceToTP = Math.Abs(p.ClosePrice - p.ProfitOrderRedLine);
                decimal distanceToSL = Math.Abs(p.ClosePrice - Calc_SL_Price(p));

                if (distanceToBE < distanceToTP && distanceToBE < distanceToSL)
                {
                    BE_Closed++;
                }
                if (distanceToTP < distanceToBE && distanceToTP < distanceToSL)
                {
                    PROFIT_Closed++;
                }
                if (distanceToSL < distanceToBE && distanceToSL < distanceToTP)
                {
                    LOSS_Closed++;
                }
            }

            int days = (_dealPositions.Last().TimeClose - _dealPositions.First().TimeOpen).Days;
            OlegUtils.Log("Poses = {0}/{1}; PR = {2}; LS = {3}; BE = {4}; days = {5}", 
                _dealPositions.Count, EP_Amount.ValueInt, PROFIT_Closed, LOSS_Closed, BE_Closed, days);
            _dealPositions = new List<Position>();
        }

        private void SavePosition(Position p)
        {
            p.DealGuid = _dealGuid;
            _dealPositions.Add(p);
        }

        private bool IsBotAboutToEnterNewDeal()
        {
            return _dealPositions.Count == 0 && HasEntryOrdersSet;
        }

        private bool IsFreeBotFoundNewSqueeze()
        {
            bool freeState = _dealPositions.Count == 0 && !HasEntryOrdersSet && HasEnoughMoneyToTrade();
            bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
            return freeState && lastCandleHasSqueeze;
        }

        private bool IsDealInProgress()
        {
            return _dealPositions.Count > 0;
        }

        private void SaveNewSqueezeInfo()
        {
            _lastUsedSqueezeUpBand = _bollingerWithSqueeze.ValuesUp.Last();
            _lastUsedSqueezeDownBand = _bollingerWithSqueeze.ValuesDown.Last();
        }

        private void Set_EP_Orders()
        {
            List<decimal> all_EP_MoneyVolumes = Calc_EP_MoneyVolumes();
            List<decimal> all_EP_Prices_LONG = IsTradingEnabled_LONG() ? Calc_EP_Prices(Side.Buy) : new List<decimal>();
            List<decimal> all_EP_Prices_SHORT = IsTradingEnabled_SHORT() ? Calc_EP_Prices(Side.Sell) : new List<decimal>();
            for (int i = 0; i < all_EP_MoneyVolumes.Count; i++)
            {
                if (IsTradingEnabled_LONG())
                {
                    Set_EP_Order_LONG(all_EP_Prices_LONG[i], all_EP_MoneyVolumes[i]);
                }
                if (IsTradingEnabled_SHORT())
                {
                    Set_EP_Order_SHORT(all_EP_Prices_SHORT[i], all_EP_MoneyVolumes[i]);
                }
            }
        }

        private void Set_EP_Order_LONG(decimal entryPrice, decimal volumeMoney)
        {
            Set_EP_Order(Side.Buy, entryPrice, volumeMoney);
        }

        private void Set_EP_Order_SHORT(decimal entryPrice, decimal volumeMoney)
        {
            Set_EP_Order(Side.Sell, entryPrice, volumeMoney);
        }

        private void Set_EP_Order(Side side, decimal entryPrice, decimal volumeMoney)
        {
            string entryGuid = Guid.NewGuid().ToString();
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

        private void Set_TP_Order(Position p)
        {
            decimal EP_ToCalc_TP_From = KeepCommon_TP.ValueBool ? _dealPositions.First().EntryPrice : p.EntryPrice;
            decimal TP_price = Calc_TP_Price(p.Direction, EP_ToCalc_TP_From);
            decimal profit = CalcPositionCleanProfitMoney(p.Direction, p.MaxVolume, p.EntryPrice, TP_price);
            if (profit < 0)
            {
                TP_price = Calc_BE_Price(p);
            }
            _bot.CloseAtProfit(p, TP_price, TP_price);
        }

        private void Set_SL_Order(Position p)
        {
            Set_SL_Order(p, Calc_SL_Price(p));
        }

        private void Set_SL_Order(Position p, decimal SL_price)
        {
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private void Move_SL_ForLastPositionTo_BE_Price_WhenProfitBigEnough(Candle lastClosedCandle)
        {
            bool last_EP_Happened = !HasEntryOrdersSet;
            if (last_EP_Happened)
            {
                Position lastPosition = _dealPositions.Last();
                decimal BE_Price = Calc_BE_Price(lastPosition);
                if (lastPosition.StopOrderRedLine != BE_Price)
                {
                    // TODO : using EPs_Distance canbe too early (new parameter?)
                    decimal triggerSet_SL_at_BE_Level = lastPosition.Direction == Side.Buy ?
                        lastPosition.EntryPrice + Calc_EPs_Distance() :
                        lastPosition.EntryPrice - Calc_EPs_Distance();
                    bool currentProfitBigEnoughToMove_SL_at_BE_Price = lastPosition.Direction == Side.Buy ?
                        lastClosedCandle.High >= triggerSet_SL_at_BE_Level :
                        lastClosedCandle.Low <= triggerSet_SL_at_BE_Level;
                    if (currentProfitBigEnoughToMove_SL_at_BE_Price)
                    {
                        Set_SL_Order(lastPosition, BE_Price);
                    }
                }
            }
        }

        private void Move_SL_ForPreLastPositionTo_BE_Price()
        {
            if (_dealPositions.Count > 1)
            {
                Position preLastPosition = _dealPositions[_dealPositions.Count - 2];
                Set_SL_Order(preLastPosition, Calc_BE_Price(preLastPosition));
            }
        }

        private void Move_SL_ForSomePositionsToBetterPlace()
        {
            if (UseTrailing_SLs.ValueBool)
            {
                List<Position> openPositions = _dealPositions.Where(p => p.State == PositionStateType.Open).ToList();
                if (openPositions.Count > 2)
                {
                    int preLastPositionIndex = openPositions.Count - 2;
                    decimal newBenefitial_SL_Price = openPositions[preLastPositionIndex].EntryPrice;
                    for (int i = 0; i < preLastPositionIndex; i++)
                    {
                        Set_SL_Order(openPositions[i], newBenefitial_SL_Price);
                    }
                }
            }
        }

        private void Cancel_EP_OrdersIfSqueezeLost(Candle closedCandle)
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

        private bool IsVolumePortionBigEnough()
        {
            return Calc_One_EP_MoneyVolume() >= MIN_TRAIDABLE_VOLUME_USDT;
        }

        private bool Is_EP_DistancesBigEnough()
        {
            Side side = Side.Buy;
            List<decimal> all_EP_Prices = Calc_EP_Prices(side);
            if (all_EP_Prices.Count > 1)
            {
                decimal LOWER_Price = all_EP_Prices[0];
                decimal UPPER_Price = all_EP_Prices[1];
                decimal volumeCoins = ConvertMoneyToCoins(Calc_One_EP_MoneyVolume(), LOWER_Price);
                decimal BE_Price = Calc_BE_Price(side, volumeCoins, LOWER_Price);
                return BE_Price - LOWER_Price < UPPER_Price - LOWER_Price;
            }
            return true;
        }

        private bool Is_TP_SizeBigEnough()
        {
            Side side = Side.Buy;
            List<decimal> all_EP_Prices = Calc_EP_Prices(side);
            if (all_EP_Prices.Count > 0)
            {
                decimal EP_Price = all_EP_Prices[0];
                decimal volumeCoins = ConvertMoneyToCoins(Calc_One_EP_MoneyVolume(), EP_Price);
                decimal BE_Price = Calc_BE_Price(side, volumeCoins, EP_Price);
                decimal TP_Price = Calc_TP_Price(side, EP_Price);
                return TP_Price > BE_Price;
            }
            return true;
        }

        private List<decimal> Calc_EP_MoneyVolumes()
        {
            // TODO : ---> play with volumes calculation (non-equal is better?)
            List<decimal> allEntryVolumesInMoney = new List<decimal>();
            decimal volumeMoneyPortion = Calc_One_EP_MoneyVolume();
            for (int i = 0; i < EP_Amount.ValueInt; i++)
            {
                allEntryVolumesInMoney.Add(volumeMoneyPortion);
            }
            return allEntryVolumesInMoney;
        }

        private decimal Calc_One_EP_MoneyVolume()
        {
            return _balanceMoneyOnDealStart / EP_Amount.ValueInt;
        }

        private List<decimal> Calc_EP_Prices(Side side)
        {
            List<decimal> allEntryPrices = new List<decimal>();
            decimal entryPrice = Calc_First_EP_Price(side);
            decimal distanceBetweenEntries = Calc_EPs_Distance();
            for (int i = 0; i < EP_Amount.ValueInt; i++)
            {
                allEntryPrices.Add(entryPrice);
                if (side == Side.Buy)
                {
                    entryPrice += distanceBetweenEntries;
                }
                else
                {
                    entryPrice -= distanceBetweenEntries;
                }
            }
            return allEntryPrices;
        }

        private decimal Calc_EPs_Distance()
        {
            decimal TP_Size = LastUsedSqueezeSize * TP_SizeInSqueezes.ValueDecimal;
            decimal EPs_ZoneSize = TP_Size * EPs_ZoneSizeFromProfitSizeInPercents.ValueInt / 100;
            return EPs_ZoneSize / EP_Amount.ValueInt;
        }

        private decimal Calc_First_EP_Price(Side side)
        {
            decimal pricePadding = LastUsedSqueezeSize * EP_PaddingInSqueezes.ValueDecimal;
            return side == Side.Buy ? _lastUsedSqueezeUpBand + pricePadding : _lastUsedSqueezeDownBand - pricePadding;
        }

        private decimal Calc_TP_Price(Side direction, decimal entryPrice)
        {
            decimal takeProfitSize = LastUsedSqueezeSize * TP_SizeInSqueezes.ValueDecimal;
            return direction == Side.Buy ? entryPrice + takeProfitSize : entryPrice - takeProfitSize;
        }

        private decimal Calc_SL_Price(Position p)
        {
            decimal stopLossSize = LastUsedSqueezeSize * SL_SizeInSqueezes.ValueDecimal;
            stopLossSize = LastUsedSqueezeSize * TP_SizeInSqueezes.ValueDecimal; // TODO : remove later
            return p.Direction == Side.Buy ? p.EntryPrice - stopLossSize : p.EntryPrice + stopLossSize;
        }

        private decimal Calc_BE_Price(Position p)
        {
            return Calc_BE_Price(p.Direction, p.MaxVolume, p.EntryPrice);
        }

        private decimal Calc_BE_Price(Side side, decimal volumeCoins, decimal entryPrice)
        {
            decimal wantedCleanProfitMoney = 0;
            return side == Side.Buy ?
                Calc_EXIT_Price_LONG_TakeWantedCleanProfit_MONEY(volumeCoins, entryPrice, wantedCleanProfitMoney) :
                Calc_EXIT_Price_SHORT_TakeWantedCleanProfit_MONEY(volumeCoins, entryPrice, wantedCleanProfitMoney);
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

        private bool HasEnoughMoneyToTrade()
        {
            return _bot.Portfolio.ValueCurrent >= MIN_TRAIDABLE_VOLUME_USDT;
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
            return IsRobotEnabled() && enoughCandlesForBollinger && enoughCandlesForBollingerSqueeze;
        }

        private bool IsRobotEnabled()
        {
            return IsTradingEnabled_LONG() || IsTradingEnabled_SHORT();
        }

        private bool IsTradingEnabled_LONG()
        {
            return Regime.ValueString == "On" || Regime.ValueString == "OnlyLong";
        }

        private bool IsTradingEnabled_SHORT()
        {
            return Regime.ValueString == "On" || Regime.ValueString == "OnlyShort";
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
