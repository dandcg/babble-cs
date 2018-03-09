using System.Collections.Generic;

namespace Babble.Core.HashgraphImpl.Model
{
    public class WireEvent
    {
        public WireBody Body { get; set; }
        public byte[] Signiture { get; set; }

 
       public BlockSignature[] BlockSignatures(byte[] validator)
       
       {
            if (Body.BlockSignatures != null)
            {
                var blockSignatures = new List<BlockSignature>();
              
                foreach (var bs in Body.BlockSignatures)
                {
                    blockSignatures.Add(new BlockSignature
                    {
                        Validator = validator,
                        Index = bs.Index,
                        Signature = bs.Signature,
                    });
                }

                return blockSignatures.ToArray();
            }

           return null;
       }

    }
}