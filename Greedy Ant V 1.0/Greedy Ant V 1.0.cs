using System;
using System.Linq;
using cAlgo.API;
using System.Collections.Generic;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;
using System.Globalization;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GreedyAnt : Robot
    {
        [Parameter("Select Risk in %: ", Group = "MONEY MANAGEMENT", DefaultValue = 5)]
        public double Risk { get; set; }

        [Parameter("Main Risk/Reward: ", Group = "MONEY MANAGEMENT", DefaultValue = 1.35, MinValue = 1, MaxValue = 5)]
        public double MainRW { get; set; }

        [Parameter("SL in pips to change \nRW ratio: ", Group = "MONEY MANAGEMENT", DefaultValue = 50, MinValue = 20, MaxValue = 100000000)]
        public double SlToChangeRW { get; set; }

        [Parameter("0 profit SL: ", Group = "MONEY MANAGEMENT", DefaultValue = 2, MinValue = 0, MaxValue = 1000000)]
        public double ZeroProfitSL { get; set; }

        [Parameter("Reduced Risk/Reward: ", Group = "MONEY MANAGEMENT", DefaultValue = 1.25, MinValue = 1, MaxValue = 5)]
        public double ReducedRW { get; set; }

        [Parameter("Closing volume in % at 50%: ", Group = "MONEY MANAGEMENT", DefaultValue = 70, MinValue = 25, MaxValue = 100)]
        public double ClosingVolume { get; set; }

        [Parameter("Account Margin: ", Group = "MONEY MANAGEMENT", DefaultValue = 500, MinValue = 10, MaxValue = 2000)]
        public int Margin { get; set; }

        //--------------------------------------------------------------------------------------------------------------------------------

        [Parameter("Min Distance from ma Breaks", Group = "DISTANCES CONTROL", DefaultValue = "10", MinValue = "2", MaxValue = "500000")]
        public int DistanceBreak { get; set; }

        [Parameter("Max Distance from ma Breaks", Group = "DISTANCES CONTROL", DefaultValue = "40", MinValue = "20", MaxValue = "500000")]
        public int MaxDistanceBreak { get; set; }

        [Parameter("Max Distance from ma Bounces", Group = "DISTANCES CONTROL", DefaultValue = "20", MinValue = "5", MaxValue = "500000")]
        public int DistanceToBounce { get; set; }

        [Parameter("Max. allowed SL in pips\nto allow open Breakout order", Group = "POSITION CONTROLS", DefaultValue = 65)]
        public double MaxSLBreakoutDistance { get; set; }

        [Parameter("Min. allowed BB distance to allow open Breakout order", Group = "POSITION CONTROLS", DefaultValue = 90)]
        public double MinBBBreakoutDistance { get; set; }

        //--------------------------------------------------------------------------------------------------------------------------------

        [Parameter("ADX Period ", Group = "INDICATOR CONTROLS", DefaultValue = "14", MinValue = "10", MaxValue = "50")]
        public int ADXPeriod { get; set; }

        [Parameter("ADX enter level ", Group = "INDICATOR CONTROLS", DefaultValue = "25", MinValue = "10", MaxValue = "50")]
        public int ADXLevel { get; set; }

        [Parameter("CCI Period ", Group = "INDICATOR CONTROLS", DefaultValue = "20", MinValue = "10", MaxValue = "50")]
        public int CciPeriod { get; set; }

        [Parameter("SMA Period ", Group = "INDICATOR CONTROLS", DefaultValue = "200", MinValue = "50", MaxValue = "500")]
        public int SmaPeriod { get; set; }

        [Parameter("SMA Source ", Group = "INDICATOR CONTROLS")]
        public DataSeries Source { get; set; }

        //BB to detect low volatility------------------------------------------------------------------------------------------------------------------------------
/*[Parameter("BOL Source", Group = "INDICATOR CONTROLS Bolinger")]
        public DataSeries SourceB { get; set; }

        [Parameter("BandPeriods", Group = "INDICATOR CONTROLS Bolinger", DefaultValue = 200)]
        public int BandPeriod { get; set; }

        [Parameter("Std", Group = "INDICATOR CONTROLS Bolinger", DefaultValue = 2)]
        public double std { get; set; }

        [Parameter("MAType", Group = "INDICATOR CONTROLS Bolinger")]
        public MovingAverageType MAType { get; set; }
        */


                private WeightedMovingAverage sma;
        private CommodityChannelIndex cci;
        private AverageDirectionalMovementIndexRating adx;
        //private BollingerBands boll;

        TimeSpan countStart = TimeSpan.ParseExact("04:30", "hh\\:mm", null);
        TimeSpan countStop = TimeSpan.ParseExact("22:00", "hh\\:mm", null);

        double CurrentLotSize;
        double LastSellSL = 0;
        double LastBuySL = 0;
        double ZeroBuySL;
        double UpCrossPrice;
        double DownCrossPrice;
        bool CanMakeAnotherBuy = false;
        bool CanMakeAnotherSell = false;
        //bool CanPositionBuyBeModified = true;
        //bool CanPositionSellBeModified = true;
        bool CanMakeAnotherBuyBounce = false;
        //bool CanMakeAnotherSellBounce = false;
        double CurrentDistanceToBuySL;
        double CurrentDistanceToBuyTP;
        double CurrentDistanceToSellSL;
        double CurrentDistanceToSellTP;
        protected override void OnStart()
        {
            sma = Indicators.WeightedMovingAverage(Source, SmaPeriod);
            cci = Indicators.CommodityChannelIndex(CciPeriod);
            adx = Indicators.AverageDirectionalMovementIndexRating(ADXPeriod);
            //boll = Indicators.BollingerBands(Source, BandPeriod, std, MAType);
            Positions.Modified += OnPositionsModified;
            Positions.Opened += OnPositionsOpened;
            //Positions.Closed += OnPositionsClosed;
        }
        private void OnPositionsOpened(PositionOpenedEventArgs args)
        {
            var position = args.Position;
            Print("Opened new pos type: {0}", position.Label);
            CanMakeAnotherBuy = false;
            CanMakeAnotherSell = false;
        }
        private void OnPositionsModified(PositionModifiedEventArgs args)
        {
            var position = args.Position;

            Print("Position {0} is modified , with closed volume {1}", position.Label, position.VolumeInUnits);
        }

        public List<double> listOfSellSL = new List<double> 
        {

                    };
        public List<double> listOfBuySL = new List<double> 
        {

                    };

        protected override void OnBar()
        {
            //Lotsize calculation
            double balance = Account.Balance;
            double requiredMargin = (balance / 100) * Risk;
            double lotSizeInVolume = Math.Round((requiredMargin * Margin) / 1000.0, 0) * 1000;
            CurrentLotSize = lotSizeInVolume;


            //SLs calculations -----------------------------------------------------------------
            bool sellSLin = cci.Result.HasCrossedAbove(150, 3);
            bool sellSlout = cci.Result.HasCrossedBelow(150, 3);
            bool buySLin = cci.Result.HasCrossedBelow(-150, 3);
            bool buySLout = cci.Result.HasCrossedAbove(-150, 3);

            if (sellSLin && !sellSlout && Bars.ClosePrices.Last(1) > sma.Result.LastValue)
            {
                //Print("Im in SELL SL range! Adding new highs to my memory..");
                listOfSellSL.Add(Bars.HighPrices.Last(1));
            }
            else
            {

                try
                {
                    //Print("Im out from SELL SL range! Finding one max high and add to new SL. \n Cleaning memory..");
                    double maxSL = listOfSellSL.Max();
                    LastSellSL = maxSL;
                    //Print("New given SL is: " + LastSellSL);
                    listOfSellSL.Clear();

                } catch (InvalidOperationException)
                {
                    //Print("List of SELL SLs is emty, waiting for new values, current value is: " + LastSellSL);
                }

            }

            if (buySLin && !buySLout && Bars.ClosePrices.Last(1) < sma.Result.LastValue)
            {
                //Print("Im in BUY SL range! Adding new lows to my memory..");
                listOfBuySL.Add(Bars.LowPrices.Last(1));
            }
            else
            {
                try
                {
                    double minSL = listOfBuySL.Min();
                    LastBuySL = minSL;
                    listOfBuySL.Clear();
                } catch (InvalidOperationException)
                {
                    //Print("List of BUY SLs is emty, waiting for new values, current value is: " + LastBuySL);
                }
            }
            //Drawing SL levels..

            if (LastBuySL != 0)
            {
                Chart.DrawHorizontalLine("Last buy sl", LastBuySL, Color.Gray);
            }
            if (LastSellSL != 0)
            {
                Chart.DrawHorizontalLine("Last sell sl", LastSellSL, Color.Gray);
            }

            // Distances calculations-----------------------------------------------------------------------------
            //double distanceToMa = (Bars.ClosePrices.LastValue - sma.Result.LastValue) / Symbol.PipSize;
            double distanceToMaPriceBuyBreak = (sma.Result.LastValue + DistanceBreak * Symbol.PipSize);
            double distanceToMaPriceSellBreak = (sma.Result.LastValue - DistanceBreak * Symbol.PipSize);

            double maxDistanceToAllowBuyBreak = (sma.Result.LastValue + MaxDistanceBreak * Symbol.PipSize);
            double maxDistanceToAllowSellBreak = (sma.Result.LastValue - MaxDistanceBreak * Symbol.PipSize);

            //double bollingerDistance = (boll.Top.LastValue - boll.Bottom.LastValue) / Symbol.PipSize;

            double distanceToMaPriceBuyBounce = (sma.Result.LastValue + DistanceToBounce * Symbol.PipSize);
            double distanceToMaPriceSellBounce = (sma.Result.LastValue - DistanceToBounce * Symbol.PipSize);

            //Breakouts limits pos count conditions-------------------------------------------------------------------
            //Print(CanMakeAnotherSell);
            if (Bars.ClosePrices.HasCrossedAbove(sma.Result.LastValue, 1))
            {
                CanMakeAnotherBuy = true;
                CanMakeAnotherSell = false;
                double _upCrossPrice = sma.Result.LastValue;
                UpCrossPrice = _upCrossPrice;
            }
            if (UpCrossPrice != 0 && (Bars.ClosePrices.LastValue - UpCrossPrice) / Symbol.PipSize > MaxDistanceBreak)
            {
                CanMakeAnotherBuy = false;
                UpCrossPrice = 0;
            }
            if (Bars.ClosePrices.HasCrossedBelow(sma.Result.LastValue, 1))
            {
                CanMakeAnotherBuy = false;
                CanMakeAnotherSell = true;
                double _downCrossPrice = sma.Result.LastValue;
                DownCrossPrice = _downCrossPrice;
            }

            if (DownCrossPrice != 0 && (DownCrossPrice - Bars.ClosePrices.LastValue) / Symbol.PipSize > MaxDistanceBreak)
            {
                CanMakeAnotherSell = false;
                DownCrossPrice = 0;
            }


            //Opening pos---------------------------------------------------------------------------------------------

            if (CanMakeAnotherBuy && Bars.ClosePrices.LastValue > distanceToMaPriceBuyBreak && adx.ADX.LastValue > ADXLevel && LastBuySL != 0 && Server.Time.TimeOfDay >= countStart && Server.Time.TimeOfDay <= countStop)
            {

                var _distanceToBuySL = (Bars.ClosePrices.LastValue - LastBuySL) / Symbol.PipSize;
                CurrentDistanceToBuySL = _distanceToBuySL;
                var _distanceToBuyTP = ((Bars.ClosePrices.LastValue - LastBuySL) / Symbol.PipSize) * MainRW;

                if (CurrentDistanceToBuySL > SlToChangeRW)
                {
                    CurrentDistanceToBuyTP = ((Bars.ClosePrices.LastValue - LastBuySL) / Symbol.PipSize) * ReducedRW;
                }
                else
                {
                    CurrentDistanceToBuyTP = _distanceToBuyTP;
                }
                if (CurrentDistanceToBuySL < MaxSLBreakoutDistance)
                {
                    ExecuteMarketOrder(TradeType.Buy, SymbolName, lotSizeInVolume, "GA Breakout buy", CurrentDistanceToBuySL, CurrentDistanceToBuyTP);

                }
                else
                {
                    Print("Pos break buy cant be opened because current {0} distance is more then user defined {1}", _distanceToBuySL, MaxSLBreakoutDistance);
                }

            }

            if (CanMakeAnotherSell && Bars.ClosePrices.LastValue < distanceToMaPriceSellBreak && adx.ADX.LastValue > ADXLevel && LastSellSL != 0 && Server.Time.TimeOfDay >= countStart && Server.Time.TimeOfDay <= countStop)
            {

                var _distanceToSellSL = (LastSellSL - Bars.ClosePrices.LastValue) / Symbol.PipSize;
                CurrentDistanceToSellSL = _distanceToSellSL;
                var _distanceToSellTP = ((LastSellSL - Bars.ClosePrices.LastValue) / Symbol.PipSize) * MainRW;

                if (CurrentDistanceToSellSL > SlToChangeRW)
                {
                    CurrentDistanceToSellTP = ((LastSellSL - Bars.ClosePrices.LastValue) / Symbol.PipSize) * ReducedRW;
                }
                else
                {
                    CurrentDistanceToSellTP = _distanceToSellTP;
                }
                if (CurrentDistanceToSellSL < MaxSLBreakoutDistance)
                {
                    ExecuteMarketOrder(TradeType.Sell, SymbolName, lotSizeInVolume, "GA Breakout sell", CurrentDistanceToSellSL + 7, CurrentDistanceToSellTP);
                }
                else
                {
                    Print("Pos break sell cant be opened because current {0} distance is more then user defined {1}", _distanceToSellSL, MaxSLBreakoutDistance);
                }


            }

            // Bounces ---------------------------------------------------------------------------------------------
        }
        /*if (cci.Result.HasCrossedAbove(-150, 1) && !cci.Result.HasCrossedBelow(-150, 1) && Bars.ClosePrices.LastValue < distanceToMaPriceBuyBounce &&
                Bars.ClosePrices.LastValue > sma.Result.LastValue)
            {
            ExecuteMarketOrder(TradeType.Buy, SymbolName, 5000, "GA Bounce buy", 25, 20);
            //Print("*");

            }*/

        // Retraces -------------------------------------------------------------------------------------------------
/*if (bollingerDistance < MinBBBreakoutDistance &&  Bars.ClosePrices.HasCrossedAbove(boll.Bottom.LastValue,1))
            {
                ExecuteMarketOrder(TradeType.Buy, SymbolName, lotSizeInVolume, "GA retrace buy", 60, 60);
            }
            if (bollingerDistance < MinBBBreakoutDistance && Bars.ClosePrices.HasCrossedBelow(boll.Top.LastValue, 1))
            {
                ExecuteMarketOrder(TradeType.Sell, SymbolName, lotSizeInVolume, "GA retrace sell", 60, 60);
            }*/

                protected override void OnTick()
        {
            foreach (var position in Positions)
            {


                if (position.TradeType == TradeType.Buy && position != null)
                {
                    double newBuySL = position.EntryPrice + ZeroProfitSL * Symbol.PipSize;
                    var distanceToTpInPips = Convert.ToDouble((position.TakeProfit - position.EntryPrice) / Symbol.PipSize);
                    ZeroBuySL = newBuySL;
                    if (position.Pips > distanceToTpInPips / 2 && position.StopLoss < newBuySL)
                    {
                        //Print("Modifying.." + positionsbuy.VolumeInUnits);
                        ModifyPosition(position, newBuySL, position.TakeProfit);


                        ClosePosition(position, (position.VolumeInUnits / 100) * ClosingVolume);
                    }

                }


                if (position.TradeType == TradeType.Sell && position != null)
                {
                    double newSellSL = position.EntryPrice - ZeroProfitSL * Symbol.PipSize;
                    var distanceToTpInPips = Convert.ToDouble((position.EntryPrice - position.TakeProfit) / Symbol.PipSize);


                    if (position.Pips > distanceToTpInPips / 2 && position.StopLoss > newSellSL)
                    {
                        ModifyPosition(position, newSellSL, position.TakeProfit);
                        ClosePosition(position, (position.VolumeInUnits / 100) * ClosingVolume);
                    }

                }

            }


        }

        protected override void OnStop()
        {
            listOfSellSL.Clear();
            listOfBuySL.Clear();
            LastSellSL = 0;
            LastBuySL = 0;
        }
    }
}
