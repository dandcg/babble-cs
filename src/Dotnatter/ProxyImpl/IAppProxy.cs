using System;
using System.Collections.Generic;
using System.Text;
using Dotnatter.HashgraphImpl;
using Dotnatter.NodeImpl;
using Dotnatter.Util;

namespace Dotnatter.ProxyImpl
{

public interface IAppProxy
{
    Channel<byte[]> SubmitCh();
    ProxyError CommitTx(byte[] tx);
}
}
