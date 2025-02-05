# LiteParallel

The `LiteParallel` class provides a high-performance, allocation-efficient solution for parallel loop execution in C#. It dynamically manages threads and minimizes garbage collection (GC) pressure by leveraging batching and array pooling, reducing allocations by **6.8x** compared to native implementations. Ideal for CPU-bound workloads where low memory overhead is critical.

## Features

- **Minimal Allocations:** GC pressure reduction of **6.8x** compared to native Parallel.For and Parallel.ForEach.
- **Dynamic Thread Management:** Automatically adjusts worker threads based on workload size and CPU cores.
- **Efficient Batching:** Processes elements in configurable batches (default: 64 items) to balance overhead and parallelism.
- **Dual Loop Support:** 
  - `For(int from, int to, Action<int>)` for index-based parallelism.
  - `ForEach<T>(IEnumerable<T>, Action<T>)` for collection iteration with automatic batching.
- **Thread-Safe Enumeration:** Safely iterates over `IEnumerable<T>` with lock-free batch extraction under a sync lock.

## Performance Highlights

- **For Loop:**
  - Splits ranges into chunks for each thread (no allocations).
  - Uses `ThreadPool.UnsafeQueueUserWorkItem` to avoid ExecutionContext capture.
- **ForEach Loop:**
  - Batches elements into pooled arrays to reduce per-item overhead.
  - Optimized for `IList<T>` with direct index access.
  - Falls back to lock-protected enumeration for arbitrary `IEnumerable<T>`.

## Usage Example

```csharp
using Threads;

// Parallel For loop
LiteParallel.For(0, 1000, i => 
{
    Console.WriteLine($"Processing index {i}");
});

// Parallel ForEach loop
var data = Enumerable.Range(1, 5000).ToList();
LiteParallel.ForEach(data, item => 
{
    Console.WriteLine($"Processing item {item}");
});

// Force multithreading for small workloads
LiteParallel.For(0, 5, i => HeavyOperation(), alwaysUseMultithreading: true);
```

## Why Use LiteParallel?

- **6.8x Fewer Allocations:** Multiple optimizations significantly shorten GC cycles.
- **CPU-Bound Optimization:** Dynamically scales threads based on workload and CPU cores.
- **Low Overhead:** Avoids `async/await` and `ExecutionContext` overhead with `UnsafeQueueUserWorkItem`.
- **Simple API:** Matches familiar `Parallel.For/ForEach` patterns with zero learning curve.

## Conclusion

`LiteParallel` is ideal for performance-sensitive applications requiring parallel processing with minimal memory overhead. Its batched approach and pool-backed arrays make it particularly suitable for games (Unity), real-time systems, and high-throughput data processing where GC pauses must be avoided.