using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Utils.Xml.Abstract;

namespace Soenneker.Utils.Xml;

///<inheritdoc cref="IXmlUtil"/>
public class XmlUtil : IXmlUtil
{
    private readonly ILogger<XmlUtil> _logger;

    private readonly bool _log;

    public XmlUtil(ILogger<XmlUtil> logger, IConfiguration config)
    {
        _log = config.GetValue<bool>("Log:XmlSerialization");
        _logger = logger;
    }

    public string? Serialize<T>(T? obj, Encoding? encoding, bool removeNamespaces = true)
    {
        if (_log)
            _logger.LogDebug("XML serializing string start");

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

        if (_log)
            _logger.LogDebug("XML serializing string result: {result}", result);

        return result;
    }

    public T? Deserialize<T>(string str)
    {
        if (_log)
            _logger.LogDebug("XML deserializing string from: {from}", str);

        if (str.IsNullOrEmpty())
            return default;

        var xs = new XmlSerializer(typeof(T));

        T? obj;

        using (TextReader reader = new StringReader(str))
        {
            obj = (T?) xs.Deserialize(reader);
        }

        if (_log)
            _logger.LogDebug("XML deserializing string completed");

        return obj;
    }
}