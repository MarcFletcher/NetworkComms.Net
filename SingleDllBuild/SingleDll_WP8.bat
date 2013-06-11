md "./%1"

COPY ..\Platforms\WP8\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll
COPY ..\Platforms\WP8\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.pdb .\%1\NetworkCommsDotNet.pdb
COPY ..\Platforms\WP8\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.xml .\%1\NetworkCommsDotNet.xml

COPY ..\DLL\WP8\protobuf-net.dll .\%1\protobuf-net.dll

COPY ..\Platforms\WP8\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll
COPY ..\Platforms\WP8\DPSBase\bin\%1\DPSBase.pdb .\%1\DPSBase.pdb
COPY ..\Platforms\WP8\DPSBase\bin\%1\DPSBase.xml .\%1\DPSBase.xml

COPY ..\Platforms\WP8\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll
COPY ..\Platforms\WP8\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.pdb .\%1\SevenZipLZMACompressor.pdb
COPY ..\Platforms\WP8\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.xml .\%1\SevenZipLZMACompressor.xml

COPY ..\DLL\WP8\NLog.dll .\%1\NLog.dll

md ".\WP8"
md ".\WP8\%1\Core"

REM .\ILMerge.exe /lib:"%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\WindowsPhone\v8.0" /out:.\WP8\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\SevenZipLZMACompressor.dll .\%1\DPSBase.dll .\%1\protobuf-net.dll .\%1\NLog.dll
REM for now we will just copy relevenat WP8 dlls to the merge directory
COPY .\%1\*.dll .\WP8\%1\Core\
COPY .\%1\*.pdb .\WP8\%1\Core\
COPY .\%1\*.xml .\WP8\%1\Core\

DEL .\%1\*.dll .\%1\*.pdb .\%1\*.xml

