using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("Averaging")]
    public class Averaging : BotPanel
    {
        private BotTabSimple _tab;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal VolumeFirstEntry;
        private StrategyParameterString VolumeMode;
        private StrategyParameterInt VolumeDecimals;

        private StrategyParameterDecimal MinProfitInPercents;

        public Averaging(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeMode = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
            VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
            VolumeFirstEntry = CreateParameter("Volume", 1, 1m, 10, 1, "Base");

            MinProfitInPercents = CreateParameter("Min PROFIT %", 0.1m, 0.1m, 1, 0.05m, "Base");

            _tab.CandleFinishedEvent += _tab_CandleFinishedEventHandler;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpenEventHandler;

            ParametrsChangeByUser += ParametersChangeByUserEventHandler;
            ParametersChangeByUserEventHandler();
        }

        public override string GetNameStrategyType() { return "Averaging"; }

        public override void ShowIndividualSettingsDialog() { }

        private void ParametersChangeByUserEventHandler() { }

        private void _tab_CandleFinishedEventHandler(List<Candle> candles)
        {
            if (Regime.ValueString == "Off" || _tab.CandlesAll == null || _tab.CandlesAll.Count < 2)
            {
                return;
            }

            // смена цвета - открываем позу, получили зеленую - открываем лонг, красную - шорт
            // если позиция в плюсе на момент смены цвета - закрываем по рынку,
            // если в минусе - усредняемся на постоянную величину или закрываем в убыток

            Candle candle = candles.Last();
            Candle previousCandle = candles[candles.Count - 2];
            bool candleColorSwitched = candle.IsUp != previousCandle.IsUp;

            if (candleColorSwitched)
            {
                if (candle.IsUp)
                {
                    if (HasPosition_LONG())
                    {
                        // EACH entry - new position
                        // if in MIN profit - close
                        // if in loss - averaging or take loss
                    }
                    else
                    {
                        _tab.BuyAtMarket(GetVolume());
                    }
                }
                else
                {
                    if (HasPosition_SHORT())
                    {
                        // EACH entry - new position
                        // if in MIN profit - close
                        // if in loss - averaging or take loss
                    }
                    else
                    {
                        _tab.SellAtMarket(GetVolume());
                    }
                }
            }

            decimal lastCandleClosePrice = candles.Last().Close;
            if (_tab.PositionsOpenAll.Count == 0)
            {
                // LONG
                _tab.BuyAtStop(GetVolume(), lastCandleClosePrice, lastCandleClosePrice, StopActivateType.HigherOrEqual, 1);
                // SHORT
                _tab.SellAtStop(GetVolume(), lastCandleClosePrice, lastCandleClosePrice, StopActivateType.LowerOrEqyal, 1);
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

        private bool HasPosition_LONG()
        {
            return _tab.PositionsOpenAll != null &&  _tab.PositionsOpenAll.Any(p => p.Direction == Side.Buy);
        }

        private bool HasPosition_SHORT()
        {
            return _tab.PositionsOpenAll != null && _tab.PositionsOpenAll.Any(p => p.Direction == Side.Sell);
        }

        private void _tab_PositionOpenEventHandler(Position position)
        {
            if (position != null && position.State == PositionStateType.Open)
            {
                // _tab.CloseAtLimit(position, takeProfitPrice, position.OpenVolume);
                // _tab.CloseAtStop(position, stopLossPrice, stopLossPrice);
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
