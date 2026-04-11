// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.
// COLRv1 support added by CodeBrix.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

#pragma warning disable IDE0059
namespace CodeBrix.Imaging.Fonts.Tables.General.Colr;

//was previously: namespace SixLabors.Fonts.Tables.General.Colr;

internal class ColrTable : Table
{
    internal const string TableName = "COLR";
    
    // COLRv0 data
    private readonly BaseGlyphRecord[] glyphRecords;
    private readonly LayerRecord[] layers;
    
    // COLRv1 data
    private readonly ushort version;
    private readonly Dictionary<ushort, Paint> baseGlyphPaints;
    private readonly Paint[] layerList;

    public ColrTable(BaseGlyphRecord[] glyphRecords, LayerRecord[] layers)
    {
        this.glyphRecords = glyphRecords;
        this.layers = layers;
        this.version = 0;
        this.baseGlyphPaints = new Dictionary<ushort, Paint>();
        this.layerList = Array.Empty<Paint>();
    }

    public ColrTable(
        ushort version,
        BaseGlyphRecord[] glyphRecords,
        LayerRecord[] layers,
        Dictionary<ushort, Paint> baseGlyphPaints,
        Paint[] layerList)
    {
        this.version = version;
        this.glyphRecords = glyphRecords;
        this.layers = layers;
        this.baseGlyphPaints = baseGlyphPaints ?? new Dictionary<ushort, Paint>();
        this.layerList = layerList ?? Array.Empty<Paint>();
    }

    /// <summary>
    /// Gets the COLR table version (0 or 1).
    /// </summary>
    public ushort Version => version;

    /// <summary>
    /// Gets a value indicating whether this table has COLRv1 paint data.
    /// </summary>
    public bool HasV1Data => baseGlyphPaints.Count > 0 || layerList.Length > 0;

    public static ColrTable Load(FontReader fontReader)
    {
        if (!fontReader.TryGetReaderAtTablePosition(TableName, out var binaryReader)) return null;

        using (binaryReader)
        {
            return Load(binaryReader);
        }
    }

    /// <summary>
    /// Gets the COLRv0 layers for a glyph.
    /// </summary>
    internal Span<LayerRecord> GetLayers(ushort glyph)
    {
        foreach (var g in glyphRecords)
            if (g.GlyphId == glyph)
                return layers.AsSpan().Slice(g.FirstLayerIndex, g.LayerCount);

        return Span<LayerRecord>.Empty;
    }

    /// <summary>
    /// Gets the COLRv1 paint for a base glyph.
    /// </summary>
    internal Paint GetBaseGlyphPaint(ushort glyphId)
    {
        baseGlyphPaints.TryGetValue(glyphId, out var paint);
        return paint;
    }

    /// <summary>
    /// Gets a paint from the layer list.
    /// </summary>
    internal Paint GetLayerPaint(uint index)
    {
        if (index < layerList.Length)
        {
            return layerList[index];
        }
        return null;
    }

    /// <summary>
    /// Tries to get COLRv1 layers for a glyph.
    /// </summary>
    internal bool TryGetV1Layers(ushort glyphId, CpalTable cpal, GlyphColor foregroundColor, out IReadOnlyList<ResolvedColorLayer> resolvedLayers)
    {
        resolvedLayers = null;
        
        if (!baseGlyphPaints.TryGetValue(glyphId, out var paint))
        {
            return false;
        }

        var context = new PaintResolutionContext(cpal, this, 0, foregroundColor);
        resolvedLayers = paint.Resolve(context);
        return resolvedLayers.Count > 0;
    }

