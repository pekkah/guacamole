using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Guacamole;

public abstract partial class ComputingContextBase
{
    /// <summary>
    /// Serializes the deliveries of one output registration (#92) without blocking and without
    /// holding a lock while user code runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// At most one thread delivers at a time (the runner). A trigger that arrives while a delivery is
    /// running, from another thread or re-entrantly from the handler itself (a handler that changes a
    /// source), does not wait and does not nest: it asks the runner to deliver again and returns. The
    /// runner, once the handler returns, reads the provider again and delivers the then current value
    /// if it differs from the last delivered one, and repeats until no trigger arrived during its last
    /// delivery.
    /// </para>
    /// <para>
    /// Hence a handler is never called concurrently with itself or re-entrantly; the comparison with
    /// the last delivered value and its update are never interleaved, so no value is delivered twice
    /// in a row; and the values are delivered in the order in which they were read. Every trigger is followed by a read of the provider that starts after it
    /// (by the runner, which a trigger reaches through the state's full fences), so once every
    /// source operation has returned, the last delivered value is the current value. Nothing waits on
    /// another thread, so no handler can deadlock with a delivery of the same registration.
    /// </para>
    /// <para>
    /// Exceptions thrown by deliveries made by the runner (its own and those it made for other
    /// triggers) are thrown by the runner: one is rethrown as is, several as an
    /// <see cref="AggregateException"/>. Once the registration is disposed (also when the context is
    /// disposed), deliveries are skipped, and an <see cref="ObjectDisposedException"/> from a provider
    /// read that raced with the context's disposal is ignored.
    /// </para>
    /// </remarks>
    private sealed class OutputGate
    {
        private const int Idle = 0;
        private const int Running = 1;
        private const int RunAgain = 2;

        private int _state;

        public void Run(OutputRegistrationBase registration, ComputingContextBase context)
        {
            // Become the runner, or hand the delivery to the current runner
            var state = Volatile.Read(ref _state);
            while (true)
            {
                if (state == RunAgain)
                    return; // the runner delivers again anyway

                var seen = Interlocked.CompareExchange(ref _state, state == Idle ? Running : RunAgain, state);
                if (seen == state)
                {
                    if (state == Idle)
                        break;

                    return; // the runner delivers again after its current delivery
                }

                state = seen;
            }

            List<Exception>? exceptions = null;
            while (true)
            {
                try
                {
                    if (!registration.IsDisposed)
                        registration.Deliver(context);
                }
                catch (ObjectDisposedException) when (registration.IsDisposed)
                {
                    // The context was disposed during the delivery (it marks its registrations
                    // disposed before its providers start throwing): its handlers no longer run
                }
                catch (Exception ex)
                {
                    (exceptions ??= new List<Exception>()).Add(ex);
                }

                if (Interlocked.CompareExchange(ref _state, Idle, Running) == Running)
                    break;

                // A trigger arrived during the delivery (the state is RunAgain, and only the runner
                // changes it from there): deliver again, after the trigger's change
                Interlocked.Exchange(ref _state, Running);
            }

            if (exceptions is null)
                return;

            if (exceptions.Count == 1)
                ExceptionDispatchInfo.Capture(exceptions[0]).Throw();

            throw new AggregateException(exceptions);
        }
    }
}
