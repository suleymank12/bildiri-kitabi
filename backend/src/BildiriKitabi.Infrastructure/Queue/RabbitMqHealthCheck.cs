using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>Healthy when the connection to RabbitMQ is open and a channel can be opened on it.</summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        connection.EnsureStarted();
        if (connection.Current is not { } current)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ bağlantısı yok.");
        }

        try
        {
            var channel = await current.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                await channel.CloseAsync(cancellationToken).ConfigureAwait(false);
            }

            return HealthCheckResult.Healthy("RabbitMQ bağlı.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ kanalı açılamadı.", ex);
        }
    }
}
