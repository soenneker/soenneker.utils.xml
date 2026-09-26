using Soenneker.Extensions.String;
using Soenneker.Utils.MemoryStream.Abstract;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Soenneker.Utils.Xml;

/// <summary>
/// Fast, allocation-conscious, and resilient XML (de)serialization utilities.
/// <para/>
/// Design:
/// <list type="bullet">
/// <item><description>Fast path: direct XmlSerializer -> XmlWriter when no filtering is needed.</description></item>
/// <item><description>Resilient filter path: serialize to temp (optionally pooled), load XDocument, remove xsi:nil nodes, save.</description></item>
/// <item><description>Serializer caching to avoid repeated XmlSerializer construction cost.</description></item>
/// <item><description>Secure deserialization defaults (DTD prohibited, resolver null).</description></item>
/// </list>
/// </summary>
public static class XmlUtil
{
    private static readonly XmlSerializerNamespaces _emptyNamespaces = CreateEmptyNamespaces();
    private static readonly SearchValues<char> _truthyNilChars = SearchValues.Create("1Tt");

    private const string _xsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    [RequiresUnreferencedCode("XmlSerializer reflects over serialized members. Use SerializeWithWriter or DeserializeWithReader for trimming.")]
    [RequiresDynamicCode("XmlSerializer requires runtime code generation. Use SerializeWithWriter or DeserializeWithReader for Native AOT.")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static XmlSerializer GetSerializer<T>()
        => SerializerCache<T>.Get();

    private static XmlSerializerNamespaces CreateEmptyNamespaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add(string.Empty, string.Empty);
        return namespaces;
    }

    /// <summary>
    /// Serialize to a string (returns null if <paramref name="obj"/> is null).
    /// Uses pooled streams when <paramref name="memoryStreamUtil"/> is provided.
    /// </summary>
    /// <returns>Serialize to a string (returns null if <paramref name="obj"/> is null). Uses pooled streams when <paramref name="memoryStreamUtil"/> is provided.</returns>
    [RequiresUnreferencedCode("XmlSerializer reflects over serialized members. Use SerializeWithWriter or DeserializeWithReader for trimming.")]
    [RequiresDynamicCode("XmlSerializer requires runtime code generation. Use SerializeWithWriter or DeserializeWithReader for Native AOT.")]
    [Pure]
    public static string? Serialize<T>(T? obj, Encoding? encoding = null, bool removeNamespaces = true,
        bool removeXsiNilElements = true, IMemoryStreamUtil? memoryStreamUtil = null)
        => SerializeWithWriter(obj, (writer, value) => GetSerializer<T>().Serialize(writer, value, removeNamespaces ? _emptyNamespaces : null),
            encoding, removeXsiNilElements, memoryStreamUtil);

    /// <summary>Serializes an object to a stream using XmlSerializer. A null object is a no-op.</summary>
    [RequiresUnreferencedCode("XmlSerializer reflects over serialized members. Use SerializeWithWriter or DeserializeWithReader for trimming.")]
    [RequiresDynamicCode("XmlSerializer requires runtime code generation. Use SerializeWithWriter or DeserializeWithReader for Native AOT.")]
    public static void Serialize<T>(T? obj, Stream destination, Encoding? encoding = null, bool removeNamespaces = true,
        bool removeXsiNilElements = true, bool leaveOpen = false, IMemoryStreamUtil? memoryStreamUtil = null)
        => SerializeWithWriter(obj, destination, (writer, value) => GetSerializer<T>().Serialize(writer, value, removeNamespaces ? _emptyNamespaces : null),
            encoding, removeXsiNilElements, leaveOpen, memoryStreamUtil);

    /// <summary>Deserializes XML using XmlSerializer. Null or empty input returns default.</summary>
    [RequiresUnreferencedCode("XmlSerializer reflects over serialized members. Use SerializeWithWriter or DeserializeWithReader for trimming.")]
    [RequiresDynamicCode("XmlSerializer requires runtime code generation. Use SerializeWithWriter or DeserializeWithReader for Native AOT.")]
    [Pure]
    public static T? Deserialize<T>(string? str)
        => DeserializeWithReader(str, static reader => (T?)GetSerializer<T>().Deserialize(reader));

    /// <summary>Deserializes XML from the current stream position using XmlSerializer.</summary>
    [RequiresUnreferencedCode("XmlSerializer reflects over serialized members. Use SerializeWithWriter or DeserializeWithReader for trimming.")]
    [RequiresDynamicCode("XmlSerializer requires runtime code generation. Use SerializeWithWriter or DeserializeWithReader for Native AOT.")]
    [Pure]
    public static T? Deserialize<T>(Stream? source, bool leaveOpen = false)
        => DeserializeWithReader(source, static reader => (T?)GetSerializer<T>().Deserialize(reader), leaveOpen);

