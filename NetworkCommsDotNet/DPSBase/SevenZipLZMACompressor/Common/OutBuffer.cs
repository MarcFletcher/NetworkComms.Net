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
