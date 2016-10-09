﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace Polly.Timeout
{
    internal static partial class TimeoutEngine
    {
        internal static async Task<TResult> ImplementationAsync<TResult>(
            Func<CancellationToken, Task<TResult>> action, 
            Context context, 
            Func<TimeSpan> timeoutProvider, 
            Func<Context, TimeSpan, Task, Task> onTimeoutAsync, 
            CancellationToken cancellationToken, 
            bool continueOnCapturedContext)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan timeout = timeoutProvider();

            using (CancellationTokenSource timeoutCancellationTokenSource = new CancellationTokenSource())
            {
                using (CancellationTokenSource combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellationTokenSource.Token))
                {
                    Task<TResult> actionTask = null;
                    try
                    {
                        //if (timeoutStrategy == TimeoutStrategy.Optimistic)
                        //{
                        //    return await action(combinedToken).ConfigureAwait(continueOnCapturedContext); 
                        //}
                        //else
                        //{

                        Task<TResult> timeoutTask = timeoutCancellationTokenSource.Token.AsTask<TResult>();

                        timeoutCancellationTokenSource.CancelAfter(timeout);

                        actionTask = action(combinedTokenSource.Token);

                        return await (await 
#if NET40
                            TaskEx
#else
                            Task
#endif
                            .WhenAny(actionTask, timeoutTask).ConfigureAwait(continueOnCapturedContext)).ConfigureAwait(continueOnCapturedContext);

                        //}

                    }
                    catch (Exception e)
                    {
                        if (timeoutCancellationTokenSource.IsCancellationRequested)
                        {
                            await onTimeoutAsync(context, timeout, actionTask).ConfigureAwait(continueOnCapturedContext);
                            throw new TimeoutRejectedException("The delegate executed asynchronously through TimeoutPolicy did not complete within the timeout.", e);
                        }

                        throw;
                    }
                }
            }
        }

        private static Task<TResult> AsTask<TResult>(this CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<TResult>();

            // A generalised version of this method would include a hotpath returning a canceled task (rather than setting up a registration) if (cancellationToken.IsCancellationRequested) on entry.  This is omitted, since we only start the timeout countdown in the token _after calling this method.

            IDisposable registration = null;
                registration = cancellationToken.Register(() =>
                {
                    tcs.TrySetCanceled();
                    registration?.Dispose();
                }, useSynchronizationContext: false);

            return tcs.Task;
        }
    }
}
