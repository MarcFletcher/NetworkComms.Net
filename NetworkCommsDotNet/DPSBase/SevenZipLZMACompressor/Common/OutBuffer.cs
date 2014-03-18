//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

// OutBuffer.cs

namespace LZMA.Buffer
{
	internal class OutBuffer
	{
		byte[] m_Buffer;
		uint m_Pos;
		uint m_BufferSize;
		System.IO.Stream m_Stream;
		ulong m_ProcessedSize;

		internal OutBuffer(uint bufferSize)
		{
			m_Buffer = new byte[bufferSize];
			m_BufferSize = bufferSize;
		}

		internal void SetStream(System.IO.Stream stream) { m_Stream = stream; }
		internal void FlushStream() { m_Stream.Flush(); }
		internal void CloseStream() 
        {
#if NETFX_CORE
            m_Stream.Dispose();
#else
            m_Stream.Close(); 
#endif
        }
		internal void ReleaseStream() { m_Stream = null; }

		internal void Init()
		{
			m_ProcessedSize = 0;
			m_Pos = 0;
		}

		internal void WriteByte(byte b)
		{
			m_Buffer[m_Pos++] = b;
			if (m_Pos >= m_BufferSize)
				FlushData();
		}

		internal void FlushData()
		{
			if (m_Pos == 0)
				return;
			m_Stream.Write(m_Buffer, 0, (int)m_Pos);
			m_Pos = 0;
		}

		internal ulong GetProcessedSize() { return m_ProcessedSize + m_Pos; }
	}
}
