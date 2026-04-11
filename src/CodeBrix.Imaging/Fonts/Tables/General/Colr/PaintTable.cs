// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.
// COLRv1 support added by CodeBrix.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace CodeBrix.Imaging.Fonts.Tables.General.Colr;

/// <summary>
/// Represents the type of paint operation in COLRv1.
/// </summary>
internal enum PaintFormat : byte
{
    PaintColrLayers = 1,
    PaintSolid = 2,
    PaintVarSolid = 3,
    PaintLinearGradient = 4,
    PaintVarLinearGradient = 5,
    PaintRadialGradient = 6,
    PaintVarRadialGradient = 7,
    PaintSweepGradient = 8,
    PaintVarSweepGradient = 9,
    PaintGlyph = 10,
    PaintColrGlyph = 11,
    PaintTransform = 12,
    PaintVarTransform = 13,
    PaintTranslate = 14,
    PaintVarTranslate = 15,
    PaintScale = 16,
    PaintVarScale = 17,
    PaintScaleAroundCenter = 18,
    PaintVarScaleAroundCenter = 19,
    PaintScaleUniform = 20,
    PaintVarScaleUniform = 21,
    PaintScaleUniformAroundCenter = 22,
    PaintVarScaleUniformAroundCenter = 23,
    PaintRotate = 24,
    PaintVarRotate = 25,
    PaintRotateAroundCenter = 26,
    PaintVarRotateAroundCenter = 27,
    PaintSkew = 28,
    PaintVarSkew = 29,
    PaintSkewAroundCenter = 30,
    PaintVarSkewAroundCenter = 31,
    PaintComposite = 32
}

/// <summary>
/// Composite mode for PaintComposite operations.
/// </summary>
internal enum CompositeMode : byte
{
    Clear = 0,
    Src = 1,
    Dest = 2,
    SrcOver = 3,
    DestOver = 4,
    SrcIn = 5,
    DestIn = 6,
    SrcOut = 7,
    DestOut = 8,
    SrcAtop = 9,
    DestAtop = 10,
    Xor = 11,
    Plus = 12,
    Screen = 13,
    Overlay = 14,
    Darken = 15,
    Lighten = 16,
    ColorDodge = 17,
    ColorBurn = 18,
    HardLight = 19,
    SoftLight = 20,
    Difference = 21,
    Exclusion = 22,
    Multiply = 23,
    Hue = 24,
    Saturation = 25,
    Color = 26,
    Luminosity = 27
}

/// <summary>
/// Extend mode for gradients.
/// </summary>
internal enum ExtendMode : byte
{
    Pad = 0,
    Repeat = 1,
    Reflect = 2
}

/// <summary>
/// Base class for all paint operations.
/// </summary>
internal abstract class Paint
{
    public abstract PaintFormat Format { get; }
    
    /// <summary>
    /// Resolves this paint to a list of colored layers for rendering.
    /// For simple solid colors, this returns a single layer.
    /// For composite operations, this may return multiple layers.
    /// </summary>
    /// <param name="context">The paint resolution context.</param>
    /// <returns>A list of resolved color layers.</returns>
    public abstract IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context);
}

/// <summary>
/// Context for resolving paint operations.
/// </summary>
internal class PaintResolutionContext
{
    public PaintResolutionContext(
        CpalTable cpal,
        ColrTable colrTable,
        ushort paletteIndex,
        GlyphColor foregroundColor)
    {
        Cpal = cpal;
        ColrTable = colrTable;
        PaletteIndex = paletteIndex;
        ForegroundColor = foregroundColor;
        Transform = Matrix3x2.Identity;
    }

    public CpalTable Cpal { get; }
    public ColrTable ColrTable { get; }
    public ushort PaletteIndex { get; }
    public GlyphColor ForegroundColor { get; }
    public Matrix3x2 Transform { get; set; }

    public PaintResolutionContext WithTransform(Matrix3x2 additionalTransform)
    {
        return new PaintResolutionContext(Cpal, ColrTable, PaletteIndex, ForegroundColor)
        {
            Transform = additionalTransform * Transform
        };
    }
}

/// <summary>
/// Represents a resolved color layer ready for rendering.
/// </summary>
internal readonly struct ResolvedColorLayer
{
    public ResolvedColorLayer(ushort glyphId, GlyphColor color, Matrix3x2 transform)
    {
        GlyphId = glyphId;
        Color = color;
        Transform = transform;
    }

    public ushort GlyphId { get; }
    public GlyphColor Color { get; }
    public Matrix3x2 Transform { get; }
}

