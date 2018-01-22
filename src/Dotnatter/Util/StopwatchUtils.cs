﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Dotnatter.Util
{
   public static class StopwatchUtils
   {

       public static long Nanoseconds(this Stopwatch watch)
       {
        return   (watch.ElapsedTicks / Stopwatch.Frequency) * 1000000000;
       }

   }
}
