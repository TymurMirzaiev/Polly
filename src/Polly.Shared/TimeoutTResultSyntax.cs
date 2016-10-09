﻿using System;
using Polly.Timeout;
using System.Threading.Tasks;

namespace Polly
{
    public partial class Policy
    {
        /// <summary>
        /// Builds a <see cref="Policy{TResult}"/> that will wait for a delegate to complete for a specified period of time. A <see cref="TimeoutRejectedException"/> will be thrown if the delegate does not complete within the configured timeout.
        /// </summary>
        /// <param name="seconds">The number of seconds after which to timeout.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">seconds;Value must be greater than zero.</exception>
        /// <returns>The policy instance.</returns>
        public static TimeoutPolicy<TResult> Timeout<TResult>(int seconds)
        {
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            Action<Context, TimeSpan, Task> doNothing = (_, __, ___) => { };

            return Timeout<TResult>(() => TimeSpan.FromSeconds(seconds), doNothing);
        }

        /// <summary>
        /// Builds a <see cref="Policy{TResult}"/> that will wait for a delegate to complete for a specified period of time. A <see cref="TimeoutRejectedException"/> will be thrown if the delegate does not complete within the configured timeout.
        /// </summary>
        /// <param name="seconds">The number of seconds after which to timeout.</param>
        /// <param name="onTimeout">An action to call on timeout, passing the execution context, the timeout applied, and a <see cref="Task{TResult}"/> capturing the abandoned, timed-out action. 
        /// <remarks>The Task parameter will be null if the executed action responded co-operatively to cancellation before the policy timed it out.</remarks></param>
        /// <returns>The policy instance.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">seconds;Value must be greater than zero.</exception>
        /// <exception cref="System.ArgumentNullException">onTimeout</exception>
        public static TimeoutPolicy<TResult> Timeout<TResult>(int seconds, Action<Context, TimeSpan, Task> onTimeout)
        {
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));
            return Timeout<TResult>(() => TimeSpan.FromSeconds(seconds), onTimeout);
        }

        /// <summary>
        /// Builds a <see cref="Policy{TResult}"/> that will wait for a delegate to complete for a specified period of time. A <see cref="TimeoutRejectedException"/> will be thrown if the delegate does not complete within the configured timeout.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns>The policy instance.</returns>
        public static TimeoutPolicy<TResult> Timeout<TResult>(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            Action<Context, TimeSpan, Task> doNothing = (_, __, ___) => { };

            return Timeout<TResult>(() => timeout, doNothing);
        }

        /// <summary>
        /// Builds a <see cref="Policy{TResult}"/> that will wait for a delegate to complete for a specified period of time. A <see cref="TimeoutRejectedException"/> will be thrown if the delegate does not complete within the configured timeout.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="onTimeout">An action to call on timeout, passing the execution context, the timeout applied, and a <see cref="Task{TResult}"/> capturing the abandoned, timed-out action. 
        /// <remarks>The Task parameter will be null if the executed action responded co-operatively to cancellation before the policy timed it out.</remarks></param>
        /// <returns>The policy instance.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">timeout;Value must be greater than zero.</exception>
        /// <exception cref="System.ArgumentNullException">onTimeout</exception>
        public static TimeoutPolicy<TResult> Timeout<TResult>(TimeSpan timeout, Action<Context, TimeSpan, Task> onTimeout)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));
            return Timeout<TResult>(() => timeout, onTimeout);
        }

        /// <summary>
        /// Builds a <see cref="Policy{TResult}"/> that will wait for a delegate to complete for a specified period of time. A <see cref="TimeoutRejectedException"/> will be thrown if the delegate does not complete within the configured timeout.
        /// </summary>
        /// <param name="timeoutProvider">A function to provide the timeout for this execution.</param>
        /// <exception cref="System.ArgumentNullException">timeoutProvider</exception>
        /// <returns>The policy instance.</returns>
        public static TimeoutPolicy<TResult> Timeout<TResult>(Func<TimeSpan> timeoutProvider)
        {
            Action<Context, TimeSpan, Task> doNothing = (_, __, ___) => { };
            return Timeout<TResult>(timeoutProvider, doNothing);
        }

        /// <summary>
        /// Builds a <see cref="Policy{TResult}"/> that will wait for a delegate to complete for a specified period of time. A <see cref="TimeoutRejectedException"/> will be thrown if the delegate does not complete within the configured timeout.
        /// </summary>
        /// <param name="timeoutProvider">A function to provide the timeout for this execution.</param>
        /// <param name="onTimeout">An action to call on timeout, passing the execution context, the timeout applied, and a <see cref="Task{TResult}"/> capturing the abandoned, timed-out action. 
        /// <remarks>The Task parameter will be null if the executed action responded co-operatively to cancellation before the policy timed it out.</remarks></param>
        /// <returns>The policy instance.</returns>
        /// <exception cref="System.ArgumentNullException">timeoutProvider</exception>
        /// <exception cref="System.ArgumentNullException">onTimeout</exception>
        public static TimeoutPolicy<TResult> Timeout<TResult>(Func<TimeSpan> timeoutProvider, Action<Context, TimeSpan, Task> onTimeout)
        {
            if (timeoutProvider == null) throw new ArgumentNullException(nameof(timeoutProvider));
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));

            return new TimeoutPolicy<TResult>(
                (action, context, cancellationToken) => TimeoutEngine.Implementation<TResult>(
                    action,
                    context,
                    cancellationToken,
                    timeoutProvider,
                    onTimeout)
                );
        }
        
    }
}