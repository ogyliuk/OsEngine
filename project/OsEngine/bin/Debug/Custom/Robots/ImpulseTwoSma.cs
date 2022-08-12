using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Drawing;

[Bot("ImpulseTwoSma")]
class ImpulseTwoSma : BotPanel
{
	BotTabSimple _tab;

	StrategyParameterString Regime;
	public StrategyParameterDecimal VolumeOnPosition;
	public StrategyParameterString VolumeRegime;
	public StrategyParameterInt VolumeDecimals;

	private StrategyParameterTimeOfDay TimeStart;
	private StrategyParameterTimeOfDay TimeEnd;

	public Aindicator _sma1;
	public StrategyParameterInt _periodSma1;

	public Aindicator _sma2;
	public StrategyParameterInt _periodSma2;

	public StrategyParameterInt LookBack;

	public Aindicator _smaFilter;
	private StrategyParameterInt SmaLengthFilter;
	public StrategyParameterBool SmaPositionFilterIsOn;
	public StrategyParameterBool SmaSlopeFilterIsOn;

	public ImpulseTwoSma(string name, StartProgram startProgram) : base(name, startProgram)
	{
		TabCreate(BotTabType.Simple);
		_tab = TabsSimple[0];

		Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
		VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
		VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
		VolumeOnPosition = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");

		TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
		TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

		LookBack = CreateParameter("Candles Look Back", 4, 1, 10, 1, "Robot parameters");

		_periodSma1 = CreateParameter("fast SMA period", 250, 50, 500, 50, "Robot parameters");
		_periodSma2 = CreateParameter("slow SMA2 period", 1000, 500, 1500, 100, "Robot parameters");

		SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");

		SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
		SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

		_smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
		_smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
		_smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
		_smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
		_smaFilter.Save();

		_sma1 = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma", canDelete: false);
		_sma1 = (Aindicator)_tab.CreateCandleIndicator(_sma1, nameArea: "Prime");
		_sma1.ParametersDigit[0].Value = _periodSma1.ValueInt;
		_sma1.DataSeries[0].Color = Color.Red;
		_sma1.Save();

		_sma2 = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma2", canDelete: false);
		_sma2 = (Aindicator)_tab.CreateCandleIndicator(_sma2, nameArea: "Prime");
		_sma2.ParametersDigit[0].Value = _periodSma2.ValueInt;
		_sma2.DataSeries[0].Color = Color.Green;
		_sma2.Save();

		StopOrActivateIndicators();
		_tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
		ParametrsChangeByUser += LRegBot_ParametrsChangeByUser;
		LRegBot_ParametrsChangeByUser();
	}

	private void LRegBot_ParametrsChangeByUser()
	{
		StopOrActivateIndicators();

		if (_sma1.ParametersDigit[0].Value != _periodSma1.ValueInt)
		{
			_sma1.ParametersDigit[0].Value = _periodSma1.ValueInt;
			_sma1.Reload();
			_sma1.Save();
		}

		if (_sma2.ParametersDigit[0].Value != _periodSma2.ValueInt)
		{
			_sma2.ParametersDigit[0].Value = _periodSma2.ValueInt;
			_sma2.Reload();
			_sma2.Save();
		}

		if (_smaFilter.ParametersDigit[0].Value != SmaLengthFilter.ValueInt)
		{
			_smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
			_smaFilter.Reload();
			_smaFilter.Save();
		}

		if (_smaFilter.DataSeries != null && _smaFilter.DataSeries.Count > 0)
		{
			if (!SmaPositionFilterIsOn.ValueBool)
			{
				_smaFilter.DataSeries[0].IsPaint = false;
			}
			else
			{
				_smaFilter.DataSeries[0].IsPaint = true;
			}
		}
	}

	private void StopOrActivateIndicators()
	{

		if (SmaPositionFilterIsOn.ValueBool
		   != _smaFilter.IsOn && SmaSlopeFilterIsOn.ValueBool
		   != _smaFilter.IsOn)
		{
			_smaFilter.IsOn = SmaPositionFilterIsOn.ValueBool;
			_smaFilter.Reload();

			_smaFilter.IsOn = SmaSlopeFilterIsOn.ValueBool;
			_smaFilter.Reload();
		}
	}

