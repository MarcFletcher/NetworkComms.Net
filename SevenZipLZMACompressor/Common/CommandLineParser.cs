//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

// CommandLineParser.cs

using System;
using System.Collections;

namespace LZMA.CommandLineParser
{
	internal enum SwitchType
	{
		Simple,
		PostMinus,
		LimitedPostString,
		UnLimitedPostString,
		PostChar
	}

	internal class SwitchForm
	{
		internal string IDString;
		internal SwitchType Type;
		internal bool Multi;
		internal int MinLen;
		internal int MaxLen;
		internal string PostCharSet;

		internal SwitchForm(string idString, SwitchType type, bool multi,
			int minLen, int maxLen, string postCharSet)
		{
			IDString = idString;
			Type = type;
			Multi = multi;
			MinLen = minLen;
			MaxLen = maxLen;
			PostCharSet = postCharSet;
		}
		internal SwitchForm(string idString, SwitchType type, bool multi, int minLen):
			this(idString, type, multi, minLen, 0, "")
		{
		}
		internal SwitchForm(string idString, SwitchType type, bool multi):
			this(idString, type, multi, 0)
		{
		}
	}

	internal class SwitchResult
	{
		internal bool ThereIs;
		internal bool WithMinus;
		internal ArrayList PostStrings = new ArrayList();
		internal int PostCharIndex;
		internal SwitchResult()
		{
			ThereIs = false;
		}
	}

	internal class Parser
	{
		internal ArrayList NonSwitchStrings = new ArrayList();
		SwitchResult[] _switches;

		internal Parser(int numSwitches)
		{
			_switches = new SwitchResult[numSwitches];
			for (int i = 0; i < numSwitches; i++)
				_switches[i] = new SwitchResult();
		}

		bool ParseString(string srcString, SwitchForm[] switchForms)
		{
			int len = srcString.Length;
			if (len == 0)
				return false;
			int pos = 0;
			if (!IsItSwitchChar(srcString[pos]))
				return false;
			while (pos < len)
			{
				if (IsItSwitchChar(srcString[pos]))
					pos++;
				const int kNoLen = -1;
				int matchedSwitchIndex = 0;
				int maxLen = kNoLen;
				for (int switchIndex = 0; switchIndex < _switches.Length; switchIndex++)
				{
					int switchLen = switchForms[switchIndex].IDString.Length;
					if (switchLen <= maxLen || pos + switchLen > len)
						continue;
					if (String.Compare(switchForms[switchIndex].IDString, 0,
							srcString, pos, switchLen, true) == 0)
					{
						matchedSwitchIndex = switchIndex;
						maxLen = switchLen;
					}
				}
				if (maxLen == kNoLen)
					throw new Exception("maxLen == kNoLen");
				SwitchResult matchedSwitch = _switches[matchedSwitchIndex];
				SwitchForm switchForm = switchForms[matchedSwitchIndex];
				if ((!switchForm.Multi) && matchedSwitch.ThereIs)
					throw new Exception("switch must be single");
				matchedSwitch.ThereIs = true;
				pos += maxLen;
				int tailSize = len - pos;
				SwitchType type = switchForm.Type;
				switch (type)
				{
					case SwitchType.PostMinus:
						{
							if (tailSize == 0)
								matchedSwitch.WithMinus = false;
							else
							{
								matchedSwitch.WithMinus = (srcString[pos] == kSwitchMinus);
								if (matchedSwitch.WithMinus)
									pos++;
							}
							break;
						}
					case SwitchType.PostChar:
						{
							if (tailSize < switchForm.MinLen)
								throw new Exception("switch is not full");
							string charSet = switchForm.PostCharSet;
							const int kEmptyCharValue = -1;
							if (tailSize == 0)
								matchedSwitch.PostCharIndex = kEmptyCharValue;
							else
							{
								int index = charSet.IndexOf(srcString[pos]);
								if (index < 0)
									matchedSwitch.PostCharIndex = kEmptyCharValue;
								else
								{
									matchedSwitch.PostCharIndex = index;
									pos++;
								}
							}
							break;
						}
					case SwitchType.LimitedPostString:
					case SwitchType.UnLimitedPostString:
						{
							int minLen = switchForm.MinLen;
							if (tailSize < minLen)
								throw new Exception("switch is not full");
							if (type == SwitchType.UnLimitedPostString)
							{
								matchedSwitch.PostStrings.Add(srcString.Substring(pos));
								return true;
							}
							String stringSwitch = srcString.Substring(pos, minLen);
							pos += minLen;
							for (int i = minLen; i < switchForm.MaxLen && pos < len; i++, pos++)
							{
								char c = srcString[pos];
								if (IsItSwitchChar(c))
									break;
								stringSwitch += c;
							}
							matchedSwitch.PostStrings.Add(stringSwitch);
							break;
						}
				}
			}
			return true;

		}

		internal void ParseStrings(SwitchForm[] switchForms, string[] commandStrings)
		{
			int numCommandStrings = commandStrings.Length;
			bool stopSwitch = false;
			for (int i = 0; i < numCommandStrings; i++)
			{
				string s = commandStrings[i];
				if (stopSwitch)
					NonSwitchStrings.Add(s);
				else
					if (s == kStopSwitchParsing)
					stopSwitch = true;
				else
					if (!ParseString(s, switchForms))
					NonSwitchStrings.Add(s);
			}
		}

		internal SwitchResult this[int index] { get { return _switches[index]; } }

		internal static int ParseCommand(CommandForm[] commandForms, string commandString,
			out string postString)
		{
			for (int i = 0; i < commandForms.Length; i++)
			{
				string id = commandForms[i].IDString;
				if (commandForms[i].PostStringMode)
				{
					if (commandString.IndexOf(id) == 0)
					{
						postString = commandString.Substring(id.Length);
						return i;
					}
				}
				else
					if (commandString == id)
				{
					postString = "";
					return i;
				}
			}
			postString = "";
			return -1;
		}

		static bool ParseSubCharsCommand(int numForms, CommandSubCharsSet[] forms,
			string commandString, ArrayList indices)
		{
			indices.Clear();
			int numUsedChars = 0;
			for (int i = 0; i < numForms; i++)
			{
				CommandSubCharsSet charsSet = forms[i];
				int currentIndex = -1;
				int len = charsSet.Chars.Length;
				for (int j = 0; j < len; j++)
				{
					char c = charsSet.Chars[j];
					int newIndex = commandString.IndexOf(c);
					if (newIndex >= 0)
					{
						if (currentIndex >= 0)
							return false;
						if (commandString.IndexOf(c, newIndex + 1) >= 0)
							return false;
						currentIndex = j;
						numUsedChars++;
					}
				}
				if (currentIndex == -1 && !charsSet.EmptyAllowed)
					return false;
				indices.Add(currentIndex);
			}
			return (numUsedChars == commandString.Length);
		}
		const char kSwitchID1 = '-';
		const char kSwitchID2 = '/';

		const char kSwitchMinus = '-';
		const string kStopSwitchParsing = "--";

		static bool IsItSwitchChar(char c)
		{
			return (c == kSwitchID1 || c == kSwitchID2);
		}
	}

	internal class CommandForm
	{
		internal string IDString = "";
		internal bool PostStringMode = false;
		internal CommandForm(string idString, bool postStringMode)
		{
			IDString = idString;
			PostStringMode = postStringMode;
		}
	}

	class CommandSubCharsSet
	{
		internal string Chars = "";
		internal bool EmptyAllowed = false;
	}
}
