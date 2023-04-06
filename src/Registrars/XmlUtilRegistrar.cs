using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Utils.Xml.Abstract;

namespace Soenneker.Utils.Xml.Registrars;

public static class XmlUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IXmlUtil"/> as a scoped service. (Recommended)<para/>
    /// Shorthand for <code>services.TryAddScoped</code> <para/>
    /// </summary>
    public static void AddXmlUtilAsScoped(this IServiceCollection services)
    {
        services.TryAddScoped<IXmlUtil, XmlUtil>();
    }

    /// <summary>
    /// Adds <see cref="IXmlUtil"/> as a singleton service.<para/>
    /// Shorthand for <code>services.TryAddSingleton</code> <para/>
    /// </summary>
    public static void AddXmlUtilAsSingleton(this IServiceCollection services)
    {
        services.TryAddSingleton<IXmlUtil, XmlUtil>();
    }
}