using System.Net;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Shortr.Web.Tests
{
    public class IntegrationTests : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        public IntegrationTests(WebApplicationFactory<Startup> factory) => _factory = factory;

        [Fact]
        public async Task RoundTrip()
        {
            var factory = _factory.WithWebHostBuilder(b =>
                b.ConfigureServices(s =>
                    s.Replace(ServiceDescriptor.Singleton<IShortrStore, InMemoryShortrStore>())));

            var options = factory.Services.GetRequiredService<IOptions<ShortrOptions>>().Value;
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect = false});

            const string longUrl = "https://nugettrends.com/packages?months=12&ids=Sentry";
            var shorten = $"/{options.ShortenUrlPath}?url={HttpUtility.UrlEncode(longUrl)}";

            var response = await client.PutAsync(shorten, null);

            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("Key", out var keys));
            Assert.True(response.Headers.TryGetValues("Location", out var locations));
            var key = Assert.Single(keys);
            var location = Assert.Single(locations);
            Assert.EndsWith(key, location);

            response = await client.GetAsync(location);
            Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
            Assert.Equal(longUrl, response.Headers.Location.AbsoluteUri);
        }
    }
}