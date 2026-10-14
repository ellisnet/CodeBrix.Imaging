// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Imaging.Formats.Jxl;

internal static class JxlParallel
{
    internal static void For(int count, int maxDegreeOfParallelism, CancellationToken cancellationToken, Action<int> action)
    {
        if (count <= 1 || maxDegreeOfParallelism == 1)
        {
            for (int i = 0; i < count; i++) { cancellationToken.ThrowIfCancellationRequested(); action(i); }
            return;
        }
        try
        { Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken }, action); }
        catch (AggregateException error) { Rethrow(error); }
    }

    // Parallel.For joins every worker before throwing. Expose the same exception
    // contract as scalar decoding, including allocator failures and corrupt input.
    internal static void Rethrow(AggregateException error) => ExceptionDispatchInfo.Capture(error.Flatten().InnerExceptions[0]).Throw();

    // Encoder stages run independent tiles, groups and chunks concurrently. Each
    // work item writes only its own results, so output bytes do not depend on
    // the degree of parallelism. The public entry points set the degree from
    // Configuration.MaxDegreeOfParallelism for the duration of one encode.
    private static readonly AsyncLocal<int> EncoderDegree = new();

    internal static int EncoderParallelism => EncoderDegree.Value is int degree and not 0 ? degree : Environment.ProcessorCount;

    internal static Scope EncoderScope(int maxDegreeOfParallelism)
    {
        int previous = EncoderDegree.Value;
        EncoderDegree.Value = maxDegreeOfParallelism is > 0 or -1 ? maxDegreeOfParallelism : Environment.ProcessorCount;
        return new(previous);
    }

    internal readonly struct Scope(int previous) : IDisposable
    {
        public void Dispose() => EncoderDegree.Value = previous;
    }

    internal static void ForEncoder(int count, CancellationToken cancellationToken, Action<int> action) =>
        For(count, EncoderParallelism, cancellationToken, action);

    // Runs independent output rows in bands of rowsPerBand: action(firstRow, endRow).
    internal static void ForEncoderRows(int rows, int rowsPerBand, CancellationToken cancellationToken, Action<int, int> action)
    {
        int bands = (rows + rowsPerBand - 1) / rowsPerBand;
        For(bands, EncoderParallelism, cancellationToken, band =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            action(band * rowsPerBand, Math.Min(rows, (band + 1) * rowsPerBand));
        });
    }

    // Runs items with one worker state per participating thread, created on
    // first use and disposed after the loop (also after a failure).
    internal static void ForEncoder<TState>(int count, CancellationToken cancellationToken, Func<TState> create,
        Action<int, TState> action) where TState : IDisposable
    {
        int degree = EncoderParallelism;
        if (count <= 1 || degree == 1)
        {
            if (count <= 0) { return; }
            using TState state = create();
            for (int i = 0; i < count; i++) { cancellationToken.ThrowIfCancellationRequested(); action(i, state); }
            return;
        }
        System.Collections.Concurrent.ConcurrentBag<TState> states = [];
        try
        {
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellationToken },
                () => { TState state = create(); states.Add(state); return state; },
                (i, _, state) => { action(i, state); return state; }, _ => { });
        }
        catch (AggregateException error) { Rethrow(error); }
        finally { foreach (TState state in states) { state.Dispose(); } }
    }
}
