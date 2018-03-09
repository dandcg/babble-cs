using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Babble.Core.Util;

namespace Babble.Core.HashgraphImpl.Model
{
    public class RoundInfo
    {
        private bool queued;

        public RoundInfo()
        {
         
        }

        public Dictionary<string, RoundEvent> Events { get; set; }  = new Dictionary<string, RoundEvent>();

        public void SetQueued()
        {
            queued = true;
        }

        public bool Queued()
        {
            return queued;
        }

        public void AddEvent(string x, bool witness)
        {
            if (!Events.ContainsKey(x))
            {
                Events.Add(x, new RoundEvent {Witness = witness});
            }
        }

        public void SetFame(string x, bool f)
        {
            if (Events.TryGetValue(x, out var e))
            {
                e.Famous = f;
            }

            else
            {
                Events.Add(x, new RoundEvent {Witness = true, Famous = f});
            }
        }

        //return true if no witnesses' fame is left undefined
        public bool WitnessesDecided()
        {
            return !Events.Values.Any(w => w.Witness && w.Famous == null);
        }

        //return witnesses
        public string[] Witnesses()

        {
            return Events
                .Where(w => w.Value.Witness)
                .Select(s => s.Key)
                .ToArray();
        }

        //return famous witnesses
        public string[] FamousWitnesses()
        {
            return Events
                .Where(w => w.Value.Witness && w.Value.Famous == true)
                .Select(s => s.Key)
                .ToArray();
        }

        public bool IsDecided(string witness)
        {
            if (Events.TryGetValue(witness, out var e))
            {
                return e.Witness && e.Famous != null;
            }

            return false;
        }

        public BigInteger PseudoRandomNumber()

        {
            BigInteger res=0;

            var x = 0;

            foreach (var e in Events)
            {
                if (e.Value.Witness && e.Value.Famous == true)
                {
                    //Todo: Clarify this...

                    //var s = BigInteger.Parse(x);
                    //res = res.Xor(s);

                    //s, _:= new(big.Int).SetString(x, 16)
                    //res = res.Xor(res, s)
                }

                x++;
            }
            
            return res;
        }

        //json encoding of body and signature
        public byte[] Marhsal()
        {
            return this.SerializeToByteArray();
        }

        public static RoundInfo Unmarshal(byte[] data)
        {
            return data.DeserializeFromByteArray<RoundInfo>();
        }
    }
}