// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

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
