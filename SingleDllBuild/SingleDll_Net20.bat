md ".\%1"

COPY ..\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll
COPY ..\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.pdb .\%1\NetworkCommsDotNet.pdb
COPY ..\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.xml .\%1\NetworkCommsDotNet.xml

COPY ..\DLL\Net20\protobuf-net.dll .\%1\protobuf-net.dll

COPY ..\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll
COPY ..\DPSBase\bin\%1\DPSBase.pdb .\%1\DPSBase.pdb
COPY ..\DPSBase\bin\%1\DPSBase.xml .\%1\DPSBase.xml

COPY ..\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll
COPY ..\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.pdb .\%1\SevenZipLZMACompressor.pdb
COPY ..\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.xml .\%1\SevenZipLZMACompressor.xml

COPY ..\DLL\Net20\NLog.dll .\%1\NLog.dll

COPY ..\SharpZipLibCompressor\bin\%1\ICSharpCode.SharpZipLib.dll .\%1\ICSharpCode.SharpZipLib.dll
COPY ..\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.dll .\%1\SharpZipLibCompressor.dll
COPY ..\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.pdb .\%1\SharpZipLibCompressor.pdb
COPY ..\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.xml .\%1\SharpZipLibCompressor.xml

COPY ..\QuickLZCompressor\bin\%1\QuickLZCompressor.dll .\%1\QuickLZCompressor.dll
COPY ..\QuickLZCompressor\bin\%1\QuickLZCompressor.pdb .\%1\QuickLZCompressor.pdb
COPY ..\QuickLZCompressor\bin\%1\QuickLZCompressor.xml .\%1\QuickLZCompressor.xml

md ".\Net20"
md ".\Net20\%1\Complete"
md ".\Net20\%1\Core"

.\ILMerge.exe /targetplatform:v2 /out:.\Net20\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll /xmldocs
.\ILMerge.exe /targetplatform:v2 /out:.\Net20\%1\Complete\NetworkCommsDotNetComplete.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\NLog.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\SharpZipLibCompressor.dll .\%1\QuickLZCompressor.dll /xmldocs

DEL .\%1\*.dll .\%1\*.pdb .\%1\*.xml

