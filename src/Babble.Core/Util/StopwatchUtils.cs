using System;
using System.Diagnostics;

namespace Babble.Core.Util
{
   public static class StopwatchUtils
   {

       public static decimal Nanoseconds(this Stopwatch watch)
       {
        return   Decimal.Round((decimal)watch.ElapsedTicks / Stopwatch.Frequency * 1000000000);
       }

   }
}
