// -------------------------------------------------------------------------------------------------
//
//    Author: Artur Martins Baarionuevo
//    Version: 1.0
//
//    Project: Forex Robot specialized in the Bollinger Bands signals using parameters to reduce losses and to identify the "Breakins" in the borders.
//
//    Versions:
//    1.0 - Initial implementation with auto close in case of a "Break In inversion".
//    
//
//    TODO:
//    * Manual Trailing Stop with Moving Take Profit
//    * Give Option to use middle bolling Line (maybe in a new bot...)
//
//
// -------------------------------------------------------------------------------------------------
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class BollingerBandsBreakIncBot : Robot
    {
        [Parameter("Source")]
        public DataSeries Source { get; set; }

        [Parameter("Quantity (Lots)", DefaultValue = 1, MinValue = 0.01, Step = 0.01)]
        public double Quantity { get; set; }

        [Parameter("Stop Loss (pips)", DefaultValue = 5, MinValue = 1)]
        public double StopLossInPips { get; set; }

        [Parameter("Take Profit (pips)", DefaultValue = 5, MinValue = 1)]
        public double TakeProfitInPips { get; set; }

        [Parameter("Entry after Candle Break In", DefaultValue = true)]
        public bool CandleBreakIn { get; set; }

        [Parameter("Wait for Break In Trend", DefaultValue = true)]
        public bool BreakInTrendCheck { get; set; }

        [Parameter("Autoclose in inversion signal", DefaultValue = true)]
        public bool AutocloseInversion { get; set; }

        [Parameter("Band Height (pips)", DefaultValue = 15, MinValue = 3, Step = 1)]
        public double BandHeightPips { get; set; }

        [Parameter("Bollinger Bands Periods", DefaultValue = 20, MinValue = 5, Step = 1)]
        public int Periods { get; set; }

        [Parameter("Bollinger Bands Deviations", DefaultValue = 2, Step = 0.1)]
        public double Deviations { get; set; }

        [Parameter("Bollinger Bands MA Type")]
        public MovingAverageType MAType { get; set; }

        [Parameter("Crossign Signal Delay In Periods", DefaultValue = 0, MinValue = 0, MaxValue = 5, Step = 1)]
        public int ExecutionDelay { get; set; }


        BollingerBands bollingerBands;

        bool hasCrossedTop = false;
        bool hasCrossedBelow = false;
        int delayCounter;

        int qtdStopLoss;

        protected override void OnStart()
        {
            bollingerBands = Indicators.BollingerBands(Source, Periods, Deviations, MAType);
            delayCounter = ExecutionDelay;
            qtdStopLoss = 0;

            Positions.Closed += OnPositionClosed;
        }

        private void CheckTopAndBottom(double top, double bottom)
        {
            if (top - bottom >= BandHeightPips * Symbol.PipSize)
            {
                //Print("Band Size: ", top - bottom);
                //Print("Configured Band Size: ", BandHeightPips * Symbol.PipSize);

                if (CandleBreakIn)
                {
                    if (Functions.HasCrossedBelow(MarketSeries.Close, bollingerBands.Top, 1))
                    {
                        Print("Market Value {0} Crossed Above Top {1}", MarketSeries.Close.LastValue, bollingerBands.Top.LastValue);
                        //ExecuteMarketOrder(TradeType.Sell, Symbol, volumeInUnits, label, StopLossInPips, TakeProfitInPips);
                        hasCrossedTop = true;
                    }
                    else if (Functions.HasCrossedAbove(MarketSeries.Close, bollingerBands.Bottom, 1))
                    {
                        Print("Market Value {0} Crossed Below Bottom {1}", MarketSeries.Close.LastValue, bollingerBands.Bottom.LastValue);
                        //ExecuteMarketOrder(TradeType.Buy, Symbol, volumeInUnits, label, StopLossInPips, TakeProfitInPips);
                        hasCrossedBelow = true;
                    }
                }
                else
                {
                    if (Functions.HasCrossedAbove(MarketSeries.Close, bollingerBands.Top, 1))
                    {
                        Print("Market Value {0} Crossed Above Top {1}", MarketSeries.Close.LastValue, bollingerBands.Top.LastValue);

                        //ExecuteMarketOrder(TradeType.Sell, Symbol, volumeInUnits, label, StopLossInPips, TakeProfitInPips);
                        hasCrossedTop = true;
                    }
                    else if (Functions.HasCrossedBelow(MarketSeries.Close, bollingerBands.Bottom, 1))
                    {
                        Print("Market Value {0} Crossed Below Bottom {1}", MarketSeries.Close.LastValue, bollingerBands.Bottom.LastValue);

                        //ExecuteMarketOrder(TradeType.Buy, Symbol, volumeInUnits, label, StopLossInPips, TakeProfitInPips);
                        hasCrossedBelow = true;
                    }
                }
            }
        }

        private void CheckOpenPositionsAndCloseInversions()
        {
            foreach (var position in Positions.FindAll("Top Line Sell", Symbol, TradeType.Sell))
            {
                if (Functions.HasCrossedAbove(MarketSeries.High, bollingerBands.Top, 0))
                {
                    Print("Inversion Safety Closure! Highest Price {0} Crossed Above Top {1}", MarketSeries.High.LastValue, bollingerBands.Top.LastValue);
                    ClosePosition(position);
                }
            }

            foreach (var position in Positions.FindAll("Bottom Line Buy", Symbol, TradeType.Buy))
            {
                if (Functions.HasCrossedBelow(MarketSeries.Low, bollingerBands.Bottom, 0))
                {
                    Print("Inversion Safety Closure! Lowest Price {0} Crossed Below Bottom {1}", MarketSeries.Low.LastValue, bollingerBands.Bottom.LastValue);
                    ClosePosition(position);
                }
            }
        }

        private void CheckAndExecute(double volumeInUnits)
        {
            if (hasCrossedTop && delayCounter <= 0 && LastHighPeriodIsFalling())
            {
                ExecuteMarketOrder(TradeType.Sell, Symbol, volumeInUnits, "Top Line Sell", StopLossInPips, TakeProfitInPips);
                hasCrossedTop = false;
                delayCounter = ExecutionDelay;
            }

            if (hasCrossedBelow && delayCounter <= 0 && LastLowPeriodIsRising())
            {
                ExecuteMarketOrder(TradeType.Buy, Symbol, volumeInUnits, "Bottom Line Buy", StopLossInPips, TakeProfitInPips);
                hasCrossedBelow = false;
                delayCounter = ExecutionDelay;
            }
        }

        private bool LastHighPeriodIsFalling()
        {
            if (!BreakInTrendCheck)
            {
                return true;
            }

            if (MarketSeries.High.Last(2) > MarketSeries.High.Last(1))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool LastLowPeriodIsRising()
        {
            if (!BreakInTrendCheck)
            {
                return true;
            }

            if (MarketSeries.Low.Last(2) < MarketSeries.Low.Last(1))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void DelayTick()
        {
            if (delayCounter > 0 && (hasCrossedTop || hasCrossedBelow))
            {
                delayCounter--;
            }
        }

        protected override void OnBar()
        {
            var top = bollingerBands.Top.Last(1);
            var bottom = bollingerBands.Bottom.Last(1);
            var volumeInUnits = Symbol.QuantityToVolumeInUnits(Quantity);

            CheckTopAndBottom(top, bottom);

            CheckAndExecute(volumeInUnits);

            DelayTick();
        }

        protected override void OnTick()
        {
            if (AutocloseInversion)
            {
                CheckOpenPositionsAndCloseInversions();
            }
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args.Reason == PositionCloseReason.StopLoss)
            {
                qtdStopLoss++;
                Print("StopLoss quantity {0}", qtdStopLoss);
            }
        }
    }
}
