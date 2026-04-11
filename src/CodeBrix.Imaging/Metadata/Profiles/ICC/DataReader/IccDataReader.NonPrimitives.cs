// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace CodeBrix.Imaging.Metadata.Profiles.Icc; //Was previously: namespace SixLabors.ImageSharp.Metadata.Profiles.Icc;

/// <summary>
/// Provides methods to read ICC data types
/// </summary>
internal sealed partial class IccDataReader
{
    /// <summary>
    /// Reads a DateTime
    /// </summary>
    /// <returns>the value</returns>
    public DateTime ReadDateTime()
    {
        try
        {
            return new DateTime(
                year: this.ReadUInt16(),
                month: this.ReadUInt16(),
                day: this.ReadUInt16(),
                hour: this.ReadUInt16(),
                minute: this.ReadUInt16(),
                second: this.ReadUInt16(),
                kind: DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Reads an ICC profile version number
    /// </summary>
    /// <returns>the version number</returns>
    public IccVersion ReadVersionNumber()
    {
        var version = this.ReadInt32();

        var major = (version >> 24) & 0xFF;
        var minor = (version >> 20) & 0x0F;
        var bugfix = (version >> 16) & 0x0F;

        return new IccVersion(major, minor, bugfix);
    }

    /// <summary>
    /// Reads an XYZ number
    /// </summary>
    /// <returns>the XYZ number</returns>
    public Vector3 ReadXyzNumber()
    {
        return new Vector3(
            x: this.ReadFix16(),
            y: this.ReadFix16(),
            z: this.ReadFix16());
    }

    /// <summary>
    /// Reads a profile ID
    /// </summary>
    /// <returns>the profile ID</returns>
    public IccProfileId ReadProfileId()
    {
        return new IccProfileId(
            p1: this.ReadUInt32(),
            p2: this.ReadUInt32(),
            p3: this.ReadUInt32(),
            p4: this.ReadUInt32());
    }

    /// <summary>
    /// Reads a position number
    /// </summary>
    /// <returns>the position number</returns>
    public IccPositionNumber ReadPositionNumber()
    {
        return new IccPositionNumber(
            offset: this.ReadUInt32(),
            size: this.ReadUInt32());
    }

    /// <summary>
    /// Reads a response number
    /// </summary>
    /// <returns>the response number</returns>
    public IccResponseNumber ReadResponseNumber()
    {
        return new IccResponseNumber(
            deviceCode: this.ReadUInt16(),
            measurementValue: this.ReadFix16());
    }

    /// <summary>
    /// Reads a named color
    /// </summary>
    /// <param name="deviceCoordCount">Number of device coordinates</param>
    /// <returns>the named color</returns>
    public IccNamedColor ReadNamedColor(uint deviceCoordCount)
    {
        var name = this.ReadAsciiString(32);
        ushort[] pcsCoord = { this.ReadUInt16(), this.ReadUInt16(), this.ReadUInt16() };
        var deviceCoord = new ushort[deviceCoordCount];

        for (var i = 0; i < deviceCoordCount; i++)
        {
            deviceCoord[i] = this.ReadUInt16();
        }

        return new IccNamedColor(name, pcsCoord, deviceCoord);
    }

    /// <summary>
    /// Reads a profile description
    /// </summary>
    /// <returns>the profile description</returns>
    public IccProfileDescription ReadProfileDescription()
    {
        var manufacturer = this.ReadUInt32();
        var model = this.ReadUInt32();
        var attributes = (IccDeviceAttribute)this.ReadInt64();
        var technologyInfo = (IccProfileTag)this.ReadUInt32();

        var manufacturerInfo = ReadText();
        var modelInfo = ReadText();

        return new IccProfileDescription(
            manufacturer,
            model,
            attributes,
            technologyInfo,
            manufacturerInfo.Texts,
            modelInfo.Texts);

        IccMultiLocalizedUnicodeTagDataEntry ReadText()
        {
            var type = this.ReadTagDataEntryHeader();
            switch (type)
            {
                case IccTypeSignature.MultiLocalizedUnicode:
                    return this.ReadMultiLocalizedUnicodeTagDataEntry();
                case IccTypeSignature.TextDescription:
                    return (IccMultiLocalizedUnicodeTagDataEntry)this.ReadTextDescriptionTagDataEntry();

                default:
                    throw new InvalidIccProfileException("Profile description can only have multi-localized Unicode or text description entries");
            }
        }
    }

    /// <summary>
    /// Reads a colorant table entry
    /// </summary>
    /// <returns>the profile description</returns>
    public IccColorantTableEntry ReadColorantTableEntry()
    {
        return new IccColorantTableEntry(
            name: this.ReadAsciiString(32),
            pcs1: this.ReadUInt16(),
            pcs2: this.ReadUInt16(),
            pcs3: this.ReadUInt16());
    }

    /// <summary>
    /// Reads a screening channel
    /// </summary>
    /// <returns>the screening channel</returns>
    public IccScreeningChannel ReadScreeningChannel()
    {
        return new IccScreeningChannel(
            frequency: this.ReadFix16(),
            angle: this.ReadFix16(),
            spotShape: (IccScreeningSpotType)this.ReadInt32());
    }
}