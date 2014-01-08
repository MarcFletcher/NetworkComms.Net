//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

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
