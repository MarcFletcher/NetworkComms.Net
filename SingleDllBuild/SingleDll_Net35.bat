COPY ..\Platforms\Net35\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll
COPY ..\Platforms\Net35\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.pdb .\%1\NetworkCommsDotNet.pdb

COPY ..\DLL\Net35\protobuf-net.dll .\%1\protobuf-net.dll

COPY ..\Platforms\Net35\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll
COPY ..\Platforms\Net35\DPSBase\bin\%1\DPSBase.pdb .\%1\DPSBase.pdb

COPY ..\Platforms\Net35\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll
COPY ..\Platforms\Net35\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.pdb .\%1\SevenZipLZMACompressor.pdb

COPY ..\DLL\Net35\NLog.dll .\%1\NLog.dll

COPY ..\Platforms\Net35\SharpZipLibCompressor\bin\%1\ICSharpCode.SharpZipLib.dll .\%1\ICSharpCode.SharpZipLib.dll
COPY ..\Platforms\Net35\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.dll .\%1\SharpZipLibCompressor.dll
COPY ..\Platforms\Net35\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.pdb .\%1\SharpZipLibCompressor.pdb
					 
COPY ..\Platforms\Net35\QuickLZCompressor\bin\%1\QuickLZCompressor.dll .\%1\QuickLZCompressor.dll
COPY ..\Platforms\Net35\QuickLZCompressor\bin\%1\QuickLZCompressor.pdb .\%1\QuickLZCompressor.pdb

md ".\Net35"
md ".\Net35\%1\Complete"
md ".\Net35\%1\Core"

.\ILMerge.exe /out:.\Net35\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll
.\ILMerge.exe /out:.\Net35\%1\Complete\NetworkCommsDotNetComplete.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\SharpZipLibCompressor.dll .\%1\QuickLZCompressor.dll

DEL .\%1\*.dll .\%1\*.pdb
REM DEL .\Net35\%1\Complete\*.pdb .\Net35\%1\Core\*.pdb

