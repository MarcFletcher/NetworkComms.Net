// WARNING
//
// This file has been generated automatically by Xamarin Studio to store outlets and
// actions made in the Xcode designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using MonoTouch.Foundation;

namespace ExamplesChat.iOS
{
	[Register ("Settings")]
	partial class Settings
	{
		[Outlet]
		MonoTouch.UIKit.UITextField MasterIP { get; set; }

		[Outlet]
		MonoTouch.UIKit.UITextField MasterPort { get; set; }

		[Outlet]
		MonoTouch.UIKit.UITextField LocalName { get; set; }

		[Outlet]
		MonoTouch.UIKit.UISwitch TCPSwitch { get; set; }

		[Outlet]
		MonoTouch.UIKit.UISwitch UDPSwitch { get; set; }

		[Action ("ToggleConnectionType:")]
		partial void ToggleConnectionType (MonoTouch.Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (MasterIP != null) {
				MasterIP.Dispose ();
				MasterIP = null;
			}

			if (MasterPort != null) {
				MasterPort.Dispose ();
				MasterPort = null;
			}

			if (LocalName != null) {
				LocalName.Dispose ();
				LocalName = null;
			}

			if (TCPSwitch != null) {
				TCPSwitch.Dispose ();
				TCPSwitch = null;
			}

			if (UDPSwitch != null) {
				UDPSwitch.Dispose ();
				UDPSwitch = null;
			}
		}
	}
}