/// <summary>
/// PaintColrLayers: References a contiguous range of layers from the LayerList.
/// </summary>
internal sealed class PaintColrLayers : Paint
{
    public PaintColrLayers(byte numLayers, uint firstLayerIndex)
    {
        NumLayers = numLayers;
        FirstLayerIndex = firstLayerIndex;
    }

    public override PaintFormat Format => PaintFormat.PaintColrLayers;
    public byte NumLayers { get; }
    public uint FirstLayerIndex { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        var layers = new List<ResolvedColorLayer>();
        for (uint i = 0; i < NumLayers; i++)
        {
            var paint = context.ColrTable.GetLayerPaint(FirstLayerIndex + i);
            if (paint != null)
            {
                layers.AddRange(paint.Resolve(context));
            }
        }
        return layers;
    }
}

/// <summary>
/// PaintSolid: A solid color fill.
/// </summary>
internal sealed class PaintSolid : Paint
{
    public PaintSolid(ushort paletteIndex, float alpha)
    {
        PaletteIndex = paletteIndex;
        Alpha = alpha;
    }

    public override PaintFormat Format => PaintFormat.PaintSolid;
    public ushort PaletteIndex { get; }
    public float Alpha { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        GlyphColor color;
        if (PaletteIndex == 0xFFFF)
        {
            // Use foreground color
            color = context.ForegroundColor;
        }
        else
        {
            color = context.Cpal.GetGlyphColor(context.PaletteIndex, PaletteIndex);
        }

        // Apply alpha
        if (Alpha < 1.0f)
        {
            var adjustedAlpha = (byte)(color.Alpha * Alpha);
            color = new GlyphColor(color.Blue, color.Green, color.Red, adjustedAlpha);
        }

        // This paint doesn't have a glyph - it needs to be combined with PaintGlyph
        return Array.Empty<ResolvedColorLayer>();
    }

    public GlyphColor GetColor(PaintResolutionContext context)
    {
        GlyphColor color;
        if (PaletteIndex == 0xFFFF)
        {
            color = context.ForegroundColor;
        }
        else
        {
            color = context.Cpal.GetGlyphColor(context.PaletteIndex, PaletteIndex);
        }

        if (Alpha < 1.0f)
        {
            var adjustedAlpha = (byte)(color.Alpha * Alpha);
            color = new GlyphColor(color.Blue, color.Green, color.Red, adjustedAlpha);
        }

        return color;
    }
}

/// <summary>
/// PaintGlyph: Renders a glyph with a paint (usually a solid color or gradient).
/// </summary>
internal sealed class PaintGlyph : Paint
{
    public PaintGlyph(Paint paint, ushort glyphId)
    {
        ChildPaint = paint;
        GlyphId = glyphId;
    }

    public override PaintFormat Format => PaintFormat.PaintGlyph;
    public Paint ChildPaint { get; }
    public ushort GlyphId { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        // For solid colors, create a layer
        if (ChildPaint is PaintSolid solid)
        {
            var color = solid.GetColor(context);
            return new[] { new ResolvedColorLayer(GlyphId, color, context.Transform) };
        }

        // For gradients, we'll approximate with the first color stop for now
        // Full gradient support would require more complex rendering
        if (ChildPaint is PaintLinearGradient gradient)
        {
            var color = gradient.GetApproximateColor(context);
            return new[] { new ResolvedColorLayer(GlyphId, color, context.Transform) };
        }

        if (ChildPaint is PaintRadialGradient radialGradient)
        {
            var color = radialGradient.GetApproximateColor(context);
            return new[] { new ResolvedColorLayer(GlyphId, color, context.Transform) };
        }

        // Fallback: resolve child and use its result
        return ChildPaint.Resolve(context);
    }
}

/// <summary>
/// PaintColrGlyph: References another base glyph's paint.
/// </summary>
internal sealed class PaintColrGlyph : Paint
{
    public PaintColrGlyph(ushort glyphId)
    {
        GlyphId = glyphId;
    }

    public override PaintFormat Format => PaintFormat.PaintColrGlyph;
    public ushort GlyphId { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        var paint = context.ColrTable.GetBaseGlyphPaint(GlyphId);
        if (paint != null)
        {
            return paint.Resolve(context);
        }
        return Array.Empty<ResolvedColorLayer>();
    }
}

/// <summary>
/// Color stop for gradients.
/// </summary>
internal readonly struct ColorStop
{
    public ColorStop(float stopOffset, ushort paletteIndex, float alpha)
    {
        StopOffset = stopOffset;
        PaletteIndex = paletteIndex;
        Alpha = alpha;
    }

    public float StopOffset { get; }
    public ushort PaletteIndex { get; }
    public float Alpha { get; }
}

