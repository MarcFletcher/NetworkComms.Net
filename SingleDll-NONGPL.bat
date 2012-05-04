COPY NetworkCommsDotNet.dll temp.dll
..\..\..\ILMerge.exe /targetplatform:v4 /out:NetworkCommsDotNet.dll temp.dll Common.Logging.dll protobuf-net.dll SerializerBase.dll SevenZipLZMACompressor.dll 
DEL temp.dll Common.Logging.dll protobuf-net.dll SerializerBase.dll SevenZipLZMACompressor.dll *.pdb