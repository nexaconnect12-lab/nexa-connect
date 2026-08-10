using NexaConnect.Infrastructure.Messaging;

namespace NexaConnect.UnitTests;

public sealed class InboxConsumerTests
{
    [Fact]
    public async Task Duplicate_delivery_is_skipped_after_completion()
    {
        var store = new InMemoryInboxStore();
        Guid messageId = Guid.NewGuid();
        int calls = 0;

        Assert.True(await store.ExecuteOnceAsync(messageId, "inventory.projection", _ =>
        {
            calls++;
            return Task.CompletedTask;
        }));
        Assert.False(await store.ExecuteOnceAsync(messageId, "inventory.projection", _ =>
        {
            calls++;
            return Task.CompletedTask;
        }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failed_delivery_is_released_for_retry()
    {
        var store = new InMemoryInboxStore();
        Guid messageId = Guid.NewGuid();
        int calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteOnceAsync(
            messageId, "kitchen.ticket", _ =>
            {
                calls++;
                throw new InvalidOperationException("transient");
            }));

        Assert.True(await store.ExecuteOnceAsync(messageId, "kitchen.ticket", _ =>
        {
            calls++;
            return Task.CompletedTask;
        }));

        Assert.Equal(2, calls);
    }
}
