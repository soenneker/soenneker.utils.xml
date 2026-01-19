using Soenneker.Extensions.String;
using Soenneker.Utils.MemoryStream.Abstract;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
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
    private static readonly ConcurrentDictionary<Type, XmlSerializer> _serializerCache = new();

    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static XmlSerializer GetSerializer(Type type)
        => _serializerCache.GetOrAdd(type, static t => new XmlSerializer(t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static XmlSerializer GetSerializer<T>()
        => GetSerializer(typeof(T));

    /// <summary>
    /// Serialize to a string (returns null if <paramref name="obj"/> is null).
    /// Uses pooled streams when <paramref name="memoryStreamUtil"/> is provided.
    /// </summary>
    [Pure]
    public static string? Serialize<T>(
        T? obj,
        Encoding? encoding = null,
        bool removeNamespaces = true,
        bool removeXsiNilElements = true,
        IMemoryStreamUtil? memoryStreamUtil = null)
    {
        if (obj is null)
            return null;

        encoding ??= Encoding.UTF8;

        // Fastest: direct serialize to MemoryStream, then decode.
        if (!removeXsiNilElements)
        {
            if (memoryStreamUtil is null)
            {
                using var ms = new System.IO.MemoryStream(capacity: 1024);
                Serialize(obj, ms, encoding, removeNamespaces, removeXsiNilElements: false, leaveOpen: true, memoryStreamUtil: null);
                return GetString(ms, encoding);
            }

            using var pooled = memoryStreamUtil.GetSync();
            Serialize(obj, pooled, encoding, removeNamespaces, removeXsiNilElements: false, leaveOpen: true, memoryStreamUtil);
            return GetString(pooled, encoding);
        }

        // Filter path (resilient): serialize->temp->XDocument filter->string
        if (memoryStreamUtil is null)
        {
            using var temp = new System.IO.MemoryStream(capacity: 1024);
            Serialize(obj, temp, encoding, removeNamespaces, removeXsiNilElements: true, leaveOpen: true, memoryStreamUtil: null);
            if (temp.CanSeek) temp.Position = 0;
            return FilterXsiNilToString(temp, encoding);
        }

        using var pooledTemp = memoryStreamUtil.GetSync();
        Serialize(obj, pooledTemp, encoding, removeNamespaces, removeXsiNilElements: true, leaveOpen: true, memoryStreamUtil);
        if (pooledTemp.CanSeek) pooledTemp.Position = 0;
        return FilterXsiNilToString(pooledTemp, encoding);
    }

    /// <summary>
    /// Serialize to a stream (no-op if <paramref name="obj"/> is null).
    /// Uses direct streaming when <paramref name="removeXsiNilElements"/> is false; otherwise uses a resilient XDocument filter pass.
    /// </summary>
    public static void Serialize<T>(
        T? obj,
        Stream destination,
        Encoding? encoding = null,
        bool removeNamespaces = true,
        bool removeXsiNilElements = true,
        bool leaveOpen = false,
        IMemoryStreamUtil? memoryStreamUtil = null)
    {
        if (obj is null)
            return;

        ArgumentNullException.ThrowIfNull(destination);

        encoding ??= Encoding.UTF8;

        // Fast path: direct serialize to destination (no temp / no DOM).
        if (!removeXsiNilElements)
        {
            WriteSerialized(obj, destination, encoding, removeNamespaces, leaveOpen);
            return;
        }

        // Filter path: requires temp buffer.
        if (memoryStreamUtil is null)
        {
            using var temp = new System.IO.MemoryStream(capacity: 1024);
            WriteSerialized(obj, temp, encoding, removeNamespaces, leaveOpen: true);
            if (temp.CanSeek) temp.Position = 0;
            FilterXsiNilElements(temp, destination, encoding, leaveOpenDestination: leaveOpen);
            return;
        }

        using var pooledTemp = memoryStreamUtil.GetSync();
        WriteSerialized(obj, pooledTemp, encoding, removeNamespaces, leaveOpen: true);
        if (pooledTemp.CanSeek) pooledTemp.Position = 0;
        FilterXsiNilElements(pooledTemp, destination, encoding, leaveOpenDestination: leaveOpen);
    }

    /// <summary>
    /// Accepts a nullable string; if null/empty returns default.
    /// </summary>
    [Pure]
    public static T? Deserialize<T>(string? str)
    {
        if (str.IsNullOrEmpty())
            return default;

        // Trim UTF-8 BOM if present
        if (str.Length > 0 && str[0] == '\uFEFF')
            str = str.TrimStart('\uFEFF');

        var xs = GetSerializer<T>();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var sr = new StringReader(str);
        using var xr = XmlReader.Create(sr, settings);

        return (T?)xs.Deserialize(xr);
    }

    /// <summary>
    /// Deserializes an object of type <typeparamref name="T"/> from a stream.
    /// Stream position will be read from its current position. Honors XML encoding/declaration automatically.
    /// </summary>
    [Pure]
    public static T? Deserialize<T>(Stream? source, bool leaveOpen = false)
    {
        if (source is null)
            return default;

        if (source.CanSeek && source.Length - source.Position == 0)
            return default;

        var xs = GetSerializer<T>();

        var settings = new XmlReaderSettings
        {
            CloseInput = !leaveOpen,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(source, settings);
        return (T?)xs.Deserialize(reader);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSerialized<T>(
        T obj,
        Stream destination,
        Encoding encoding,
        bool removeNamespaces,
        bool leaveOpen)
    {
        var serializer = GetSerializer<T>();

        XmlSerializerNamespaces? ns = null;
        if (removeNamespaces)
        {
            ns = new XmlSerializerNamespaces();
            ns.Add(string.Empty, string.Empty);
        }

        var settings = new XmlWriterSettings
        {
            Encoding = encoding,
            OmitXmlDeclaration = false,
            Indent = false,
            CloseOutput = !leaveOpen,
            NamespaceHandling = NamespaceHandling.OmitDuplicates
        };

        using var xw = XmlWriter.Create(destination, settings);
        serializer.Serialize(xw, obj, ns);
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
            CloseOutput = !leaveOpenDestination,

            // Important: avoids writer-state failures if doc ends up being "fragment-ish"
            ConformanceLevel = ConformanceLevel.Auto
        };

        try
        {
            using var xw = XmlWriter.Create(output, writerSettings);
            doc.Save(xw);
            xw.Flush();
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException)
        {
            if (!input.CanSeek)
                throw;

            input.Position = 0;
            input.CopyTo(output);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RemoveXsiNilElementsInPlace(XDocument doc)
    {
        XNamespace xsi = XsiNs;

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
            return c == '1' || c == 'T' || c == 't';
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

    private static string FilterXsiNilToString(Stream input, Encoding encoding)
    {
        using var output = new System.IO.MemoryStream(capacity: 1024);
        FilterXsiNilElements(input, output, encoding, leaveOpenDestination: true);
        return GetString(output, encoding);
    }
}
