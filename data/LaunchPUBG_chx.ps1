Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public class DisplayHelper {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Ansi)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string dmDeviceName;
        public short dmSpecVersion,dmDriverVersion,dmSize,dmDriverExtra;
        public int dmFields,dmPositionX,dmPositionY,dmDisplayOrientation,dmDisplayFixedOutput;
        public short dmColor,dmDuplex,dmYResolution,dmTTOption,dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel,dmPelsWidth,dmPelsHeight,dmDisplayFlags,dmDisplayFrequency;
    }
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);
    [DllImport("user32.dll")] public static extern bool EnumDisplaySettings(string d, int n, ref DEVMODE dm);
    public static int SetRes(int w, int h) {
        DEVMODE dm = new DEVMODE(); dm.dmSize=(short)Marshal.SizeOf(dm);
        EnumDisplaySettings(null,-1,ref dm);
        dm.dmPelsWidth=w; dm.dmPelsHeight=h; dm.dmFields=0x180000;
        // CDS_UPDATEREGISTRY (1) | CDS_GLOBAL (8) = permanent change
        return ChangeDisplaySettings(ref dm, 9);
    }
}
"@

Write-Host "Setting resolution to 1728x1080..."
$r = [DisplayHelper]::SetRes(1728, 1080)
if ($r -ne 0) { Write-Host "FAILED (code $r). Aborting."; pause; exit }
Write-Host "Resolution set OK."

Start-Sleep -Seconds 2

Write-Host "Launching PUBG..."
Start-Process "steam://rungameid/578080"

Write-Host "Waiting for PUBG process..."
$pubg = $null
$waited = 0
while ($null -eq $pubg -and $waited -lt 120) {
    $pubg = Get-Process -Name "TslGame" -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    $waited += 3
}

if ($null -eq $pubg) { Write-Host "PUBG did not start. Restoring resolution..."; [DisplayHelper]::SetRes(1920, 1080) | Out-Null; exit }

Write-Host "PUBG running. Waiting for it to close..."
$pubg | Wait-Process

Write-Host "PUBG closed. Restoring 1920x1080..."
[DisplayHelper]::SetRes(1920, 1080) | Out-Null
Write-Host "Done."

