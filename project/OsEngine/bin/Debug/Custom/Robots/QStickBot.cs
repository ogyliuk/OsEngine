/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using System.Collections.Generic;

/// <summary>
/// A robot based on the QStick indicator Buy at the high of the Q Stick value, sell at the low, close by SMA.
/// Робот основанный на индикаторе QStick 
/// Покупка на максимуме значения Qstick, продажа на минимуме, закрытие по SMA.
/// </summary>
class QStickBot : BotPanel
{ /// <summary>
  /// tab to trade
  /// вкладка для торговли
  /// </summary>
    private BotTabSimple _tab;

    /// <summary>
    /// connect indicators подключаем индикаторы
    /// </summary>
    private Aindicator _sma;
    private Aindicator _qstick;
    //---

    /// <summary>
    /// slippage
    /// проскальзывание параметр
    /// </summary>
    public StrategyParameterDecimal Slippage;
    /// <summary>
    /// volume to inter
    /// фиксированный объем для входа
    /// </summary>
    public StrategyParameterDecimal Volume;
    /// <summary>
    /// Regime
    /// режим работы
    /// </summary>
    public StrategyParameterString Regime;

    public StrategyParameterInt _periodQstick;
    public StrategyParameterInt _periodSma;
    /// <summary>
    ///Bot constructor executed on initialization 
    ///Конструктор бота выполняемый при инициализации
    /// </summary>
    /// <param name="name">Name bot Имя бота</param>
    /// <param name="startProgram">the name of the program that launched the class имя программы запустившей класс</param>
    public QStickBot(string name, StartProgram startProgram) : base(name, startProgram)
    {
        TabCreate(BotTabType.Simple);
        _tab = TabsSimple[0];

        Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
        Volume = CreateParameter("Volume", 1, 1.0m, 50, 4);
        Slippage = CreateParameter("Slippage %", 0, 0, 20m, 1);

        _periodQstick = CreateParameter("Period Qstick", 14, 20, 500, 50);
        _periodSma = CreateParameter("Period Sma", 14, 20, 500, 50);

        _qstick = IndicatorsFactory.CreateIndicatorByName("QStick", name + "Qstick", false);
        _qstick = (Aindicator)_tab.CreateCandleIndicator(_qstick, "QStickArea");
        _qstick.ParametersDigit[0].Value = _periodQstick.ValueInt;
        _qstick.Save();

        _sma = IndicatorsFactory.CreateIndicatorByName("Sma", name + "MovingAverage", false);
        _sma = (Aindicator)_tab.CreateCandleIndicator(_sma, "Prime");
        _sma.ParametersDigit[0].Value = _periodSma.ValueInt;
        _sma.Save();

        ParametrsChangeByUser += QStickBot_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
    }
    /// <summary>
    /// User parameter change event 
    /// Событие изменения параметров пользователем
    /// </summary>
    private void QStickBot_ParametrsChangeByUser()
    {
        if (_qstick.ParametersDigit[0].Value != _periodQstick.ValueInt)
        {
            _qstick.ParametersDigit[0].Value = _periodQstick.ValueInt;
            _qstick.Reload();
            _qstick.Save();
        }
        if (_sma.ParametersDigit[0].Value != _periodSma.ValueInt)
        {
            _sma.ParametersDigit[0].Value = _periodSma.ValueInt;
            _sma.Reload();
            _sma.Save();
        }
    }
    private decimal _lastSma;
    private decimal _lastPrice;
    private decimal _lastqstick;
    private decimal _slippage;
    private decimal _minValueQstick;
    private decimal _maxValueQstick;
    /// <summary>
    /// Событие завершения свечи
    /// </summary>
    /// <param name="candles">коллекция свечей</param>
    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        if (Regime.ValueString == "Off")
        {
            return;
        }

        if (_qstick.DataSeries[0] == null || _qstick.ParametersDigit[0].Value + 3 > candles.Count || _sma.DataSeries[0] == null || _sma.ParametersDigit[0].Value + 3 > candles.Count)
        {
            return;
        }
        _lastSma = _sma.DataSeries[0].Last;
        _lastPrice = candles[candles.Count - 1].Close;
        _lastqstick = _qstick.DataSeries[0].Last;        
        _slippage = Slippage.ValueDecimal * _lastPrice / 100;

        CalcMinAndMaxQstickOfPeriod(candles);

        List<Position> openPositions = _tab.PositionsOpenAll;

        //Если позиций больше нуля  - проверяем условие на закрытие
        //If there are more than zero positions, we check the condition for closing
        if (openPositions != null && openPositions.Count != 0)
        {
            for (int i = 0; i < openPositions.Count; i++)
            {
                LogicClosePosition(candles, openPositions[i]);
            }
        }

        if (Regime.ValueString == "OnlyClosePosition")
        {
            return;
        }
        //Если нет открытых позиций - проверяем условие на открытие
        //If there are no open positions, we check the condition for opening
        if (openPositions == null || openPositions.Count == 0)
        {
            LogicOpenPosition(candles);
        }
    }
    /// <summary>
    /// Логика открытия позиции
    /// </summary>
    /// <param name="candles">коллекция свечей</param>    
    private void LogicOpenPosition(List<Candle> candles)
    {
        if (_lastqstick >= _maxValueQstick && _lastPrice > _lastSma &&
            Regime.ValueString != "OnlyShort")
        {
            _tab.BuyAtLimit(Volume.ValueDecimal, _lastPrice + _slippage);
        }

        if (_lastqstick <= _minValueQstick && _lastPrice < _lastSma &&
            Regime.ValueString != "OnlyLong")
        {
            _tab.SellAtLimit(Volume.ValueDecimal, _lastPrice - _slippage);
        }
    }

    /// <summary>
    /// logic close position
    /// логика зыкрытия позиции
    /// </summary>
    private void LogicClosePosition(List<Candle> candles, Position position)
    {
        if (position.Direction == Side.Buy)
        {
            if (_lastPrice < _lastSma)
            {
                _tab.CloseAtLimit(position, _lastPrice - _slippage, position.OpenVolume);
            }
        }
        if (position.Direction == Side.Sell)
        {
            if (_lastPrice > _lastSma)
            {
                _tab.CloseAtLimit(position, _lastPrice - _slippage, position.OpenVolume);
            }
        }
    }
  
    /// <summary>
    /// Calculate the minimum and maximum value for the period
    /// Рассчитать минимальное и максимальное значение  за период
    /// </summary>
    /// <param name="candles"></param>
    public void CalcMinAndMaxQstickOfPeriod(List<Candle> candles)
    {
        _minValueQstick = int.MaxValue;
        _maxValueQstick = int.MinValue;

        for (int i = candles.Count - 1; i >= candles.Count - _periodQstick.ValueInt; i--)
        {          
            if (_minValueQstick > _qstick.DataSeries[0].Values[i])
            {
                _minValueQstick = _qstick.DataSeries[0].Values[i];
            }
            if (_maxValueQstick < _qstick.DataSeries[0].Values[i])
            {
                _maxValueQstick = _qstick.DataSeries[0].Values[i];
            }
        }      
    }

    /// <summary>
    /// uniq strategy name
    /// взять уникальное имя
    /// </summary>
    public override string GetNameStrategyType()
    {
        return "QStickBot";
    }
    /// <summary>
    /// settings GUI
    /// показать окно настроек
    /// </summary>
    public override void ShowIndividualSettingsDialog()
    {

    }
}

