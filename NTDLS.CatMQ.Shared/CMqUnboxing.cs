using Newtonsoft.Json;
using NTDLS.Semaphore;
using System.Reflection;

namespace NTDLS.CatMQ.Shared
{
    public static class CMqUnboxing
    {
        private static readonly JsonSerializerSettings _typeNameHandlingAll = new()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        private static readonly PessimisticCriticalResource<Dictionary<string, MethodInfo>> _reflectionCache = new();

        /// <summary>
        /// Deserialization function called from MessageDeliveryQuery via reflection.
        /// </summary>
        internal static T? MqDeserializeToObject<T>(string json)
            => JsonConvert.DeserializeObject<T>(json, _typeNameHandlingAll);

        /// <summary>
        /// Deserializes json to an object of the given type string.
        /// </summary>
        public static object PolymorphicDeserialize(string assemblyQualifiedTypeName, string objectJson)
        {
            string cacheKey = $"{assemblyQualifiedTypeName}";

            var genericToObjectMethod = _reflectionCache.Use((o) =>
            {
                if (o.TryGetValue(cacheKey, out var method))
                {
                    return method;
                }
                return null;
            });

            object? deserializedMessage = null;

            if (genericToObjectMethod != null) //Reflection cache hit.
            {
                //Call the generic deserialization:
                deserializedMessage = genericToObjectMethod.Invoke(null, [objectJson])
                    ?? throw new Exception($"Extraction message can not be null.");
            }
            else
            {
                var genericType = Type.GetType(assemblyQualifiedTypeName)
                    ?? throw new Exception($"Unknown extraction message type {assemblyQualifiedTypeName}.");

                var toObjectMethod = typeof(CMqUnboxing).GetMethod("MqDeserializeToObject", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new Exception("Could not resolve MqDeserializeToObject.");

                genericToObjectMethod = toObjectMethod.MakeGenericMethod(genericType);

                _reflectionCache.Use((o) => o.TryAdd(cacheKey, genericToObjectMethod));

                //Call the generic deserialization:
                deserializedMessage = genericToObjectMethod.Invoke(null, [objectJson])
                    ?? throw new Exception($"Extraction message can not be null.");
            }

            return deserializedMessage;
        }

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        internal static ICMqMessage Unbox(CMqReceivedMessage message)
        {
            string cacheKey = $"{message.ObjectType}";

            var genericToObjectMethod = _reflectionCache.Use((o) =>
            {
                if (o.TryGetValue(cacheKey, out var method))
                {
                    return method;
                }
                return null;
            });

            ICMqMessage? deserializedMessage = null;

            if (genericToObjectMethod != null) //Reflection cache hit.
            {
                //Call the generic deserialization:
                deserializedMessage = genericToObjectMethod.Invoke(null, [message.MessageJson]) as ICMqMessage
                    ?? throw new Exception($"Extraction message can not be null.");
            }
            else
            {
                var genericType = Type.GetType(message.ObjectType)
                    ?? throw new Exception($"Unknown extraction message type {message.ObjectType}.");

                var toObjectMethod = typeof(CMqUnboxing).GetMethod("MqDeserializeToObject")
                        ?? throw new Exception($"Could not resolve MqDeserializeToObject().");

                genericToObjectMethod = toObjectMethod.MakeGenericMethod(genericType);

                _reflectionCache.Use((o) => o.TryAdd(cacheKey, genericToObjectMethod));

                //Call the generic deserialization:
                deserializedMessage = genericToObjectMethod.Invoke(null, [message.MessageJson]) as ICMqMessage
                    ?? throw new Exception($"Extraction message can not be null.");
            }

            return deserializedMessage;
        }
    }
}
