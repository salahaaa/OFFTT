DateERP 1.50.66 - OFFLINE INSTALLERS - OPTIONAL
================================================

This folder is for offline-first installation (no internet needed).

If you have poor or no internet, download these files on a PC with internet
using your phone + USB, then copy them into this Tools folder next to
RUN-DATEERP-1.50.66.bat and INSTALL-1.50.66.bat:

1. .NET 8 SDK (Windows x64) - ~220 MB
   File name: dotnet-sdk-8-win-x64.exe
   Direct link: https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe
   Official: https://dotnet.microsoft.com/download/dotnet/8.0

2. SQL Server 2022 Express - FULL package - ~1.3 GB (offline, no internet needed)
   File name: SQLEXPR_x64_ENU.exe
   Direct link: https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SQLEXPR_x64_ENU.exe
   If you have only bootstrapper (250 MB), it will still download engine packages online.

3. SSMS 20.2.1 Full - ~700 MB (optional)
   File name: SSMS-Setup-ENU.exe
   Direct link: https://go.microsoft.com/fwlink/?linkid=2313753&clcid=0x409
   Official: https://aka.ms/ssmsfullsetup

4. Visual Studio 2022 Community bootstrapper - ~3 MB (still needs internet for workloads)
   File name: vs_community.exe
   Direct link: https://aka.ms/vs/17/release/vs_community.exe

How offline detection works:
- RUN-DATEERP-1.50.66.bat and INSTALL-1.50.66.bat check if these files exist next to them
- If found, they are used WITHOUT internet
- If not found, they are downloaded automatically from Microsoft servers

You do NOT need to download all - only .NET 8 SDK is mandatory for building.
SQL Server Express is needed if you don't have SQL Server instance .\SQLEXPRESS
SSMS and Visual Studio are optional.

After copying offline installers here, run:
RUN-DATEERP-1.50.66.bat as administrator (right-click -> Run as administrator)

Size if you include all offline installers: ~2.5 GB
Size without offline installers (online mode): ~5-10 MB source + installer scripts (will download 220 MB SDK on first run)
