using System.Text;

namespace Soenneker.Utils.Xml.Abstract;

/// <summary>
/// Singleton IoC
/// </summary>
public interface IXmlUtil
{
    /// <summary>
    /// Accepts a nullable object... if null returns null.
    /// </summary>
    string? Serialize<T>(T? obj, Encoding? encoding, bool removeNamespaces = true);

    T? Deserialize<T>(string str);
}