using System.Collections.Generic;
using System.Linq;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.Robots.Oleg.Good
{
    public class MyEnvelopTrend : BotPanel
    {
        private decimal currentPriceClose;
        private decimal currentEnvelopUp;
        private decimal currentEnvelopDown;
        private decimal currentSma;
        private decimal currentAtr;

        private BotTabSimple botTab;
        private Atr atr;
        private MovingAverage sma;
        private Envelops envelops;

        public StrategyParameterString Regime;
        public StrategyParameterInt Slippage;
        public StrategyParameterDecimal LotSize;
        public StrategyParameterInt AtrLength;
        public StrategyParameterDecimal EnvelopDeviation;
        public StrategyParameterInt EnvelopMovingLength;

        public MyEnvelopTrend(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            botTab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            Slippage = CreateParameter("Slipage", 0, 0, 20, 1);
            LotSize = CreateParameter("LotSize", 1, 1.0m, 50, 1);

            // BNB 1h optimization:
            // AvgProfit : atr = 10 | env_dev = 4.2 | env_ma = 10
            // MaxProfit : atr = 10 | env_dev = 1.8 | env_ma = 70
            AtrLength = CreateParameter("Atr length", 10, 5, 50, 1);
            EnvelopDeviation = CreateParameter("Envelop Deviation", 4.2m, 0.3m, 4.2m, 0.3m);
            EnvelopMovingLength = CreateParameter("Envelop Moving Length", 10, 10, 200, 5);

            atr = new Atr(name + "ATR", false);
            atr = (Atr)botTab.CreateCandleIndicator(atr, "ATRArea");
            atr.Save();

            sma = new MovingAverage(name + "SMA", false);
            sma = (MovingAverage)botTab.CreateCandleIndicator(sma, "Prime");
            sma.Save();

            envelops = new Envelops(name + "Envelop", false);
            envelops = (Envelops)botTab.CreateCandleIndicator(envelops, "Prime");
            envelops.Save();

            botTab.CandleFinishedEvent += CandleFinishedEventHandler;
            ParametrsChangeByUser += ParametrsChangeByUserEventHandler;
        }

        private void CandleFinishedEventHandler(List<Candle> candles)
        {
            if (Regime.ValueString == "Off" || 
                atr.Lenght > candles.Count ||
                sma.Lenght > candles.Count || 
                envelops.MovingAverage.Lenght > candles.Count)
            {
                return;
            }

            currentPriceClose = candles.Last().Close;
            currentEnvelopUp = envelops.ValuesUp.Last();
            currentEnvelopDown = envelops.ValuesDown.Last();
            currentSma = sma.Values.Last();
            currentAtr = atr.Values.Last();

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
            if (currentPriceClose > currentEnvelopUp && Regime.ValueString != "OnlyShort")
            {
                botTab.BuyAtLimit(LotSize.ValueDecimal, currentPriceClose + Slippage.ValueInt * botTab.Securiti.PriceStep);
            }

            if (currentPriceClose < currentEnvelopDown && Regime.ValueString != "OnlyLong")
            {
                botTab.SellAtLimit(LotSize.ValueDecimal, currentPriceClose + Slippage.ValueInt * botTab.Securiti.PriceStep);
            }
        }

        private void ClosePosition(Position position)
        {
            if (position.State == PositionStateType.Open)
            {
                if (position.Direction == Side.Buy)
                {
                    if (currentPriceClose < currentSma - currentAtr * 2)
                    {
                        botTab.CloseAtLimit(position, currentPriceClose - Slippage.ValueInt * botTab.Securiti.PriceStep, position.OpenVolume);
                    }
                }

                if (position.Direction == Side.Sell)
                {
                    if (currentPriceClose > currentSma + currentAtr * 2)
                    {
                        botTab.CloseAtLimit(position, currentPriceClose + Slippage.ValueInt * botTab.Securiti.PriceStep, position.OpenVolume);
                    }
                }
            }
        }

        private void ParametrsChangeByUserEventHandler()
        {
            atr.Lenght = AtrLength.ValueInt;
            atr.Reload();

            sma.Lenght = EnvelopMovingLength.ValueInt;
            sma.Reload();

            envelops.Deviation = EnvelopDeviation.ValueDecimal;
            envelops.MovingAverage.Lenght = EnvelopMovingLength.ValueInt;
            envelops.Reload();
        }

        public override string GetNameStrategyType()
        {
            return "MyEnvelopTrend";
        }

        public override void ShowIndividualSettingsDialog()
        {
        }
    }
}
