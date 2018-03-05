using System.Diagnostics;

namespace Dotnatter.Core.Util
{
   public static class StopwatchUtils
   {

       public static long Nanoseconds(this Stopwatch watch)
       {
        return   (watch.ElapsedTicks / Stopwatch.Frequency) * 1000000000;
       }

   }
}
