using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.Util;
using Serilog;

namespace Dotnatter.NodeImpl
{
    public class Core
    {
        public Core(int id, CngKey key, Dictionary<string, int> pmap, IStore store, Channel<Event> commitCh, ILogger logger)
        {
            throw new NotImplementedException();
        }
    }
}