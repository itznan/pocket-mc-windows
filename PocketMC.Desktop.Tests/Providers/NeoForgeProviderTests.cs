using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Instances.Models;
using Xunit;

namespace PocketMC.Desktop.Tests.Providers;

public class NeoForgeProviderTests
{
    private const string MockMetadata = @"
<metadata>
  <groupId>net.neoforged</groupId>
  <artifactId>neoforge</artifactId>
  <versioning>
    <latest>21.1.65</latest>
    <release>21.1.65</release>
    <versions>
      <version>20.2.86-beta</version>
      <version>20.4.127</version>
      <version>21.1.65</version>
    </versions>
    <lastUpdated>20241021123456</lastUpdated>
  </versioning>
</metadata>";

    [Fact]
    public async Task GetAvailableVersionsAsync_MapsCorrectlyToMinecraft()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
           .Protected()
           .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>()
           )
           .ReturnsAsync(new HttpResponseMessage()
           {
              StatusCode = HttpStatusCode.OK,
              Content = new StringContent(MockMetadata),
           })
           .Verifiable();

        var httpClient = new HttpClient(handlerMock.Object);
        var provider = new NeoForgeProvider(httpClient, null!);

        // Act
        var versions = await provider.GetAvailableVersionsAsync();

        // Assert
        Assert.NotEmpty(versions);
        
        // 21.1.65 -> 1.21.1
        var v1211 = versions.FirstOrDefault(v => v.Id == "1.21.1") as GameVersionWithLoaders;
        Assert.NotNull(v1211);
        Assert.Contains(v1211!.LoaderVersions, l => l.Version == "21.1.65");

        // 20.4.127 -> 1.20.4
        var v1204 = versions.FirstOrDefault(v => v.Id == "1.20.4") as GameVersionWithLoaders;
        Assert.NotNull(v1204);
        Assert.Contains(v1204!.LoaderVersions, l => l.Version == "20.4.127");

        // 20.2.86-beta -> 1.20.2
        var v1202 = versions.FirstOrDefault(v => v.Id == "1.20.2") as GameVersionWithLoaders;
        Assert.NotNull(v1202);
        Assert.Contains(v1202!.LoaderVersions, l => l.Version == "20.2.86-beta");
    }
}
