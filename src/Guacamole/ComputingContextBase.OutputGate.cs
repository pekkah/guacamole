using System;
using System.Collections.Generic;
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
    /// in a row; and the values are delivered in the order in which they were read. Every trigger is
    /// followed by a read of the provider that starts after it (by the runner, which a trigger reaches
    /// through the state's full fences), so once every source operation has returned, the last
    /// delivered value is the current value. Nothing waits on another thread, so no handler can
    /// deadlock with a delivery of the same registration. The flip side: the runner's source operation
    /// returns only once no trigger arrived during its last delivery, so while other threads keep
    /// changing sources it can be held for as long as they do.
    /// </para>
    /// <para>
    /// Exceptions thrown by deliveries made by the runner (its own and those it made for other
    /// triggers) are added to the runner's list, so that <see cref="TriggerOutputs"/> throws them all
    /// as the inner exceptions of one <see cref="AggregateException"/> on the runner's thread. Once
    /// the registration is disposed (also when the context is disposed), deliveries are skipped.
    /// </para>
    /// </remarks>
    private sealed class OutputGate
    {
        private const int Idle = 0;
        private const int Running = 1;
        private const int RunAgain = 2;

        private int _state;

        public void Run(OutputRegistrationBase registration, ComputingContextBase context, ref List<Exception>? exceptions)
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

            while (true)
            {
                try
                {
                    if (!registration.IsDisposed)
                        registration.Deliver(context);
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
        }
    }
}
