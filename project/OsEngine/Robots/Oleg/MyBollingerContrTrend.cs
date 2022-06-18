using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.Robots.Oleg
{
    public class MyBollingerContrTrend : BotPanel
    {
        private decimal currentPriceClose;
        private decimal currentPriceLow;
        private decimal currentPriceHigh;
        private decimal bollingerUpPrice;
        private decimal bollingerMiddlePrice;
        private decimal bollingerDownPrice;

        private Bollinger bollinger;
        private MovingAverage bollingerSma;
        private Volume volume;
        private BotTabSimple botTab;

        public StrategyParameterString Regime;
        public StrategyParameterInt Slippage;
        public StrategyParameterDecimal LotSize;
        public StrategyParameterInt MaxVolumePeriod;
        public StrategyParameterInt BollingerLength;
        public StrategyParameterDecimal BollingerDeviation;

        public MyBollingerContrTrend(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            botTab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            Slippage = CreateParameter("Slipage", 0, 0, 20, 1);
            LotSize = CreateParameter("LotSize", 1, 1.0m, 50, 1);
            MaxVolumePeriod = CreateParameter("MaxVolumePeriod", 40, 20, 200, 10);
            BollingerLength = CreateParameter("BollingerLength", 20, 10, 100, 10);
            BollingerDeviation = CreateParameter("BollingerDeviation", 1.5m, 1m, 3m, 0.1m);

            bollingerSma = new MovingAverage(name + "SMA", false) { TypeCalculationAverage = MovingAverageTypeCalculation.Simple };
            bollingerSma = (MovingAverage)botTab.CreateCandleIndicator(bollingerSma, "Prime");
            bollingerSma.ColorBase = Color.Yellow;
            bollingerSma.Save();

            bollinger = new Bollinger(name + "Bollinger", false);
            bollinger = (Bollinger)botTab.CreateCandleIndicator(bollinger, "Prime");
            bollinger.ColorUp = Color.Blue;
            bollinger.ColorDown = Color.Blue;
            bollinger.Save();

            volume = new Volume(name + "Volume", false);
            volume = (Volume)botTab.CreateCandleIndicator(volume, "VolumeArea");
            volume.Save();

            botTab.CandleFinishedEvent += CandleFinishedEventHandler;
            ParametrsChangeByUser += ParametrsChangeByUserEventHandler;
        }

        private void CandleFinishedEventHandler(List<Candle> candles)
        {
            if (Regime.ValueString == "Off" || 
                bollinger.ValuesUp == null || 
                bollinger.ValuesDown == null || 
                bollinger.Lenght > candles.Count ||
                volume.Values == null ||
                volume.Values.Count < MaxVolumePeriod.ValueInt)
            {
                return;
            }

            currentPriceClose = candles.Last().Close;
            currentPriceLow = candles.Last().Low;
            currentPriceHigh = candles.Last().High;
            bollingerUpPrice = bollinger.ValuesUp.Last();
            bollingerDownPrice = bollinger.ValuesDown.Last();
            bollingerMiddlePrice = bollingerSma.Values.Last();

            List <Position> openPositions = botTab.PositionsOpenAll;
            if (openPositions != null && openPositions.Count > 0)
            {
                foreach (Position position in openPositions)
                {
                    ClosePosition(position);
                }
            }

            if (openPositions == null || openPositions.Count == 0)
            {
                OpenPosition(candles);
            }
        }

        private void OpenPosition(List<Candle> candles)
        {
            if (IsMaxVolumeNow())
            {
                if (currentPriceLow < bollingerDownPrice && IsDownGreenPinBar(candles.Last()) && Regime.ValueString != "OnlyShort")
                {
                    botTab.BuyAtLimit(LotSize.ValueDecimal, currentPriceClose + Slippage.ValueInt * botTab.Securiti.PriceStep);
                }

                if (currentPriceHigh > bollingerUpPrice && IsUpRedPinBar(candles.Last()) && Regime.ValueString != "OnlyLong")
                {
                    botTab.SellAtLimit(LotSize.ValueDecimal, currentPriceClose + Slippage.ValueInt * botTab.Securiti.PriceStep);
                }
            }
        }

        private bool IsDownGreenPinBar(Candle candle)
        {
            bool green = candle.Close > candle.Open;
            if (green)
            {
                decimal bodySize = candle.Close - candle.Open;
                decimal taleSize = candle.Open - candle.Low;
                decimal shadowSize = candle.High - candle.Close;
                return bodySize * 3 <= taleSize && bodySize > shadowSize;
            }
            return false;
        }

        private bool IsUpRedPinBar(Candle candle)
        {
            bool red = candle.Open > candle.Close;
            if (red)
            {
                decimal bodySize = candle.Open - candle.Close;
                decimal taleSize = candle.High - candle.Open;
                decimal shadowSize = candle.Close - candle.Low;
                return bodySize * 3 <= taleSize && bodySize > shadowSize;
            }
            return false;
        }

        private bool IsMaxVolumeNow()
        {
            decimal currentVolume = volume.Values.Last();
            for (int i = volume.Values.Count - MaxVolumePeriod.ValueInt; i < volume.Values.Count; i++)
            {
                if (currentVolume < volume.Values[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void ClosePosition(Position position)
        {
            if (position.State == PositionStateType.Open)
            {
                if (position.Direction == Side.Buy)
                {
                    if (currentPriceClose >= bollingerMiddlePrice)
                    {
                        botTab.CloseAtLimit(position, currentPriceClose - Slippage.ValueInt * botTab.Securiti.PriceStep, position.OpenVolume);
                    }
                }

                if (position.Direction == Side.Sell)
                {
                    if (currentPriceClose <= bollingerMiddlePrice)
                    {
                        botTab.CloseAtLimit(position, currentPriceClose + Slippage.ValueInt * botTab.Securiti.PriceStep, position.OpenVolume);
                    }
                }
            }
        }

        private void ParametrsChangeByUserEventHandler()
        {
            if (BollingerLength.ValueInt != bollinger.Lenght || BollingerDeviation.ValueDecimal != bollinger.Deviation)
            {
                bollinger.Lenght = BollingerLength.ValueInt;
                bollinger.Deviation = BollingerDeviation.ValueDecimal;
                bollinger.Reload();
            }

            if (BollingerLength.ValueInt != bollingerSma.Lenght)
            {
                bollingerSma.Lenght = BollingerLength.ValueInt;
                bollingerSma.Reload();
            }
        }

        public override string GetNameStrategyType()
        {
            return "MyBollingerContrTrend";
        }

        public override void ShowIndividualSettingsDialog()
        {
        }
    }
}
