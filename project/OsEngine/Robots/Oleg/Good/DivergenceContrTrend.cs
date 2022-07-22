using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("DivergenceContrTrend")]
    public class DivergenceContrTrend : BotPanel
    {
        BotTabSimple _tab;
        StrategyParameterString Regime;
        public StrategyParameterDecimal VolumeOnPosition;
        public StrategyParameterString VolumeRegime;
        public StrategyParameterInt VolumeDecimals;
        StrategyParameterDecimal Slippage;

        private StrategyParameterTimeOfDay TimeStart;
        private StrategyParameterTimeOfDay TimeEnd;

        public Aindicator _smaFilter;
        private StrategyParameterInt SmaLengthFilter;
        public StrategyParameterBool SmaPositionFilterIsOn;
        public StrategyParameterBool SmaSlopeFilterIsOn;

        Aindicator _zz;
        StrategyParameterInt _lengthZZ;

        public DivergenceContrTrend(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
            VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
            VolumeOnPosition = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");
            Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");

            TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
            TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

            _lengthZZ = CreateParameter("Length ZZ", 50, 50, 200, 20, "Robot parameters");

            SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");
            SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
            SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

            _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
            _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
            _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
            _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
            _smaFilter.Save();

            _zz = IndicatorsFactory.CreateIndicatorByName(nameClass: "ZigZagIndicator", name: name + "ZigZag", canDelete: false);
            _zz = (Aindicator)_tab.CreateCandleIndicator(_zz, nameArea: "Prime");
            _zz.ParametersDigit[0].Value = _lengthZZ.ValueInt;
            _zz.Save();

            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            ParametrsChangeByUser += DivergenceContrTrend_ParametrsChangeByUserEventHandler;
            DivergenceContrTrend_ParametrsChangeByUserEventHandler();
        }

        private void DivergenceContrTrend_ParametrsChangeByUserEventHandler()
        {
            if (_zz.ParametersDigit[0].Value != _lengthZZ.ValueInt)
            {
                _zz.ParametersDigit[0].Value = _lengthZZ.ValueInt;
                _zz.Reload();
                _zz.Save();
            }

            if (_smaFilter.ParametersDigit[0].Value != SmaLengthFilter.ValueInt)
            {
                _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
                _smaFilter.Reload();
                _smaFilter.Save();
            }

            if (_smaFilter.DataSeries != null && _smaFilter.DataSeries.Count > 0)
            {
                _smaFilter.DataSeries[0].IsPaint = SmaPositionFilterIsOn.ValueBool;
            }
        }

        public override string GetNameStrategyType()
        {
            return "DivergenceContrTrend";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            if (TimeStart.Value > _tab.TimeServerCurrent ||
                TimeEnd.Value < _tab.TimeServerCurrent)
            {
                CancelStopsAndProfits();
                return;
            }

            if (_tab.CandlesAll == null)
            {
                return;
            }
            if (_lengthZZ.ValueInt >= candles.Count)

            {
                return;
            }

            if (SmaLengthFilter.ValueInt >= candles.Count)
            {
                return;
            }

            List<Position> positions = _tab.PositionsOpenAll;
            decimal lastCandleClosePrice = candles[candles.Count - 1].Close;
            decimal bb_up = _zz.DataSeries[4].Last;
            decimal bb_down = _zz.DataSeries[5].Last;

            decimal lastMaFilter = _smaFilter.DataSeries[0].Last;
            if (bb_down <= 0) return;
            if (bb_up <= 0) return;

            decimal _slippage = 0;

            if (positions.Count == 0)
            {// enter logic

                if (bb_down > bb_up)
                {
                    return;
                }
                _slippage = Slippage.ValueDecimal * bb_up / 100;

                if (!BuySignalIsFiltered(candles))
                {
                    // если мы уже выше уровня покупок - ничего не делаем
                    if (lastCandleClosePrice > bb_up + _slippage)
                    {
                        return;
                    }

                    _tab.BuyAtStop(GetVolume(), bb_up + _slippage, bb_up, StopActivateType.HigherOrEqual, 1);
                }
                _slippage = Slippage.ValueDecimal * bb_down / 100;

                if (!SellSignalIsFiltered(candles))
                {
                    // если мы уже ниже уровня продаж - ничего не делаем
                    if (lastCandleClosePrice < bb_down - _slippage)
                    {
                        return;
                    }

                    _tab.SellAtStop(GetVolume(), bb_down - _slippage, bb_down, StopActivateType.LowerOrEqyal, 1);
                }
            }
            else
            {//exit logic
                for (int i = 0; i < positions.Count; i++)
                {
                    _tab.BuyAtStopCancel();
                    _tab.SellAtStopCancel();

                    if (positions[i].State != PositionStateType.Open)
                    {
                        continue;
                    }
                    decimal stop_level = 0;

                    if (positions[i].Direction == Side.Buy)
                    {// logic to close long position
                        stop_level = bb_down > lastMaFilter ? bb_down : lastMaFilter;
                        _slippage = Slippage.ValueDecimal * stop_level / 100;

                        //   _tab.CloseAtStop(positions[i], stop_level, stop_level - _slippage.ValueInt * _tab.Securiti.PriceStep);
                        _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level - _slippage);
                    }
                    else if (positions[i].Direction == Side.Sell)
                    {//logic to close short position
                        stop_level = bb_up < lastMaFilter && bb_up > 0 ? bb_up : lastMaFilter;
                        _slippage = Slippage.ValueDecimal * stop_level / 100;

                        // _tab.CloseAtStop(positions[i], stop_level, stop_level + _slippage.ValueInt * _tab.Securiti.PriceStep);
                        _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level + _slippage);
                    }

                }
            }
        }

        private void CancelStopsAndProfits()
        {
            List<Position> positions = _tab.PositionsOpenAll;

            for (int i = 0; i < positions.Count; i++)
            {
                Position pos = positions[i];
                pos.StopOrderIsActiv = false;
                pos.ProfitOrderIsActiv = false;
            }

            _tab.BuyAtStopCancel();
            _tab.SellAtStopCancel();
        }

        private bool BuySignalIsFiltered(List<Candle> candles)
        {
            decimal lastPrice = candles[candles.Count - 1].Close;
            decimal lastSma = _smaFilter.DataSeries[0].Last;
            // фильтр для покупок
            if (Regime.ValueString == "Off" ||
                Regime.ValueString == "OnlyShort" ||
                Regime.ValueString == "OnlyClosePosition")
            {
                return true;
                //если режим работы робота не соответсвует направлению позиции
            }

            if (SmaPositionFilterIsOn.ValueBool)
            {
                if (lastSma > lastPrice)
                {
                    return true;
                }
                // если цена ниже последней сма - возвращаем на верх true
            }
            if (SmaSlopeFilterIsOn.ValueBool)
            {
                decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];
                if (lastSma < prevSma)
                {
                    return true;
                }
                // если последняя сма ниже предыдущей сма - возвращаем на верх true
            }

            return false;
        }

        private bool SellSignalIsFiltered(List<Candle> candles)
        {
            decimal lastPrice = candles[candles.Count - 1].Close;
            decimal lastSma = _smaFilter.DataSeries[0].Last;
            // фильтр для продаж
            if (Regime.ValueString == "Off" ||
                Regime.ValueString == "OnlyLong" ||
                Regime.ValueString == "OnlyClosePosition")
            {
                return true;
                //если режим работы робота не соответсвует направлению позиции
            }
            if (SmaPositionFilterIsOn.ValueBool)
            {
                if (lastSma < lastPrice)
                {
                    return true;
                }
                // если цена выше последней сма - возвращаем на верх true
            }
            if (SmaSlopeFilterIsOn.ValueBool)
            {
                decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];
                if (lastSma > prevSma)
                {
                    return true;
                }
                // если последняя сма выше предыдущей сма - возвращаем на верх true
            }

            return false;
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
