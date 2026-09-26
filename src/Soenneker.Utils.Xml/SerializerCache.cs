using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Xml.Serialization;

namespace Soenneker.Utils.Xml;

internal static class SerializerCache<T>
{
    private static XmlSerializer? _instance;
    private static object? _syncLock;
    private static bool _initialized;

    [RequiresUnreferencedCode("XmlSerializer reflects over serialized members.")]
    [RequiresDynamicCode("XmlSerializer requires runtime code generation.")]
    internal static XmlSerializer Get()
        => LazyInitializer.EnsureInitialized(ref _instance, ref _initialized, ref _syncLock, static () => new XmlSerializer(typeof(T)))!;
}
