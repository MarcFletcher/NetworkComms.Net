//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DistributedFileSystem
{
    /// <summary>
    /// Describes where a distributed item should be stored during and after being assembled
    /// </summary>
    public enum DataBuildMode
    {
        /// <summary>
        /// Build the item to a single continuous memory stream. Requires sufficient memory during build.
        /// </summary>
        Memory_Single,

        /// <summary>
        /// Build the item to an array of memory streams. More high performance as reduces locking during build. Requires sufficient memory during build.
        /// </summary>
        Memory_Blocks,

        /// <summary>
        /// Build the item to the local application directory as a single file stream.
        /// </summary>
        Disk_Single,

        /// <summary>
        /// Build the item to the local application directory as an array of file streams. More high performance as reduces locking.
        /// </summary>
        Disk_Blocks,
    }
}
