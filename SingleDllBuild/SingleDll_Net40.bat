COPY ..\Platforms\Net40\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll
COPY ..\Platforms\Net40\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.pdb .\%1\NetworkCommsDotNet.pdb

COPY ..\DLL\Net40\protobuf-net.dll .\%1\protobuf-net.dll

COPY ..\Platforms\Net40\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll
COPY ..\Platforms\Net40\DPSBase\bin\%1\DPSBase.pdb .\%1\DPSBase.pdb

COPY ..\Platforms\Net40\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll
COPY ..\Platforms\Net40\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.pdb .\%1\SevenZipLZMACompressor.pdb

COPY ..\DLL\Net40\NLog.dll .\%1\NLog.dll

COPY ..\Platforms\Net40\SharpZipLibCompressor\bin\%1\ICSharpCode.SharpZipLib.dll .\%1\ICSharpCode.SharpZipLib.dll
COPY ..\Platforms\Net40\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.dll .\%1\SharpZipLibCompressor.dll
COPY ..\Platforms\Net40\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.pdb .\%1\SharpZipLibCompressor.pdb

COPY ..\Platforms\Net40\QuickLZCompressor\bin\%1\QuickLZCompressor.dll .\%1\QuickLZCompressor.dll
COPY ..\Platforms\Net40\QuickLZCompressor\bin\%1\QuickLZCompressor.pdb .\%1\QuickLZCompressor.pdb

COPY ..\DistributedFileSystem\bin\%1\DistributedFileSystem.dll .\%1\DistributedFileSystem.dll
COPY ..\DistributedFileSystem\bin\%1\DistributedFileSystem.pdb .\%1\DistributedFileSystem.pdb

COPY ..\RemoteProcedureCalls\bin\%1\RemoteProcedureCalls.dll .\%1\RemoteProcedureCalls.dll
COPY ..\RemoteProcedureCalls\bin\%1\RemoteProcedureCalls.pdb .\%1\RemoteProcedureCalls.pdb

md ".\Net40"
md ".\Net40\%1\Complete"
md ".\Net40\%1\Core"

.\ILMerge.exe /targetplatform:v4 /out:.\Net40\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll
.\ILMerge.exe /targetplatform:v4 /out:.\Net40\%1\Complete\NetworkCommsDotNetComplete.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\SharpZipLibCompressor.dll .\%1\QuickLZCompressor.dll .\%1\RemoteProcedureCalls.dll .\%1\DistributedFileSystem.dll

DEL .\%1\*.dll .\%1\*.pdb
REM DEL .\Net40\%1\Complete\*.pdb .\Net40\%1\Core\*.pdb

