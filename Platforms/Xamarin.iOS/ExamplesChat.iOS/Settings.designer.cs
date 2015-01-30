// WARNING
//
// This file has been generated automatically by Xamarin Studio to store outlets and
// actions made in the Xcode designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;

namespace ExamplesChat.iOS
{
	[Register ("Settings")]
	partial class Settings
	{
		[Outlet]
		UIKit.UITextField MasterIP { get; set; }

		[Outlet]
		UIKit.UITextField MasterPort { get; set; }

		[Outlet]
		UIKit.UITextField LocalName { get; set; }

		[Outlet]
		UIKit.UISegmentedControl ConnectionMode { get; set; }

		[Outlet]
		UIKit.UISwitch EncryptionEnabled { get; set; }

		[Outlet]
		UIKit.UISwitch LocalServerEnabled { get; set; }
		
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

			if (ConnectionMode != null) {
				ConnectionMode.Dispose ();
				ConnectionMode = null;
			}

			if (EncryptionEnabled != null) {
				EncryptionEnabled.Dispose ();
				EncryptionEnabled = null;
			}

			if (LocalServerEnabled != null) {
				LocalServerEnabled.Dispose ();
				LocalServerEnabled = null;
			}
		}
	}
}
