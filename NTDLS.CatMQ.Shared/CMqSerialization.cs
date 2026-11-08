using System.Collections.Concurrent;
using System.Text.Json;

namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Provides methods for serializing and deserializing objects to and from JSON,  as well as utilities for working
    /// with assembly-qualified type names.
    /// </summary>
    /// <remarks>This class includes functionality for deserializing JSON into objects of specific types, 
    /// either directly or using a custom serialization provider. It also provides methods for  retrieving
    /// assembly-qualified type names, which are used to identify types during deserialization.</remarks>
    public static class CMqSerialization
    {
        private static readonly ConcurrentDictionary<string, Func<string, ICMqSerializationProvider?, ICMqMessage>> _deserializationCache = new();
        private static readonly ConcurrentDictionary<string, Type> _registeredTypes = new();

        /// <summary>
        /// Messages can come from other languages, which usually name properties in camelCase. Without
        /// this, those properties would silently be left with their default values rather than failing.
        /// </summary>
        private static readonly JsonSerializerOptions _deserializationOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly ConcurrentDictionary<Type, string> _registeredTypeNames = new();

        /// <summary>
        /// Associates a language-neutral message type name, such as "orders.created", with a .NET type. The name is
        /// sent in place of the assembly-qualified type name, and resolves back to the type when messages are received.
        /// </summary>
        public static void RegisterMessageType<T>(string messageType) where T : ICMqMessage
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

            _registeredTypes[messageType] = typeof(T);
            _registeredTypeNames[typeof(T)] = messageType;
            _deserializationCache.TryRemove(messageType, out _);
        }

        /// <summary>
        /// Gets the message type name to send for an object: its registered name if there is one, otherwise its assembly-qualified type name.
        /// </summary>
        public static string GetMessageTypeName(object obj)
            => _registeredTypeNames.TryGetValue(obj.GetType(), out var messageType) ? messageType : GetAssemblyQualifiedTypeName(obj);

        /// <summary>
        /// Finds the .NET type for a message type name. Names from other languages will usually not resolve, which is not an error.
        /// </summary>
        private static Type? ResolveMessageType(string? messageType)
        {
            if (string.IsNullOrWhiteSpace(messageType))
            {
                return null;
            }
            if (_registeredTypes.TryGetValue(messageType, out var registeredType))
            {
                return registeredType;
            }

            try
            {
                return Type.GetType(messageType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deserialization function called from MessageDeliveryQuery via reflection.
        /// </summary>
        public static T? MqDeserializeToObject<T>(string json, ICMqSerializationProvider? serializationProvider = null)
        {
            if (serializationProvider == null)
            {
                return JsonSerializer.Deserialize<T>(json, _deserializationOptions);
            }
            else
            {
                return serializationProvider.DeserializeToObject<T>(json);
            }
        }

        /// <summary>
        /// Retrieves the assembly-qualified name of the type of the specified object.
        /// </summary>
        /// <param name="obj">The object whose type's assembly-qualified name is to be retrieved. Cannot be <see langword="null"/>.</param>
        /// <returns>The assembly-qualified name of the object's type.</returns>
        public static string GetAssemblyQualifiedTypeName(object obj)
            => GetAssemblyQualifiedTypeName(obj.GetType());

        /// <summary>
        /// Retrieves the assembly-qualified name of the specified <see cref="Type"/>.
        /// </summary>
        /// <remarks>This method handles both generic and non-generic types. For generic types, the
        /// assembly-qualified name is constructed using the type definition and assembly name. Additional cleanup is
        /// applied to remove unnecessary tags and formatting.</remarks>
        /// <param name="type">The <see cref="Type"/> for which to retrieve the assembly-qualified name. Cannot be <see langword="null"/>.</param>
        /// <returns>A string representing the assembly-qualified name of the specified type, with additional formatting applied.</returns>
        /// <exception cref="Exception">Thrown if the type's name or assembly information is unavailable, or if the operation fails to generate the
        /// assembly-qualified name.</exception>
        public static string GetAssemblyQualifiedTypeName(Type type)
        {
            return CMqCaching.GetOrCreateOneMinute($"AQTN:{type}", entry =>
            {
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

                string objectTypeName = CMqCompiledRegEx.TypeTagsRegex().Replace(assemblyQualifiedName, string.Empty);
                objectTypeName = CMqCompiledRegEx.TypeCleanupRegex().Replace(objectTypeName, ", ").Trim();

                return objectTypeName;
            }) ?? throw new Exception("Failed to get assembly qualified type name.");
        }

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        internal static ICMqMessage Deserialize(CMqReceivedMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.MessageType))
            {
                throw new Exception("The message has no message type. Use Deserialize<T>() to choose the type to deserialize to.");
            }
            return DeserializeAs(message.MessageType, message.Body, message.SerializationProvider);
        }

        /// <summary>
        /// Deserializes the received message to the given type, preferring the message's own type when it is compatible.
        /// </summary>
        internal static T? Deserialize<T>(CMqReceivedMessage message) where T : class
        {
            var messageType = ResolveMessageType(message.MessageType);
            if (messageType != null && typeof(T).IsAssignableFrom(messageType) && typeof(ICMqMessage).IsAssignableFrom(messageType))
            {
                return DeserializeAs(message.MessageType!, message.Body, message.SerializationProvider) as T;
            }
            return MqDeserializeToObject<T>(message.Body, message.SerializationProvider);
        }

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
                var genericType = ResolveMessageType(typeName)
                    ?? throw new Exception($"Unknown extraction message type [{typeName}].");

                if (!typeof(ICMqMessage).IsAssignableFrom(genericType))
                    throw new Exception($"Type [{genericType.FullName}] does not implement ICMqMessage.");

                var methodInfo = typeof(CMqSerialization).GetMethod(nameof(MqDeserializeToObject), new[] { typeof(string), typeof(ICMqSerializationProvider) })
                    ?? throw new Exception("Could not resolve MqDeserializeToObject().");

                var genericMethod = methodInfo.MakeGenericMethod(genericType);

                return (Func<string, ICMqSerializationProvider?, ICMqMessage>)
                    Delegate.CreateDelegate(typeof(Func<string, ICMqSerializationProvider?, ICMqMessage>), genericMethod);
            });

            return deserializeMethod(objectJson, serializationProvider)
                ?? throw new Exception("Extraction message cannot be null.");
        }
    }
}
