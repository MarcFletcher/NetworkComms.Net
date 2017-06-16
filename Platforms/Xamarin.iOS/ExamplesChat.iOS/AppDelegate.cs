using System;
using System.Collections.Generic;
using System.Linq;

using Foundation;
using UIKit;
using NetworkCommsDotNet;

namespace ExamplesChat.iOS
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the 
    // User Interface of the application, as well as listening (and optionally responding) to 
    // application events from iOS.
    [Register("AppDelegate")]
    public partial class AppDelegate : UIApplicationDelegate
    {
        // class-level declarations
        public override UIWindow Window
        {
            get;
            set;
        }

        /// <summary>
        /// If the application is minimised we need to correctly shutdown NetworkComms.Net
        /// </summary>
        /// <param name="application"></param>
        public override void DidEnterBackground(UIApplication application)
        {
            NetworkComms.Shutdown();
        }
    }
}