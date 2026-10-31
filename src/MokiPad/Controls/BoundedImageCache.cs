using System.Collections.Concurrent;

namespace MokiPad.Controls;

/// <summary>
/// 一个带容量上限的“异步对象缓存”。
/// 图片解码既耗时又占内存，所以同一张图片的并发请求会共用同一个任务；
/// 解码完成后按字节数计入预算，超预算时按完成顺序淘汰最旧的项。
/// </summary>
/// <typeparam name="T">缓存的对象类型，通常是 WPF 的 <c>ImageSource</c>。</typeparam>
internal sealed class BoundedImageCache<T>(long budget) where T : class
{
    /// <summary>
    /// 一个缓存条目。<see cref="Lazy{T}"/> 保证多个线程同时请求同一张图时，
    /// 真正执行解码的只有一个；<see cref="Admitted"/> 保证权重只统计一次。
    /// </summary>
    private sealed class Entry(Func<Task<T?>> create)
    {
        public readonly Lazy<Task<T?>> Work = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

        public int Admitted;
    }

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, Entry Entry, long Weight)> completed = new();
    private readonly Lock gate = new();
    private long bytes;

    /// <summary>
    /// 读取缓存；没有命中时调用 <paramref name="create"/> 创建。
    /// </summary>
    /// <param name="key">缓存键，通常是图片地址或本地路径。</param>
    /// <param name="create">真正创建对象的异步方法，失败时可以返回 <c>null</c>。</param>
    /// <param name="weight">对象的字节数，用于容量预算。</param>
    public async Task<T?> GetAsync(string key, Func<Task<T?>> create, Func<T, long> weight)
    {
        var entry = entries.GetOrAdd(key, _ => new Entry(create));

        T? value;
        try
        {
            // 所有调用者 await 同一个 Task，不会重复解码。
            value = await entry.Work.Value.ConfigureAwait(false);
        }
        catch
        {
            // 失败的任务不能留在缓存里，否则下次请求会一直拿到同一个异常。
            entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            throw;
        }

        if (value is null)
        {
            entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            return null;
        }

        // 只有第一次成功完成时才计入预算；并发的其他调用直接拿结果。
        if (Interlocked.Exchange(ref entry.Admitted, 1) != 0) return value;

        lock (gate)
        {
            var size = weight(value);
            bytes += size;
            completed.Enqueue((key, entry, size));

            // 超出字节预算，或完成项太多时，从最旧的开始淘汰。
            while (completed.Count > 0 && (bytes > budget || completed.Count > 128))
            {
                var old = completed.Dequeue();
                bytes -= old.Weight;
                entries.TryRemove(new KeyValuePair<string, Entry>(old.Key, old.Entry));
            }
        }

        return value;
    }
}
