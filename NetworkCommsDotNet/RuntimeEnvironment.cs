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
