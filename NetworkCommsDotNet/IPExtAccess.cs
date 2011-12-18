using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// External dll access. This will fail on a non windows system.
    /// </summary>
    class IPExtAccess
    {
        [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
        public static extern int GetBestInterface(UInt32 DestAddr, out UInt32 BestIfIndex);
    }
}
