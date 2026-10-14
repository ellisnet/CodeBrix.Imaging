// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

// Histogram analysis and virtual ANS costing can walk group storage directly.
// No flattened token array, index table, iterator allocation or per-token box.
internal readonly ref struct JxlTokenSequence
{
    private readonly ReadOnlySpan<JxlToken> single;
    private readonly ReadOnlySpan<JxlToken[]> groups;
    private readonly bool reverse;
    internal long Length { get; }

    internal JxlTokenSequence(ReadOnlySpan<JxlToken> tokens)
    { this.single = tokens; this.Length = tokens.Length; }

    internal JxlTokenSequence(ReadOnlySpan<JxlToken[]> groups)
    {
        this.groups = groups;
        foreach (JxlToken[] group in groups)
        { ArgumentNullException.ThrowIfNull(group); this.Length = checked(this.Length + group.Length); }
    }

    private JxlTokenSequence(JxlTokenSequence source, bool reverse)
    { this.single = source.single; this.groups = source.groups; this.Length = source.Length; this.reverse = reverse; }

    internal JxlTokenSequence Reversed() => new(this, !this.reverse);
    public Enumerator GetEnumerator() => new(this.single, this.groups, this.reverse);

    internal ref struct Enumerator
    {
        private ReadOnlySpan<JxlToken> current;
        private readonly ReadOnlySpan<JxlToken[]> groups;
        private readonly int step;
        private int group, index;

        internal Enumerator(ReadOnlySpan<JxlToken> single, ReadOnlySpan<JxlToken[]> groups, bool reverse)
        {
            this.current = single; this.groups = groups; this.step = reverse ? -1 : 1;
            this.group = reverse ? groups.Length : -1;
            this.index = reverse ? single.Length : -1;
        }

        public readonly JxlToken Current => this.current[this.index];

        public bool MoveNext()
        {
            this.index += this.step;
            while ((uint)this.index >= (uint)this.current.Length)
            {
                if (this.groups.IsEmpty) { return false; }
                this.group += this.step;
                if ((uint)this.group >= (uint)this.groups.Length) { return false; }
                this.current = this.groups[this.group];
                this.index = this.step > 0 ? 0 : this.current.Length - 1;
            }
            return true;
        }
    }
}
