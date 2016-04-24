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

using System;

namespace LZMA.RangeCoder
{
	internal struct BitEncoder
	{
		internal const int kNumBitModelTotalBits = 11;
		internal const uint kBitModelTotal = (1 << kNumBitModelTotalBits);
		const int kNumMoveBits = 5;
		const int kNumMoveReducingBits = 2;
		internal const int kNumBitPriceShiftBits = 6;

		uint Prob;

		internal void Init() { Prob = kBitModelTotal >> 1; }

		internal void UpdateModel(uint symbol)
		{
			if (symbol == 0)
				Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
			else
				Prob -= (Prob) >> kNumMoveBits;
		}

		internal void Encode(Encoder encoder, uint symbol)
		{
			// encoder.EncodeBit(Prob, kNumBitModelTotalBits, symbol);
			// UpdateModel(symbol);
			uint newBound = (encoder.Range >> kNumBitModelTotalBits) * Prob;
			if (symbol == 0)
			{
				encoder.Range = newBound;
				Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
			}
			else
			{
				encoder.Low += newBound;
				encoder.Range -= newBound;
				Prob -= (Prob) >> kNumMoveBits;
			}
			if (encoder.Range < Encoder.kTopValue)
			{
				encoder.Range <<= 8;
				encoder.ShiftLow();
			}
		}

		private static UInt32[] ProbPrices = new UInt32[kBitModelTotal >> kNumMoveReducingBits];

		static BitEncoder()
		{
			const int kNumBits = (kNumBitModelTotalBits - kNumMoveReducingBits);
			for (int i = kNumBits - 1; i >= 0; i--)
			{
				UInt32 start = (UInt32)1 << (kNumBits - i - 1);
				UInt32 end = (UInt32)1 << (kNumBits - i);
				for (UInt32 j = start; j < end; j++)
					ProbPrices[j] = ((UInt32)i << kNumBitPriceShiftBits) +
						(((end - j) << kNumBitPriceShiftBits) >> (kNumBits - i - 1));
			}
		}

		internal uint GetPrice(uint symbol)
		{
			return ProbPrices[(((Prob - symbol) ^ ((-(int)symbol))) & (kBitModelTotal - 1)) >> kNumMoveReducingBits];
		}
	  internal uint GetPrice0() { return ProbPrices[Prob >> kNumMoveReducingBits]; }
		internal uint GetPrice1() { return ProbPrices[(kBitModelTotal - Prob) >> kNumMoveReducingBits]; }
	}

	struct BitDecoder
	{
		internal const int kNumBitModelTotalBits = 11;
		internal const uint kBitModelTotal = (1 << kNumBitModelTotalBits);
		const int kNumMoveBits = 5;

		uint Prob;

		internal void UpdateModel(int numMoveBits, uint symbol)
		{
			if (symbol == 0)
				Prob += (kBitModelTotal - Prob) >> numMoveBits;
			else
				Prob -= (Prob) >> numMoveBits;
		}

		internal void Init() { Prob = kBitModelTotal >> 1; }

		internal uint Decode(RangeCoder.Decoder rangeDecoder)
		{
			uint newBound = (uint)(rangeDecoder.Range >> kNumBitModelTotalBits) * (uint)Prob;
			if (rangeDecoder.Code < newBound)
			{
				rangeDecoder.Range = newBound;
				Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
				if (rangeDecoder.Range < Decoder.kTopValue)
				{
					rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
					rangeDecoder.Range <<= 8;
				}
				return 0;
			}
			else
			{
				rangeDecoder.Range -= newBound;
				rangeDecoder.Code -= newBound;
				Prob -= (Prob) >> kNumMoveBits;
				if (rangeDecoder.Range < Decoder.kTopValue)
				{
					rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
					rangeDecoder.Range <<= 8;
				}
				return 1;
			}
		}
	}
}
