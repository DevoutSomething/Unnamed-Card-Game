using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Core.Commands;
using Game.Core.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Game.Net
{
    /// <summary>
    /// Everything that crosses the wire — NetMessage envelopes, and the
    /// Command/GameEvent records nested inside them — goes through this one
    /// JsonSerializerSettings so polymorphic types round-trip correctly.
    /// </summary>
    public static class NetJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new ShortNameBinder(typeof(Command), typeof(GameEvent), typeof(NetMessage)),
            Formatting = Formatting.None,
        };

        /// <summary>
        /// Generic (not `object`) so the compile-time type of the caller's
        /// expression becomes T via inference — e.g. a NetMessage-typed local
        /// holding a WelcomeMessage instance. That matters: TypeNameHandling.Auto
        /// decides whether to write "$type" by comparing a value's declared type
        /// against its actual type, and passing `object` here would erase the
        /// declared type down to Object, so the root of the payload (unlike
        /// everything nested inside it) would never get a "$type" and
        /// Deserialize<NetMessage> couldn't tell which concrete message arrived.
        /// </summary>
        public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, typeof(T), Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>
        /// Newtonsoft's default TypeNameHandling embeds assembly-qualified type
        /// names ("Game.Core.Commands.PlayCardCommand, Assembly-CSharp, ..."),
        /// which is noisy and ties the wire format to build details that can
        /// differ between a host and a client. This writes/reads just the bare
        /// class name instead, resolved against every concrete type under the
        /// given base types (Command, GameEvent, NetMessage) — so adding a new
        /// record type anywhere in those hierarchies is automatically wire-ready.
        /// </summary>
        private class ShortNameBinder : ISerializationBinder
        {
            private readonly Dictionary<string, Type> _byName;

            public ShortNameBinder(params Type[] baseTypes)
            {
                _byName = baseTypes
                    .SelectMany(b => GetLoadableTypes(b.Assembly).Where(t => b.IsAssignableFrom(t) && !t.IsAbstract))
                    .Distinct()
                    .ToDictionary(t => t.Name, t => t);
            }

            /// <summary>
            /// Assembly.GetTypes() throws if ANY type in the assembly fails to
            /// load — and Assembly-CSharp holds the whole game, not just the
            /// networked Command/GameEvent/NetMessage hierarchies, so something
            /// unrelated (an editor-only type, a platform-conditional module)
            /// failing to load must not take networking down with it.
            /// </summary>
            private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
            {
                try { return assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            }

            public Type BindToType(string assemblyName, string typeName)
            {
                if (_byName.TryGetValue(typeName, out var type)) return type;
                throw new JsonSerializationException($"Unknown networked type '{typeName}'");
            }

            public void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                assemblyName = null;
                typeName = serializedType.Name;
            }
        }
    }
}
