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
        private BotTabSimple _bot;
        private TradingState _state;
        private decimal _zoneUp;
        private decimal _zoneDown;
        private decimal _balanceOnStart;

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
            _bot = TabsSimple[0];
            _state = TradingState.FREE;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in Volume", 0, 0, 4, 1, "Base");
            VolumeMultiplier = CreateParameter("Volume multiplier", 0.75m, 0.25m, 1, 0.05m, "Base");
            MinVolumeUSDT = CreateParameter("Min Volume USDT", 7m, 7m, 7m, 1m, "Base");
            BollingerLength = CreateParameter("Length BOLLINGER", 20, 10, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("Bollinger deviation", 2m, 1m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezeLength = CreateParameter("Length BOLLINGER SQUEEZE", 130, 100, 600, 5, "Robot parameters");
            ProfitSizeFromRZ = CreateParameter("Profit size from RZ", 2m, 0.5m, 3, 0.5m, "Base");

            _bollingerSma = new MovingAverage(false);
            _bollingerSma = (MovingAverage)_bot.CreateCandleIndicator(_bollingerSma, "Prime");
            _bollingerSma.TypeCalculationAverage = MovingAverageTypeCalculation.Simple;
            _bollingerSma.Lenght = BollingerLength.ValueInt;
            _bollingerSma.Save();

            _bollinger = new Bollinger(name + "Bollinger", false);
            _bollinger = (Bollinger)_bot.CreateCandleIndicator(_bollinger, "Prime");
            _bollinger.Lenght = BollingerLength.ValueInt;
            _bollinger.Deviation = BollingerDeviation.ValueDecimal;
            _bollinger.Save();

            _bollingerWithSqueeze = new BollingerWithSqueeze(name + "BollingerWithSqueeze", false);
            _bollingerWithSqueeze = (BollingerWithSqueeze)_bot.CreateCandleIndicator(_bollingerWithSqueeze, "Prime");
            _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
            _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
            _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezeLength.ValueInt;
            _bollingerWithSqueeze.Save();

            _bot.CandleFinishedEvent += event_CandleFinished_SQUEEZE_FOUND;
            _bot.CandleUpdateEvent += event_CandleUpdated_SET_NEXT_ORDERS;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_MANAGE_ENTRIES;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
        }

        public override string GetNameStrategyType()
        {
            return "RecoveryZone";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void event_ParametersChangedByUser()
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

        private void event_CandleFinished_SQUEEZE_FOUND(List<Candle> candles)
        {
            if (ReadyToTrade())
            {
                bool noPositions = _bot.PositionsOpenAll.Count == 0;
                bool freeState = _state == TradingState.FREE;
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (noPositions && freeState && lastCandleHasSqueeze)
                {
                    _state = TradingState.SQUEEZE_FOUND;
                    _balanceOnStart = _bot.Portfolio.ValueCurrent;

                    decimal squeezeSize = _bollingerWithSqueeze.ValuesUp.Last() - _bollingerWithSqueeze.ValuesDown.Last();
                    _zoneUp = _bollingerWithSqueeze.ValuesUp.Last() + squeezeSize / 2;
                    _zoneDown = _bollingerWithSqueeze.ValuesDown.Last() - squeezeSize / 2;
                    SetNewEntryOrder_LONG();
                    SetNewEntryOrder_SHORT();
                }
            }
        }

        private void event_PositionOpened_MANAGE_ENTRIES(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                if (p.Direction == Side.Buy)
                {
                    _state = TradingState.LONG_ENTERED;
                    _zoneDown = _bot.PositionsOpenAll.Count == 1 ? _bollingerSma.Values.Last() : _zoneDown;
                    SetNewEntryOrder_SHORT();
                }

                if (p.Direction == Side.Sell)
                {
                    _state = TradingState.SHORT_ENTERED;
                    _zoneUp = _bot.PositionsOpenAll.Count == 1 ? _bollingerSma.Values.Last() : _zoneUp;
                    SetNewEntryOrder_LONG();
                }
            }
        }

        private void event_CandleUpdated_SET_NEXT_ORDERS(List<Candle> candles)
        {
            if (_bot.PositionsOpenAll.Count > 0)
            {
                if (_state == TradingState.LONG_ENTERED)
                {
                    Position longPosition = _bot.PositionsOpenAll
                        .Where(p => p.State == PositionStateType.Open && p.Direction == Side.Buy).FirstOrDefault();
                    if (longPosition != null)
                    {
                        decimal SL_price = _zoneDown;
                        decimal SL_size = longPosition.EntryPrice - SL_price;
                        decimal TP_price = longPosition.EntryPrice + SL_size * ProfitSizeFromRZ.ValueDecimal;
                        _bot.CloseAtProfit(longPosition, TP_price, longPosition.OpenVolume);
                        _bot.CloseAtStop(longPosition, SL_price, SL_price);
                        _state = TradingState.LONG_TARGETS_SET;
                    }
                }

                if (_state == TradingState.SHORT_ENTERED)
                {
                    Position shortPosition = _bot.PositionsOpenAll
                        .Where(p => p.State == PositionStateType.Open && p.Direction == Side.Sell).FirstOrDefault();
                    if (shortPosition != null)
                    {
                        decimal SL_price = _zoneUp;
                        decimal SL_size = SL_price - shortPosition.EntryPrice;
                        decimal TP_price = shortPosition.EntryPrice - SL_size * ProfitSizeFromRZ.ValueDecimal;
                        _bot.CloseAtProfit(shortPosition, TP_price, shortPosition.OpenVolume);
                        _bot.CloseAtStop(shortPosition, SL_price, SL_price);
                        _state = TradingState.SHORT_TARGETS_SET;
                    }
                }
            }
        }

        private void event_PositionClosed_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
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

        private void SetNewEntryOrder_LONG()
        {
            _bot.BuyAtStopCancel();
            decimal buyCoinsVolume = GetNewAttemptCoinsVolume(Side.Buy);
            if (buyCoinsVolume > 0)
            {
                _bot.BuyAtStop(buyCoinsVolume, _zoneUp, _zoneUp, StopActivateType.HigherOrEqual, 100);
            }
        }

        private void SetNewEntryOrder_SHORT()
        {
            _bot.SellAtStopCancel();
            decimal sellCoinsVolume = GetNewAttemptCoinsVolume(Side.Sell);
            if (sellCoinsVolume > 0)
            {
                _bot.SellAtStop(sellCoinsVolume, _zoneDown, _zoneDown, StopActivateType.LowerOrEqyal, 100);
            }
        }

        private bool ReadyToTrade()
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
