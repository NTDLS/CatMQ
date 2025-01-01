using Newtonsoft.Json;
using NTDLS.Semaphore;
using System.Reflection;

namespace NTDLS.CatMQ.Shared
{
    internal static class CMqUnboxing
    {
        private static readonly JsonSerializerSettings _typeNameHandlingAll = new()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        private static readonly PessimisticCriticalResource<Dictionary<string, MethodInfo>> _reflectionCache = new();

        /// <summary>
        /// Deserialization function called from MessageDeliveryQuery via reflection.
        /// </summary>
        public static T? MqDeserializeToObject<T>(string json)
            => JsonConvert.DeserializeObject<T>(json, _typeNameHandlingAll);

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        public static ICMqMessage Unbox(CMqReceivedMessage message)
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
