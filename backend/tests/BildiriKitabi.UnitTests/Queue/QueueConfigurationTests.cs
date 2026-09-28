using System.ComponentModel.DataAnnotations;
using System.Text;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Infrastructure.Queue;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.UnitTests.Queue;

public sealed class GenerationMessageTests
{
    [Fact]
    public void The_body_is_the_book_uid_and_the_version_in_json()
    {
        var bookUid = Guid.Parse("3f2c1a9e-5b7d-4c21-9e0a-1b2c3d4e5f60");

        var body = Encoding.UTF8.GetString(GenerationMessage.Serialize(bookUid));

        body.ShouldBe("""{"bookUid":"3f2c1a9e-5b7d-4c21-9e0a-1b2c3d4e5f60","version":2}""");
        GenerationMessage.TryParse(Encoding.UTF8.GetBytes(body), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(bookUid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"version":2}""")]
    [InlineData("""{"bookUid":"abc","version":2}""")]
    [InlineData("""{"bookUid":"00000000-0000-0000-0000-000000000000","version":2}""")]
    [InlineData("""{"bookUid":42,"version":2}""")]
    [InlineData("""{"bookUid":"3f2c1a9e-5b7d-4c21-9e0a-1b2c3d4e5f60"}""")]
    [InlineData("""{"bookUid":"3f2c1a9e-5b7d-4c21-9e0a-1b2c3d4e5f60","version":1}""")]
    [InlineData("""{"bookId":"3f2c1a9e-5b7d-4c21-9e0a-1b2c3d4e5f60","version":1}""")]
    [InlineData("""{"bookId":"3f2c1a9e-5b7d-4c21-9e0a-1b2c3d4e5f60","version":2}""")]
    public void Invalid_bodies_are_rejected(string body)
    {
        GenerationMessage.TryParse(Encoding.UTF8.GetBytes(body), out var bookUid).ShouldBeFalse();
        bookUid.ShouldBe(Guid.Empty);
    }
}

public sealed class QueueOptionsTests
{
    [Theory]
    [InlineData("InMemory", true)]
    [InlineData("RabbitMq", true)]
    [InlineData("rabbitmq", false)]
    [InlineData("Kafka", false)]
    [InlineData("", false)]
    public void Only_the_two_known_providers_are_accepted(string provider, bool valid)
    {
        var options = new QueueOptions { Provider = provider };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true).ShouldBe(valid);
        if (!valid && provider.Length > 0)
        {
            results.ShouldContain(r => r.ErrorMessage == "Queue:Provider must be 'InMemory' or 'RabbitMq'.");
        }
    }

    [Fact]
    public void Credentials_are_required_only_for_rabbitmq_and_guest_is_refused()
    {
        var inMemory = new RabbitMqOptionsValidator(Options.Create(new QueueOptions { Provider = QueueOptions.InMemory }));
        var rabbit = new RabbitMqOptionsValidator(Options.Create(new QueueOptions { Provider = QueueOptions.RabbitMq }));

        inMemory.Validate(null, new RabbitMqOptions()).Succeeded.ShouldBeTrue();

        var missing = rabbit.Validate(null, new RabbitMqOptions());
        missing.Failed.ShouldBeTrue();
        missing.Failures!.ShouldContain(f => f.Contains("RabbitMq:Username", StringComparison.Ordinal));
        missing.Failures!.ShouldContain(f => f.Contains("RabbitMq:Password", StringComparison.Ordinal));

        rabbit.Validate(null, new RabbitMqOptions { Username = "guest", Password = "guest" }).Failed.ShouldBeTrue();
        rabbit.Validate(null, new RabbitMqOptions { Username = "kitap", Password = "gizli-parola" }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Dead_letter_names_follow_the_queue_name()
    {
        var options = new RabbitMqOptions { QueueName = "bildiri-kitabi.uretim" };

        options.DeadLetterExchange.ShouldBe("bildiri-kitabi.uretim.dlx");
        options.DeadLetterQueue.ShouldBe("bildiri-kitabi.uretim.dlq");
    }
}
