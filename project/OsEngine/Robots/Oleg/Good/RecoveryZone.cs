using OsEngine.Charts.CandleChart.Indicators;
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
    [Bot("RecoveryZone")]
    public class RecoveryZone : BotPanel
    {
        private BotTabSimple _tab;
        
        private Bollinger _bollinger;
        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterDecimal BollingerDeviation;
        private StrategyParameterInt BollingerSqueezeLength;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal VolumeFirstEntry;
        private StrategyParameterString VolumeMode;
        private StrategyParameterInt VolumeDecimals;

        private StrategyParameterDecimal RecoveryZoneSizePercents;
        private StrategyParameterDecimal LongProfitSizePercents;
        private StrategyParameterDecimal ShortProfitSizePercents;

        public RecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeMode = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
            VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
            VolumeFirstEntry = CreateParameter("Volume", 1, 1m, 10, 1, "Base");

            BollingerLength = CreateParameter("Length BOLLINGER", 20, 10, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("Bollinger deviation", 2m, 1m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezeLength = CreateParameter("Length BOLLINGER SQUEEZE", 130, 100, 600, 5, "Robot parameters");

            RecoveryZoneSizePercents = CreateParameter("Recovery zone size %", 0.3m, 0.1m, 1, 0.05m, "Base");
            LongProfitSizePercents = CreateParameter("Long profit size %", 0.1m, 0.1m, 1, 0.05m, "Base");
            ShortProfitSizePercents = CreateParameter("Short profit size %", 0.1m, 0.1m, 1, 0.05m, "Base");

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

            _tab.PositionOpeningSuccesEvent += _tab_PositionOpenEventHandler;
            _tab.CandleUpdateEvent += _tab_CandleUpdateEventHandler;

            ParametrsChangeByUser += ParametersChangeByUserEventHandler;
            ParametersChangeByUserEventHandler();
        }

        private void ParametersChangeByUserEventHandler()
        {
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

        private void _tab_CandleUpdateEventHandler(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }
            if (_tab.CandlesAll == null)
            {
                return;
            }
            if (BollingerLength.ValueInt >= _tab.CandlesAll.Count)
            {
                return;
            }

            if (_tab.PositionsOpenAll.Count == 0)
            {
                if (candles.Last().Close > _bollinger.ValuesUp.Last())
                {
                    _tab.BuyAtLimit(VolumeFirstEntry.ValueDecimal, candles.Last().Close);
                }

                // _tab.BuyAtStop(GetVolume(), lastCandleClosePrice + slippage, lastCandleClosePrice, StopActivateType.HigherOrEqual, 1);
                // _tab.SellAtStop(GetVolume(), lastCandleClosePrice - slippage, lastCandleClosePrice, StopActivateType.LowerOrEqyal, 1);
            }
            else
            {
                foreach (Position position in _tab.PositionsOpenAll)
                {
                    if (position.State == PositionStateType.Open)
                    {
                        // _tab.CloseAtLimit(position, closePrice, position.OpenVolume);
                    }
                }
            }
        }

        private void _tab_PositionOpenEventHandler(Position position)
        {
            if (position != null && position.State == PositionStateType.Open)
            {
                decimal takeProfitPrice = position.EntryPrice * (100 + LongProfitSizePercents.ValueDecimal) / 100;
                decimal stopLossPrice = position.EntryPrice * (100 - RecoveryZoneSizePercents.ValueDecimal) / 100;
                _tab.CloseAtLimit(position, takeProfitPrice, position.OpenVolume);
                _tab.CloseAtStop(position, stopLossPrice, stopLossPrice);
            }
        }

        private decimal GetVolume()
        {
            decimal volume = VolumeFirstEntry.ValueDecimal;

            if (VolumeMode.ValueString == "Contract currency") // "Валюта контракта"
            {
                decimal contractPrice = TabsSimple[0].PriceBestAsk;
                volume = Math.Round(VolumeFirstEntry.ValueDecimal / contractPrice, VolumeDecimals.ValueInt);
                return volume;
            }
            else if (VolumeMode.ValueString == "Number of contracts")
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