	public override string GetNameStrategyType()
	{
		return "ImpulseTwoSma";
	}

	public override void ShowIndividualSettingsDialog()
	{

	}

	// логика

	private void _tab_CandleFinishedEvent(List<Candle> candles)
	{
		if (Regime.ValueString == "Off") { return; }

		if (TimeStart.Value > _tab.TimeServerCurrent ||
	        TimeEnd.Value < _tab.TimeServerCurrent)
		{
			CancelStopsAndProfits();
			return;
		}

		if (SmaLengthFilter.ValueInt >= candles.Count)
		{
			return;
		}

		if (LookBack.ValueInt + 2 > candles.Count)
		{
			return;
		}

		if (candles.Count < _periodSma1.ValueInt || candles.Count < _periodSma2.ValueInt) { return; }

		decimal lastMa2 = _sma2.DataSeries[0].Last;
		decimal prewMa2 = _sma2.DataSeries[0].Values[candles.Count - 2];

		bool sigUp = false;
		bool sigDown = false;
		bool sigUpClose = false;
		bool sigDownClose = false;

		// сигнал long
		for (int i = candles.Count - 1; i > candles.Count - 1 - LookBack.ValueInt; i--)
		{
			if (_sma1.DataSeries[0].Values[i] < _sma2.DataSeries[0].Values[i])
			{
				sigUp = false;
				sigDownClose = false;
				break;
			}

			sigUp = true;
			sigDownClose = true;
		}

		if (sigUp == true && _sma1.DataSeries[0].Values[candles.Count - LookBack.ValueInt - 2] > _sma2.DataSeries[0].Values[candles.Count - LookBack.ValueInt - 2])
		{ // повтор сигнала
			sigUp = false;
		}

		if (lastMa2 < prewMa2) { sigUp = false; }

		// сигнал short
		for (int i = candles.Count - 1; i > candles.Count - 1 - LookBack.ValueInt; i--)
		{
			if (_sma1.DataSeries[0].Values[i] > _sma2.DataSeries[0].Values[i])
			{
				sigDown = false;
				sigUpClose = false;
				break;
			}
			sigDown = true;
			sigUpClose = true;
		}

		if (sigDown == true && _sma1.DataSeries[0].Values[candles.Count - LookBack.ValueInt - 2] < _sma2.DataSeries[0].Values[candles.Count - LookBack.ValueInt - 2])
		{ // повтор сигнала
			sigDown = false;
		}

		if (lastMa2 > prewMa2)
		{
			sigDown = false;
		}

		List<Position> positions = _tab.PositionsOpenAll;

		if (positions.Count == 0)
		{
			// enter logic
			if (!BuySignalIsFiltered(candles) && sigUp)
			{
				_tab.BuyAtMarket(GetVolume());
			}

			if (!SellSignalIsFiltered(candles) && sigDown)
			{
				_tab.SellAtMarket(GetVolume());
			}

		}
		else
		{
			//exit logic
			for (int i = 0; i < positions.Count; i++)
			{
				if (positions[i].State == PositionStateType.ClosingFail)
				{
					_tab.CloseAtMarket(positions[i], positions[i].OpenVolume);
					continue;
				}

				if (positions[i].State != PositionStateType.Open) { continue; }

				// logic to reverse long position
				if (positions[i].Direction == Side.Buy && (sigDown || sigUpClose))
				{
					_tab.CloseAtMarket(positions[i], positions[i].OpenVolume);

					if (!SellSignalIsFiltered(candles))
					{ _tab.SellAtMarket(GetVolume()); }
					continue;
				}

				// logic to reverse short position
				if (positions[i].Direction == Side.Sell && (sigUp || sigDownClose))
				{
					_tab.CloseAtMarket(positions[i], positions[i].OpenVolume);

					if (!BuySignalIsFiltered(candles))
					{ _tab.BuyAtMarket(GetVolume()); }
					continue;
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
			if (_smaFilter.DataSeries[0].Last > lastPrice)
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

