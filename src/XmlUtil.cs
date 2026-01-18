using System;
using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Soenneker.Extensions.String;
using Soenneker.Utils.MemoryStream.Abstract;

namespace Soenneker.Utils.Xml;

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

        if (memoryStreamUtil is null)
        {
            // fallback: no pool available
            using var ms = new System.IO.MemoryStream(capacity: 1024);
            Serialize(obj, ms, encoding, removeNamespaces, removeXsiNilElements, leaveOpen: true, memoryStreamUtil: null);

            return ms.TryGetBuffer(out ArraySegment<byte> seg)
                ? encoding.GetString(seg.Array!, seg.Offset, (int)ms.Length)
                : encoding.GetString(ms.ToArray());
        }

        using var output = memoryStreamUtil.GetSync();
        Serialize(obj, output, encoding, removeNamespaces, removeXsiNilElements, leaveOpen: true, memoryStreamUtil);

        if (output.TryGetBuffer(out ArraySegment<byte> outSeg))
            return encoding.GetString(outSeg.Array!, outSeg.Offset, (int)output.Length);

        return encoding.GetString(output.ToArray());
    }

    /// <summary>
    /// Serialize to a stream (no-op if <paramref name="obj"/> is null).
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

        if (destination is null)
            throw new ArgumentNullException(nameof(destination));

        encoding ??= Encoding.UTF8;

        // Fastest path: direct write, no temp.
        if (!removeXsiNilElements)
        {
            WriteSerialized(obj, destination, encoding, removeNamespaces, leaveOpen);
            return;
        }

        // Filter path requires a temp buffer.
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

        var xs = GetSerializer<T>();

        using var sr = new StringReader(str);
        using var xr = XmlReader.Create(sr, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit
        });

        return (T?)xs.Deserialize(xr);
    }

    /// <summary>
    /// Deserializes an object of type <typeparamref name="T"/> from a stream.
    /// </summary>
    [Pure]
    public static T? Deserialize<T>(Stream? source, bool leaveOpen = false)
    {
        if (source is null)
            return default;

        if (source.CanSeek && source.Length - source.Position == 0)
            return default;

        var xs = GetSerializer<T>();

        using var xr = XmlReader.Create(source, new XmlReaderSettings
        {
            CloseInput = !leaveOpen,
            DtdProcessing = DtdProcessing.Prohibit
        });

        return (T?)xs.Deserialize(xr);
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
            CloseOutput = !leaveOpen
        };

        using var xw = XmlWriter.Create(destination, settings);
        serializer.Serialize(xw, obj, ns);
        xw.Flush();
    }

    /// <summary>
    /// Streaming filter: copies XML from <paramref name="input"/> to <paramref name="output"/>,
    /// skipping any element with xsi:nil="true" or "1".
    /// </summary>
    private static void FilterXsiNilElements(
        Stream input,
        Stream output,
        Encoding encoding,
        bool leaveOpenDestination)
    {
        using var xr = XmlReader.Create(input, new XmlReaderSettings
        {
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = false
        });

        using var xw = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = encoding,
            OmitXmlDeclaration = false,
            Indent = false,
            CloseOutput = !leaveOpenDestination
        });

        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element)
            {
                string? nil = xr.GetAttribute("nil", XsiNs);

                if (nil is not null &&
                    (nil.Length == 1 ? nil[0] == '1' : nil.Equals("true", StringComparison.OrdinalIgnoreCase)))
                {
                    xr.Skip();
                    continue;
                }
            }

            xw.WriteNode(xr, defattr: false);
        }

        xw.Flush();
    }
}
