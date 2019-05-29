﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Open.ChannelExtensions
{
	public static partial class Extensions
	{
		/// <summary>
		/// Asynchronously writes all entries from the source to the channel.
		/// </summary>
		/// <typeparam name="T">The input type of the channel.</typeparam>
		/// <param name="target">The channel to write to.</param>
		/// <param name="maxConcurrency">The maximum number of concurrent operations.</param>
		/// <param name="source">The asynchronous source data to use.</param>
		/// <param name="complete">If true, will call .Complete() if all the results have successfully been written (or the source is emtpy).</param>
		/// <param name="cancellationToken">An optional cancellation token.</param>
		/// <returns>A task which completes when all the data has been written to the channel writer.</returns>
		public static Task WriteAllConcurrentlyAsync<T>(this ChannelWriter<T> target,
			int maxConcurrency, IEnumerable<ValueTask<T>> source, bool complete = false, CancellationToken cancellationToken = default)
		{
			if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "Must be at least 1.");
			Contract.EndContractBlock();

			if (cancellationToken.IsCancellationRequested)
				return Task.FromCanceled(cancellationToken);

			if (maxConcurrency == 1)
				return target.WriteAllAsync(source, complete, cancellationToken).AsTask();

			var shouldWait = target
				.WaitToWriteAndThrowIfClosedAsync("The target channel was closed before writing could begin.", cancellationToken)
				.AsTask(); // ValueTasks can only have a single await.

			var enumerator = source.GetEnumerator();
			var writers = new Task<bool>[maxConcurrency];
			for (var w = 0; w < maxConcurrency; w++)
				writers[w] = WriteAllAsyncCore();

			return Task
				.WhenAll(writers)
				.ContinueWith(t =>
				{
					if (complete)
						target.Complete();

					return (!t.IsFaulted && !t.IsCanceled && t.Result.Any(r => r)) ? Task.FromCanceled(cancellationToken) : t;
				}, TaskContinuationOptions.ExecuteSynchronously)
				.Unwrap();

			// returns false if there's no more (wasn't cancelled).
			async Task<bool> WriteAllAsyncCore()
			{
				await shouldWait;
				var next = new ValueTask();
				var more = false; // if it completed and actually returned false, no need to bubble the cancellation since it actually completed.
				while (!cancellationToken.IsCancellationRequested
					&& (more = TryMoveNextSynchronized(enumerator, out var e)))
				{
					var value = await e.ConfigureAwait(false);
					await next.ConfigureAwait(false);
					next = target.WriteAsync(value, cancellationToken);
				}
				await next.ConfigureAwait(false);
				return more;
			}
		}

		static bool TryMoveNextSynchronized<T>(IEnumerator<T> source, out T value)
		{
			lock (source)
			{
				if (source.MoveNext())
				{
					value = source.Current;
					return true;
				}
			}

			value = default;
			return false;
		}

		/// <summary>
		/// Asynchronously writes all entries from the source to the channel.
		/// </summary>
		/// <typeparam name="T">The input type of the channel.</typeparam>
		/// <param name="target">The channel to write to.</param>
		/// <param name="maxConcurrency">The maximum number of concurrent operations.</param>
		/// <param name="source">The asynchronous source data to use.</param>
		/// <param name="complete">If true, will call .Complete() if all the results have successfully been written (or the source is emtpy).</param>
		/// <param name="cancellationToken">An optional cancellation token.</param>
		/// <returns>A task which completes when all the data has been written to the channel writer.</returns>
		public static Task WriteAllConcurrentlyAsync<T>(this ChannelWriter<T> target,
			int maxConcurrency, IEnumerable<Task<T>> source, bool complete = false, CancellationToken cancellationToken = default)
			=> WriteAllConcurrentlyAsync(target, maxConcurrency, source.Select(e => new ValueTask<T>(e)), complete, cancellationToken);

		/// <summary>
		/// Asynchronously executes all entries and writes their results to the channel.
		/// </summary>
		/// <typeparam name="T">The input type of the channel.</typeparam>
		/// <param name="target">The channel to write to.</param>
		/// <param name="maxConcurrency">The maximum number of concurrent operations.</param>
		/// <param name="source">The asynchronous source data to use.</param>
		/// <param name="complete">If true, will call .Complete() if all the results have successfully been written (or the source is emtpy).</param>
		/// <param name="cancellationToken">An optional cancellation token.</param>
		/// <returns>A task which completes when all the data has been written to the channel writer.</returns>
		public static Task WriteAllConcurrentlyAsync<T>(this ChannelWriter<T> target,
			int maxConcurrency, IEnumerable<Func<T>> source, bool complete = false, CancellationToken cancellationToken = default)
			=> WriteAllConcurrentlyAsync(target, maxConcurrency, source.Select(e => new ValueTask<T>(e())), complete, cancellationToken);

		/// <summary>
		/// Asynchronously writes all entries from the source to the channel.
		/// </summary>
		/// <typeparam name="T">The input type of the channel.</typeparam>
		/// <param name="target">The channel to write to.</param>
		/// <param name="maxConcurrency">The maximum number of concurrent operations.</param>
		/// <param name="source">The source data to use.</param>
		/// <param name="complete">If true, will call .Complete() if all the results have successfully been written (or the source is emtpy).</param>
		/// <param name="cancellationToken">An optional cancellation token.</param>
		/// <returns>A task which completes when all the data has been written to the channel writer.</returns>
		public static Task WriteAllConcurrently<T>(this ChannelWriter<T> target,
			int maxConcurrency, IEnumerable<T> source, bool complete = false, CancellationToken cancellationToken = default)
			=> WriteAllConcurrentlyAsync(target, maxConcurrency, source.Select(e => new ValueTask<T>(e)), complete, cancellationToken);

	}
}
