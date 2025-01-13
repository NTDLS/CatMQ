﻿using NTDLS.Semaphore;
using System.Reflection;
using System.Text.Json;

namespace NTDLS.CatMQ.Shared
{
    public static class CMqUnboxing
    {
        private static readonly PessimisticCriticalResource<Dictionary<string, MethodInfo>> _reflectionCache = new();

        /// <summary>
        /// Deserialization function called from MessageDeliveryQuery via reflection.
        /// </summary>
        public static T? MqDeserializeToObject<T>(string json)
            => JsonSerializer.Deserialize<T>(json);

        public static string GetAssemblyQualifiedTypeName(object obj)
        {
            return GetAssemblyQualifiedTypeName(obj.GetType());
        }

        public static string GetAssemblyQualifiedTypeName(Type type)
        {
            if (Caching.CacheTryGet<string>(type, out var objectTypeName) && objectTypeName != null)
            {
                return objectTypeName;
            }

            string assemblyQualifiedName;

            if (type.IsGenericType)
            {
                var typeDefinitionName = type.GetGenericTypeDefinition().FullName
                     ?? throw new Exception("The generic type name is not available.");

                var assemblyName = type.Assembly.FullName
                     ?? throw new Exception("The generic assembly type name is not available.");

                assemblyQualifiedName = $"{typeDefinitionName}, {assemblyName}";
            }
            else
            {
                assemblyQualifiedName = type.AssemblyQualifiedName ?? type.Name
                    ?? throw new Exception("The type name is not available.");
            }

            objectTypeName = CompiledRegEx.TypeTagsRegex().Replace(assemblyQualifiedName, string.Empty);
            objectTypeName = CompiledRegEx.TypeCleanupRegex().Replace(objectTypeName, ", ").Trim();

            Caching.CacheSetOneMinute(type, objectTypeName);

            return objectTypeName;
        }

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

                var toObjectMethod = typeof(CMqUnboxing).GetMethod("MqDeserializeToObject")
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