    /// <summary>
    /// Writes XML using a caller-supplied writer and returns the encoded XML string. A null object returns null.
    /// </summary>
    /// <remarks>The callback writes the complete root element, including namespaces, and must leave the writer open.
    /// Use a callback without reflection or dynamic code for Native AOT. XML serialization attributes are not applied automatically.</remarks>
    [Pure]
    public static string? SerializeWithWriter<T>(
        T? obj,
        Action<XmlWriter, T> writeXml,
        Encoding? encoding = null,
        bool removeXsiNilElements = true,
        IMemoryStreamUtil? memoryStreamUtil = null)
    {
        ArgumentNullException.ThrowIfNull(writeXml);

        if (obj is null)
            return null;

        encoding ??= Encoding.UTF8;

        // Fastest: direct serialize to MemoryStream, then decode.
        if (!removeXsiNilElements)
        {
            if (memoryStreamUtil is null)
            {
                using var ms = new System.IO.MemoryStream(capacity: 1024);
                SerializeWithWriter(obj, ms, writeXml, encoding, removeXsiNilElements: false, leaveOpen: true, memoryStreamUtil: null);
                return GetString(ms, encoding);
            }

            using var pooled = memoryStreamUtil.GetSync();
            SerializeWithWriter(obj, pooled, writeXml, encoding, removeXsiNilElements: false, leaveOpen: true, memoryStreamUtil);
            return GetString(pooled, encoding);
        }

        // The stream overload already performs the filter pass; decode its result directly.
        if (memoryStreamUtil is null)
        {
            using var temp = new System.IO.MemoryStream(capacity: 1024);
            SerializeWithWriter(obj, temp, writeXml, encoding, removeXsiNilElements: true, leaveOpen: true, memoryStreamUtil: null);
            return GetString(temp, encoding);
        }

        using var pooledTemp = memoryStreamUtil.GetSync();
        SerializeWithWriter(obj, pooledTemp, writeXml, encoding, removeXsiNilElements: true, leaveOpen: true, memoryStreamUtil);
        return GetString(pooledTemp, encoding);
    }

    /// <summary>
    /// Writes XML using a caller-supplied writer (no-op if <paramref name="obj"/> is null).
    /// Uses direct streaming when <paramref name="removeXsiNilElements"/> is false; otherwise uses a resilient XDocument filter pass.
    /// </summary>
    /// <remarks>The callback writes the complete root element, including namespaces, and must leave the writer open.
    /// Use a callback without reflection or dynamic code for Native AOT.</remarks>
    public static void SerializeWithWriter<T>(
        T? obj,
        Stream destination,
        Action<XmlWriter, T> writeXml,
        Encoding? encoding = null,
        bool removeXsiNilElements = true,
        bool leaveOpen = false,
        IMemoryStreamUtil? memoryStreamUtil = null)
    {
        ArgumentNullException.ThrowIfNull(writeXml);

        if (obj is null)
            return;

        ArgumentNullException.ThrowIfNull(destination);

        encoding ??= Encoding.UTF8;

        // Fast path: direct serialize to destination (no temp / no DOM).
        if (!removeXsiNilElements)
        {
            WriteSerialized(obj, destination, encoding, writeXml, leaveOpen);
            return;
        }

        // Filter path: requires temp buffer.
        if (memoryStreamUtil is null)
        {
            using var temp = new System.IO.MemoryStream(capacity: 1024);
            WriteSerialized(obj, temp, encoding, writeXml, leaveOpen: true);
            if (temp.CanSeek) temp.Position = 0;
            FilterXsiNilElements(temp, destination, encoding, leaveOpenDestination: leaveOpen);
            return;
        }

        using var pooledTemp = memoryStreamUtil.GetSync();
        WriteSerialized(obj, pooledTemp, encoding, writeXml, leaveOpen: true);
        if (pooledTemp.CanSeek) pooledTemp.Position = 0;
        FilterXsiNilElements(pooledTemp, destination, encoding, leaveOpenDestination: leaveOpen);
    }

    /// <summary>
    /// Reads XML using a caller-supplied reader callback; null/empty input returns default.
    /// </summary>
    /// <returns>Accepts a nullable string; if null/empty returns default.</returns>
    /// <remarks>The callback receives a reader before its first node and must consume the desired XML without retaining the reader.
    /// DTD processing is prohibited. Use a callback without reflection or dynamic code for Native AOT.</remarks>
    [Pure]
    public static T? DeserializeWithReader<T>(string? str, Func<XmlReader, T> readXml)
    {
        ArgumentNullException.ThrowIfNull(readXml);

        if (str.IsNullOrEmpty())
            return default;

        // Trim UTF-8 BOM if present
        if (str.Length > 0 && str[0] == '\uFEFF')
            str = str.TrimStart('\uFEFF');

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var sr = new StringReader(str);
        using var xr = XmlReader.Create(sr, settings);

        return readXml(xr);
    }

