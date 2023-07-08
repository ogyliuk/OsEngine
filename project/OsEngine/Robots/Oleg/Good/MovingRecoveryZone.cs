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
    [Bot("MovingRecoveryZone")]
    public class MovingRecoveryZone : BotPanel
    {
        private BotTabSimple _bot;
        private Side _mainDirection;
        private TradingState _state;
        private decimal _balanceOnStart;
        private decimal _squeezeSize; // ************************************
        private int _dealAttemptsCounter;
        private string _dealGuid;

        private Position _mainPosition;
        private Position _recoveryPosition;

        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterDecimal BollingerDeviation;
        private StrategyParameterInt BollingerSqueezePeriod;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal RecoveryVolumeMultiplier;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal MinVolumeUSDT;

        private StrategyParameterDecimal ProfitInSqueezes; // ************************************
        private StrategyParameterDecimal RiskZoneInSqueezes; // ************************************

        public MovingRecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _state = TradingState.FREE;
            _mainDirection = Side.None;
            _dealAttemptsCounter = 0;
            _dealGuid = String.Empty;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in Volume", 0, 0, 4, 1, "Base");
            RecoveryVolumeMultiplier = CreateParameter("Recovery volume multiplier", 0.8m, 0.8m, 0.9m, 0.05m, "Base");

            MinVolumeUSDT = CreateParameter("Min Volume USDT", 7m, 7m, 7m, 1m, "Base");
            BollingerLength = CreateParameter("BOLLINGER - Length", 20, 20, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("BOLLINGER - Deviation", 2m, 2m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezePeriod = CreateParameter("BOLLINGER - Squeeze period", 130, 130, 600, 5, "Robot parameters");
            
            ProfitInSqueezes = CreateParameter("Profit in SQUEEZEs", 2.8m, 1m, 10, 0.1m, "Base");
            RiskZoneInSqueezes = CreateParameter("RiskZone in SQUEEZEs", 2.9m, 1m, 10, 0.1m, "Base");

            _bollingerWithSqueeze = new BollingerWithSqueeze(name + "BollingerWithSqueeze", false);
            _bollingerWithSqueeze = (BollingerWithSqueeze)_bot.CreateCandleIndicator(_bollingerWithSqueeze, "Prime");
            _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
            _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
            _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezePeriod.ValueInt;
            _bollingerWithSqueeze.Save();

            _bot.CandleFinishedEvent += event_CandleClosed_SQUEEZE_FOUND;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
        }

        public override string GetNameStrategyType()
        {
            return "MovingRecoveryZone";
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
                bool freeState = _state == TradingState.FREE;
                bool noPositions = _bot.PositionsOpenAll.Count == 0;
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (freeState && noPositions && lastCandleHasSqueeze)
                {
                    _state = TradingState.SQUEEZE_FOUND;
                    _balanceOnStart = _bot.Portfolio.ValueCurrent;

                    decimal squeezeUpBand = _bollingerWithSqueeze.ValuesUp.Last();
                    decimal squeezeDownBand = _bollingerWithSqueeze.ValuesDown.Last();
                    Set_EN_Order_LONG(squeezeUpBand);
                    Set_EN_Order_SHORT(squeezeDownBand);

                    _squeezeSize = squeezeUpBand - squeezeDownBand; // TODO : check if it is needed
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                if (_dealAttemptsCounter == 0)
                {
                    _dealGuid = Guid.NewGuid().ToString();
                    _mainPosition = p;
                }
                else
                {
                    _recoveryPosition = p;
                }

                _dealAttemptsCounter++;
                p.DealGuid = _dealGuid;

                if (p.Direction == Side.Buy)
                {
                    if (IsFirstAttempt())
                    {
                        _mainDirection = Side.Buy;
                        _bot.SellAtStopCancel();
                        _zoneDown = p.EntryPrice - _squeezeSize * RiskZoneInSqueezes.ValueDecimal;
                    }

                    Set_TP_Order_LONG(p);
                    Set_SL_Order_LONG(p);
                    // Set_EN_Order_SHORT();

                    _state = TradingState.LONG_ENTERED;
                }

                if (p.Direction == Side.Sell)
                {
                    if (IsFirstAttempt())
                    {
                        _mainDirection = Side.Sell;
                        _bot.BuyAtStopCancel();
                        _zoneUp = p.EntryPrice + _squeezeSize * RiskZoneInSqueezes.ValueDecimal;
                    }

                    Set_TP_Order_SHORT(p);
                    Set_SL_Order_SHORT(p);
                    // Set_EN_Order_LONG();

                    _state = TradingState.SHORT_ENTERED;
                }
            }
        }

        private void event_PositionClosed_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
                bool closingPositionByTakeProfit =
                    (p.Direction == Side.Sell && p.EntryPrice > p.ClosePrice) ||
                    (p.Direction == Side.Buy && p.EntryPrice < p.ClosePrice);
                bool closingLastPosition = _bot.PositionsOpenAll.Count == 0;
                bool noMoreEntryOrdersSet = _bot.PositionOpenerToStopsAll.Count == 0;
                if (closingLastPosition && (noMoreEntryOrdersSet || closingPositionByTakeProfit))
                {
                    _bot.BuyAtStopCancel();
                    _bot.SellAtStopCancel();
                    _state = TradingState.FREE;
                    _mainDirection = Side.None;
                    _dealAttemptsCounter = 0;
                    _dealGuid = String.Empty;
                    _mainPosition = null;
                    _recoveryPosition = null;
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
            decimal SL_price = Calc_TP_Price_SHORT(_zoneDown);
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private void Set_SL_Order_SHORT(Position p)
        {
            decimal SL_price = Calc_TP_Price_LONG(_zoneUp);
            _bot.CloseAtStop(p, SL_price, SL_price);
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

        private void Set_EN_Order_LONG(decimal entryPrice)
        {
            decimal buyCoinsVolume = GetNewAttemptCoinsVolume(Side.Buy);
            if (buyCoinsVolume > 0)
            {
                _bot.BuyAtStop(buyCoinsVolume, entryPrice, entryPrice, StopActivateType.HigherOrEqual, 100000);
            }
        }

        private void Set_EN_Order_SHORT(decimal entryPrice)
        {
            decimal sellCoinsVolume = GetNewAttemptCoinsVolume(Side.Sell);
            if (sellCoinsVolume > 0)
            {
                _bot.SellAtStop(sellCoinsVolume, entryPrice, entryPrice, StopActivateType.LowerOrEqyal, 100000);
            }
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
            return moneyToCalcFrom * RecoveryVolumeMultiplier.ValueDecimal;
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

        private bool IsFirstAttempt()
        {
            return _dealAttemptsCounter == 1;
        }

        private bool IsEnoughDataAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool robotEnabled = Regime.ValueString == "On";
            bool enoughCandlesForBollinger = candlesCount > BollingerLength.ValueInt;
            bool enoughCandlesForBollingerSqueeze = candlesCount > BollingerSqueezePeriod.ValueInt;
            return robotEnabled && enoughCandlesForBollinger && enoughCandlesForBollingerSqueeze;
        }
    }

    enum TradingState
    {
        FREE,
        SQUEEZE_FOUND,
        LONG_ENTERED,
        SHORT_ENTERED
    }
}
