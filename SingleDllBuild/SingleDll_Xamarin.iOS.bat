md ".\%1"

COPY ..\Platforms\Xamarin.iOS\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll
COPY ..\Platforms\Xamarin.iOS\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.pdb .\%1\NetworkCommsDotNet.pdb

COPY ..\DLL\Xamarin.iOS\protobuf-net.dll .\%1\protobuf-net.dll

COPY ..\Platforms\Xamarin.iOS\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll
COPY ..\Platforms\Xamarin.iOS\DPSBase\bin\%1\DPSBase.pdb .\%1\DPSBase.pdb

COPY ..\Platforms\Xamarin.iOS\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll
COPY ..\Platforms\Xamarin.iOS\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.pdb .\%1\SevenZipLZMACompressor.pdb

COPY ..\Platforms\Xamarin.iOS\SharpZipLibCompressor\bin\%1\ICSharpCode.SharpZipLib.dll .\%1\ICSharpCode.SharpZipLib.dll
COPY ..\Platforms\Xamarin.iOS\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.dll .\%1\SharpZipLibCompressor.dll
COPY ..\Platforms\Xamarin.iOS\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.pdb .\%1\SharpZipLibCompressor.pdb

md ".\Xamarin.iOS"
REM md ".\Xamarin.iOS\%1\Complete"
md ".\Xamarin.iOS\%1\Core"

.\ILMerge.exe /targetplatform:v4 /out:.\Xamarin.iOS\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll
REM .\ILMerge.exe /targetplatform:v4 /out:.\Xamarin.iOS\%1\Complete\NetworkCommsDotNetComplete.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\SharpZipLibCompressor.dll

DEL .\%1\*.dll .\%1\*.pdb
REM DEL .\Xamarin.iOS\%1\Complete\*.pdb .\Xamarin.iOS\%1\Core\*.pdb

