using System.Runtime.InteropServices;

namespace CodeBrix.Imaging.Advanced;

/// <summary>
/// Provides information about the processor architecture the current process is running on.
/// </summary>
/// <remarks>
/// Used to select architecture-appropriate code paths - for example the JPEG encoder falls
/// back to a bounds-checked RGB-to-YCbCr lookup on RISC-V.
/// </remarks>
public static class ArchitectureInfo
{
    // Compared against the enum member rather than RuntimeInformation.ProcessArchitecture
    // .ToString(), so the check does not depend on how the enum happens to format itself.
    private static readonly bool IsRiscV =
        RuntimeInformation.ProcessArchitecture == Architecture.RiscV64;

    /// <summary>
    /// Gets a value indicating whether the current process is running on a RISC-V processor.
    /// The result is computed once, when the type is first used.
    /// </summary>
    public static bool IsRiscVArchitecture => IsRiscV;
}