    public static ColrTable Load(BigEndianBinaryReader reader)
    {
        var tableStart = reader.BaseStream.Position;
        
        // HEADER (common to v0 and v1)
        // Type      | Name                   | Description
        // ----------|------------------------|----------------------------------------------------------------------------------------------------
        // uint16    | version                | Table version number (0 or 1).
        // uint16    | numBaseGlyphRecords    | Number of Base Glyph Records (may be 0 in v1).
        // Offset32  | baseGlyphRecordsOffset | Offset to Base Glyph records.
        // Offset32  | layerRecordsOffset     | Offset to Layer Records.
        // uint16    | numLayerRecords        | Number of Layer Records.

        var version = reader.ReadUInt16();
        var numBaseGlyphRecords = reader.ReadUInt16();
        var baseGlyphRecordsOffset = reader.ReadOffset32();
        var layerRecordsOffset = reader.ReadOffset32();
        var numLayerRecords = reader.ReadUInt16();

        // Read v0 data
        var glyphs = new BaseGlyphRecord[numBaseGlyphRecords];
        var layerRecords = new LayerRecord[numLayerRecords];

        if (numBaseGlyphRecords > 0 && baseGlyphRecordsOffset > 0)
        {
            reader.BaseStream.Position = tableStart + baseGlyphRecordsOffset;
            for (var i = 0; i < numBaseGlyphRecords; i++)
            {
                var gi = reader.ReadUInt16();
                var idx = reader.ReadUInt16();
                var num = reader.ReadUInt16();
                glyphs[i] = new BaseGlyphRecord(gi, idx, num);
            }
        }

        if (numLayerRecords > 0 && layerRecordsOffset > 0)
        {
            reader.BaseStream.Position = tableStart + layerRecordsOffset;
            for (var i = 0; i < numLayerRecords; i++)
            {
                var gi = reader.ReadUInt16();
                var pi = reader.ReadUInt16();
                layerRecords[i] = new LayerRecord(gi, pi);
            }
        }

        if (version == 0)
        {
            return new ColrTable(glyphs, layerRecords);
        }

        // COLRv1 additional header fields
        // Offset32  | baseGlyphListOffset    | Offset to BaseGlyphList table.
        // Offset32  | layerListOffset        | Offset to LayerList table.
        // Offset32  | clipListOffset         | Offset to ClipList table.
        // Offset32  | varIndexMapOffset      | Offset to DeltaSetIndexMap table.
        // Offset32  | itemVariationStoreOffset | Offset to ItemVariationStore.

        reader.BaseStream.Position = tableStart + 14; // After v0 header
        
        var baseGlyphListOffset = reader.ReadOffset32();
        var layerListOffset = reader.ReadOffset32();
        var clipListOffset = reader.ReadOffset32();
        var varIndexMapOffset = reader.ReadOffset32();
        var itemVariationStoreOffset = reader.ReadOffset32();

        var baseGlyphPaints = new Dictionary<ushort, Paint>();
        var layerList = Array.Empty<Paint>();

        // Read BaseGlyphList
        if (baseGlyphListOffset > 0)
        {
            reader.BaseStream.Position = tableStart + baseGlyphListOffset;
            baseGlyphPaints = ReadBaseGlyphList(reader, tableStart);
        }

        // Read LayerList
        if (layerListOffset > 0)
        {
            reader.BaseStream.Position = tableStart + layerListOffset;
            layerList = ReadLayerList(reader, tableStart);
        }

        return new ColrTable(version, glyphs, layerRecords, baseGlyphPaints, layerList);
    }

    private static Dictionary<ushort, Paint> ReadBaseGlyphList(BigEndianBinaryReader reader, long tableStart)
    {
        var result = new Dictionary<ushort, Paint>();
        var listStart = reader.BaseStream.Position;
        
        var numBaseGlyphPaintRecords = reader.ReadUInt32();
        
        for (uint i = 0; i < numBaseGlyphPaintRecords; i++)
        {
            var glyphId = reader.ReadUInt16();
            var paintOffset = reader.ReadOffset32();
            
            if (paintOffset > 0)
            {
                var currentPos = reader.BaseStream.Position;
                reader.BaseStream.Position = listStart + paintOffset;
                var paint = ReadPaint(reader, tableStart);
                if (paint != null)
                {
                    result[glyphId] = paint;
                }
                reader.BaseStream.Position = currentPos;
            }
        }

        return result;
    }

