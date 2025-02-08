//Copyright 2025 Daniil Glagolev
//Licensed under the Apache License, Version 2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Threads
{
    /// <summary>
    /// Provides parallel execution of a loop with dynamic thread management, with minimal allocations.
    /// </summary>
    public static class LiteParallel
    {
        /// <summary>
        /// Executes a parallel loop from a specified start index to an exclusive end index, 
        /// distributing iterations among available threads for improved performance.
        /// </summary>
        /// <param name="fromInclusive">The start index (inclusive).</param>
        /// <param name="toExclusive">The end index (exclusive).</param>
        /// <param name="body">The action to execute for each iteration.</param>
        /// <param name="alwaysUseMultithreading">If true, forces the use of multiple threads even for small workloads.</param>
        public static void For(int fromInclusive, int toExclusive, Action<int> body, bool alwaysUseMultithreading = false)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            var total = toExclusive - fromInclusive;
            if (total <= 0) return;

            var workersCount = alwaysUseMultithreading
                ? Math.Min(total, Environment.ProcessorCount)
                : Environment.ProcessorCount;

            if (total < workersCount)
            {
                for (var i = fromInclusive; i < toExclusive; i++) body(i);
                return;
            }

            var chunkSize = total / workersCount;
            using var countdown = new CountdownEvent(workersCount);

            for (var i = 0; i < workersCount; i++)
            {
                var start = fromInclusive + i * chunkSize;
                var end = (i == workersCount - 1) ? toExclusive : start + chunkSize;

                ThreadPool.UnsafeQueueUserWorkItem(static state =>
                {
                    var (s, e, b, c) = ((int, int, Action<int>, CountdownEvent))state!;
                    
                    try
                    {
                        for (var j = s; j < e; j++) b(j);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Thread failed: {ex}");
                    }
                    finally
                    {
                        c.Signal();
                    }
                }, (start, end, body, countdown));
            }

            countdown.Wait();
        }

        /// <summary>
        /// Executes a parallel loop over a collection, distributing iterations among available threads 
        /// for improved performance. Supports both indexed and non-indexed collections.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="source">The collection of elements to process.</param>
        /// <param name="body">The action to execute for each element.</param>
        /// <param name="alwaysUseMultithreading">If true, forces the use of multiple threads even for small collections.</param>
        public static void ForEach<T>(IEnumerable<T> source, Action<T> body, bool alwaysUseMultithreading = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (body == null) throw new ArgumentNullException(nameof(body));

            if (source is IList<T> list)
            {
                For(0, list.Count, i => body(list[i]), alwaysUseMultithreading);
                return;
            }
            
            var estimatedCount = source switch
            {
                ICollection<T> collection => collection.Count,
                IReadOnlyCollection<T> readOnlyCollection => readOnlyCollection.Count,
                _ => (int?)null
            };

            if (estimatedCount <= 0) return;

            var workersCount = alwaysUseMultithreading
                ? Environment.ProcessorCount
                : Math.Min(Environment.ProcessorCount, estimatedCount ?? Environment.ProcessorCount);

            if (estimatedCount < workersCount && !alwaysUseMultithreading)
            {
                foreach (var item in source) body(item);
                return;
            }

            using var enumerator = source.GetEnumerator();
            var syncLock = new object();
            var batchSize = 64;

            if (estimatedCount.HasValue) batchSize = Math.Clamp(estimatedCount.Value / (workersCount * 4), 4, 512);

            using var countdown = new CountdownEvent(workersCount);

            // ReSharper disable AccessToDisposedClosure
            for (var i = 0; i < workersCount; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    try
                    {
                        while (true)
                        {
                            T[] batch;
                            int actualCount;

                            lock (syncLock)
                            {
                                if (!enumerator.MoveNext()) break;

                                batch = ArrayPool<T>.Shared.Rent(batchSize);
                                batch[0] = enumerator.Current;
                                actualCount = 1;

                                while (actualCount < batchSize && enumerator.MoveNext())
                                {
                                    batch[actualCount++] = enumerator.Current;
                                }
                            }

                            try
                            {
                                for (var j = 0; j < actualCount; j++)
                                {
                                    body(batch[j]);
                                }
                            }
                            finally
                            {
                                ArrayPool<T>.Shared.Return(batch);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Thread failed: {ex}");
                    }
                    finally
                    {
                        countdown.Signal();
                    }
                }, null);
            }
            // ReSharper restore AccessToDisposedClosure

            countdown.Wait();
        }

        /// <summary>
        /// Executes a parallel loop over a collection, distributing iterations among available threads 
        /// for improved performance. Supports both indexed and non-indexed collections.
        /// Optimization for the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="source">The collection of elements to process.</param>
        /// <param name="body">The action to execute for each element.</param>
        /// <param name="alwaysUseMultithreading">If true, forces the use of multiple threads even for small collections.</param>
        public static void ForEach<T>(List<T> source, Action<T> body, bool alwaysUseMultithreading = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (body == null) throw new ArgumentNullException(nameof(body));
            
            For(0, source.Count, i => body(source[i]), alwaysUseMultithreading);
        }
        
        /// <summary>
        /// Executes a parallel loop over a collection, distributing iterations among available threads 
        /// for improved performance. Supports both indexed and non-indexed collections.
        /// Optimization for the array.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="source">The collection of elements to process.</param>
        /// <param name="body">The action to execute for each element.</param>
        /// <param name="alwaysUseMultithreading">If true, forces the use of multiple threads even for small collections.</param>
        public static void ForEach<T>(T[] source, Action<T> body, bool alwaysUseMultithreading = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (body == null) throw new ArgumentNullException(nameof(body));
            
            For(0, source.Length, i => body(source[i]), alwaysUseMultithreading);
        }
    }
}
