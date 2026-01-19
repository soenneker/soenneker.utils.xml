using System.Text;
using System.Xml.Serialization;
using Soenneker.Tests.FixturedUnit;
using Xunit;


namespace Soenneker.Utils.Xml.Tests;

[Collection("Collection")]
public class XmlUtilTests : FixturedUnitTest
{
    public XmlUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
    }

    [Fact]
    public void Serialize_removes_xsi_nil_elements_by_default()
    {
        var xml = XmlUtil.Serialize(new Sample
        {
            Required = "hello",
            Optional = null
        }, Encoding.UTF8, removeNamespaces: true, removeXsiNilElements: true);

        Assert.NotNull(xml);
        Assert.DoesNotContain("nil=\"true\"", xml);
        Assert.DoesNotContain("nil=\"1\"", xml);
        Assert.DoesNotContain("<Optional", xml);
        Assert.Contains("<Required>hello</Required>", xml);
    }

    [Fact]
    public void Serialize_keeps_xsi_nil_elements_when_disabled()
    {
        var xml = XmlUtil.Serialize(new Sample
        {
            Required = "hello",
            Optional = null
        }, Encoding.UTF8, removeNamespaces: true, removeXsiNilElements: false);

        Assert.NotNull(xml);
        Assert.True(xml.Contains("nil=\"true\"") || xml.Contains("nil=\"1\""));
        Assert.Contains("<Optional", xml);
        Assert.Contains("<Required>hello</Required>", xml);
    }

    [Fact]
    public void Deserialize_round_trip()
    {
        var original = new Sample
        {
            Required = "req",
            Optional = "opt"
        };

        var xml = XmlUtil.Serialize(original, Encoding.UTF8, removeNamespaces: true, removeXsiNilElements: true);
        var result = XmlUtil.Deserialize<Sample>(xml);

        Assert.NotNull(result);
        Assert.Equal(original.Required, result!.Required);
        Assert.Equal(original.Optional, result.Optional);
    }
}

[XmlRoot("Sample")]
public class Sample
{
    [XmlElement("Required")]
    public string Required { get; set; } = string.Empty;

    [XmlElement("Optional", IsNullable = true)]
    public string? Optional { get; set; }
}
