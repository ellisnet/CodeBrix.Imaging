// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_ans.cc, ANSEncodingHistogram normalization,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

internal sealed partial class JxlAnsEncoder
{
    private readonly record struct AllowedCount(int Count, int StepLog, int DeltaLog);
    private readonly record struct Bin(uint Frequency, int Index, int Position);
    private static readonly uint[] Logs = CreateLogs();
    private static readonly (AllowedCount[] Values, int[] Index)[] Allowed = CreateAllowed();

    private static (int[] Counts, int Omitted) Normalize(ReadOnlySpan<uint> histogram, long total, int shift)
    {
        (AllowedCount[] allowed, int[] index) = Allowed[shift];
        int[] counts = new int[histogram.Length];
        List<Bin> bins = [];
        int omitted = 0, rest = 4096;
        long highest = 0;
        double normalization = 4096.0 / total;
        for (int i = 0; i < histogram.Length; i++)
        {
            uint frequency = histogram[i];
            if (frequency > highest) { highest = frequency; omitted = i; }
            double target = frequency * normalization;
            int count = Math.Min(4095, Math.Max((int)Math.Round(target, MidpointRounding.AwayFromZero), frequency == 0 ? 0 : 1));
            int drop = IncrementLog(count, shift);
            count &= ~((1 << drop) - 1); counts[i] = count; rest -= count;
            if (target > 1) { bins.Add(new(frequency, index[count], i)); }
        }
        bins.RemoveAll(b => b.Position == omitted);
        rest += counts[omitted];
        if (bins.Count != 0)
        {
            long[] increasePenalty = new long[11], decreasePenalty = new long[11];
            while (true)
            {
                for (int log = 0; log <= allowed[1].StepLog; log++)
                {
                    int delta = 1 << log;
                    if (rest >= 4096) { increasePenalty[log] = decreasePenalty[log] = 0; }
                    else if (rest > 1)
                    {
                        increasePenalty[log] = rest > delta ? highest * (Logs[rest] - Logs[rest - delta]) : long.MaxValue;
                        decreasePenalty[log] = rest + delta < 4096 ? highest * (Logs[rest + delta] - Logs[rest]) : 0;
                    }
                    else { increasePenalty[log] = decreasePenalty[log] = long.MaxValue; }
                }
                int best = 0; long bestValue = long.MinValue;
                for (int i = 0; i < bins.Count; i++)
                {
                    Bin bin = bins[i]; AllowedCount count = allowed[bin.Index];
                    long score = (((long)bin.Frequency * count.DeltaLog) - increasePenalty[count.StepLog]) >> count.StepLog;
                    if (score > bestValue) { bestValue = score; best = i; }
                }
                Bin choice = bins[best]; AllowedCount selected = allowed[choice.Index];
                long change = ((long)choice.Frequency * selected.DeltaLog) - increasePenalty[selected.StepLog];
                if (change > 0) { rest -= 1 << selected.StepLog; bins[best] = choice with { Index = choice.Index - 1 }; continue; }
                bestValue = long.MaxValue; best = -1;
                for (int i = 0; i < bins.Count; i++)
                {
                    Bin bin = bins[i];
                    // Rounding many equally frequent bins upward can leave a
                    // nonpositive balancing count. Its sentinel penalty then
                    // favors shrinking a small bin repeatedly. A used symbol
                    // must keep at least one state even while repairing that
                    // balance; the sentinel alone does not enforce this.
                    if (allowed[bin.Index].Count <= 1) { continue; }
                    AllowedCount count = allowed[bin.Index + 1];
                    long score = (((long)bin.Frequency * count.DeltaLog) - decreasePenalty[count.StepLog]) >> count.StepLog;
                    if (score < bestValue) { bestValue = score; best = i; }
                }
                if (best < 0) { break; }
                choice = bins[best]; selected = allowed[choice.Index + 1];
                change = ((long)choice.Frequency * selected.DeltaLog) - decreasePenalty[selected.StepLog];
                if (change >= 0) { break; }
                rest += 1 << selected.StepLog; bins[best] = choice with { Index = choice.Index + 1 };
            }
            foreach (Bin bin in bins) { counts[bin.Position] = allowed[bin.Index].Count; }
            for (int i = 0; i < omitted; i++)
            {
                if (counts[i] >= 2048) { counts[omitted] = counts[i]; omitted = i; break; }
            }
        }
        counts[omitted] = rest;
        if (rest <= 0) { throw new InvalidOperationException("JPEG XL ANS normalization failed."); }
        return (counts, omitted);
    }

    private static int IncrementLog(int count, int shift)
    { int bits = BitOperations.Log2((uint)count); return count == 0 ? 0 : bits - Precision(bits, shift); }

    private static uint[] CreateLogs()
    {
        uint[] logs = new uint[4097];
        for (int i = 1; i < logs.Length; i++) { logs[i] = (uint)Math.Round(Math.Log2(i) / 12 * 2147483648.0, MidpointRounding.AwayFromZero); }
        return logs;
    }

    private static (AllowedCount[] Values, int[] Index)[] CreateAllowed()
    {
        (AllowedCount[], int[])[] result = new (AllowedCount[], int[])[12];
        for (int shift = 0; shift < result.Length; shift++)
        {
            List<AllowedCount> values = []; int[] index = new int[4096];
            int last = -1;
            for (int i = 4095; i >= 0; i--)
            {
                int current = i & ~((1 << IncrementLog(i, shift)) - 1);
                if (current == last) { continue; }
                last = current; index[current] = values.Count;
                int delta = 0, step = 0;
                if (current == 0) { delta = int.MaxValue; }
                else if (values.Count != 0)
                {
                    int previous = values[^1].Count;
                    delta = (int)Math.Round(Math.Log2((double)previous / current) / 12 * 2147483648.0, MidpointRounding.AwayFromZero);
                    step = BitOperations.Log2((uint)(previous - current));
                }
                values.Add(new(current, step, delta));
            }
            result[shift] = (values.ToArray(), index);
        }
        return result;
    }
}