    /// <summary>
    /// Uses a caller-supplied reader callback to deserialize an object of type <typeparamref name="T"/> from a stream.
    /// Stream position will be read from its current position. Honors XML encoding/declaration automatically.
    /// </summary>
    /// <returns>Deserializes an object of type <typeparamref name="T"/> from a stream. Stream position will be read from its current position. Honors XML encoding/declaration automatically.</returns>
    /// <remarks>The callback receives a reader before its first node and must consume the desired XML without retaining the reader.
    /// DTD processing is prohibited. Use a callback without reflection or dynamic code for Native AOT.</remarks>
    [Pure]
    public static T? DeserializeWithReader<T>(Stream? source, Func<XmlReader, T> readXml, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(readXml);

        if (source is null)
            return default;

        if (source.CanSeek && source.Length - source.Position == 0)
            return default;

        var settings = new XmlReaderSettings
        {
            CloseInput = !leaveOpen,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(source, settings);
        return readXml(reader);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSerialized<T>(
        T obj,
        Stream destination,
        Encoding encoding,
        Action<XmlWriter, T> writeXml,
        bool leaveOpen)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = encoding,
            OmitXmlDeclaration = false,
            Indent = false,
            CloseOutput = !leaveOpen,
            NamespaceHandling = NamespaceHandling.OmitDuplicates
        };

        using var xw = XmlWriter.Create(destination, settings);
        writeXml(xw, obj);
        xw.Flush();
    }

    /// <summary>
    /// Resilient filter: parse as XDocument, remove xsi:nil elements, then save.
    /// If parsing/saving fails and <paramref name="input"/> is seekable, falls back to raw copy.
    /// </summary>
    private static void FilterXsiNilElements(
        Stream input,
        Stream output,
        Encoding encoding,
        bool leaveOpenDestination)
    {
        try
        {
            // Secure-ish reader settings for Load (even though input is typically our own serializer output).
            var readerSettings = new XmlReaderSettings
            {
                CloseInput = false,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = false,
                IgnoreComments = false,
                IgnoreProcessingInstructions = false
            };

            XDocument doc;

            try
            {
                using var xr = XmlReader.Create(input, readerSettings);
                doc = XDocument.Load(xr, LoadOptions.PreserveWhitespace);
            }
            catch (Exception e) when (e is XmlException or InvalidOperationException)
            {
                if (!input.CanSeek)
                    throw;

                input.Position = 0;
                input.CopyTo(output);
                return;
            }

            RemoveXsiNilElementsInPlace(doc);

            var writerSettings = new XmlWriterSettings
            {
                Encoding = encoding,
                OmitXmlDeclaration = false,
                Indent = false,
                CloseOutput = false,

                // Important: avoids writer-state failures if doc ends up being "fragment-ish"
                ConformanceLevel = ConformanceLevel.Auto
            };

            long outputStart = output.CanSeek ? output.Position : -1;

            try
            {
                using var xw = XmlWriter.Create(output, writerSettings);
                doc.Save(xw);
                xw.Flush();
            }
            catch (Exception e) when (e is XmlException or InvalidOperationException)
            {
                if (!input.CanSeek || outputStart < 0)
                    throw;

                output.Position = outputStart;
                output.SetLength(outputStart);
                input.Position = 0;
                input.CopyTo(output);
            }
        }
        finally
        {
            if (!leaveOpenDestination)
                output.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RemoveXsiNilElementsInPlace(XDocument doc)
    {
        XNamespace xsi = _xsiNs;

        List<XElement>? remove = null;

        foreach (XElement element in doc.Descendants())
        {
            // Primary: xsi:nil
            XAttribute? attr = element.Attribute(xsi + "nil");

            // Some payloads may have "nil" without namespace; keep fallback for resilience.
            attr ??= element.Attribute("nil");

            if (attr is null)
                continue;

            if (IsTruthyNil(attr.Value))
            {
                remove ??= new List<XElement>(8);
                remove.Add(element);
            }
        }

        if (remove is null)
            return;

        for (int i = 0; i < remove.Count; i++)
            remove[i].Remove();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTruthyNil(string value)
    {
        if (value.Length == 1)
        {
            char c = value[0];
            return _truthyNilChars.Contains(c);
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetString(System.IO.MemoryStream ms, Encoding encoding)
    {
        if (ms.TryGetBuffer(out ArraySegment<byte> seg))
            return encoding.GetString(seg.Array!, seg.Offset, (int)ms.Length);

        return encoding.GetString(ms.ToArray());
    }
}
