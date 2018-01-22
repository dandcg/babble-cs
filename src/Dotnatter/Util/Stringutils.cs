using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Dotnatter.Util
{
   public static class StringUtils
    {

        public static string DumpToString(this object o)
        {
 
            string json = JsonConvert.SerializeObject(o, Formatting.Indented);
            return json;

        }
    }
}
