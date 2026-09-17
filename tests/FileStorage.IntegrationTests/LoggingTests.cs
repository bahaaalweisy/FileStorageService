using System.Collections.Concurrent;
using FileStorage.Api.Middleware;
using FileStorage.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace FileStorage.IntegrationTests;

public class LoggingTests : IntegrationTestBase
{

    private sealed class CapturedLine
    {
        public required string Message { get; init; }
        public required IReadOnlyList<object?> Scopes { get; init; }

        public string Rendered => Message + " " + string.Join(" ", Scopes);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLine> Lines { get; } = new();
        private readonly IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, _scopeProvider);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner, IExternalScopeProvider scopeProvider) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => scopeProvider.Push(state);

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var scopes = new List<object?>();
                scopeProvider.ForEachScope((scopeValue, list) =>
                {
                    if (scopeValue is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        foreach (var pair in pairs)
                        {
                            list.Add($"{pair.Key}={pair.Value}");
                        }
                    }
                    else
                    {
                        list.Add(scopeValue);
                    }
                }, scopes);

                owner.Lines.Enqueue(new CapturedLine { Message = formatter(state, exception), Scopes = scopes });
            }
        }
    }

    private (WebApplicationFactory<Program> factory, HttpClient client, CapturingLoggerProvider sink) CreateCapturingClient()
    {
        var sink = new CapturingLoggerProvider();
        var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(sink));
        });
        return (factory, factory.CreateClient(), sink);
    }

    [Fact]
    public async Task GeneratedCorrelationId_AppearsInBothResponseHeaderAndApplicationLogEntry()
    {
        var (factory, client, sink) = CreateCapturingClient();
        try
        {
            var token = await TestHelpers.GetTokenAsync(client, "user");
            TestHelpers.AuthorizeAs(client, token);

            using var content = TestHelpers.BuildUploadContent(new byte[50], "logging-test-generated.txt", "text/plain");
            var response = await client.PostAsync("/api/files", content);
            response.EnsureSuccessStatusCode();

            var correlationId = response.Headers.GetValues(CorrelationId.HeaderName).Single();
            correlationId.Should().NotBeNullOrWhiteSpace();

            var matching = sink.Lines.FirstOrDefault(l => l.Message.Contains("Upload completed"));
            matching.Should().NotBeNull("the application's own log statement for this request should have fired");
            matching!.Rendered.Should().Contain(correlationId,
                "the correlation ID returned in the response header must also appear in the application's rendered log entry for this request, not only in an unrendered logging scope");
        }
        finally
        {
            client.Dispose();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task SuppliedCorrelationId_IsEchoedBackAndAppearsInApplicationLogEntry()
    {
        var (factory, client, sink) = CreateCapturingClient();
        try
        {
            var token = await TestHelpers.GetTokenAsync(client, "user");
            TestHelpers.AuthorizeAs(client, token);

            var suppliedCorrelationId = "qa-supplied-" + Guid.NewGuid().ToString("N");
            client.DefaultRequestHeaders.Add(CorrelationId.HeaderName, suppliedCorrelationId);

            using var content = TestHelpers.BuildUploadContent(new byte[50], "logging-test-supplied.txt", "text/plain");
            var response = await client.PostAsync("/api/files", content);
            response.EnsureSuccessStatusCode();

            var echoedCorrelationId = response.Headers.GetValues(CorrelationId.HeaderName).Single();
            echoedCorrelationId.Should().Be(suppliedCorrelationId, "a valid client-supplied correlation ID must be echoed back, not replaced with a generated one");

            var matching = sink.Lines.FirstOrDefault(l => l.Message.Contains("Upload completed"));
            matching.Should().NotBeNull();
            matching!.Rendered.Should().Contain(suppliedCorrelationId,
                "the client-supplied correlation ID must appear in the application's rendered log entry for the request it was sent with");
        }
        finally
        {
            client.Dispose();
            factory.Dispose();
        }
    }
}
