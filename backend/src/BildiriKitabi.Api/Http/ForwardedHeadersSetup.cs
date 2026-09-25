using Microsoft.AspNetCore.HttpOverrides;

namespace BildiriKitabi.Api.Http;

/// <summary>
/// Behind a reverse proxy (nginx in Docker) the client address arrives in <c>X-Forwarded-For</c>. When
/// <c>ForwardedHeaders:Enabled</c> is true, the headers are applied for requests coming from loopback or from the
/// networks in <c>ForwardedHeaders:KnownNetworks</c> (CIDR), so rate limiting partitions by the real client.
/// Off by default: without a trusted proxy the headers could be forged by any client.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue($"{SectionName}:Enabled", false);

    public static IServiceCollection AddApiForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var networks = configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? [];
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var network in networks)
            {
                if (!System.Net.IPNetwork.TryParse(network, out var parsed))
                {
                    throw new InvalidOperationException($"{SectionName}:KnownNetworks contains an invalid CIDR value: '{network}'.");
                }

                options.KnownIPNetworks.Add(parsed);
            }
        });
        return services;
    }
}
