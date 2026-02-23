using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Imaging.Advanced;

public class ArchitectureInfo
{
    private static bool? _isRiscVArchitecture;

    public static bool IsRiscVArchitecture
    {
        get
        {
            if (!_isRiscVArchitecture.HasValue)
            {
                var processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
                _isRiscVArchitecture = processArchitecture.Contains("RiscV", StringComparison.OrdinalIgnoreCase);
            }
            
            return _isRiscVArchitecture!.Value;
        }
    }
}
