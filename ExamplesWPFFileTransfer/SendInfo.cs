using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;

namespace ExamplesWPFFileTransfer
{
    [ProtoContract]
    class SendInfo
    {
        [ProtoMember(1)]
        public string Filename { get; private set; }

        [ProtoMember(2)]
        public long BytesStart { get; private set; }

        [ProtoMember(3)]
        public long TotalBytes { get; private set; }

        [ProtoMember(4)]
        public long PacketSequenceNumber { get; private set; }

        private SendInfo() { }

        public SendInfo(string filename, long totalBytes, long bytesStart, long packetSequenceNumber)
        {
            this.Filename = filename;
            this.TotalBytes = totalBytes;
            this.BytesStart = bytesStart;
            this.PacketSequenceNumber = packetSequenceNumber;
        }
    }
}
