using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Babble.Core.Crypto;
using Babble.Core.Util;

namespace Babble.Core.HashgraphImpl.Model
{
    public class Block
    {
        public BlockBody Body { get; set; }
        public Dictionary<string, byte[]> Signatures { get; set; }

        private byte[] hash;

        public Block()
        {

        }
        public Block(int blockIndex, int roundReceived, byte[] frameHash, byte[][] transactions)
        {
            Body = new BlockBody
            {
                Index = blockIndex,
                RoundReceived = roundReceived,
                Transactions = transactions,
                FrameHash =frameHash
            };

            Signatures = new Dictionary<string, byte[]>();
        }

        public static (Block block, HashgraphError err) NewBlockFromFrame(int blockIndex, Frame frame)
        {
            var frameHash = frame.Hash();
     
            var transactions = new List<byte[]> { };
            foreach (var e in frame.Events)
            {
                transactions.AddRange(e.Transactions());
            }

            return (new Block(blockIndex, frame.Round, frameHash, transactions.ToArray()),null);

        }





        public int Index()
        {
            return Body.Index;
        }

        public byte[][] Transactions()
        {
            return Body.Transactions;
        }

        public int RoundReceived()
        {
            return Body.RoundReceived;
        }

        public byte[] StateHash()
        {
            return Body.StateHash;
        }


        public byte[] FrameHash()
        {
            return Body.FrameHash;
        }

        public  BlockSignature[] GetSignatures()
        {
            var res = new List<BlockSignature>(Signatures.Count);
            var i = 0;

            foreach (var s in Signatures)
            {
                var val = s.Key;
                var sig = s.Value;

                var validatorBytes = StringUtils.GetBytesFromHexString(val.Skip(2).ToString());
                res[i] = new BlockSignature
                {
                    Validator = validatorBytes,
                    Index = Index(),
                    Signature = sig,
                };
                i++;
            }

            return res.ToArray();
        }


        public (BlockSignature bs, HashgraphError err ) GetSignature(string validator)
        {
            var ok = Signatures.TryGetValue(validator, out var sig);
            if (!ok)
            {
                return (null, new HashgraphError("signature not found"));
            }

            var validatorBytes =  validator.FromHex();
            return (new BlockSignature
            {
                Validator = validatorBytes,
                Index = Index(),
                Signature = sig
            }, null);
        }

        public void AppendTransactions(byte[][] txs)
        {
            Body.Transactions = Body.Transactions.Concat(txs).ToArray();
        }

        public byte[] Marshal()
        {
            return this.SerializeToByteArray();
        }

        public static BlockBody Unmarshal(byte[] data)
        {
            return data.DeserializeFromByteArray<BlockBody>();
        }

        public byte[] Hash()
        {
            return hash ?? (hash = Body.Hash());
        }

        public string Hex()
        {
            return Hash().ToHex();
        }

        //ecdsa sig
        public (BlockSignature bs, HashgraphError err) Sign(CngKey privKey)
        {
            var signBytes = Body.Hash();

            var signiture = CryptoUtils.Sign(privKey, signBytes);

            var bs = new BlockSignature
            {
                Validator = CryptoUtils.FromEcdsaPub(privKey),
                Index = Index(),
                Signature = signiture
            };

            return (bs, null);
        }

        public HashgraphError SetSignature(BlockSignature bs)
        {
            Signatures[bs.ValidatorHex()] = bs.Signature;
            return null;
        }

        public (bool res, HashgraphError err) Verify(BlockSignature sig)
        {
            var signBytes = Body.Hash();
            var pubKey = CryptoUtils.ToEcdsaPub(sig.Validator);
            var signature = sig.Signature;
            return (CryptoUtils.Verify(pubKey, signBytes, signature), null);
        }
    }
}