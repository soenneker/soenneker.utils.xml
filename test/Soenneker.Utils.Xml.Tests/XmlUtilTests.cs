using System.Text;
using System.Xml.Serialization;
using AwesomeAssertions;
using Soenneker.Tests.HostedUnit;


namespace Soenneker.Utils.Xml.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public class XmlUtilTests : HostedUnitTest
{
    public XmlUtilTests(Host host) : base(host)
    {
    }

    [Test]
    public void Serialize_removes_xsi_nil_elements_by_default()
    {
        var xml = XmlUtil.Serialize(new Sample
        {
            Required = "hello",
            Optional = null
        }, Encoding.UTF8, removeNamespaces: true, removeXsiNilElements: true);

        xml.Should().NotBeNull();
        xml.Should().NotContain("nil=\"true\"");
        xml.Should().NotContain("nil=\"1\"");
        xml.Should().NotContain("<Optional");
        xml.Should().Contain("<Required>hello</Required>");
    }

    [Test]
    public void Serialize_keeps_xsi_nil_elements_when_disabled()
    {
        var xml = XmlUtil.Serialize(new Sample
        {
            Required = "hello",
            Optional = null
        }, Encoding.UTF8, removeNamespaces: true, removeXsiNilElements: false);

        xml.Should().NotBeNull();
        (xml.Contains("nil=\"true\"") || xml.Contains("nil=\"1\"")).Should().BeTrue();
        xml.Should().Contain("<Optional");
        xml.Should().Contain("<Required>hello</Required>");
    }

    [Test]
    public void Deserialize_round_trip()
    {
        var original = new Sample
        {
            Required = "req",
            Optional = "opt"
        };

        var xml = XmlUtil.Serialize(original, Encoding.UTF8, removeNamespaces: true, removeXsiNilElements: true);
        var result = XmlUtil.Deserialize<Sample>(xml);

        result.Should().NotBeNull();
        result!.Required.Should().Be(original.Required);
        result.Optional.Should().Be(original.Optional);
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
