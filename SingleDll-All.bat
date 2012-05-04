COPY NetworkCommsDotNet.dll temp.dll
COPY ..\..\..\SharpZipLibCompressor\bin\SingleDLL-All\*.dll .\
COPY ..\..\..\QuickLZCompressor\bin\SingleDLL-All\*.dll .\
..\..\..\ILMerge.exe /targetplatform:v4 /out:NetworkCommsDotNet.dll temp.dll Common.Logging.dll protobuf-net.dll SerializerBase.dll SevenZipLZMACompressor.dll ICSharpCode.SharpZipLib.dll QuickLZCompressor.dll SharpZipLibCompressor.dll
DEL temp.dll Common.Logging.dll protobuf-net.dll SerializerBase.dll SevenZipLZMACompressor.dll ICSharpCode.SharpZipLib.dll QuickLZCompressor.dll SharpZipLibCompressor.dll *.pdb