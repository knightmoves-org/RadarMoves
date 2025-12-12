// using System.Collections;
// using System.Collections.Generic;
// using StackExchange.Redis;
// using StackExchange.Redis;
// using System;
// using System.Collections;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading.Tasks;

// // Corresponds to Python's collections.abc.Mapping
// namespace RadarMoves.Server.Data;
// // public class MyMapping<K, V> : IMapping<K, V> {
// //     public V this[K key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
// //     public IEnumerable<K> Keys => throw new NotImplementedException();
// //     public IEnumerable<V> Values => throw new NotImplementedException();
// //     public int Count => throw new NotImplementedException();
// //     public bool ContainsKey(K key) => throw new NotImplementedException();
// //     public bool TryGetValue(K key, out V value) => throw new NotImplementedException();
// //     public IEnumerator<KeyValuePair<K, V>> GetEnumerator() => throw new NotImplementedException();
// //     IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
// // }
// public interface IMapping<K, V> : IReadOnlyDictionary<K, V> {
//     public IEnumerator<V> this[IEnumerator<K> keys] {
//         get {
//             while (keys.MoveNext()) {
//                 yield return this[keys.Current];
//             }
//         }
//     }
// }
// public interface IMutableMapping<K, V> : IDictionary<K, V> {
//     public IEnumerator<V> this[IEnumerator<K> keys] {
//         get {
//             while (keys.MoveNext()) {
//                 yield return this[keys.Current];
//             }
//         }
//         set {
//             while (keys.MoveNext()) {
//                 this[keys.Current] = value.Current;
//             }
//         }
//     }
// }

// public class RedisMapping<K, V> : IMutableMapping<K, V> {
//     private readonly IDatabase _database;
//     private readonly string _keyPrefix; // Use a prefix to namespace Redis keys

//     public RedisMapping(IConnectionMultiplexer connectionMultiplexer, string keyPrefix = "") {
//         _database = connectionMultiplexer.GetDatabase();
//         _keyPrefix = keyPrefix ?? "";
//     }

//     // --- Helper Methods for Serialization/Key Formatting ---

//     private RedisKey FormatKey(K key) => $"{_keyPrefix}:{key}";
//     private RedisValue FormatValue(V value) => Newtonsoft.Json.JsonConvert.SerializeObject(value);
//     private V ParseValue(RedisValue value) => Newtonsoft.Json.JsonConvert.DeserializeObject<V>(value);

//     // --- IReadOnlyDictionary & IDictionary Implementations (Single Key Access) ---

//     public V this[K key] {
//         get {
//             RedisValue redisValue = _database.StringGet(FormatKey(key));
//             if (redisValue.IsNullOrEmpty) {
//                 throw new KeyNotFoundException($"The key '{key}' was not found in Redis.");
//             }
//             return ParseValue(redisValue);
//         }
//         set => _database.StringSet(FormatKey(key), FormatValue(value));
//     }

//     public bool ContainsKey(K key) => _database.KeyExists(FormatKey(key));

//     public bool TryGetValue(K key, out V value) {
//         RedisValue redisValue = _database.StringGet(FormatKey(key));
//         if (redisValue.IsNullOrEmpty) {
//             value = default!; // Use default value if not found
//             return false;
//         }

//         value = ParseValue(redisValue);
//         return true;
//     }

//     // Note: Iteration over all keys in Redis is generally inefficient (O(N) operation) and should be used with caution.
//     // Full implementations require using IServer.Keys() and managing cursor-based scanning for production systems.
//     public IEnumerable<K> Keys => throw new NotImplementedException("Retrieving all keys from Redis is not efficient for a mapping implementation.");
//     public IEnumerable<V> Values => throw new NotImplementedException("Retrieving all values from Redis is not efficient for a mapping implementation.");
//     public int Count => (int)_database.StringGet(FormatKey("count_key")); // Requires manual counting/tracking
//     public void Add(K key, V value) => _database.StringSet(FormatKey(key), FormatValue(value), When.NotExists);
//     public bool Remove(K key) => _database.KeyDelete(FormatKey(key));
//     public void Clear() => throw new NotImplementedException("Clearing all keys requires IServer access and a SCAN operation.");

//     // (Remaining ICollection methods for IMutableMapping are omitted for brevity but follow similar patterns)
//     public bool IsReadOnly => false;
//     // ...

//     // --- Bulk Operations using Redis native MGET/MSET ---

//     public async Task<IDictionary<K, V>> GetManyAsync(IEnumerable<K> keys) {
//         // Convert the C# keys list to a RedisKey array with the correct formatting
//         RedisKey[] redisKeys = keys.Select(FormatKey).ToArray();

