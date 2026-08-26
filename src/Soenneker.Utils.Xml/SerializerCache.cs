using System.Xml.Serialization;

namespace Soenneker.Utils.Xml;

internal static class SerializerCache<T>
{
    internal static readonly XmlSerializer Instance = new(typeof(T));
}
