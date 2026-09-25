using System.ComponentModel.DataAnnotations;
using BildiriKitabi.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>
/// <c>RabbitMq</c> section. The password never comes from an appsettings file: use the environment variable
/// <c>RabbitMq__Password</c> or user secrets.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = "localhost";

    [Range(1, 65_535)]
    public int Port { get; set; } = 5672;

    [Required(AllowEmptyStrings = false)]
    public string VirtualHost { get; set; } = "/";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Name of the work queue; the dead-letter exchange and queue are named after it.</summary>
    [Required(AllowEmptyStrings = false)]
    public string QueueName { get; set; } = "bildiri-kitabi.uretim";

    /// <summary>How long a publish waits for the broker's confirmation.</summary>
    [Range(1, 300)]
    public int PublishConfirmTimeoutSeconds { get; set; } = 10;

    public string DeadLetterExchange => $"{QueueName}.dlx";

    public string DeadLetterQueue => $"{QueueName}.dlq";
}

/// <summary>Credentials are required when RabbitMQ is the provider, and the well-known guest account is refused.</summary>
public sealed class RabbitMqOptionsValidator(IOptions<QueueOptions> queue) : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!queue.Value.UsesRabbitMq)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Username))
        {
            failures.Add("RabbitMq:Username is required when Queue:Provider is 'RabbitMq'.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            failures.Add("RabbitMq:Password is required when Queue:Provider is 'RabbitMq' (set RabbitMq__Password or a user secret).");
        }

        if (string.Equals(options.Username, "guest", StringComparison.Ordinal) && string.Equals(options.Password, "guest", StringComparison.Ordinal))
        {
            failures.Add("The default RabbitMQ guest/guest account is not allowed; create a dedicated user.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
