using System.Runtime.ExceptionServices;

namespace DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff;

internal static class ValueShapeProducerConsumerRunner
{
    public static TResult RunProducerConsumer<TValue, TResult>(
        Action<SingleValueHandoff<TValue>> produce,
        Func<SingleValueHandoff<TValue>, TResult> consume)
        where TValue : class
    {
        using var handoff = new SingleValueHandoff<TValue>();
        Exception? producerError = null;
        Exception? consumerError = null;
        TResult result = default!;
        var producer = new Thread(() => RunProducer(handoff, produce, ref producerError))
        {
            IsBackground = true,
            Name = "ValueShapeCache producer",
        };
        var consumer = new Thread(() =>
        {
            try
            {
                result = consume(handoff);
            }
            catch (Exception error)
            {
                consumerError = error;
                handoff.Abort(error);
            }
        })
        {
            IsBackground = true,
            Name = "ValueShapeCache consumer",
        };

        consumer.Start();
        producer.Start();
        producer.Join();
        consumer.Join();
        var error = producerError ?? consumerError;
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result;
    }

    private static void RunProducer<TValue>(
        SingleValueHandoff<TValue> handoff,
        Action<SingleValueHandoff<TValue>> produce,
        ref Exception? error)
        where TValue : class
    {
        try
        {
            produce(handoff);
        }
        catch (Exception caught)
        {
            error = caught;
            handoff.Abort(caught);
        }
    }
}
