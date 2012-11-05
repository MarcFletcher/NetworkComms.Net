COPY ..\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll

COPY ..\DPSBase\bin\%1\protobuf-net.dll .\%1\protobuf-net.dll
COPY ..\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll

COPY ..\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll

COPY ..\SharpZipLibCompressor\bin\%1\ICSharpCode.SharpZipLib.dll .\%1\ICSharpCode.SharpZipLib.dll
COPY ..\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.dll .\%1\SharpZipLibCompressor.dll

COPY ..\QuickLZCompressor\bin\%1\QuickLZCompressor.dll .\%1\QuickLZCompressor.dll

COPY ..\DistributedFileSystem\bin\%1\Common.Logging.dll .\%1\Common.Logging.dll
COPY ..\DistributedFileSystem\bin\%1\DistributedFileSystem.dll .\%1\DistributedFileSystem.dll

COPY ..\RemoteProcedureCalls\bin\%1\RemoteProcedureCalls.dll .\%1\RemoteProcedureCalls.dll

COPY ..\Common.Logging.Log4Net.dll .\%1\Common.Logging.Log4Net.dll
COPY ..\log4net.dll .\%1\log4net.dll

md ".\%1\Complete"
md ".\%1\Lite"

.\ILMerge.exe /targetplatform:v4 /out:.\%1\Complete\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll .\%1\Common.Logging.Log4Net.dll .\%1\log4net.dll .\%1\Common.Logging.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\QuickLZCompressor.dll .\%1\SharpZipLibCompressor.dll .\%1\DistributedFileSystem.dll .\%1\RemoteProcedureCalls.dll
.\ILMerge.exe /targetplatform:v4 /out:.\%1\Lite\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll .\%1\Common.Logging.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll

DEL .\%1\*.dll .\%1\Complete\*.pdb .\%1\Lite\*.pdb 