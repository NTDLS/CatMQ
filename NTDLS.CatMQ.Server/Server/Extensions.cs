using RocksDbSharp;
using System.Text;
using System.Text.Json;

namespace NTDLS.CatMQ.Server.Server
{
    internal static class Extensions
    {
        /// <summary>
        /// Removes an item from a database.
        /// </summary>
        public static void RemoveFromDatabase(this RocksDb? db, ulong serialNumber)
        {
            var keyBytes = serialNumber.ToKey();
            db?.Remove(keyBytes, keyBytes.Length);
        }

        /// <summary>
        /// Removes an item from a database.
        /// </summary>
        public static void Remove(this RocksDb? db, EnqueuedMessage message)
        {
            var keyBytes = message.SerialNumber.ToKey();
            db?.Remove(keyBytes, keyBytes.Length);
        }

        /// <summary>
        /// Removes all items from a database.
        /// </summary>
        public static void Purge(this RocksDb? db)
        {
            if (db != null)
            {
                var keysToDelete = new List<byte[]>();

                // Collect all keys
                using (var iterator = db.NewIterator())
                {
                    for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                    {
                        keysToDelete.Add(iterator.Key());
                    }
                }

                // Delete all keys
                foreach (var key in keysToDelete)
                {
                    db.Remove(key);
                }
            }
        }

        /// <summary>
        /// Removes an item from a database and the message buffer.
        /// </summary>
        public static void RemoveFromBufferAndDatabase(this EnqueuedMessageContainer container, EnqueuedMessage message)
        {
            var keyBytes = message.SerialNumber.ToKey();
            container.Database?.Remove(keyBytes, keyBytes.Length);
            container.MessageBuffer.Remove(message);
        }

        /// <summary>
        /// Stores a message in the database.
        /// </summary>
        public static void Store(this RocksDb? db, EnqueuedMessage message)
        {
            var keyBytes = message.SerialNumber.ToKey();
            var persistedBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            db?.Put(keyBytes, keyBytes.Length, persistedBytes, persistedBytes.Length);
        }

        /// <summary>
        /// Gets a message object from the database.
        /// </summary>
        public static EnqueuedMessage? RetrieveMessage(this RocksDb? db, ulong serialNumber)
        {
            var keyBytes = serialNumber.ToKey();
            var jsonBytes = db?.Get(keyBytes, keyBytes.Length)
                ?? throw new Exception($"Message not found: [{serialNumber}].");
            if (jsonBytes != null)
            {
                var json = Encoding.UTF8.GetString(jsonBytes);
                return JsonSerializer.Deserialize<EnqueuedMessage>(json);
            }
            return null;
        }

        /// <summary>
        /// Converts a ulong Serial Number to a big-endian key.
        /// We do this so the items will be stored in order.
        /// </summary>
        public static byte[] ToKey(this ulong serialNumber)
        {
            var keyBytes = BitConverter.GetBytes(serialNumber);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(keyBytes); // Convert to big-endian.
            }
            return keyBytes;
        }
    }
}
