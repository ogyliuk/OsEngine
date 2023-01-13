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
        private static readonly bool LOGGING_ENABLED = false;

        private BotTabSimple _bot;
        private TradingState _state;
        private decimal _zoneUp;
        private decimal _zoneDown;
        private decimal _balanceOnStart;

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
            VolumeMultiplier = CreateParameter("Volume multiplier", 0.6m, 0.25m, 1, 0.05m, "Base");
            MinVolumeUSDT = CreateParameter("Min Volume USDT", 7m, 7m, 7m, 1m, "Base");
            BollingerLength = CreateParameter("Length BOLLINGER", 20, 10, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("Bollinger deviation", 2m, 1m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezeLength = CreateParameter("Length BOLLINGER SQUEEZE", 130, 100, 600, 5, "Robot parameters");
            // TODO : set size not from RZ but from squeeze size and put 1.8 as a best one for 1m TF (also good are: 0.8 and 1.7)
            ProfitSizeFromRZ = CreateParameter("Profit size from RZ", 0.25m, 0.2m, 3, 0.2m, "Base");

            _bollingerWithSqueeze = new BollingerWithSqueeze(name + "BollingerWithSqueeze", false);
            _bollingerWithSqueeze = (BollingerWithSqueeze)_bot.CreateCandleIndicator(_bollingerWithSqueeze, "Prime");
            _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
            _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
            _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezeLength.ValueInt;
            _bollingerWithSqueeze.Save();

            _bot.CandleFinishedEvent += event_CandleFinished_SQUEEZE_FOUND;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
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
                bool freeState = _state == TradingState.FREE;
                bool noPositions = _bot.PositionsOpenAll.Count == 0;
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (freeState && noPositions && lastCandleHasSqueeze)
                {
                    _state = TradingState.SQUEEZE_FOUND;
                    _balanceOnStart = _bot.Portfolio.ValueCurrent;

                    _zoneUp = _bollingerWithSqueeze.ValuesUp.Last();
                    _zoneDown = _bollingerWithSqueeze.ValuesDown.Last();
                    Set_EN_Order_LONG();
                    Set_EN_Order_SHORT();
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                if (p.Direction == Side.Buy)
                {
                    if (IsFirstEntry())
                    {
                        _bot.SellAtStopCancel();
                        _zoneDown = _bollingerWithSqueeze.ValuesSma.Last();
                    }                    

                    Set_TP_Order_LONG(p);
                    Set_SL_Order_LONG(p);
                    Set_EN_Order_SHORT();

                    _state = TradingState.LONG_ENTERED;
                }

                if (p.Direction == Side.Sell)
                {
                    if (IsFirstEntry())
                    {
                        _bot.BuyAtStopCancel();
                        _zoneUp = _bollingerWithSqueeze.ValuesSma.Last();
                    }

                    Set_TP_Order_SHORT(p);
                    Set_SL_Order_SHORT(p);
                    Set_EN_Order_LONG();

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
                }

                // TODO : uncomment this logging
                // LogPositionResults(p);
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
            // TODO : uncomment - decimal SL_price = Calc_TP_Price_SHORT(_zoneDown);
            decimal SL_price = _zoneDown;
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private void Set_SL_Order_SHORT(Position p)
        {
            // TODO : uncomment - decimal SL_price = Calc_TP_Price_LONG(_zoneUp);
            decimal SL_price = _zoneUp;
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
            decimal zoneSize = _zoneUp - _zoneDown;
            decimal TP_size = zoneSize * ProfitSizeFromRZ.ValueDecimal;
            return entryPrice + TP_size;
        }

        private decimal Calc_TP_Price_SHORT(decimal entryPrice)
        {
            decimal zoneSize = _zoneUp - _zoneDown;
            decimal TP_size = zoneSize * ProfitSizeFromRZ.ValueDecimal;
            return entryPrice - TP_size;
        }

        private bool IsFirstEntry()
        {
            return _bot.PositionsOpenAll.Count == 1;
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

        private static void LogPositionResults(Position p)
        {
            if (LOGGING_ENABLED)
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
        }

        enum TradingState
        {
            FREE,
            SQUEEZE_FOUND,
            LONG_ENTERED,
            SHORT_ENTERED
        }

        // ******************** CANDLE UPDATE ***********************
        // _bot.CandleUpdateEvent += event_CandleUpdated;
        // private void event_CandleUpdated(List<Candle> candles) { }
        // **********************************************************
    }
}
