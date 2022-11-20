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
        private decimal _balanceOnDealStart;

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
            _balanceOnDealStart = 0;
            _state = TradingState.FREE;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in Volume", 0, 0, 4, 1, "Base");
            VolumeMultiplier = CreateParameter("Volume multiplier", 0.75m, 0.25m, 1, 0.05m, "Base");
            MinVolumeUSDT = CreateParameter("Min Volume USDT", 7m, 7m, 7m, 1m, "Base");

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
            if (IsBotEnabled())
            {
                bool noPositions = _tab.PositionsOpenAll.Count == 0;
                bool freeState = _state == TradingState.FREE;
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (noPositions && freeState && lastCandleHasSqueeze)
                {
                    _state = TradingState.SQUEEZE_FOUND;
                    _balanceOnDealStart = _tab.Portfolio.ValueCurrent;

                    decimal semiSqueezeVolatility = (_bollingerWithSqueeze.ValuesUp.Last() - _bollingerWithSqueeze.ValuesDown.Last()) / 2;

                    bool longsEnabled = Regime.ValueString == "On" || Regime.ValueString == "OnlyLong";
                    if (longsEnabled)
                    {
                        decimal buyCoinsVolume = GetNewAttemptCoinsVolume(Side.Buy);
                        if (buyCoinsVolume > 0)
                        {
                            decimal longEntryPrice = _bollingerWithSqueeze.ValuesUp.Last() + semiSqueezeVolatility;
                            _tab.BuyAtStop(buyCoinsVolume, longEntryPrice, longEntryPrice, StopActivateType.HigherOrEqual, 100);
                        }
                    }

                    bool shortsEnabled = Regime.ValueString == "On" || Regime.ValueString == "OnlyShort";
                    if (shortsEnabled)
                    {
                        decimal sellCoinsVolume = GetNewAttemptCoinsVolume(Side.Sell);
                        if (sellCoinsVolume > 0)
                        {
                            decimal shortEntryPrice = _bollingerWithSqueeze.ValuesDown.Last() - semiSqueezeVolatility;
                            _tab.SellAtStop(sellCoinsVolume, shortEntryPrice, shortEntryPrice, StopActivateType.LowerOrEqyal, 100);
                        }
                    }
                }
            }
        }

        private void _tab_PositionOpenEventHandler_FILLED_ENTRY_ORDER(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                _tab.SellAtStopCancel();
                _tab.BuyAtStopCancel();

                if (p.Direction == Side.Buy)
                {
                    _state = TradingState.LONG_ENTERED;
                }
                if (p.Direction == Side.Sell)
                {
                    _state = TradingState.SHORT_ENTERED;
                }
            }
        }

        private void _tab_CandleUpdateEventHandler_SET_NEXT_ORDERS(List<Candle> candles)
        {
            if (IsBotEnabled() && _tab.PositionsOpenAll.Count > 0)
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

        private void _tab_PositionCloseEventHandler_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
                _balanceOnDealStart = 0;
                _state = TradingState.FREE;
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
        }

        private bool IsBotEnabled()
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

        private decimal GetNewAttemptCoinsVolume(Side side)
        {
            decimal coinsVolume = 0;
            if (HasMoneyForNewAttempt() && !IsMoneyForNewAttemptTooSmall())
            {
                decimal moneyAvailableForNewAttempt = GetNewAttemptMoneyNeeded();
                decimal moneyNeededForFee = moneyAvailableForNewAttempt / 100 * _tab.ComissionValue;
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
            bool hasOpenPositionsAlready = _tab.PositionsLast != null && _tab.PositionsLast.State == PositionStateType.Open;
            decimal moneyToCalcFrom = hasOpenPositionsAlready ? GetLastPositionEntryMoney() : _tab.Portfolio.ValueCurrent;
            return moneyToCalcFrom * VolumeMultiplier.ValueDecimal;
        }

        private decimal GetLastPositionEntryMoney()
        {
            return _tab.PositionsLast != null ? GetPositionEntryMoney(_tab.PositionsLast) : 0;
        }

        private decimal GetFreeMoney()
        {
            decimal freeMoney = _balanceOnDealStart;
            _tab.PositionsOpenAll.ForEach(p => { freeMoney -= GetPositionEntryMoney(p); });
            return freeMoney;
        }

        private decimal GetPositionEntryMoney(Position p)
        {
            return p.OpenVolume * p.EntryPrice + p.CommissionTotal();
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
