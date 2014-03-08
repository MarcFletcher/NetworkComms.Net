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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Specifies the current runtime environment. Used for changing minor settings based on environment.
    /// </summary>
    public enum RuntimeEnvironment
    {
        /// <summary>
        /// Native .Net 4.0 - Default
        /// </summary>
        Native_Net4,

        /// <summary>
        /// Mono .Net 4.0
        /// </summary>
        Mono_Net4,

        /// <summary>
        /// Native .Net3.5
        /// </summary>
        Native_Net35,

        /// <summary>
        /// Mono .Net 3.5
        /// </summary>
        Mono_Net35,

        /// <summary>
        /// Native .Net 2
        /// </summary>
        Native_Net2,

        /// <summary>
        /// Mono .Net 2
        /// </summary>
        Mono_Net2,

        /// <summary>
        /// Windows Phone 7.1 (8) or Silverlight
        /// </summary>
        WindowsPhone_Silverlight,

        /// <summary>
        /// Xamarin.Android
        /// </summary>
        Xamarin_Android,

        /// <summary>
        /// Xamarin.iOS
        /// </summary>
        Xamarin_iOS,

        /// <summary>
        /// Windows RT or Windows Store 
        /// </summary>
        Windows_RT,
    }
}
