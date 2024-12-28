using Newtonsoft.Json;
using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ServerToClient;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using System.Reflection;

namespace NTDLS.CatMQ.Client.Client.QueryHandlers
{
    internal class InternalClientQueryHandlers(CMqClient mqClient)
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

        public CMqMessageDeliveryQueryReply MessageDeliveryQuery(RmContext context, CMqMessageDeliveryQuery param)
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

                ICMqMessage? deserializedMessage = null;

                if (genericToObjectMethod != null) //Reflection cache hit.
                {
                    //Call the generic deserialization:
                    deserializedMessage = genericToObjectMethod.Invoke(null, [param.MessageJson]) as ICMqMessage
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
                    deserializedMessage = genericToObjectMethod.Invoke(null, [param.MessageJson]) as ICMqMessage
                        ?? throw new Exception($"Extraction message can not be null.");
                }

                bool wasMessageConsumed = mqClient.InvokeOnReceived(mqClient, param.QueueName, deserializedMessage);
                return new CMqMessageDeliveryQueryReply(wasMessageConsumed);
            }
            catch (Exception ex)
            {
                return new CMqMessageDeliveryQueryReply(ex.GetBaseException());
            }
        }
    }
}
