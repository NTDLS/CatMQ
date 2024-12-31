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

        /// <summary>
        /// The client has received a message from the server which needs to be consumed.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public CMqMessageDeliveryQueryReply MessageDeliveryQuery(RmContext context, CMqMessageDeliveryQuery param)
        {
            try
            {
                bool unboxedMessageConsumed = false;
                bool boxedMessageConsumed = false;

                if (mqClient.ProcessBoxedMessages)
                {
                    boxedMessageConsumed = mqClient.InvokeOnReceivedBoxed(mqClient, param.QueueName, param.ObjectType, param.MessageJson);
                }

                if (mqClient.ProcessUnboxedMessages)
                {
                    #region Automatic Message Unboxing.

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

                    unboxedMessageConsumed = mqClient.InvokeOnReceivedUnboxed(mqClient, param.QueueName, deserializedMessage);

                    #endregion
                }

                return new CMqMessageDeliveryQueryReply(boxedMessageConsumed || unboxedMessageConsumed);
            }
            catch (Exception ex)
            {
                mqClient.InvokeOnException(mqClient, null, ex);
                return new CMqMessageDeliveryQueryReply(ex.GetBaseException());
            }
        }
    }
}
