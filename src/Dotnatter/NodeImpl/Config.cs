using System;

namespace Dotnatter.NodeImpl
{
    public class Config
    {
        public TimeSpan HeartbeatTimeout { get; set; }
        public TimeSpan TcpTimeout { get; set; }
        public int CacheSize { get; set; }
        public int SyncLimit { get; set; }
        public string StoreType { get; set; }
        public string StorePath { get; set; }

        public Config(TimeSpan heartbeatTimeout,
            TimeSpan tcpTimeout,
            int cacheSize,
            int syncLimit,
            string storeType,
            string storePath)

        {
            HeartbeatTimeout = heartbeatTimeout;
            TcpTimeout = tcpTimeout;
            CacheSize = cacheSize;
            SyncLimit = syncLimit;
            StoreType = storeType;
            StorePath = storePath;
        }

        protected Config()
        {
        }

        public static Config DefaultConfig()
        {
            var storeType = "badger";
            var storePath = "badger";

            return new Config
            {
                HeartbeatTimeout = new TimeSpan(0, 0, 0, 0, 1000),
                TcpTimeout = new TimeSpan(0, 0, 0, 0, 1000),
                CacheSize = 500,
                SyncLimit = 100,
                StoreType = storeType,
                StorePath = storePath
            };
        }

        public static Config TestConfig()
        {
            var config = DefaultConfig();

            config.StoreType = "inmem";

            return config;

        }
    }
}