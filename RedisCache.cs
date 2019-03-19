using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using Newtonsoft.Json;
using Sitecore.Caching;
using Sitecore.Caching.Generics;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Diagnostics.PerformanceCounters;
using StackExchange.Redis;

namespace Foundation.Caching
{
    public class RedisCache : ICache
    {
        /// <summary>The locker.</summary>
        private readonly ReaderWriterLockSlim locker = new ReaderWriterLockSlim();

        private readonly IDatabase _cache;

        private static ConnectionMultiplexer _connectionMultiplexer;

        /// <summary>The strategy for cache size calculation</summary>
        private volatile ICacheSizeCalculationStrategy strategy;
        /// <summary>The current size.</summary>
        private long currentSize;
        /// <summary>The configuration.</summary>
        private NameValueCollection config;
        /// <summary>Identifies if the cache is enabled.</summary>
        private bool enabled;

        public int CacheExpiration { get; set; }

        public RedisCache(int cacheExpiration, IDatabase connectionResolver)
        {
            _cache = _connectionMultiplexer.GetDatabase();
            CacheExpiration = cacheExpiration;
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        public void Clear()
        {
            this.locker.EnterWriteLock();
            var endpoints = _connectionMultiplexer.GetEndPoints(true);
            foreach (var endpoint in endpoints)
            {
                var server = _connectionMultiplexer.GetServer(endpoint);
                server.FlushAllDatabases();
            }
            this.locker.ExitWriteLock();
        }

        /// <summary>Does nothing since MemoryCache monitors its size.</summary>
        public void Scavenge()
        {
        }

        /// <summary>Gets the records count.</summary>
        /// <value>The count.</value>
        public int Count
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="T:Sitecore.Caching.ICache" /> is enabled.
        /// </summary>
        /// <value>
        ///   <c>true</c> if enabled; otherwise, <c>false</c>.
        /// </value>
        public bool Enabled
        {
            get
            {
                if (this.enabled)
                    return this.MaxSize > 0L;
                return false;
            }
            set => this.enabled = value;
        }

        public ID Id { get; }

        public long MaxSize { get; set; }

        public string Name { get; }

        public long RemainingSpace { get; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="T:Sitecore.Caching.ICache" /> can be scavenged.
        /// <remarks>
        /// <see cref="T:Sitecore.Caching.MemoryCacheAdapter" /> does not support changing this property.
        /// </remarks>
        /// </summary>
        /// <value>
        ///   Always returns <c>true</c> since <see cref="T:System.Runtime.Caching.MemoryCache" /> does not support disabling this property.
        /// </value>
        public bool Scavengable
        {
            get => true;
            set
            {
                Log.SingleWarn("MemoryCacheAdapter does not support changing property 'Scavengable'.", (object)this);
            }
        }

        public long Size { get; }

        public AmountPerSecondCounter ExternalCacheClearingsCounter { get; set; }

        /// <summary>
        /// Adds the specified data to the cache by the specified key.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="data">The data to cache.</param>
        public void Add(string key, object data)
        {
            Assert.ArgumentNotNull((object)key, nameof(key));
            Assert.ArgumentNotNull(data, nameof(data));
            _cache.StringSet(key, JsonConvert.SerializeObject(data), TimeSpan.FromHours(CacheExpiration));
        }

        /// <summary>Adds the specified object.</summary>
        /// <param name="key">The cache key.</param>
        /// <param name="data">The data to cache.</param>
        /// <param name="slidingExpiration">The sliding expiration.</param>
        public void Add(string key, object data, TimeSpan slidingExpiration)
        {
            _cache.StringSet(key, JsonConvert.SerializeObject(data), slidingExpiration);
        }

        /// <summary>Adds the specified object.</summary>
        /// <param name="key">The cache key.</param>
        /// <param name="data">The data to cache.</param>
        /// <param name="absoluteExpiration">The absolute expiration.</param>
        public void Add(string key, object data, DateTime absoluteExpiration)
        {
            _cache.StringSet(key, JsonConvert.SerializeObject(data), absoluteExpiration.Subtract(DateTime.UtcNow));
        }

        /// <summary>
        /// Adds the specified data to the cache by the specified key.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="data">The data to cache.</param>
        /// <param name="removedHandler">The removed handler.</param>
        public void Add(string key, object data, EventHandler<EntryRemovedEventArgs<string>> removedHandler)
        {
            throw new NotImplementedException();
        }

        /// <summary>Adds the specified object.</summary>
        /// <param name="key">The cache key.</param>
        /// <param name="data">The data to cache.</param>
        /// <param name="slidingExpiration">The sliding expiration.</param>
        /// <param name="absoluteExpiration">The absolute expiration.</param>
        public void Add(string key, object data, TimeSpan slidingExpiration, DateTime absoluteExpiration)
        {
            if (!this.Enabled)
                return;
            if (slidingExpiration == CacheManager.NoSlidingExpiration)
                this.Add(key, data, absoluteExpiration);
            else
                this.Add(key, data, slidingExpiration);
        }

        /// <summary>Adds the specified object.</summary>
        /// <param name="key">The cache key.</param>
        /// <param name="data">The data to cache.</param>
        /// <param name="slidingExpiration">The sliding expiration.</param>
        /// <param name="absoluteExpiration">The absolute expiration.</param>
        /// <param name="removedHandler">The removed handler.</param>
        public void Add(string key, object data, TimeSpan slidingExpiration, DateTime absoluteExpiration, EventHandler<EntryRemovedEventArgs<string>> removedHandler)
        {
            throw new NotImplementedException();
        }

        /// <summary>Determines whether the specified key contains key.</summary>
        /// <param name="key">The key.</param>
        /// <returns>
        ///   <c>true</c> if the specified key is contained; otherwise, <c>false</c>.
        /// </returns>
        public bool ContainsKey(string key)
        {
            Assert.ArgumentNotNull((object)key, nameof(key));
            return _cache.KeyExists(key);
        }

        /// <summary>Gets the cache keys.</summary>
        /// <returns>The cache keys.</returns>
        public string[] GetCacheKeys()
        {
            //return this._cache.Select(pair => pair.Key).ToArray<string>();
            throw new NotImplementedException();
        }

        /// <summary>Gets the value.</summary>
        /// <param name="key">The key.</param>
        /// <returns>Value or null.</returns>
        public object GetValue(string key)
        {
            Assert.ArgumentNotNull((object)key, nameof(key));
            var obj = _cache.StringGet(key);
            if (string.IsNullOrEmpty(obj))
            {
                CachingCount.CacheMisses.Increment();
            }
            else
            {
                CachingCount.CacheHits.Increment();
            }

            return obj;
        }

        /// <summary>Removes the specified key.</summary>
        /// <param name="key">The key.</param>
        public void Remove(string key)
        {
            Assert.ArgumentNotNull((object)key, nameof(key));
            _cache.KeyDelete(key);
        }

        /// <summary>
        /// Removes entries from the cache using the specified predicate.
        /// </summary>
        /// <param name="predicate">The predicate.</param>
        /// <returns>Keys which have been removed from cache.</returns>
        public ICollection<string> Remove(Predicate<string> predicate)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the <see cref="T:System.Object" /> with the specified key from the cache.
        /// </summary>
        /// <value>
        /// The <see cref="T:System.Object" />.
        /// </value>
        /// <param name="key">The key.</param>
        /// <returns>Cached object or null.</returns>
        public object this[string key] => this.GetValue(key);

        public ICacheSizeCalculationStrategy CacheSizeCalculationStrategy { get; }

        /// <summary>
        /// Removes cache records with keys starting with the prefix.
        /// </summary>
        /// <param name="prefix">The prefix.</param>
        public void RemovePrefix(string prefix)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes cache records with keys containing the key part.
        /// </summary>
        /// <param name="keyPart">The key part.</param>
        public void RemoveKeysContaining(string keyPart)
        {
            foreach (string cacheKey in this.GetCacheKeys())
            {
                if (cacheKey.IndexOf(keyPart, StringComparison.InvariantCultureIgnoreCase) > -1)
                    this.Remove(cacheKey);
            }
        }

        /// <summary>Gets DateTimeOffset from DateTime.</summary>
        /// <param name="dateTime">The date time.</param>
        /// <returns>Converted date time.</returns>
        private static DateTimeOffset GetFromDateTime(DateTime dateTime)
        {
            if (dateTime == CacheManager.InfiniteAbsoluteExpiration)
                return DateTimeOffset.MaxValue;
            return (DateTimeOffset)dateTime;
        }

        /// <summary>Increases the size of the cache using strategy.</summary>
        /// <param name="key">The key.</param>
        /// <param name="data">The data.</param>
        private void IncreaseCacheSize(object key, object data)
        {
            Interlocked.Add(ref this.currentSize, this.CacheSizeCalculationStrategy.GetCacheRecordSize(key, data));
        }

        /// <summary>Caches the entry removed.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="T:Sitecore.Caching.Generics.EntryRemovedEventArgs`1" /> instance containing the event data.</param>
        private void CacheEntryRemoved(object sender, EntryRemovedEventArgs<string> e)
        {
            Interlocked.Add(ref this.currentSize, -this.CacheSizeCalculationStrategy.GetCacheRecordSize((object)e.Entry.Key, e.Entry.Data));
        }
    }
}
