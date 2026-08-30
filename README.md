[![](https://img.shields.io/nuget/v/Soenneker.Utils.Xml.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Xml/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.xml/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.utils.xml/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/Soenneker.Utils.Xml.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Xml/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.xml/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.utils.xml/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Utils.Xml
Cached `XmlSerializer` helpers for strings and streams, with secure reader defaults and optional removal of nil elements.

## Installation

```bash
dotnet add package Soenneker.Utils.Xml
```

## Serialize to a string

```csharp
using Soenneker.Utils.Xml;

string? xml = XmlUtil.Serialize(order);
```

`Serialize<T>` returns `null` when the object is null. By default it uses UTF-8, suppresses the serializer's standard namespace declarations, and removes elements marked `xsi:nil="true"`, `xsi:nil="1"`, or the equivalent unqualified `nil` attribute.

Removing nil elements is a structural transformation: the entire marked element is removed, not converted to an empty element. It requires an `XDocument` parse-and-save pass. Set `removeXsiNilElements: false` for direct streaming and standard `XmlSerializer` nil output.

```csharp
string? xml = XmlUtil.Serialize(
    order,
    encoding: Encoding.UTF8,
    removeNamespaces: false,
    removeXsiNilElements: false);
```

`removeNamespaces` controls the namespace declarations supplied to `XmlSerializer`; it does not strip namespaces required by attributes or type metadata.

## Serialize to a stream

```csharp
await using var destination = File.Create("order.xml");

XmlUtil.Serialize(
    order,
    destination,
    leaveOpen: true);
```

Serialization begins at the stream's current position. The utility does not rewind or truncate an existing destination. With the default `leaveOpen: false`, the destination is closed after a non-null object is serialized. A null object is a no-op.

Pass an `IMemoryStreamUtil` to use its pooled stream for temporary serialization. The caller retains ownership of the utility; temporary streams obtained from it are disposed by `XmlUtil`.

## Deserialize

```csharp
Order? fromString = XmlUtil.Deserialize<Order>(xml);
Order? fromStream = XmlUtil.Deserialize<Order>(stream, leaveOpen: true);
```

Null or empty input returns `default`. Stream deserialization starts at the current position and returns `default` when a seekable stream has no remaining bytes. XML declarations determine stream encoding automatically.

DTD processing is prohibited and external XML resolution is disabled. Malformed XML, schema/type mismatches, and serializer errors propagate. The utility does not impose a document-size limit, so constrain untrusted input before deserializing when resource exhaustion is a concern.

`XmlSerializer` instances are cached per closed generic type. Call the static methods directly; no dependency-injection registration is required.
