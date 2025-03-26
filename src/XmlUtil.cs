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
    /// Accepts a nullable object... if null returns null.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="obj"></param>
    /// <param name="encoding"></param>
    /// <param name="removeNamespaces"></param>
    /// <returns></returns>
    public static string? Serialize<T>(T? obj, Encoding? encoding, bool removeNamespaces = true)
    {
        if (obj is null)
            return null;

        encoding ??= Encoding.UTF8;

        var serializer = new XmlSerializer(typeof(T));

        XmlSerializerNamespaces? namespaces = null;

        if (removeNamespaces)
        {
            namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", "");
        }

        var doc = new XDocument {Declaration = new XDeclaration("1.0", encoding.HeaderName, null)};

        using (XmlWriter writer = doc.CreateWriter())
        {
            serializer.Serialize(writer, obj, namespaces);
        }

        // remove all nulls
        doc.Descendants()
            .Where(x => (bool?) x.Attribute(XName.Get("nil", "http://www.w3.org/2001/XMLSchema-instance")) == true)
            .Remove();

        string result = doc.Declaration + doc.ToString();

        return result;
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
}