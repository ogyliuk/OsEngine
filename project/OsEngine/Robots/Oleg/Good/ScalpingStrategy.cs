using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;

namespace OsEngine.Robots.Oleg.Good
{
    // https://www.youtube.com/watch?v=ohtnf4H_HMA
    [Bot("ScalpingStrategy")]
    public class ScalpingStrategy : BotPanel
    {
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

        public ScalpingStrategy(string name, StartProgram startProgram) : base(name, startProgram)
        {
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

            decimal lastCandleClosePrice = candles[candles.Count - 1].Close;
            if (_tab.PositionsOpenAll.Count == 0)
            {
                // TODO : enter here
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
                decimal SL_TriggerPrice = longDeal ? position.EntryPrice - atrValue : position.EntryPrice + atrValue;
                decimal SL_Price = longDeal ? SL_TriggerPrice - slippage : SL_TriggerPrice + slippage;

                // Orders
                _tab.CloseAtStop(position, SL_TriggerPrice, SL_Price);
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
    }
}
