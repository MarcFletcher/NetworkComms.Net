//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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

// LzmaBase.cs

namespace LZMA
{
	internal abstract class Base
	{
		internal const uint kNumRepDistances = 4;
		internal const uint kNumStates = 12;

		// static byte []kLiteralNextStates  = {0, 0, 0, 0, 1, 2, 3, 4,  5,  6,   4, 5};
		// static byte []kMatchNextStates    = {7, 7, 7, 7, 7, 7, 7, 10, 10, 10, 10, 10};
		// static byte []kRepNextStates      = {8, 8, 8, 8, 8, 8, 8, 11, 11, 11, 11, 11};
		// static byte []kShortRepNextStates = {9, 9, 9, 9, 9, 9, 9, 11, 11, 11, 11, 11};

		internal struct State
		{
			internal uint Index;
			internal void Init() { Index = 0; }
			internal void UpdateChar()
			{
				if (Index < 4) Index = 0;
				else if (Index < 10) Index -= 3;
				else Index -= 6;
			}
			internal void UpdateMatch() { Index = (uint)(Index < 7 ? 7 : 10); }
			internal void UpdateRep() { Index = (uint)(Index < 7 ? 8 : 11); }
			internal void UpdateShortRep() { Index = (uint)(Index < 7 ? 9 : 11); }
			internal bool IsCharState() { return Index < 7; }
		}

		internal const int kNumPosSlotBits = 6;
		internal const int kDicLogSizeMin = 0;
		// internal const int kDicLogSizeMax = 30;
		// internal const uint kDistTableSizeMax = kDicLogSizeMax * 2;

		internal const int kNumLenToPosStatesBits = 2; // it's for speed optimization
		internal const uint kNumLenToPosStates = 1 << kNumLenToPosStatesBits;

		internal const uint kMatchMinLen = 2;

		internal static uint GetLenToPosState(uint len)
		{
			len -= kMatchMinLen;
			if (len < kNumLenToPosStates)
				return len;
			return (uint)(kNumLenToPosStates - 1);
		}

		internal const int kNumAlignBits = 4;
		internal const uint kAlignTableSize = 1 << kNumAlignBits;
		internal const uint kAlignMask = (kAlignTableSize - 1);

		internal const uint kStartPosModelIndex = 4;
		internal const uint kEndPosModelIndex = 14;
		internal const uint kNumPosModels = kEndPosModelIndex - kStartPosModelIndex;

		internal const uint kNumFullDistances = 1 << ((int)kEndPosModelIndex / 2);

		internal const uint kNumLitPosStatesBitsEncodingMax = 4;
		internal const uint kNumLitContextBitsMax = 8;

		internal const int kNumPosStatesBitsMax = 4;
		internal const uint kNumPosStatesMax = (1 << kNumPosStatesBitsMax);
		internal const int kNumPosStatesBitsEncodingMax = 4;
		internal const uint kNumPosStatesEncodingMax = (1 << kNumPosStatesBitsEncodingMax);

		internal const int kNumLowLenBits = 3;
		internal const int kNumMidLenBits = 3;
		internal const int kNumHighLenBits = 8;
		internal const uint kNumLowLenSymbols = 1 << kNumLowLenBits;
		internal const uint kNumMidLenSymbols = 1 << kNumMidLenBits;
		internal const uint kNumLenSymbols = kNumLowLenSymbols + kNumMidLenSymbols +
				(1 << kNumHighLenBits);
		internal const uint kMatchMaxLen = kMatchMinLen + kNumLenSymbols - 1;
	}
}