/// <summary>
/// PaintLinearGradient: A linear gradient fill.
/// </summary>
internal sealed class PaintLinearGradient : Paint
{
    public PaintLinearGradient(
        ExtendMode extend,
        ColorStop[] colorStops,
        float x0, float y0,
        float x1, float y1,
        float x2, float y2)
    {
        Extend = extend;
        ColorStops = colorStops;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public override PaintFormat Format => PaintFormat.PaintLinearGradient;
    public ExtendMode Extend { get; }
    public ColorStop[] ColorStops { get; }
    public float X0 { get; }
    public float Y0 { get; }
    public float X1 { get; }
    public float Y1 { get; }
    public float X2 { get; }
    public float Y2 { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        // Gradients need to be combined with a glyph
        return Array.Empty<ResolvedColorLayer>();
    }

    public GlyphColor GetApproximateColor(PaintResolutionContext context)
    {
        // Use the middle color stop as an approximation
        if (ColorStops.Length == 0)
        {
            return context.ForegroundColor;
        }

        var middleStop = ColorStops[ColorStops.Length / 2];
        GlyphColor color;
        if (middleStop.PaletteIndex == 0xFFFF)
        {
            color = context.ForegroundColor;
        }
        else
        {
            color = context.Cpal.GetGlyphColor(context.PaletteIndex, middleStop.PaletteIndex);
        }

        if (middleStop.Alpha < 1.0f)
        {
            var adjustedAlpha = (byte)(color.Alpha * middleStop.Alpha);
            color = new GlyphColor(color.Blue, color.Green, color.Red, adjustedAlpha);
        }

        return color;
    }
}

/// <summary>
/// PaintRadialGradient: A radial gradient fill.
/// </summary>
internal sealed class PaintRadialGradient : Paint
{
    public PaintRadialGradient(
        ExtendMode extend,
        ColorStop[] colorStops,
        float x0, float y0, float radius0,
        float x1, float y1, float radius1)
    {
        Extend = extend;
        ColorStops = colorStops;
        X0 = x0;
        Y0 = y0;
        Radius0 = radius0;
        X1 = x1;
        Y1 = y1;
        Radius1 = radius1;
    }

    public override PaintFormat Format => PaintFormat.PaintRadialGradient;
    public ExtendMode Extend { get; }
    public ColorStop[] ColorStops { get; }
    public float X0 { get; }
    public float Y0 { get; }
    public float Radius0 { get; }
    public float X1 { get; }
    public float Y1 { get; }
    public float Radius1 { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        return Array.Empty<ResolvedColorLayer>();
    }

    public GlyphColor GetApproximateColor(PaintResolutionContext context)
    {
        if (ColorStops.Length == 0)
        {
            return context.ForegroundColor;
        }

        var middleStop = ColorStops[ColorStops.Length / 2];
        GlyphColor color;
        if (middleStop.PaletteIndex == 0xFFFF)
        {
            color = context.ForegroundColor;
        }
        else
        {
            color = context.Cpal.GetGlyphColor(context.PaletteIndex, middleStop.PaletteIndex);
        }

        if (middleStop.Alpha < 1.0f)
        {
            var adjustedAlpha = (byte)(color.Alpha * middleStop.Alpha);
            color = new GlyphColor(color.Blue, color.Green, color.Red, adjustedAlpha);
        }

        return color;
    }
}

/// <summary>
/// PaintTransform: Applies an affine transformation to a paint.
/// </summary>
internal sealed class PaintTransform : Paint
{
    public PaintTransform(Paint paint, Matrix3x2 transform)
    {
        ChildPaint = paint;
        TransformMatrix = transform;
    }

    public override PaintFormat Format => PaintFormat.PaintTransform;
    public Paint ChildPaint { get; }
    public Matrix3x2 TransformMatrix { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        var newContext = context.WithTransform(TransformMatrix);
        return ChildPaint.Resolve(newContext);
    }
}

/// <summary>
/// PaintTranslate: Applies translation to a paint.
/// </summary>
internal sealed class PaintTranslate : Paint
{
    public PaintTranslate(Paint paint, float dx, float dy)
    {
        ChildPaint = paint;
        Dx = dx;
        Dy = dy;
    }

