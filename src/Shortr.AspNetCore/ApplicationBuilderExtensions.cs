using System;
using System.ComponentModel;
using System.Net;
using System.Security.Policy;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shortr;

// ReSharper disable once CheckNamespace -- Discoverability
namespace Microsoft.AspNetCore.Builder
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseShortr(this IApplicationBuilder builder)
        {
            builder.UseEndpoints(endpoints =>
            {
                var generator = endpoints.ServiceProvider.GetRequiredService<IKeyGenerator>();
                var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ShortrOptions>>().Value;

                var shortenUrlPath = options.ShortenUrlPath.ToString();
                var shortUrlPath = options.ShortUrlPath;

                var baseAddress = options.BaseAddress;

                // baseAddress now is expected to only be concat to a key to result in a final shortened URL.
                var baseAddressShortUrl = new Uri(baseAddress, shortUrlPath!);

                var store = endpoints.ServiceProvider.GetRequiredService<IShortrStore>();
                var urlValidation = endpoints.ServiceProvider.GetRequiredService<UrlValidation>();

                endpoints.MapPut(shortenUrlPath, async context =>
                {
                    if (!context.Request.Query.TryGetValue("url", out var url)
                        || string.IsNullOrWhiteSpace(url)
                        || !urlValidation.IsValidUrl(url[0]))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        // TODO: Add body with reason
                        return;
                    }

                    var useTtl = options.AllowDomainTtl;
                    var intTtl = 0;
                    if (useTtl && context.Request.Query.TryGetValue("ttl", out var ttl)
                               && int.TryParse(ttl, out intTtl)
                        || !(await store.GetKey(url, context.RequestAborted) is {} key))
                    {
                        key = generator.Generate();
                        await store.RegisterUrl(
                            new RegistrationOptions(key, url, useTtl ? intTtl : (int?)null),
                            context.RequestAborted);
                    }

                    context.Response.Headers.Add("Location", baseAddressShortUrl + key);
                    context.Response.Headers.Add("Key", key);
                    context.Response.Headers.Add("Access-Control-Expose-Headers", "Location,Key");

                    context.Response.StatusCode = (int)HttpStatusCode.Created;
                    await context.Response.CompleteAsync().ConfigureAwait(false);
                });

                endpoints.MapGet(shortUrlPath + "{key}", async context =>
                {
                    var key = (string)context.GetRouteValue("key");
                    if (await store.GetUrl(key, context.RequestAborted) is {} url)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.PermanentRedirect;
                        context.Response.Headers.Add("Location", url);
                        context.Response.Headers.Add("Key", key);
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    }

                    await context.Response.CompleteAsync().ConfigureAwait(false);
                });
            });
            return builder;
        }
    }
}
