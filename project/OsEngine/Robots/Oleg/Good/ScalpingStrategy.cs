using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.Robots.Oleg.Good
{
    // https://www.youtube.com/watch?v=ohtnf4H_HMA
    [Bot("ScalpingStrategy")]
    public class ScalpingStrategy : BotPanel
    {
        private TradingState _state;
        private int _badCandlesCount;

        private BotTabSimple _tab;

        private Aindicator _emaFast;
        private Aindicator _emaSlow;
        private Aindicator _atr;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal VolumeOnPosition;
        private StrategyParameterString VolumeRegime;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal Slippage;

        private StrategyParameterInt EmaFastLength;
        private StrategyParameterInt EmaSlowLength;
        private StrategyParameterInt AtrLength;
        private StrategyParameterInt BadCandlesNumber;
        private StrategyParameterDecimal StopLossSizeFromAtr;
        private StrategyParameterDecimal TakeProfitSizeFromStopLoss;

        public ScalpingStrategy(string name, StartProgram startProgram) : base(name, startProgram)
        {
            ResetTempParams();

            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
            VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
            VolumeOnPosition = CreateParameter("Volume", 1, 1m, 10, 1, "Base");
            Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");

            EmaFastLength = CreateParameter("EMA FAST length", 50, 20, 100, 5, "Robot parameters");
            EmaSlowLength = CreateParameter("EMA SLOW length", 200, 100, 400, 10, "Robot parameters");
            AtrLength = CreateParameter("ATR length", 14, 10, 50, 2, "Robot parameters");
            BadCandlesNumber = CreateParameter("Bad candles number", 3, 2, 5, 1, "Robot parameters");
            StopLossSizeFromAtr = CreateParameter("SL size (from ATR)", 2m, 1m, 3m, 0.5m, "Robot parameters");
            TakeProfitSizeFromStopLoss = CreateParameter("TP size (from SL)", 1.5m, 1m, 3m, 0.5m, "Robot parameters");

            _emaFast = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "EmaFAST", canDelete: false);
            _emaFast = (Aindicator)_tab.CreateCandleIndicator(_emaFast, nameArea: "Prime");
            _emaFast.DataSeries[0].Color = System.Drawing.Color.Green;
            _emaFast.ParametersDigit[0].Value = EmaFastLength.ValueInt;
            _emaFast.Save();

            _emaSlow = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "EmaSLOW", canDelete: false);
            _emaSlow = (Aindicator)_tab.CreateCandleIndicator(_emaSlow, nameArea: "Prime");
            _emaSlow.DataSeries[0].Color = System.Drawing.Color.Blue;
            _emaSlow.ParametersDigit[0].Value = EmaSlowLength.ValueInt;
            _emaSlow.Save();

            _atr = IndicatorsFactory.CreateIndicatorByName(nameClass: "ATR", name: name + "ATR", canDelete: false);
            _atr = (Aindicator)_tab.CreateCandleIndicator(_atr, nameArea: "AtrArea");
            _atr.DataSeries[0].Color = System.Drawing.Color.Red;
            _atr.ParametersDigit[0].Value = AtrLength.ValueInt;
            _atr.Save();

            _tab.CandleFinishedEvent += _tab_CandleFinishedEventHandler;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpenEventHandler;

            ParametrsChangeByUser += ParametersChangeByUserEventHandler;
            ParametersChangeByUserEventHandler();
        }

        private void ResetTempParams()
        {
            _state = TradingState.FREE;
            _badCandlesCount = 0;
        }

        private void ParametersChangeByUserEventHandler()
        {
            if (_emaFast.ParametersDigit[0].Value != EmaFastLength.ValueInt)
            {
                _emaFast.ParametersDigit[0].Value = EmaFastLength.ValueInt;
                _emaFast.Reload();
                _emaFast.Save();
            }

            if (_emaSlow.ParametersDigit[0].Value != EmaSlowLength.ValueInt)
            {
                _emaSlow.ParametersDigit[0].Value = EmaSlowLength.ValueInt;
                _emaSlow.Reload();
                _emaSlow.Save();
            }

            if (_atr.ParametersDigit[0].Value != AtrLength.ValueInt)
            {
                _atr.ParametersDigit[0].Value = AtrLength.ValueInt;
                _atr.Reload();
                _atr.Save();
            }
        }

        public override string GetNameStrategyType() { return "ScalpingStrategy"; }

        public override void ShowIndividualSettingsDialog() { }

        private void _tab_CandleFinishedEventHandler(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            if (_tab.CandlesAll == null)
            {
                return;
            }

            if (EmaFastLength.ValueInt >= candles.Count || 
                EmaSlowLength.ValueInt >= candles.Count || 
                AtrLength.ValueInt >= candles.Count)
            {
                return;
            }

            if (_tab.PositionsOpenAll.Count == 0)
            {
                decimal emaFastValue = _emaFast.DataSeries[0].Values.Last();
                decimal emaSlowValue = _emaSlow.DataSeries[0].Values.Last();
                
                bool freeState = _state == TradingState.FREE;
                bool crossFoundState = _state == TradingState.LONG_CROSS_FOUND || _state == TradingState.SHORT_CROSS_FOUND;

                // CROSS check
                if (freeState)
                {
                    decimal emaFastPreviousValue = _emaFast.DataSeries[0].Values[_emaFast.DataSeries[0].Values.Count - 2];
                    decimal emaSlowPreviousValue = _emaSlow.DataSeries[0].Values[_emaSlow.DataSeries[0].Values.Count - 2];

                    bool longCross = emaFastValue >= emaSlowValue && emaFastPreviousValue < emaSlowPreviousValue;
                    if (longCross)
                    {
                        _state = TradingState.LONG_CROSS_FOUND;
                    }

                    bool shortCross = emaFastValue <= emaSlowValue && emaFastPreviousValue > emaSlowPreviousValue;
                    if (shortCross)
                    {
                        _state = TradingState.SHORT_CROSS_FOUND;
                    }
                }
                // TOUCH check
                else if (crossFoundState)
                {
                    bool longCross = _state == TradingState.LONG_CROSS_FOUND;
                    bool fastEmaTouchedDown = candles.Last().Low < emaFastValue;
                    if (longCross && fastEmaTouchedDown)
                    {
                        _state = TradingState.LONG_TOUCH_DETECTED;
                    }

                    bool shortCross = _state == TradingState.SHORT_CROSS_FOUND;
                    bool fastEmaTouchedUp = candles.Last().High > emaFastValue;
                    if (shortCross && fastEmaTouchedUp)
                    {
                        _state = TradingState.SHORT_TOUCH_DETECTED;
                    }
                }

                // REBOUND or LOST SIGNAL check
                bool touchDetectedState = _state == TradingState.LONG_TOUCH_DETECTED || _state == TradingState.SHORT_TOUCH_DETECTED;
                if (touchDetectedState)
                {
                    bool longTouch = _state == TradingState.LONG_TOUCH_DETECTED;
                    if (longTouch)
                    {
                        // Check SIGNAL lose
                        if (candles.Last().Close < emaSlowValue) _badCandlesCount++;
                        bool signalLost = _badCandlesCount >= BadCandlesNumber.ValueInt;
                        if (signalLost)
                        {
                            ResetTempParams();
                            return;
                        }

                        // Check ENTRY
                        bool reboundedUp = candles.Last().Close > emaFastValue;
                        if (reboundedUp)
                        {
                            ResetTempParams();
                            _tab.BuyAtMarket(GetVolume());
                        }
                    }

                    bool shortTouch = _state == TradingState.SHORT_TOUCH_DETECTED;
                    if (shortTouch)
                    {
                        // Check SIGNAL lose
                        if (candles.Last().Close > emaSlowValue) _badCandlesCount++;
                        bool signalLost = _badCandlesCount >= BadCandlesNumber.ValueInt;
                        if (signalLost)
                        {
                            ResetTempParams();
                            return;
                        }

                        // Check ENTRY
                        bool reboundedDown = candles.Last().Close < emaFastValue;
                        if (reboundedDown)
                        {
                            ResetTempParams();
                            _tab.SellAtMarket(GetVolume());
                        }
                    }
                }
            }
        }

        private void _tab_PositionOpenEventHandler(Position position)
        {
            if (position != null && position.State == PositionStateType.Open)
            {
                // Generic parameters
                bool longDeal = position.Direction == Side.Buy;
                decimal slippage = position.EntryPrice * Slippage.ValueDecimal / 100;
                decimal atrValue = _atr.DataSeries[0].Last;

                // STOP LOSS
                decimal SL_size = atrValue * StopLossSizeFromAtr.ValueDecimal;
                decimal SL_TriggerPrice = longDeal ? position.EntryPrice - SL_size : position.EntryPrice + SL_size;
                decimal SL_Price = longDeal ? SL_TriggerPrice - slippage : SL_TriggerPrice + slippage;
                _tab.CloseAtStop(position, SL_TriggerPrice, SL_Price);

                // TAKE PROFIT
                decimal TP_size = SL_size * TakeProfitSizeFromStopLoss.ValueDecimal;
                decimal TP_TriggerPrice = longDeal ? position.EntryPrice + TP_size : position.EntryPrice - TP_size;
                decimal TP_Price = longDeal ? TP_TriggerPrice - slippage : TP_TriggerPrice + slippage;
                _tab.CloseAtProfit(position, TP_TriggerPrice, TP_Price);
            }
        }

        private decimal GetVolume()
        {
            decimal volume = VolumeOnPosition.ValueDecimal;

            if (VolumeRegime.ValueString == "Contract currency") // "Валюта контракта"
            {
                decimal contractPrice = TabsSimple[0].PriceBestAsk;
                volume = Math.Round(VolumeOnPosition.ValueDecimal / contractPrice, VolumeDecimals.ValueInt);
                return volume;
            }
            else if (VolumeRegime.ValueString == "Number of contracts")
            {
                return volume;
            }
            else //if (VolumeRegime.ValueString == "% of the total portfolio")
            {
                return Math.Round(_tab.Portfolio.ValueCurrent * (volume / 100) / _tab.PriceBestAsk / _tab.Securiti.Lot, VolumeDecimals.ValueInt);
            }
        }

        enum TradingState
        {
            FREE,
            LONG_CROSS_FOUND,
            LONG_TOUCH_DETECTED,
            SHORT_CROSS_FOUND,
            SHORT_TOUCH_DETECTED
        }
    }
}
