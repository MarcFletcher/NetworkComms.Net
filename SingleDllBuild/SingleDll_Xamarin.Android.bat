md ".\%1"

COPY ..\Platforms\Xamarin.Android\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.dll .\%1\NetworkCommsDotNet.dll
COPY ..\Platforms\Xamarin.Android\NetworkCommsDotNet\bin\%1\NetworkCommsDotNet.pdb .\%1\NetworkCommsDotNet.pdb

COPY ..\DLL\Xamarin.Android\protobuf-net.dll .\%1\protobuf-net.dll

COPY ..\Platforms\Xamarin.Android\DPSBase\bin\%1\DPSBase.dll .\%1\DPSBase.dll
COPY ..\Platforms\Xamarin.Android\DPSBase\bin\%1\DPSBase.pdb .\%1\DPSBase.pdb

COPY ..\Platforms\Xamarin.Android\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.dll .\%1\SevenZipLZMACompressor.dll
COPY ..\Platforms\Xamarin.Android\SevenZipLZMACompressor\bin\%1\SevenZipLZMACompressor.pdb .\%1\SevenZipLZMACompressor.pdb

COPY ..\Platforms\Xamarin.Android\SharpZipLibCompressor\bin\%1\ICSharpCode.SharpZipLib.dll .\%1\ICSharpCode.SharpZipLib.dll
COPY ..\Platforms\Xamarin.Android\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.dll .\%1\SharpZipLibCompressor.dll
COPY ..\Platforms\Xamarin.Android\SharpZipLibCompressor\bin\%1\SharpZipLibCompressor.pdb .\%1\SharpZipLibCompressor.pdb

md ".\Xamarin.Android"
md ".\Xamarin.Android\%1\Complete"
md ".\Xamarin.Android\%1\Core"

.\ILMerge.exe /lib:"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\MonoAndroid\v4.2" /out:.\Xamarin.Android\%1\Core\NetworkCommsDotNetCore.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll
.\ILMerge.exe /lib:"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\MonoAndroid\v4.2" /out:.\Xamarin.Android\%1\Complete\NetworkCommsDotNetComplete.dll .\%1\NetworkCommsDotNet.dll .\%1\protobuf-net.dll .\%1\DPSBase.dll .\%1\SevenZipLZMACompressor.dll .\%1\ICSharpCode.SharpZipLib.dll .\%1\SharpZipLibCompressor.dll

DEL .\%1\*.dll .\%1\*.pdb
REM DEL .\Xamarin.Android\%1\Complete\*.pdb .\Xamarin.Android\%1\Core\*.pdb