    private static Paint[] ReadLayerList(BigEndianBinaryReader reader, long tableStart)
    {
        var listStart = reader.BaseStream.Position;
        var numLayers = reader.ReadUInt32();
        
        var offsets = new uint[numLayers];
        for (uint i = 0; i < numLayers; i++)
        {
            offsets[i] = reader.ReadOffset32();
        }

        var paints = new Paint[numLayers];
        for (uint i = 0; i < numLayers; i++)
        {
            if (offsets[i] > 0)
            {
                reader.BaseStream.Position = listStart + offsets[i];
                paints[i] = ReadPaint(reader, tableStart);
            }
        }

        return paints;
    }

    private static Paint ReadPaint(BigEndianBinaryReader reader, long tableStart)
    {
        var paintStart = reader.BaseStream.Position;
        var format = (PaintFormat)reader.ReadByte();

        switch (format)
        {
            case PaintFormat.PaintColrLayers:
                return ReadPaintColrLayers(reader);

            case PaintFormat.PaintSolid:
                return ReadPaintSolid(reader);

            case PaintFormat.PaintVarSolid:
                return ReadPaintVarSolid(reader);

            case PaintFormat.PaintLinearGradient:
            case PaintFormat.PaintVarLinearGradient:
                return ReadPaintLinearGradient(reader, paintStart, format == PaintFormat.PaintVarLinearGradient);

            case PaintFormat.PaintRadialGradient:
            case PaintFormat.PaintVarRadialGradient:
                return ReadPaintRadialGradient(reader, paintStart, format == PaintFormat.PaintVarRadialGradient);

            case PaintFormat.PaintGlyph:
                return ReadPaintGlyph(reader, paintStart, tableStart);

            case PaintFormat.PaintColrGlyph:
                return ReadPaintColrGlyph(reader);

            case PaintFormat.PaintTransform:
            case PaintFormat.PaintVarTransform:
                return ReadPaintTransform(reader, paintStart, tableStart);

            case PaintFormat.PaintTranslate:
            case PaintFormat.PaintVarTranslate:
                return ReadPaintTranslate(reader, paintStart, tableStart);

            case PaintFormat.PaintScale:
            case PaintFormat.PaintVarScale:
            case PaintFormat.PaintScaleAroundCenter:
            case PaintFormat.PaintVarScaleAroundCenter:
            case PaintFormat.PaintScaleUniform:
            case PaintFormat.PaintVarScaleUniform:
            case PaintFormat.PaintScaleUniformAroundCenter:
            case PaintFormat.PaintVarScaleUniformAroundCenter:
                return ReadPaintScale(reader, paintStart, tableStart, format);

            case PaintFormat.PaintRotate:
            case PaintFormat.PaintVarRotate:
            case PaintFormat.PaintRotateAroundCenter:
            case PaintFormat.PaintVarRotateAroundCenter:
                return ReadPaintRotate(reader, paintStart, tableStart, format);

            case PaintFormat.PaintSkew:
            case PaintFormat.PaintVarSkew:
            case PaintFormat.PaintSkewAroundCenter:
            case PaintFormat.PaintVarSkewAroundCenter:
                return ReadPaintSkew(reader, paintStart, tableStart, format);

            case PaintFormat.PaintComposite:
                return ReadPaintComposite(reader, paintStart, tableStart);

            default:
                // Unknown paint format - skip
                return null;
        }
    }

    private static PaintColrLayers ReadPaintColrLayers(BigEndianBinaryReader reader)
    {
        var numLayers = reader.ReadByte();
        var firstLayerIndex = reader.ReadUInt32();
        return new PaintColrLayers(numLayers, firstLayerIndex);
    }

    private static PaintSolid ReadPaintSolid(BigEndianBinaryReader reader)
    {
        var paletteIndex = reader.ReadUInt16();
        var alpha = reader.ReadF2Dot14();
        return new PaintSolid(paletteIndex, alpha);
    }

    private static PaintSolid ReadPaintVarSolid(BigEndianBinaryReader reader)
    {
        var paletteIndex = reader.ReadUInt16();
        var alpha = reader.ReadF2Dot14();
        var varIndexBase = reader.ReadUInt32(); // Ignored for now
        return new PaintSolid(paletteIndex, alpha);
    }