//         // Use the native MGET command (implemented via StringSet(KeyValuePair<>,...))
//         RedisValue[] redisValues = await _database.StringGetAsync(redisKeys);

//         var result = new Dictionary<K, V>();
//         int i = 0;
//         foreach (var key in keys) // Iterate over original keys to match order
//         {
//             RedisValue value = redisValues[i++];
//             // Only add keys that actually existed and had a value
//             if (!value.IsNullOrEmpty) {
//                 result.Add(key, ParseValue(value));
//             }
//         }
//         return result;
//     }

//     public async Task SetManyAsync(IDictionary<K, V> items) {
//         // Convert the C# dictionary items to a KeyValuePair<RedisKey, RedisValue> array
//         var keyValuePairArray = items.Select(kvp =>
//             new KeyValuePair<RedisKey, RedisValue>(FormatKey(kvp.Key), FormatValue(kvp.Value))
//         ).ToArray();

//         // Use the native MSET command (implemented via StringSet(KeyValuePair<>,...))
//         // This is a single atomic operation in Redis.
//         await _database.StringSetAsync(keyValuePairArray);
//     }
// }

// // A custom Async Mapping interface (No longer inherits IReadOnlyDictionary)
// public interface IAsyncMapping<K, V> {
//     // The indexer must remain synchronous in C# syntax, but the implementation will use async calls internally
//     // and rely on the consumer potentially blocking, which is poor practice.
//     // We prefer explicit GetAsync/SetAsync methods.

//     Task<V> GetAsync(K key);
//     Task<bool> ContainsKeyAsync(K key);
//     Task<IDictionary<K, V>> GetManyAsync(IEnumerable<K> keys);
//     // Note: Iteration/Count remain problematic in pure async interfaces without async enumerators (IAsyncEnumerable)
// }

// // A custom Async Mutable Mapping interface (No longer inherits IDictionary)
// public interface IAsyncMutableMapping<K, V> : IAsyncMapping<K, V> {
//     Task SetAsync(K key, V value);
//     Task<bool> RemoveAsync(K key);
//     Task SetManyAsync(IDictionary<K, V> items);
//     // AddAsync, ClearAsync, etc.
// }

// public class AsyncRedisMapping<K, V> : IAsyncMutableMapping<K, V> {
//     private readonly IDatabase _database;
//     private readonly string _keyPrefix;

//     public AsyncRedisMapping(IConnectionMultiplexer connectionMultiplexer, string keyPrefix = "") {
//         _database = connectionMultiplexer.GetDatabase();
//         _keyPrefix = keyPrefix ?? "";
//     }

//     // --- Helper Methods ---

//     private RedisKey FormatKey(K key) => $"{_keyPrefix}:{key}";
//     private RedisValue FormatValue(V value) => JsonConvert.SerializeObject(value);
//     private V ParseValue(RedisValue value) => JsonConvert.DeserializeObject<V>(value);

//     // --- IAsyncMapping Implementation ---

//     public async Task<V> GetAsync(K key) {
//         RedisValue redisValue = await _database.StringGetAsync(FormatKey(key));
//         if (redisValue.IsNullOrEmpty) {
//             // You should define behavior for missing keys (e.g., throw or return default(V))
//             throw new KeyNotFoundException($"The key '{key}' was not found in Redis.");
//         }
//         return ParseValue(redisValue);
//     }

//     public Task<bool> ContainsKeyAsync(K key) => _database.KeyExistsAsync(FormatKey(key));

//     public async Task<IDictionary<K, V>> GetManyAsync(IEnumerable<K> keys) {
//         // Use native Redis MGET command
//         RedisKey[] redisKeys = keys.Select(FormatKey).ToArray();
//         RedisValue[] redisValues = await _database.StringGetAsync(redisKeys);

//         var result = new Dictionary<K, V>();
//         int i = 0;
//         foreach (var key in keys) {
//             RedisValue value = redisValues[i++];
//             if (!value.IsNullOrEmpty) {
//                 result.Add(key, ParseValue(value));
//             }
//         }
//         return result;
//     }

//     // --- IAsyncMutableMapping Implementation ---

//     public Task SetAsync(K key, V value) =>
//         _database.StringSetAsync(FormatKey(key), FormatValue(value));

//     public Task<bool> RemoveAsync(K key) => _database.KeyDeleteAsync(FormatKey(key));

//     public async Task SetManyAsync(IDictionary<K, V> items) {
//         // Use native Redis MSET command
//         var keyValuePairArray = items.Select(kvp =>
//             new KeyValuePair<RedisKey, RedisValue>(FormatKey(kvp.Key), FormatValue(kvp.Value))
//         ).ToArray();

//         // StringSetAsync with a KeyValuePair array executes MSET
//         await _database.StringSetAsync(keyValuePairArray);
//     }
// }
