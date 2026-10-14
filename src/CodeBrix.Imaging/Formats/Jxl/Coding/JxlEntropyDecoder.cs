// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-coding/src/lib.rs and permutation.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Buffers;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Coding;

internal sealed partial class JxlEntropyDecoder : IDisposable
{
    private readonly MemoryAllocator allocator;
    private JxlIntegerConfig[] configs;
    private JxlPrefixHistogram[] prefix;
    private JxlAnsHistogram[] ans;
    private uint state;
    private bool started;
    private bool lzEnabled;
    private uint minSymbol, minLength;
    private JxlIntegerConfig lengthConfig;
    private IMemoryOwner<uint> window;
    private ulong decoded, copyPosition;
    private uint remaining;

    private JxlEntropyDecoder(MemoryAllocator allocator) => this.allocator = allocator;
    internal byte[] Clusters { get; private set; }

    internal static JxlEntropyDecoder Read(JxlBitReader reader, int contexts, MemoryAllocator allocator, bool allowLz = true)
    {
        if (contexts < 1 || contexts > (1 << 20)) { JxlThrowHelper.Invalid("invalid entropy context count."); }
        JxlEntropyDecoder decoder = new(allocator) { lzEnabled = reader.ReadBool() };
        if (decoder.lzEnabled)
        {
            if (!allowLz) { JxlThrowHelper.Invalid("LZ77 is not permitted in this context map."); }
            decoder.minSymbol = reader.ReadU32(224, 512, 4096, JxlU32.U(15, 8));
            decoder.minLength = reader.ReadU32(3, 4, JxlU32.U(2, 5), JxlU32.U(8, 9));
            decoder.lengthConfig = JxlIntegerConfig.Read(reader, 8);
            contexts++;
        }

        decoder.Clusters = ReadClusters(reader, contexts, allocator, out int clusterCount);
        bool usePrefix = reader.ReadBool();
        int alphabetBits = usePrefix ? 15 : (int)reader.ReadBits(2) + 5;
        decoder.configs = new JxlIntegerConfig[clusterCount];
        for (int i = 0; i < clusterCount; i++) { decoder.configs[i] = JxlIntegerConfig.Read(reader, alphabetBits); }
        if (usePrefix)
        {
            int[] counts = new int[clusterCount];
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = 1;
                if (!reader.ReadBool()) { continue; }
                int bits = (int)reader.ReadBits(4);
                counts[i] += (1 << bits) + (int)reader.ReadBits(bits);
                if (counts[i] > 32768) { JxlThrowHelper.Invalid("invalid prefix alphabet size."); }
            }
            decoder.prefix = new JxlPrefixHistogram[clusterCount];
            for (int i = 0; i < clusterCount; i++) { decoder.prefix[i] = JxlPrefixHistogram.Read(reader, counts[i]); }
        }
        else
        {
            decoder.ans = new JxlAnsHistogram[clusterCount];
            for (int i = 0; i < clusterCount; i++) { decoder.ans[i] = JxlAnsHistogram.Read(reader, alphabetBits); }
        }
        return decoder;
    }

    internal JxlEntropyDecoder CreateStream() => new(this.allocator)
    {
        configs = this.configs, prefix = this.prefix, ans = this.ans, Clusters = this.Clusters,
        lzEnabled = this.lzEnabled, minSymbol = this.minSymbol, minLength = this.minLength, lengthConfig = this.lengthConfig,
    };

    internal void Begin(JxlBitReader reader)
    {
        if (this.started) { return; }
        if (this.ans != null) { this.state = reader.ReadBits(32); }
        this.started = true;
    }

    internal void Finish()
    {
        if (this.ans != null && (!this.started || this.state != 0x130000)) { JxlThrowHelper.Invalid("invalid final ANS state."); }
    }

    internal uint? SingleToken(int cluster)
    {
        if (this.lzEnabled) { return null; }
        uint? symbol = this.prefix != null ? this.prefix[cluster].SingleSymbol : this.ans[cluster].SingleSymbol;
        return symbol < this.configs[cluster].Split ? symbol : null;
    }

    private uint ReadSymbol(JxlBitReader reader, int cluster)
    {
        this.Begin(reader);
        return this.prefix != null ? this.prefix[cluster].ReadSymbol(reader) : this.ans[cluster].ReadSymbol(reader, ref this.state);
    }

    internal uint Read(JxlBitReader reader, int context, uint distanceMultiplier = 0)
    {
        if ((uint)context >= this.Clusters.Length) { JxlThrowHelper.Invalid("invalid entropy context."); }
        return this.ReadClustered(reader, this.Clusters[context], distanceMultiplier);
    }

    internal uint ReadClustered(JxlBitReader reader, int cluster, uint distanceMultiplier = 0)
    {
        if ((uint)cluster >= this.configs.Length) { JxlThrowHelper.Invalid("invalid entropy cluster."); }
        uint value;
        if (this.remaining == 0)
        {
            uint token = this.ReadSymbol(reader, cluster);
            if (this.lzEnabled && token >= this.minSymbol)
            {
                if (this.decoded == 0) { JxlThrowHelper.Invalid("LZ77 repeat before the first value."); }
                uint length = this.lengthConfig.Decode(reader, token - this.minSymbol);
                if (length > uint.MaxValue - this.minLength) { JxlThrowHelper.Invalid("LZ77 repeat length overflow."); }
                this.remaining = length + this.minLength;
                int distanceCluster = this.Clusters[^1];
                uint distance = this.configs[distanceCluster].Decode(reader, this.ReadSymbol(reader, distanceCluster));
                if (distanceMultiplier != 0)
                {
                    if (distance < 120)
                    {
                        int offset = SpecialDistances[(int)distance * 2];
                        int rows = SpecialDistances[((int)distance * 2) + 1];
                        distance = (uint)Math.Max(0, offset + ((long)distanceMultiplier * rows) - 1);
                    }
                    else { distance -= 120; }
                }
                ulong actualDistance = Math.Min(this.decoded, Math.Min(distance, 0xFFFFFU) + 1UL);
                this.copyPosition = this.decoded - actualDistance;
                value = this.window.Memory.Span[(int)(this.copyPosition++ & 0xFFFFF)];
                this.remaining--;
            }
            else { value = this.configs[cluster].Decode(reader, token); }
        }
        else
        {
            value = this.window.Memory.Span[(int)(this.copyPosition++ & 0xFFFFF)];
            this.remaining--;
        }

        if (this.lzEnabled)
        {
            this.window ??= this.allocator.Allocate<uint>(1 << 20);
            this.window.Memory.Span[(int)(this.decoded++ & 0xFFFFF)] = value;
        }
        return value;
    }

    internal static byte[] ReadClusters(JxlBitReader reader, int contexts, MemoryAllocator allocator, out int clusterCount)
    {
        byte[] clusters = new byte[contexts];
        if (contexts == 1) { clusterCount = 1; return clusters; }
        if (reader.ReadBool())
        {
            int bits = (int)reader.ReadBits(2);
            for (int i = 0; i < contexts; i++) { clusters[i] = (byte)reader.ReadBits(bits); }
        }
        else
        {
            bool useMtf = reader.ReadBool();
            using JxlEntropyDecoder decoder = Read(reader, 1, allocator, contexts > 2);
            decoder.Begin(reader);
            for (int i = 0; i < contexts; i++)
            {
                uint value = decoder.Read(reader, 0);
                if (value > 255) { JxlThrowHelper.Invalid("entropy cluster exceeds 255."); }
                clusters[i] = (byte)value;
            }
            decoder.Finish();
            if (useMtf)
            {
                Span<byte> map = stackalloc byte[256];
                for (int i = 0; i < map.Length; i++) { map[i] = (byte)i; }
                for (int i = 0; i < clusters.Length; i++)
                {
                    int index = clusters[i];
                    clusters[i] = map[index];
                    map[..index].CopyTo(map[1..]);
                    map[0] = clusters[i];
                }
            }
        }

        Span<bool> used = stackalloc bool[256];
        int maximum = 0;
        foreach (byte cluster in clusters) { maximum = Math.Max(maximum, cluster); used[cluster] = true; }
        for (int i = 0; i <= maximum; i++) { if (!used[i]) { JxlThrowHelper.Invalid("entropy cluster map contains a hole."); } }
        clusterCount = maximum + 1;
        return clusters;
    }

    internal int[] ReadPermutation(JxlBitReader reader, int size, int skip = 0)
    {
        uint end = this.Read(reader, Math.Min(JxlIntegerConfig.ContextBits((uint)size), 7));
        if (end > size - skip) { JxlThrowHelper.Invalid("invalid permutation length."); }
        // A Fenwick tree selects/removes the Lehmer-ranked item in O(log size).
        // This avoids quadratic work on the largest valid (65536-entry) TOCs.
        int[] tree = new int[size - skip + 1];
        for (int i = 1; i < tree.Length; i++) { tree[i] = i & -i; }
        int[] output = new int[size];
        for (int i = 0; i < skip; i++) { output[i] = i; }
        uint previous = 0;
        for (int i = skip; i < size; i++)
        {
            uint rank = i - skip < end ? this.Read(reader, Math.Min(JxlIntegerConfig.ContextBits(previous), 7)) : 0;
            if (rank >= size - i) { JxlThrowHelper.Invalid("invalid permutation rank."); }
            previous = rank;
            int index = 0;
            for (int step = 1 << System.Numerics.BitOperations.Log2((uint)(size - skip)); step != 0; step >>= 1)
            {
                int candidate = index + step;
                if (candidate < tree.Length && tree[candidate] <= rank) { rank -= (uint)tree[candidate]; index = candidate; }
            }
            output[i] = index + skip;
            for (int j = index + 1; j < tree.Length; j += j & -j) { tree[j]--; }
        }
        return output;
    }

    public void Dispose()
    {
        this.window?.Dispose();
        this.window = null;
    }
}
