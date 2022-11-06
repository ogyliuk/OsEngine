﻿using OsEngine.Charts.CandleChart.Indicators;
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
        private TradingState _state;
        private BotTabSimple _tab;

        private MovingAverage _bollingerSma;
        private Bollinger _bollinger;
        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterDecimal BollingerDeviation;
        private StrategyParameterInt BollingerSqueezeLength;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal VolumeFirstEntry;
        private StrategyParameterInt VolumeDecimals;

        private StrategyParameterDecimal ProfitSizeFromRZ;

        public RecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _state = TradingState.FREE;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
            VolumeFirstEntry = CreateParameter("Volume %", 100, 1m, 100, 1, "Base");

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

            _tab.CandleFinishedEvent += _tab_CandleFinishedEventHandler_SET_ENTRY_ORDERS;
            _tab.CandleUpdateEvent += _tab_CandleUpdateEventHandler_SET_TARGETS;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpenEventHandler_SET_ENTERED_STATE;
            _tab.PositionClosingSuccesEvent += _tab_PositionCloseEventHandler_SET_FREE_STATE;

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

        private void _tab_CandleFinishedEventHandler_SET_ENTRY_ORDERS(List<Candle> candles)
        {
            if (IsRobotEnabled())
            {
                bool noPositions = _tab.PositionsOpenAll.Count == 0;
                bool freeState = _state == TradingState.FREE;
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (noPositions && freeState && lastCandleHasSqueeze)
                {
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

        private void _tab_CandleUpdateEventHandler_SET_TARGETS(List<Candle> candles)
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

        private void _tab_PositionOpenEventHandler_SET_ENTERED_STATE(Position position)
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
            }
        }

        private void _tab_PositionCloseEventHandler_SET_FREE_STATE(Position position)
        {
            if (position != null && position.State == PositionStateType.Done)
            {
                _state = TradingState.FREE;
                OlegUtils.Log("\n#{0} {1}\n\topen price = {2}\n\tclose price = {3}\n\tvolume = {4}\n\tfee = {5}% = {6}$" + 
                    "\n\tpos profit = {7}% = {8}$\n\tdepo profit = {9}% = {10}$\n\tDEPO: {11}$ ===> {12}$", 
                    position.Number,
                    position.Direction,
                    position.EntryPrice,
                    position.ClosePrice,
                    position.OpenOrders.First().Volume,
                    position.ComissionValue,
                    Math.Round(position.CommissionTotal(), 2),
                    Math.Round(position.ProfitOperationPersent, 2),
                    Math.Round(position.ProfitOperationPunkt, 4),
                    Math.Round(position.ProfitPortfolioPersent, 2),
                    Math.Round(position.ProfitPortfolioPunkt, 4),
                    Math.Round(position.PortfolioValueOnOpenPosition, 2),
                    Math.Round(_tab.Portfolio.ValueCurrent, 2)
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
            // TODO : take into account fee (we need to left some money for it)
            decimal volumePercent = VolumeFirstEntry.ValueDecimal;
            decimal depoBalance = _tab.Portfolio.ValueCurrent;
            decimal price = side == Side.Buy ? TabsSimple[0].PriceBestAsk : TabsSimple[0].PriceBestBid;
            decimal volume = depoBalance * (volumePercent / 100) / price / _tab.Securiti.Lot;
            return Math.Round(volume, VolumeDecimals.ValueInt);
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
