[![](https://img.shields.io/nuget/v/Soenneker.Utils.Xml.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Xml/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.xml/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.utils.xml/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/Soenneker.Utils.Xml.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Xml/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.xml/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.utils.xml/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Utils.Xml
A utility library handling (de)serialization and other useful XML functions.

## Installation

```bash
dotnet add package Soenneker.Utils.Xml
```

## Quick start

```csharp
using Soenneker.Utils.Xml;
```

Call the static `XmlUtil` methods directly; no dependency-injection registration is required.

## Common operations

- `Serialize()` - Serialize to a string (returns null if `obj` is null). Uses pooled streams when `memoryStreamUtil` is provided.
- `Deserialize()` - Accepts a nullable string; if null/empty returns default.