    private static PaintLinearGradient ReadPaintLinearGradient(BigEndianBinaryReader reader, long paintStart, bool hasVariation)
    {
        var colorLineOffset = reader.ReadOffset24();
        var x0 = reader.ReadFWord();
        var y0 = reader.ReadFWord();
        var x1 = reader.ReadFWord();
        var y1 = reader.ReadFWord();
        var x2 = reader.ReadFWord();
        var y2 = reader.ReadFWord();

        // Read color line
        reader.BaseStream.Position = paintStart + colorLineOffset;
        var (extend, colorStops) = ReadColorLine(reader, hasVariation);

        return new PaintLinearGradient(extend, colorStops, x0, y0, x1, y1, x2, y2);
    }

    private static PaintRadialGradient ReadPaintRadialGradient(BigEndianBinaryReader reader, long paintStart, bool hasVariation)
    {
        var colorLineOffset = reader.ReadOffset24();
        var x0 = reader.ReadFWord();
        var y0 = reader.ReadFWord();
        var radius0 = reader.ReadUFWord();
        var x1 = reader.ReadFWord();
        var y1 = reader.ReadFWord();
        var radius1 = reader.ReadUFWord();

        // Read color line
        reader.BaseStream.Position = paintStart + colorLineOffset;
        var (extend, colorStops) = ReadColorLine(reader, hasVariation);

        return new PaintRadialGradient(extend, colorStops, x0, y0, radius0, x1, y1, radius1);
    }

    private static (ExtendMode extend, ColorStop[] stops) ReadColorLine(BigEndianBinaryReader reader, bool hasVariation)
    {
        var extend = (ExtendMode)reader.ReadByte();
        var numStops = reader.ReadUInt16();
        
        var stops = new ColorStop[numStops];
        for (var i = 0; i < numStops; i++)
        {
            var stopOffset = reader.ReadF2Dot14();
            var paletteIndex = reader.ReadUInt16();
            var alpha = reader.ReadF2Dot14();
            
            if (hasVariation)
            {
                reader.ReadUInt32(); // varIndexBase - ignored for now
            }
            
            stops[i] = new ColorStop(stopOffset, paletteIndex, alpha);
        }

        return (extend, stops);
    }

    private static PaintGlyph ReadPaintGlyph(BigEndianBinaryReader reader, long paintStart, long tableStart)
    {
        var paintOffset = reader.ReadOffset24();
        var glyphId = reader.ReadUInt16();

        Paint childPaint = null;
        if (paintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + paintOffset;
            childPaint = ReadPaint(reader, tableStart);
        }

        return new PaintGlyph(childPaint, glyphId);
    }

    private static PaintColrGlyph ReadPaintColrGlyph(BigEndianBinaryReader reader)
    {
        var glyphId = reader.ReadUInt16();
        return new PaintColrGlyph(glyphId);
    }

    private static PaintTransform ReadPaintTransform(BigEndianBinaryReader reader, long paintStart, long tableStart)
    {
        var paintOffset = reader.ReadOffset24();
        var transformOffset = reader.ReadOffset24();

        Paint childPaint = null;
        if (paintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + paintOffset;
            childPaint = ReadPaint(reader, tableStart);
        }

        // Read Affine2x3 transform
        reader.BaseStream.Position = paintStart + transformOffset;
        var xx = reader.ReadFixed();
        var yx = reader.ReadFixed();
        var xy = reader.ReadFixed();
        var yy = reader.ReadFixed();
        var dx = reader.ReadFixed();
        var dy = reader.ReadFixed();

        var transform = new Matrix3x2(xx, yx, xy, yy, dx, dy);
        return new PaintTransform(childPaint, transform);
    }

    private static PaintTranslate ReadPaintTranslate(BigEndianBinaryReader reader, long paintStart, long tableStart)
    {
        var paintOffset = reader.ReadOffset24();
        var dx = reader.ReadFWord();
        var dy = reader.ReadFWord();

        Paint childPaint = null;
        if (paintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + paintOffset;
            childPaint = ReadPaint(reader, tableStart);
        }

        return new PaintTranslate(childPaint, dx, dy);
    }

