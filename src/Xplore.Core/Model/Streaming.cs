using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Xplore.Core.Model;

public static class Streaming
{
    // Runs the enumeration on a worker thread and relays its items to the consumer as they arrive.
    public static async IAsyncEnumerable<T> Run<T>(Func<IEnumerable<T>> source, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<T>();
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var item in source())
                {
                    channel.Writer.TryWrite(item);
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            yield return item;
        }
    }
}