    public override PaintFormat Format => PaintFormat.PaintTranslate;
    public Paint ChildPaint { get; }
    public float Dx { get; }
    public float Dy { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        var translation = Matrix3x2.CreateTranslation(Dx, Dy);
        var newContext = context.WithTransform(translation);
        return ChildPaint.Resolve(newContext);
    }
}

/// <summary>
/// PaintScale: Applies uniform or non-uniform scaling to a paint.
/// </summary>
internal sealed class PaintScale : Paint
{
    public PaintScale(Paint paint, float scaleX, float scaleY, float centerX = 0, float centerY = 0)
    {
        ChildPaint = paint;
        ScaleX = scaleX;
        ScaleY = scaleY;
        CenterX = centerX;
        CenterY = centerY;
    }

    public override PaintFormat Format => PaintFormat.PaintScale;
    public Paint ChildPaint { get; }
    public float ScaleX { get; }
    public float ScaleY { get; }
    public float CenterX { get; }
    public float CenterY { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        Matrix3x2 transform;
        if (CenterX == 0 && CenterY == 0)
        {
            transform = Matrix3x2.CreateScale(ScaleX, ScaleY);
        }
        else
        {
            transform = Matrix3x2.CreateScale(ScaleX, ScaleY, new Vector2(CenterX, CenterY));
        }
        var newContext = context.WithTransform(transform);
        return ChildPaint.Resolve(newContext);
    }
}

/// <summary>
/// PaintRotate: Applies rotation to a paint.
/// </summary>
internal sealed class PaintRotate : Paint
{
    public PaintRotate(Paint paint, float angle, float centerX = 0, float centerY = 0)
    {
        ChildPaint = paint;
        Angle = angle;
        CenterX = centerX;
        CenterY = centerY;
    }

    public override PaintFormat Format => PaintFormat.PaintRotate;
    public Paint ChildPaint { get; }
    public float Angle { get; }
    public float CenterX { get; }
    public float CenterY { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        // Convert angle from degrees to radians
        var radians = Angle * MathF.PI / 180f;
        Matrix3x2 transform;
        if (CenterX == 0 && CenterY == 0)
        {
            transform = Matrix3x2.CreateRotation(radians);
        }
        else
        {
            transform = Matrix3x2.CreateRotation(radians, new Vector2(CenterX, CenterY));
        }
        var newContext = context.WithTransform(transform);
        return ChildPaint.Resolve(newContext);
    }
}

/// <summary>
/// PaintSkew: Applies skew transformation to a paint.
/// </summary>
internal sealed class PaintSkew : Paint
{
    public PaintSkew(Paint paint, float xSkewAngle, float ySkewAngle, float centerX = 0, float centerY = 0)
    {
        ChildPaint = paint;
        XSkewAngle = xSkewAngle;
        YSkewAngle = ySkewAngle;
        CenterX = centerX;
        CenterY = centerY;
    }

    public override PaintFormat Format => PaintFormat.PaintSkew;
    public Paint ChildPaint { get; }
    public float XSkewAngle { get; }
    public float YSkewAngle { get; }
    public float CenterX { get; }
    public float CenterY { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        var xTan = MathF.Tan(XSkewAngle * MathF.PI / 180f);
        var yTan = MathF.Tan(YSkewAngle * MathF.PI / 180f);
        var skew = new Matrix3x2(1, yTan, xTan, 1, 0, 0);
        
        Matrix3x2 transform;
        if (CenterX == 0 && CenterY == 0)
        {
            transform = skew;
        }
        else
        {
            var toOrigin = Matrix3x2.CreateTranslation(-CenterX, -CenterY);
            var fromOrigin = Matrix3x2.CreateTranslation(CenterX, CenterY);
            transform = toOrigin * skew * fromOrigin;
        }
        
        var newContext = context.WithTransform(transform);
        return ChildPaint.Resolve(newContext);
    }
}

/// <summary>
/// PaintComposite: Composites two paint operations.
/// </summary>
internal sealed class PaintComposite : Paint
{
    public PaintComposite(Paint source, CompositeMode mode, Paint backdrop)
    {
        Source = source;
        Mode = mode;
        Backdrop = backdrop;
    }

    public override PaintFormat Format => PaintFormat.PaintComposite;
    public Paint Source { get; }
    public CompositeMode Mode { get; }
    public Paint Backdrop { get; }

    public override IReadOnlyList<ResolvedColorLayer> Resolve(PaintResolutionContext context)
    {
        // For simple compositing, we render backdrop first, then source
        // More complex compositing modes would require proper blending
        var layers = new List<ResolvedColorLayer>();
        layers.AddRange(Backdrop.Resolve(context));
        layers.AddRange(Source.Resolve(context));
        return layers;
    }
}
