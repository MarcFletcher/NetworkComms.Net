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

// InBuffer.cs

namespace LZMA.Buffer
{
	internal class InBuffer
	{
		byte[] m_Buffer;
		uint m_Pos;
		uint m_Limit;
		uint m_BufferSize;
		System.IO.Stream m_Stream;
		bool m_StreamWasExhausted;
		ulong m_ProcessedSize;

		internal InBuffer(uint bufferSize)
		{
			m_Buffer = new byte[bufferSize];
			m_BufferSize = bufferSize;
		}

		internal void Init(System.IO.Stream stream)
		{
			m_Stream = stream;
			m_ProcessedSize = 0;
			m_Limit = 0;
			m_Pos = 0;
			m_StreamWasExhausted = false;
		}

		internal bool ReadBlock()
		{
			if (m_StreamWasExhausted)
				return false;
			m_ProcessedSize += m_Pos;
			int aNumProcessedBytes = m_Stream.Read(m_Buffer, 0, (int)m_BufferSize);
			m_Pos = 0;
			m_Limit = (uint)aNumProcessedBytes;
			m_StreamWasExhausted = (aNumProcessedBytes == 0);
			return (!m_StreamWasExhausted);
		}


		internal void ReleaseStream()
		{
			// m_Stream.Close(); 
			m_Stream = null;
		}

		internal bool ReadByte(byte b) // check it
		{
			if (m_Pos >= m_Limit)
				if (!ReadBlock())
					return false;
			b = m_Buffer[m_Pos++];
			return true;
		}

		internal byte ReadByte()
		{
			// return (byte)m_Stream.ReadByte();
			if (m_Pos >= m_Limit)
				if (!ReadBlock())
					return 0xFF;
			return m_Buffer[m_Pos++];
		}

		internal ulong GetProcessedSize()
		{
			return m_ProcessedSize + m_Pos;
		}
	}
}
