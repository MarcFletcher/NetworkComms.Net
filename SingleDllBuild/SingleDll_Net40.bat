md ".\%1"

COPY ..\Platforms\Net40\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll
COPY ..\Platforms\Net40\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.pdb .\%1\NetworkCommsDotNet.pdb
COPY ..\Platforms\Net40\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.xml .\%1\NetworkCommsDotNet.xml

COPY ..\DLL\Net40\protobuf-net.dll .\%1\protobuf-net.dll

COPY ..\Platforms\Net40\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll
COPY ..\Platforms\Net40\DPSBase\bin\%1\DPSBase.pdb .\%1\DPSBase.pdb
COPY ..\Platforms\Net40\DPSBase\bin\%1\DPSBase.xml .\%1\DPSBase.xml

COPY ..\Platforms\Net40\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll
COPY ..\Platforms\Net40\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.pdb .\%1\SevenZipLZMACompressor.pdb
COPY ..\Platforms\Net40\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.xml .\%1\SevenZipLZMACompressor.xml

COPY ..\DLL\Net40\NLog.dll .\%1\NLog.dll

COPY ..\Platforms\Net40\SharpZipLibCompressor\bin\%1\ICSharpCode.SharpZipLib.dll .\%1\ICSharpCode.SharpZipLib.dll
COPY ..\Platforms\Net40\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.dll .\%1\SharpZipLibCompressor.dll
COPY ..\Platforms\Net40\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.pdb .\%1\SharpZipLibCompressor.pdb
COPY ..\Platforms\Net40\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.xml .\%1\SharpZipLibCompressor.xml

COPY ..\Platforms\Net40\QuickLZCompressor\bin\%1\QuickLZCompressor.dll .\%1\QuickLZCompressor.dll
COPY ..\Platforms\Net40\QuickLZCompressor\bin\%1\QuickLZCompressor.pdb .\%1\QuickLZCompressor.pdb
COPY ..\Platforms\Net40\QuickLZCompressor\bin\%1\QuickLZCompressor.xml .\%1\QuickLZCompressor.xml

COPY ..\DistributedFileSystem\bin\%1\DistributedFileSystem.dll .\%1\DistributedFileSystem.dll
COPY ..\DistributedFileSystem\bin\%1\DistributedFileSystem.pdb .\%1\DistributedFileSystem.pdb
COPY ..\DistributedFileSystem\bin\%1\DistributedFileSystem.xml .\%1\DistributedFileSystem.xml

COPY ..\RemoteProcedureCalls\bin\%1\RemoteProcedureCalls.dll .\%1\RemoteProcedureCalls.dll
COPY ..\RemoteProcedureCalls\bin\%1\RemoteProcedureCalls.pdb .\%1\RemoteProcedureCalls.pdb
COPY ..\RemoteProcedureCalls\bin\%1\RemoteProcedureCalls.xml .\%1\RemoteProcedureCalls.xml

md ".\Net40"
md ".\Net40\%1\Complete"
md ".\Net40\%1\Core"

REM The following line is for building on .Net4.0 systems
REM .\ILMerge.exe /targetplatform:v4 /out:.\Net40\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll /xmldocs
REM .\ILMerge.exe /targetplatform:v4 /out:.\Net40\%1\Complete\NetworkCommsDotNetComplete.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\SharpZipLibCompressor.dll .\%1\QuickLZCompressor.dll .\%1\RemoteProcedureCalls.dll .\%1\DistributedFileSystem.dll /xmldocs

REM On .Net4.5 systems (Windows 8) a static path to the 4.0 assemblies is required. This may require manually creating this path.
.\ILMerge.exe /targetplatform:"v4,C:\Program Files\Reference Assemblies\Microsoft\Framework\v4.0" /out:.\Net40\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll /xmldocs
.\ILMerge.exe /targetplatform:"v4,C:\Program Files\Reference Assemblies\Microsoft\Framework\v4.0" /out:.\Net40\%1\Complete\NetworkCommsDotNetComplete.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\SharpZipLibCompressor.dll .\%1\QuickLZCompressor.dll .\%1\RemoteProcedureCalls.dll .\%1\DistributedFileSystem.dll /xmldocs

DEL .\%1\*.dll .\%1\*.pdb .\%1\*.xml
