using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("RecoveryZone")]
    public class RecoveryZone : BotPanel
    {
        private static readonly string RECOVERY_MODE_BOTH_DIRECTIONS = "BOTH DIRECTIONS";
        private static readonly string RECOVERY_MODE_LOSS_DIRECTION_ONLY = "LOSS DIRECTION ONLY";
        private static readonly string RECOVERY_MODE_NONE = "NONE";

        private BotTabSimple _bot;
        private Side _dealDirection;
        private TradingState _state;
        private PriceLocation _priceLocation;
        private decimal _zoneUp;
        private decimal _zoneDown;
        private decimal _balanceOnStart;
        private decimal _squeezeSize;
        private int _dealAttemptsCounter;

        private event Action<PriceLocation, PriceLocation> RecoveryZoneCrossedEvent;

        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterDecimal BollingerDeviation;
        private StrategyParameterInt BollingerSqueezeLength;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal VolumeMultiplier;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal MinVolumeUSDT;

        private StrategyParameterString RecoveryMode;
        private StrategyParameterBool PositionResultsLoggingEnabled;
        private StrategyParameterDecimal RiskZoneInSqueezes;
        private StrategyParameterDecimal ProfitInSqueezes;

        public RecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _state = TradingState.FREE;
            _dealDirection = Side.None;
            _priceLocation = PriceLocation.UNDEFINED;
            _dealAttemptsCounter = 0;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in Volume", 0, 0, 4, 1, "Base");
            VolumeMultiplier = CreateParameter("Volume multiplier", 0.5m, 0.25m, 0.5m, 0.05m, "Base");
            MinVolumeUSDT = CreateParameter("Min Volume USDT", 7m, 7m, 7m, 1m, "Base");
            BollingerLength = CreateParameter("Length BOLLINGER", 20, 10, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("Bollinger deviation", 2m, 1m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezeLength = CreateParameter("Length BOLLINGER SQUEEZE", 130, 100, 600, 5, "Robot parameters");
            PositionResultsLoggingEnabled = CreateParameter("Log position results", false, "Base");
            RiskZoneInSqueezes = CreateParameter("RiskZone in SQUEEZEs", 2.9m, 0.1m, 10, 0.1m, "Base");
            ProfitInSqueezes = CreateParameter("Profit in SQUEEZEs", 2.8m, 0.1m, 10, 0.1m, "Base");
            RecoveryMode = CreateParameter("Recovery mode", RECOVERY_MODE_BOTH_DIRECTIONS, 
                new[] { RECOVERY_MODE_BOTH_DIRECTIONS, RECOVERY_MODE_LOSS_DIRECTION_ONLY, RECOVERY_MODE_NONE }, "Base");

            _bollingerWithSqueeze = new BollingerWithSqueeze(name + "BollingerWithSqueeze", false);
            _bollingerWithSqueeze = (BollingerWithSqueeze)_bot.CreateCandleIndicator(_bollingerWithSqueeze, "Prime");
            _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
            _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
            _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezeLength.ValueInt;
            _bollingerWithSqueeze.Save();

            _bot.CandleFinishedEvent += event_CandleClosed_SQUEEZE_FOUND;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            this.RecoveryZoneCrossedEvent += event_RecoveryZoneCrossed_SET_ORDERS;
            event_ParametersChangedByUser();
        }

        public override string GetNameStrategyType()
        {
            return "RecoveryZone";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void event_ParametersChangedByUser()
        {
            if (_bollingerWithSqueeze.Lenght != BollingerLength.ValueInt ||
                _bollingerWithSqueeze.Deviation != BollingerDeviation.ValueDecimal ||
                _bollingerWithSqueeze.SqueezePeriod != BollingerSqueezeLength.ValueInt)
            {
                _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
                _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
                _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezeLength.ValueInt;
                _bollingerWithSqueeze.Reload();
                _bollingerWithSqueeze.Save();
            }

            if (RecoveryMode.ValueString == RECOVERY_MODE_LOSS_DIRECTION_ONLY)
            {
                _bot.CandleUpdateEvent += event_CandleUpdated_CROSS_ZONE;
            }
            else
            {
                _bot.CandleUpdateEvent -= event_CandleUpdated_CROSS_ZONE;
            }
        }

        private void event_CandleClosed_SQUEEZE_FOUND(List<Candle> candles)
        {
            if (IsEnoughDataAndEnabledToTrade())
            {
                bool freeState = _state == TradingState.FREE;
                bool noPositions = _bot.PositionsOpenAll.Count == 0;
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (freeState && noPositions && lastCandleHasSqueeze)
                {
                    _state = TradingState.SQUEEZE_FOUND;
                    _balanceOnStart = _bot.Portfolio.ValueCurrent;
                    _squeezeSize = _bollingerWithSqueeze.ValuesUp.Last() - _bollingerWithSqueeze.ValuesDown.Last();

                    _zoneUp = _bollingerWithSqueeze.ValuesUp.Last();
                    _zoneDown = _bollingerWithSqueeze.ValuesDown.Last();
                    Set_EN_Order_LONG();
                    Set_EN_Order_SHORT();
                }
            }
        }

        private void event_CandleUpdated_CROSS_ZONE(List<Candle> candles)
        {
            bool hasCandles = candles != null && candles.Count > 0;
            bool dealInProgress = _state == TradingState.LONG_ENTERED || _state == TradingState.SHORT_ENTERED;

            if (hasCandles && dealInProgress)
            {
                decimal currentPrice = candles.Last().Close;
                PriceLocation oldPriceLocation = _priceLocation;

                if (currentPrice > _zoneUp && _priceLocation != PriceLocation.ABOVE_ZONE)
                {
                    _priceLocation = PriceLocation.ABOVE_ZONE;
                }
                else if (currentPrice < _zoneUp && currentPrice > _zoneDown && _priceLocation != PriceLocation.IN_ZONE)
                {
                    _priceLocation = PriceLocation.IN_ZONE;
                }
                else if (currentPrice < _zoneDown && _priceLocation != PriceLocation.UNDER_ZONE)
                {
                    _priceLocation = PriceLocation.UNDER_ZONE;
                }

                if (oldPriceLocation != _priceLocation && RecoveryZoneCrossedEvent != null)
                {
                    RecoveryZoneCrossedEvent(oldPriceLocation, _priceLocation);
                }
            }
        }

        private void event_RecoveryZoneCrossed_SET_ORDERS(PriceLocation oldPriceLocation, PriceLocation newPriceLocation)
        {
            bool needToSetOrdersOnRecoveryZoneCross = RecoveryMode.ValueString == RECOVERY_MODE_LOSS_DIRECTION_ONLY;
            if (needToSetOrdersOnRecoveryZoneCross)
            {
                bool longDeal = _dealDirection == Side.Buy;
                bool shortDeal = _dealDirection == Side.Sell;
                bool dealInProgress = longDeal || shortDeal;
                bool crossedRecoveryZoneInside = newPriceLocation == PriceLocation.IN_ZONE;

                if (dealInProgress && crossedRecoveryZoneInside)
                {
                    bool crossedRecoveryZoneFromTop = oldPriceLocation == PriceLocation.ABOVE_ZONE;
                    bool crossedRecoveryZoneFromBottom = oldPriceLocation == PriceLocation.UNDER_ZONE;

                    if (longDeal && crossedRecoveryZoneFromTop)
                    {
                        bool shortEntryOrderAlreadySet = _bot.PositionOpenerToStopsAll != null && 
                            _bot.PositionOpenerToStopsAll.Any(order => order.Side == Side.Sell);
                        if (!shortEntryOrderAlreadySet)
                        {
                            Set_EN_Order_SHORT();
                        }
                    }
                    else if (shortDeal && crossedRecoveryZoneFromBottom)
                    {
                        bool longEntryOrderAlreadySet = _bot.PositionOpenerToStopsAll != null && 
                            _bot.PositionOpenerToStopsAll.Any(order => order.Side == Side.Buy);
                        if (!longEntryOrderAlreadySet)
                        {
                            Set_EN_Order_LONG();
                        }
                    }
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                _dealAttemptsCounter++;

                bool recoveryNeeded = 
                    RecoveryMode.ValueString == RECOVERY_MODE_BOTH_DIRECTIONS || 
                    (RecoveryMode.ValueString == RECOVERY_MODE_LOSS_DIRECTION_ONLY && IsFirstAttempt());

                if (p.Direction == Side.Buy)
                {
                    if (IsFirstAttempt())
                    {
                        _dealDirection = Side.Buy;
                        _bot.SellAtStopCancel();
                        _zoneDown = p.EntryPrice - _squeezeSize * RiskZoneInSqueezes.ValueDecimal;
                    }                    

                    Set_TP_Order_LONG(p);
                    Set_SL_Order_LONG(p);
                    if (recoveryNeeded)
                    {
                        Set_EN_Order_SHORT();
                    }

                    _state = TradingState.LONG_ENTERED;
                }

                if (p.Direction == Side.Sell)
                {
                    if (IsFirstAttempt())
                    {
                        _dealDirection = Side.Sell;
                        _bot.BuyAtStopCancel();
                        _zoneUp = p.EntryPrice + _squeezeSize * RiskZoneInSqueezes.ValueDecimal;
                    }

                    Set_TP_Order_SHORT(p);
                    Set_SL_Order_SHORT(p);
                    if (recoveryNeeded)
                    {
                        Set_EN_Order_LONG();
                    }

                    _state = TradingState.SHORT_ENTERED;                    
                }
            }
        }

        private void event_PositionClosed_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
                if (_bot.PositionsOpenAll.Count == 0)
                {
                    _bot.BuyAtStopCancel();
                    _bot.SellAtStopCancel();
                    _state = TradingState.FREE;
                    _dealDirection = Side.None;
                    _priceLocation = PriceLocation.UNDEFINED;
                    _dealAttemptsCounter = 0;
                }

                if (PositionResultsLoggingEnabled.ValueBool)
                {
                    LogPositionResults(p);
                }
            }
        }

        private void Set_TP_Order_LONG(Position p)
        {
            decimal TP_price = Calc_TP_Price_LONG(p.EntryPrice);
            _bot.CloseAtProfit(p, TP_price, p.OpenVolume);
        }

        private void Set_TP_Order_SHORT(Position p)
        {
            decimal TP_price = Calc_TP_Price_SHORT(p.EntryPrice);
            _bot.CloseAtProfit(p, TP_price, p.OpenVolume);
        }

        private void Set_SL_Order_LONG(Position p)
        {
            decimal SL_price = RecoveryMode.ValueString == RECOVERY_MODE_NONE ? _zoneDown : Calc_TP_Price_SHORT(_zoneDown);
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private void Set_SL_Order_SHORT(Position p)
        {
            decimal SL_price = RecoveryMode.ValueString == RECOVERY_MODE_NONE ? _zoneUp : Calc_TP_Price_LONG(_zoneUp);
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private void Set_EN_Order_LONG()
        {
            decimal buyCoinsVolume = GetNewAttemptCoinsVolume(Side.Buy);
            if (buyCoinsVolume > 0)
            {
                _bot.BuyAtStop(buyCoinsVolume, _zoneUp, _zoneUp, StopActivateType.HigherOrEqual, 100);
            }
        }

        private void Set_EN_Order_SHORT()
        {
            decimal sellCoinsVolume = GetNewAttemptCoinsVolume(Side.Sell);
            if (sellCoinsVolume > 0)
            {
                _bot.SellAtStop(sellCoinsVolume, _zoneDown, _zoneDown, StopActivateType.LowerOrEqyal, 100);
            }
        }

        private decimal Calc_TP_Price_LONG(decimal entryPrice)
        {
            decimal TP_size = _squeezeSize * ProfitInSqueezes.ValueDecimal;
            return entryPrice + TP_size;
        }

        private decimal Calc_TP_Price_SHORT(decimal entryPrice)
        {
            decimal TP_size = _squeezeSize * ProfitInSqueezes.ValueDecimal;
            return entryPrice - TP_size;
        }

        private bool IsFirstAttempt()
        {
            return _bot.PositionsOpenAll.Count == 1;
        }

        private bool IsEnoughDataAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool robotEnabled = Regime.ValueString == "On";
            bool enoughCandlesForBollinger = candlesCount > BollingerLength.ValueInt;
            bool enoughCandlesForBollingerSqueeze = candlesCount > BollingerSqueezeLength.ValueInt;
            return robotEnabled && enoughCandlesForBollinger && enoughCandlesForBollingerSqueeze;
        }

        private decimal GetNewAttemptCoinsVolume(Side side)
        {
            decimal coinsVolume = 0;
            if (HasMoneyForNewAttempt() && !IsMoneyForNewAttemptTooSmall())
            {
                decimal moneyAvailableForNewAttempt = GetNewAttemptMoneyNeeded();
                decimal moneyNeededForFee = moneyAvailableForNewAttempt / 100 * _bot.ComissionValue;
                decimal moneyLeftForCoins = moneyAvailableForNewAttempt - moneyNeededForFee;
                decimal price = side == Side.Buy ? TabsSimple[0].PriceBestAsk : TabsSimple[0].PriceBestBid;
                decimal volume = moneyLeftForCoins / price;
                coinsVolume = Math.Round(volume, VolumeDecimals.ValueInt);
            }
            return coinsVolume;
        }

        private bool IsMoneyForNewAttemptTooSmall()
        {
            return GetNewAttemptMoneyNeeded() < MinVolumeUSDT.ValueDecimal;
        }

        private bool HasMoneyForNewAttempt()
        {
            return GetFreeMoney() > GetNewAttemptMoneyNeeded();
        }

        private decimal GetNewAttemptMoneyNeeded()
        {
            bool hasOpenPositionsAlready = _bot.PositionsLast != null && _bot.PositionsLast.State == PositionStateType.Open;
            decimal moneyToCalcFrom = hasOpenPositionsAlready ? GetLastPositionEntryMoney() : _bot.Portfolio.ValueCurrent;
            return moneyToCalcFrom * VolumeMultiplier.ValueDecimal;
        }

        private decimal GetLastPositionEntryMoney()
        {
            return _bot.PositionsLast != null ? GetPositionEntryMoney(_bot.PositionsLast) : 0;
        }

        private decimal GetFreeMoney()
        {
            decimal freeMoney = _balanceOnStart;
            _bot.PositionsOpenAll.ForEach(p => { freeMoney -= GetPositionEntryMoney(p); });
            return freeMoney;
        }

        private decimal GetPositionEntryMoney(Position p)
        {
            return p.OpenVolume * p.EntryPrice + p.CommissionTotal();
        }

        private static void LogPositionResults(Position p)
        {
            OlegUtils.Log("\n#{0} {1}\n\tvolume = {2}\n\topen price = {3}\n\tclose price = {4}\n\tfee = {5}% = {6}$" +
                "\n\tprice change = {7}% = {8}$\n\tdepo profit = {9}% = {10}$\n\tDEPO: {11}$ ===> {12}$",
                p.Number,
                p.Direction,
                p.OpenOrders.First().Volume,
                p.EntryPrice,
                p.ClosePrice,
                p.ComissionValue,
                Math.Round(p.CommissionTotal(), 2),
                Math.Round(p.ProfitOperationPersent, 2),
                Math.Round(p.ProfitOperationPunkt, 4),
                Math.Round(p.ProfitPortfolioPersent, 2),
                Math.Round(p.ProfitPortfolioPunkt, 4),
                Math.Round(p.PortfolioValueOnOpenPosition, 2),
                Math.Round(p.PortfolioValueOnOpenPosition + p.ProfitPortfolioPunkt, 2)
                );
        }

        enum TradingState
        {
            FREE,
            SQUEEZE_FOUND,
            LONG_ENTERED,
            SHORT_ENTERED
        }

        enum PriceLocation
        {
            ABOVE_ZONE,
            IN_ZONE,
            UNDER_ZONE,
            UNDEFINED
        }
    }
}
