// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Fonts.Tables;

//was previously: namespace SixLabors.Fonts.Tables;

internal sealed class UnknownTable : Table
{
    internal UnknownTable(string name)
    {
        Name = name;
    }

    public string Name { get; }
}