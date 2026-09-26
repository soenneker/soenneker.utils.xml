using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Soenneker.Utils.Xml;

const string value = "A & B <xml> \u00e9\u6f22";

foreach (Encoding encoding in new[] { Encoding.UTF8, Encoding.Unicode })
{
    foreach (bool filter in new[] { false, true })
    {
        string xml = XmlUtil.SerializeWithWriter(value, Write, encoding, filter)!;
        Check(XmlUtil.DeserializeWithReader(xml, Read) == value, "String round trip");
        Check(xml.Contains("<Optional") != filter, "Nil filtering");

        using var stream = new MemoryStream();
        XmlUtil.SerializeWithWriter(value, stream, Write, encoding, filter, leaveOpen: true);
        Check(stream.CanWrite, "Writer leaves stream open");
        stream.Position = 0;
        Check(XmlUtil.DeserializeWithReader(stream, Read, leaveOpen: true) == value, "Stream round trip");
        Check(stream.CanRead, "Reader leaves stream open");

        var closed = new MemoryStream();
        XmlUtil.SerializeWithWriter(value, closed, Write, encoding, filter);
        Check(!closed.CanWrite, "Writer closes stream");
    }
}

var input = new MemoryStream(Encoding.UTF8.GetBytes("<Sample><Required>ok</Required></Sample>"));
Check(XmlUtil.DeserializeWithReader(input, Read) == "ok", "Reader result");
Check(!input.CanRead, "Reader closes stream");
Check(XmlUtil.SerializeWithWriter<string>(null, Write) is null, "Null serialization");
Check(XmlUtil.DeserializeWithReader<string>((string?)null, Read) is null, "Null deserialization");
Check(XmlUtil.DeserializeWithReader("", Read) is null, "Empty deserialization");
Check(XmlUtil.DeserializeWithReader("\uFEFF<Sample><Required>bom</Required></Sample>", Read) == "bom", "BOM");

try
{
    XmlUtil.DeserializeWithReader("<!DOCTYPE Sample [<!ENTITY text 'forbidden'>]><Sample><Required>&text;</Required></Sample>", Read);
    throw new Exception("DTD was accepted");
}
catch (XmlException) { }

Console.WriteLine("XML callback smoke checks passed.");

static void Write(XmlWriter writer, string text)
{
    writer.WriteStartElement("Sample");
    writer.WriteElementString("Required", text);
    writer.WriteStartElement("Optional");
    writer.WriteAttributeString("xsi", "nil", "http://www.w3.org/2001/XMLSchema-instance", "true");
    writer.WriteEndElement();
    writer.WriteEndElement();
}

static string Read(XmlReader reader) => XElement.Load(reader).Element("Required")!.Value;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
