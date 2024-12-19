using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Soenneker.Utils.Xml.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;


namespace Soenneker.Utils.Xml.Tests;

[Collection("Collection")]
public class XmlUtilTests : FixturedUnitTest
{
    private readonly IXmlUtil _util;

    public XmlUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IXmlUtil>(true);
    }

    [Fact]
    public void Default()
    {

    }
}
