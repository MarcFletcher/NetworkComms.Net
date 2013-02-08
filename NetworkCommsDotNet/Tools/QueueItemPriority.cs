using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet
{
#if WINDOWS_PHONE
    public enum QueueItemPriority
    {
        // Summary:
        //     The work item should run at low priority.
        Low = -1,
        //
        // Summary:
        //     The work item should run at normal priority. This is the default value.
        Normal = 0,
        //
        // Summary:
        //     The work item should run at high priority.
        High = 1,
    }
#else
    public enum QueueItemPriority
    {
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with any other
        //     priority.
        Lowest = 0,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with Normal priority
        //     and before those with Lowest priority.
        BelowNormal = 1,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with AboveNormal
        //     priority and before those with BelowNormal priority. Threads have Normal
        //     priority by default.
        Normal = 2,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with Highest priority
        //     and before those with Normal priority.
        AboveNormal = 3,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled before threads with any other
        //     priority.
        Highest = 4,
    }
#endif

}
