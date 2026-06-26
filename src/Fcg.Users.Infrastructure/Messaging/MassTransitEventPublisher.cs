using Fcg.Users.Application.Interfaces;
using MassTransit;

namespace Fcg.Users.Infrastructure.Messaging;

public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class =>
        publishEndpoint.Publish(message, ct);
}
