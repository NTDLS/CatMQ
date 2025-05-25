using System.Collections.Concurrent;
using System.Text.Json;

namespace NTDLS.CatMQ.Shared
{
    public static class CMqUnboxing
    {
        private static readonly ConcurrentDictionary<string, Func<string, ICMqSerializationProvider?, ICMqMessage>> _deserializationCache = new();

        /// <summary>
        /// Deserialization function called from MessageDeliveryQuery via reflection.
        /// </summary>
        public static T? MqDeserializeToObject<T>(string json, ICMqSerializationProvider? serializationProvider = null)
        {
            if (serializationProvider == null)
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            else
            {
                return serializationProvider.DeserializeToObject<T>(json);
            }
        }

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
        /// Deserializes the received message to its original type.
        /// </summary>
        internal static ICMqMessage Deserialize(CMqReceivedMessage message)
            => DeserializeAs(message.AssemblyQualifiedTypeName, message.MessageJson, message.SerializationProvider);

        /// <summary>
        /// Deserializes JSON to an object of type T using its assembly-qualified name.
        /// </summary>
        public static T? DeserializeAs<T>(string objectJson, ICMqSerializationProvider? serializationProvider = null) where T : class
        {
            var typeName = GetAssemblyQualifiedTypeName(typeof(T))
                ?? throw new Exception($"Cannot resolve AssemblyQualifiedName for type {typeof(T).FullName}.");

            return DeserializeAs(typeName, objectJson, serializationProvider) as T;
        }

        /// <summary>
        /// Deserializes JSON to an object of the given type string.
        /// </summary>
        public static ICMqMessage DeserializeAs(string assemblyQualifiedTypeName, string objectJson, ICMqSerializationProvider? serializationProvider = null)
        {
            var deserializeMethod = _deserializationCache.GetOrAdd(assemblyQualifiedTypeName, typeName =>
            {
                var genericType = Type.GetType(typeName)
                    ?? throw new Exception($"Unknown extraction payload type [{typeName}].");

                if (!typeof(ICMqMessage).IsAssignableFrom(genericType))
                    throw new Exception($"Type [{genericType.FullName}] does not implement ICMqMessage.");

                var methodInfo = typeof(CMqUnboxing).GetMethod(nameof(MqDeserializeToObject), new[] { typeof(string), typeof(ICMqSerializationProvider) })
                    ?? throw new Exception("Could not resolve MqDeserializeToObject().");

                var genericMethod = methodInfo.MakeGenericMethod(genericType);

                return (Func<string, ICMqSerializationProvider?, ICMqMessage>)
                    Delegate.CreateDelegate(typeof(Func<string, ICMqSerializationProvider?, ICMqMessage>), genericMethod);
            });

            return deserializeMethod(objectJson, serializationProvider)
                ?? throw new Exception("Extraction payload cannot be null.");
        }
    }
}
