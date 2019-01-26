using System;
using Newtonsoft.Json;

namespace Babble.Test.Utils
{
    public static class DebugExtensions
    {
        public static void Dump(this object o)
        {
 
            string json = JsonConvert.SerializeObject(o, Formatting.Indented);
            Console.WriteLine(json);

        }

     
    }
}