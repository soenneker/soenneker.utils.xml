using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Soenneker.Extensions.String;

namespace Soenneker.Utils.Xml;

/// <summary>
/// A utility library handling (de)serialization and other useful XML functionalities
/// </summary>
public static class XmlUtil
{
    /// <summary>
    /// Serialize to a string (returns null if <paramref name="obj"/> is null).
    /// </summary>
    public static string? Serialize<T>(T? obj, Encoding? encoding, bool removeNamespaces = true)
    {
        encoding ??= Encoding.UTF8;
        XDocument? doc = SerializeToXDocument(obj, encoding, removeNamespaces);

        if (doc is null)
            return null;

        return doc.Declaration + doc.ToString();
    }

    /// <summary>
    /// Serialize to a stream (no-op if <paramref name="obj"/> is null).
    /// </summary>
    public static void Serialize<T>(T? obj, Stream destination, Encoding? encoding = null, bool removeNamespaces = true, bool leaveOpen = false)
    {
        if (obj is null)
            return;

        encoding ??= Encoding.UTF8;

        XDocument doc = SerializeToXDocument(obj, encoding, removeNamespaces)!;

        var settings = new XmlWriterSettings
        {
            Encoding = encoding,
            OmitXmlDeclaration = false,
            CloseOutput = !leaveOpen,
            Indent = false
        };

        using var xw = XmlWriter.Create(destination, settings);
        doc.WriteTo(xw);
        xw.Flush();
    }
    /// <summary>
    /// Accepts a nullable object... if null returns null.
    /// </summary>
    public static T? Deserialize<T>(string? str)
    {
        if (str.IsNullOrEmpty())
            return default;

        var xs = new XmlSerializer(typeof(T));

        T? obj;

        using (TextReader reader = new StringReader(str))
        {
            obj = (T?) xs.Deserialize(reader);
        }

        return obj;
    }

    /// <summary>
    /// Deserializes an object of type <typeparamref name="T"/> from a stream.
    /// Stream position will be read from its current position. Honors XML encoding/declaration automatically.
    /// </summary>
    /// <param name="source">Source stream containing XML.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="source"/> open after reading.</param>
    /// <returns>Deserialized object or default if stream is null/empty.</returns>
    public static T? Deserialize<T>(Stream? source, bool leaveOpen = false)
    {
        if (source is null)
            return default;

        // If we can determine emptiness, short-circuit. If not seekable, let XmlSerializer handle it.
        if (source.CanSeek && source.Length - source.Position == 0)
            return default;

        var xs = new XmlSerializer(typeof(T));

        var xrSettings = new XmlReaderSettings
        {
            CloseInput = !leaveOpen
        };

        using var reader = XmlReader.Create(source, xrSettings);
        return (T?)xs.Deserialize(reader);
    }

    /// <summary>
    /// Core: build an <see cref="XDocument"/> with declaration, optional namespace stripping,
    /// and remove nodes marked <c>xsi:nil="true"</c>.
    /// </summary>
    private static XDocument? SerializeToXDocument<T>(T? obj, Encoding encoding, bool removeNamespaces)
    {
        if (obj is null)
            return null;

        var serializer = new XmlSerializer(typeof(T));

        XmlSerializerNamespaces? ns = null;
        if (removeNamespaces)
        {
            ns = new XmlSerializerNamespaces();
            ns.Add("", "");
        }

        var doc = new XDocument { Declaration = new XDeclaration("1.0", encoding.HeaderName, null) };

        using (XmlWriter writer = doc.CreateWriter())
        {
            serializer.Serialize(writer, obj, ns);
        }

        // Strip xsi:nil elements
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        doc.Descendants()
            .Where(e => (bool?)e.Attribute(xsi + "nil") == true)
            .Remove();

        return doc;
    }
}