using Newtonsoft.Json;
using NTDLS.PrudentMessageQueueLibrary.Payloads.Queries.ServerToClient;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using System.Reflection;

namespace NTDLS.PrudentMessageQueueClient.Client.QueryHandlers
{
    internal class InternalClientQueryHandlers(MqClient mqClient)
        : IRmMessageHandler
    {
        private static readonly PessimisticCriticalResource<Dictionary<string, MethodInfo>> _reflectionCache = new();

        private static readonly JsonSerializerSettings _typeNameHandlingAll = new()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        /// <summary>
        /// Deserialization function called from MessageDeliveryQuery via reflection.
        /// </summary>
        public static T? MqDeserializeToObject<T>(string json)
            => JsonConvert.DeserializeObject<T>(json, _typeNameHandlingAll);

        public MessageDeliveryQueryReply MessageDeliveryQuery(RmContext context, MessageDeliveryQuery param)
        {
            try
            {
                string cacheKey = $"{param.ObjectType}";

                var genericToObjectMethod = _reflectionCache.Use((o) =>
                {
                    if (o.TryGetValue(cacheKey, out var method))
                    {
                        return method;
                    }
                    return null;
                });

                IPMqMessage? deserializedMessage = null;

                if (genericToObjectMethod != null) //Reflection cache hit.
                {
                    //Call the generic deserialization:
                    deserializedMessage = genericToObjectMethod.Invoke(null, [param.MessageJson]) as IPMqMessage
                        ?? throw new Exception($"Extraction message can not be null.");
                }
                else
                {
                    var genericType = Type.GetType(param.ObjectType)
                        ?? throw new Exception($"Unknown extraction message type {param.ObjectType}.");

                    var toObjectMethod = typeof(InternalClientQueryHandlers).GetMethod("MqDeserializeToObject")
                            ?? throw new Exception($"Could not resolve MqDeserializeToObject().");

                    genericToObjectMethod = toObjectMethod.MakeGenericMethod(genericType);

                    _reflectionCache.Use((o) => o.TryAdd(cacheKey, genericToObjectMethod));

                    //Call the generic deserialization:
                    deserializedMessage = genericToObjectMethod.Invoke(null, [param.MessageJson]) as IPMqMessage
                        ?? throw new Exception($"Extraction message can not be null.");
                }

                bool wasMessageConsumed = mqClient.InvokeOnReceived(mqClient, deserializedMessage);
                return new MessageDeliveryQueryReply(wasMessageConsumed);
            }
            catch (Exception ex)
            {
                return new MessageDeliveryQueryReply(ex.GetBaseException());
            }
        }
    }
}
