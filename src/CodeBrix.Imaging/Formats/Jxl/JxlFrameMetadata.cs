// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Identifies a JPEG XL frame blending operation.</summary>
public enum JxlBlendMode
{
    /// <summary>Replace samples in the layer rectangle.</summary>
    Replace = 0,
    /// <summary>Add layer samples to the reference.</summary>
    Add = 1,
    /// <summary>Blend using the selected alpha channel.</summary>
    Blend = 2,
    /// <summary>Add the layer multiplied by its selected alpha.</summary>
    MultiplyAdd = 3,
    /// <summary>Multiply layer and reference samples.</summary>
    Multiply = 4,
}

/// <summary>Describes one channel group's blending against a reference frame.</summary>
/// <param name="Mode">The blending operation.</param>
/// <param name="AlphaChannel">The selected extra-channel index when the operation uses alpha.</param>
/// <param name="Clamp">Whether to clamp the blend multiplier to zero through one.</param>
/// <param name="SourceReference">The source reference slot, numbered zero through three.</param>
public readonly record struct JxlBlendInfo(JxlBlendMode Mode, int AlphaChannel, bool Clamp, int SourceReference);

/// <summary>Describes JPEG XL display-frame timing and layer placement.</summary>
public sealed class JxlFrameMetadata : IDeepCloneable
{
    /// <summary>Gets or sets the low-frequency reference level, or zero for an ordinary layer.</summary>
    public int LowFrequencyLevel { get; set; }
    /// <summary>Gets or sets the duration in the image's animation ticks.</summary>
    public uint Duration { get; set; }
    /// <summary>Gets or sets the optional frame name, limited to 1071 UTF-8 bytes without unpaired surrogates.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the encoded animation timecode.</summary>
    public uint Timecode { get; set; }
    /// <summary>Gets or sets whether this is a displayed keyframe.</summary>
    public bool IsKeyframe { get; set; }
    /// <summary>Gets or sets the layer's horizontal offset in the coded canvas.</summary>
    public int CropLeft { get; set; }
    /// <summary>Gets or sets the layer's vertical offset in the coded canvas.</summary>
    public int CropTop { get; set; }
    /// <summary>Gets or sets the coded layer width, before clipping to the canvas.</summary>
    public int CropWidth { get; set; }
    /// <summary>Gets or sets the coded layer height, before clipping to the canvas.</summary>
    public int CropHeight { get; set; }
    /// <summary>Gets or sets whether this layer exists only for subsequent references.</summary>
    public bool IsReferenceOnly { get; set; }
    /// <summary>Gets or sets the color-channel blending operation.</summary>
    public JxlBlendInfo Blending { get; set; }
    /// <summary>Gets or sets the extra-channel blending operations in encoded order.</summary>
    public JxlBlendInfo[] ExtraChannelBlending { get; set; } = [];
    /// <summary>Gets or sets the destination reference slot, or minus one when the frame is not retained.</summary>
    public int SaveAsReference { get; set; } = -1;
    /// <summary>Gets or sets whether retained samples precede the color transform.</summary>
    public bool SaveBeforeColorTransform { get; set; }
    /// <inheritdoc/>
    public IDeepCloneable DeepClone()
    {
        JxlFrameMetadata clone = (JxlFrameMetadata)this.MemberwiseClone();
        clone.ExtraChannelBlending = this.ExtraChannelBlending == null ? null : (JxlBlendInfo[])this.ExtraChannelBlending.Clone();
        return clone;
    }
}