    private static PaintScale ReadPaintScale(BigEndianBinaryReader reader, long paintStart, long tableStart, PaintFormat format)
    {
        var paintOffset = reader.ReadOffset24();
        
        float scaleX, scaleY, centerX = 0, centerY = 0;

        var isUniform = format == PaintFormat.PaintScaleUniform || 
                        format == PaintFormat.PaintVarScaleUniform ||
                        format == PaintFormat.PaintScaleUniformAroundCenter ||
                        format == PaintFormat.PaintVarScaleUniformAroundCenter;

        var hasCenter = format == PaintFormat.PaintScaleAroundCenter ||
                        format == PaintFormat.PaintVarScaleAroundCenter ||
                        format == PaintFormat.PaintScaleUniformAroundCenter ||
                        format == PaintFormat.PaintVarScaleUniformAroundCenter;

        if (isUniform)
        {
            scaleX = scaleY = reader.ReadF2Dot14();
        }
        else
        {
            scaleX = reader.ReadF2Dot14();
            scaleY = reader.ReadF2Dot14();
        }

        if (hasCenter)
        {
            centerX = reader.ReadFWord();
            centerY = reader.ReadFWord();
        }

        Paint childPaint = null;
        if (paintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + paintOffset;
            childPaint = ReadPaint(reader, tableStart);
        }

        return new PaintScale(childPaint, scaleX, scaleY, centerX, centerY);
    }

    private static PaintRotate ReadPaintRotate(BigEndianBinaryReader reader, long paintStart, long tableStart, PaintFormat format)
    {
        var paintOffset = reader.ReadOffset24();
        var angle = reader.ReadF2Dot14() * 180f; // Convert from turns to degrees

        var hasCenter = format == PaintFormat.PaintRotateAroundCenter ||
                        format == PaintFormat.PaintVarRotateAroundCenter;

        float centerX = 0, centerY = 0;
        if (hasCenter)
        {
            centerX = reader.ReadFWord();
            centerY = reader.ReadFWord();
        }

        Paint childPaint = null;
        if (paintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + paintOffset;
            childPaint = ReadPaint(reader, tableStart);
        }

        return new PaintRotate(childPaint, angle, centerX, centerY);
    }

    private static PaintSkew ReadPaintSkew(BigEndianBinaryReader reader, long paintStart, long tableStart, PaintFormat format)
    {
        var paintOffset = reader.ReadOffset24();
        var xSkewAngle = reader.ReadF2Dot14() * 180f;
        var ySkewAngle = reader.ReadF2Dot14() * 180f;

        var hasCenter = format == PaintFormat.PaintSkewAroundCenter ||
                        format == PaintFormat.PaintVarSkewAroundCenter;

        float centerX = 0, centerY = 0;
        if (hasCenter)
        {
            centerX = reader.ReadFWord();
            centerY = reader.ReadFWord();
        }

        Paint childPaint = null;
        if (paintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + paintOffset;
            childPaint = ReadPaint(reader, tableStart);
        }

        return new PaintSkew(childPaint, xSkewAngle, ySkewAngle, centerX, centerY);
    }

    private static PaintComposite ReadPaintComposite(BigEndianBinaryReader reader, long paintStart, long tableStart)
    {
        var sourcePaintOffset = reader.ReadOffset24();
        var compositeMode = (CompositeMode)reader.ReadByte();
        var backdropPaintOffset = reader.ReadOffset24();

        Paint sourcePaint = null;
        Paint backdropPaint = null;

        if (sourcePaintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + sourcePaintOffset;
            sourcePaint = ReadPaint(reader, tableStart);
        }

        if (backdropPaintOffset > 0)
        {
            reader.BaseStream.Position = paintStart + backdropPaintOffset;
            backdropPaint = ReadPaint(reader, tableStart);
        }

        return new PaintComposite(sourcePaint, compositeMode, backdropPaint);
    }
}