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
        private BotTabSimple _tab;
        private TradingState _state;
        private decimal _balanceAvailable; // TODO : Start to use
        private decimal _moneyFirstEntry;
        private int _attemptNumber;

        private MovingAverage _bollingerSma;
        private Bollinger _bollinger;
        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterDecimal BollingerDeviation;
        private StrategyParameterInt BollingerSqueezeLength;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal VolumeMultiplier;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal MinVolumeUSDT;

        private StrategyParameterDecimal ProfitSizeFromRZ;

        public RecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _state = TradingState.FREE;
            _moneyFirstEntry = 0;
            _attemptNumber = 0;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in Volume", 0, 0, 4, 1, "Base");
            VolumeMultiplier = CreateParameter("Volume multiplier", 0.75m, 0.25m, 1, 0.05m, "Base");
            MinVolumeUSDT = CreateParameter("Min Volume USDT", 5.5m, 5.5m, 5.5m, 1m, "Base");

            BollingerLength = CreateParameter("Length BOLLINGER", 20, 10, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("Bollinger deviation", 2m, 1m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezeLength = CreateParameter("Length BOLLINGER SQUEEZE", 130, 100, 600, 5, "Robot parameters");

            ProfitSizeFromRZ = CreateParameter("Profit size from RZ", 2m, 0.5m, 3, 0.5m, "Base");

            _bollingerSma = new MovingAverage(false);
            _bollingerSma = (MovingAverage)_tab.CreateCandleIndicator(_bollingerSma, "Prime");
            _bollingerSma.TypeCalculationAverage = MovingAverageTypeCalculation.Simple;
            _bollingerSma.Lenght = BollingerLength.ValueInt;
            _bollingerSma.Save();

            _bollinger = new Bollinger(name + "Bollinger", false);
            _bollinger = (Bollinger)_tab.CreateCandleIndicator(_bollinger, "Prime");
            _bollinger.Lenght = BollingerLength.ValueInt;
            _bollinger.Deviation = BollingerDeviation.ValueDecimal;
            _bollinger.Save();

            _bollingerWithSqueeze = new BollingerWithSqueeze(name + "BollingerWithSqueeze", false);
            _bollingerWithSqueeze = (BollingerWithSqueeze)_tab.CreateCandleIndicator(_bollingerWithSqueeze, "Prime");
            _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
            _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
            _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezeLength.ValueInt;
            _bollingerWithSqueeze.Save();

            _tab.CandleFinishedEvent += _tab_CandleFinishedEventHandler_SQUEEZE_FOUND;
            _tab.CandleUpdateEvent += _tab_CandleUpdateEventHandler_SET_NEXT_ORDERS;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpenEventHandler_FILLED_ENTRY_ORDER;
            _tab.PositionClosingSuccesEvent += _tab_PositionCloseEventHandler_FINISH_DEAL;

            ParametrsChangeByUser += ParametersChangeByUserEventHandler;
            ParametersChangeByUserEventHandler();
        }

        private void ParametersChangeByUserEventHandler()
        {
            if (_bollingerSma.Lenght != BollingerLength.ValueInt)
            {
                _bollingerSma.Lenght = BollingerLength.ValueInt;
                _bollingerSma.Reload();
                _bollingerSma.Save();
            }

            if (_bollinger.Lenght != BollingerLength.ValueInt || 
                _bollinger.Deviation != BollingerDeviation.ValueDecimal)
            {
                _bollinger.Lenght = BollingerLength.ValueInt;
                _bollinger.Deviation = BollingerDeviation.ValueDecimal;
                _bollinger.Reload();
                _bollinger.Save();
            }

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
        }

        public override string GetNameStrategyType()
        {
            return "RecoveryZone";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void _tab_CandleFinishedEventHandler_SQUEEZE_FOUND(List<Candle> candles)
        {
            if (IsRobotEnabled())
            {
                bool noPositions = _tab.PositionsOpenAll.Count == 0;
                bool freeState = _state == TradingState.FREE;
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (noPositions && freeState && lastCandleHasSqueeze)
                {
                    _balanceAvailable = _tab.Portfolio.ValueCurrent;
                    _state = TradingState.SQUEEZE_FOUND;
                    decimal semiSqueezeVolatility = (_bollingerWithSqueeze.ValuesUp.Last() - _bollingerWithSqueeze.ValuesDown.Last()) / 2;
                    bool longsEnabled = Regime.ValueString == "On" || Regime.ValueString == "OnlyLong";
                    if (longsEnabled)
                    {
                        decimal longEntryPrice = _bollingerWithSqueeze.ValuesUp.Last() + semiSqueezeVolatility;
                        _tab.BuyAtStop(GetVolume(Side.Buy), longEntryPrice, longEntryPrice, StopActivateType.HigherOrEqual, 100);
                    }
                    bool shortsEnabled = Regime.ValueString == "On" || Regime.ValueString == "OnlyShort";
                    if (shortsEnabled)
                    {
                        decimal shortEntryPrice = _bollingerWithSqueeze.ValuesDown.Last() - semiSqueezeVolatility;
                        _tab.SellAtStop(GetVolume(Side.Sell), shortEntryPrice, shortEntryPrice, StopActivateType.LowerOrEqyal, 100);
                    }
                }
            }
        }

        private void _tab_PositionOpenEventHandler_FILLED_ENTRY_ORDER(Position position)
        {
            if (position != null && position.State == PositionStateType.Open)
            {
                _tab.SellAtStopCancel();
                _tab.BuyAtStopCancel();

                if (position.Direction == Side.Buy)
                {
                    _state = TradingState.LONG_ENTERED;
                }
                if (position.Direction == Side.Sell)
                {
                    _state = TradingState.SHORT_ENTERED;
                }
                _attemptNumber++;
            }
        }

        private void _tab_CandleUpdateEventHandler_SET_NEXT_ORDERS(List<Candle> candles)
        {
            if (IsRobotEnabled() && _tab.PositionsOpenAll.Count > 0)
            {
                if (_state == TradingState.LONG_ENTERED)
                {
                    Position longPosition = _tab.PositionsOpenAll
                        .Where(p => p.State == PositionStateType.Open && p.Direction == Side.Buy).FirstOrDefault();
                    if (longPosition != null)
                    {
                        decimal SL_price = _bollingerSma.Values.Last();
                        decimal SL_size = longPosition.EntryPrice - SL_price;
                        decimal TP_price = longPosition.EntryPrice + SL_size * ProfitSizeFromRZ.ValueDecimal;
                        _tab.CloseAtProfit(longPosition, TP_price, longPosition.OpenVolume);
                        _tab.CloseAtStop(longPosition, SL_price, SL_price);
                        _state = TradingState.LONG_TARGETS_SET;
                    }
                }

                if (_state == TradingState.SHORT_ENTERED)
                {
                    Position shortPosition = _tab.PositionsOpenAll
                        .Where(p => p.State == PositionStateType.Open && p.Direction == Side.Sell).FirstOrDefault();
                    if (shortPosition != null)
                    {
                        decimal SL_price = _bollingerSma.Values.Last();
                        decimal SL_size = SL_price - shortPosition.EntryPrice;
                        decimal TP_price = shortPosition.EntryPrice - SL_size * ProfitSizeFromRZ.ValueDecimal;
                        _tab.CloseAtProfit(shortPosition, TP_price, shortPosition.OpenVolume);
                        _tab.CloseAtStop(shortPosition, SL_price, SL_price);
                        _state = TradingState.SHORT_TARGETS_SET;
                    }
                }
            }
        }

        private void _tab_PositionCloseEventHandler_FINISH_DEAL(Position position)
        {
            if (position != null && position.State == PositionStateType.Done)
            {
                _attemptNumber = 0;
                _moneyFirstEntry = 0;
                _state = TradingState.FREE;
                OlegUtils.Log("\n#{0} {1}\n\tvolume = {2}\n\topen price = {3}\n\tclose price = {4}\n\tfee = {5}% = {6}$" + 
                    "\n\tprice change = {7}% = {8}$\n\tdepo profit = {9}% = {10}$\n\tDEPO: {11}$ ===> {12}$", 
                    position.Number,
                    position.Direction,
                    position.OpenOrders.First().Volume,
                    position.EntryPrice,
                    position.ClosePrice,
                    position.ComissionValue,
                    Math.Round(position.CommissionTotal(), 2),
                    Math.Round(position.ProfitOperationPersent, 2),
                    Math.Round(position.ProfitOperationPunkt, 4),
                    Math.Round(position.ProfitPortfolioPersent, 2),
                    Math.Round(position.ProfitPortfolioPunkt, 4),
                    Math.Round(position.PortfolioValueOnOpenPosition, 2),
                    Math.Round(position.PortfolioValueOnOpenPosition + position.ProfitPortfolioPunkt, 2)
                    );
            }
        }

        private bool IsRobotEnabled()
        {
            if (Regime.ValueString == "Off" ||
                _tab.CandlesAll == null ||
                BollingerLength.ValueInt >= _tab.CandlesAll.Count ||
                BollingerSqueezeLength.ValueInt >= _tab.CandlesAll.Count)
            {
                return false;
            }
            return true;
        }

        private decimal GetVolume(Side side)
        {
            if (_attemptNumber == 0)
            {
                _moneyFirstEntry = _tab.Portfolio.ValueCurrent;
            }            
            decimal moneyNewAttemptNoFee = GetMoneyForNewAttempt(_attemptNumber + 1);
            decimal moneyNewAttemptAfterFee = moneyNewAttemptNoFee - moneyNewAttemptNoFee / 100 * _tab.ComissionValue;
            decimal price = side == Side.Buy ? TabsSimple[0].PriceBestAsk : TabsSimple[0].PriceBestBid;
            decimal volume = moneyNewAttemptAfterFee / price;
            return Math.Round(volume, VolumeDecimals.ValueInt);
        }

        private bool IsNextAttemptPossible()
        {
            // TODO : work with depo size and MinVolumeUSDT here
            return true;
        }

        private decimal GetMoneyForNewAttempt(int attemptNumber)
        {
            decimal moneyCurrentEntry = _moneyFirstEntry;
            for (int i = 0; i < attemptNumber - 1; i++)
            {
                moneyCurrentEntry = moneyCurrentEntry * VolumeMultiplier.ValueDecimal;
            }
            return moneyCurrentEntry;
        }

        enum TradingState
        {
            FREE,
            SQUEEZE_FOUND,
            LONG_ENTERED,
            SHORT_ENTERED,
            LONG_TARGETS_SET,
            SHORT_TARGETS_SET
        }
    }
}
