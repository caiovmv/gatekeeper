using gatekeeper.Authorization;

namespace gatekeeper.Api;

public sealed class PolicySync : IHostedService
{
    private readonly IPolicyStore _store;
    private readonly IPolicyPublisher _publisher;

    public PolicySync(IPolicyStore store, IPolicyPublisher publisher)
    {
        _store = store;
        _publisher = publisher;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.PublishAsync(_store.History(), cancellationToken);
        }
        catch (HttpRequestException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
