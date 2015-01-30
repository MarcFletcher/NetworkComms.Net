// WARNING
//
// This file has been generated automatically by Xamarin Studio to store outlets and
// actions made in the Xcode designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;

namespace ExamplesChat.iOS
{
	[Register ("ChatWindow")]
	partial class ChatWindow
	{
		[Outlet]
		UIKit.UITextView ChatHistory { get; set; }

		[Outlet]
		UIKit.UITextField MessageBox { get; set; }

		[Outlet]
		UIKit.UIView ChatView { get; set; }

		[Outlet]
		UIKit.UIButton SendButton { get; set; }

		[Action ("SendButtonClick:")]
		partial void SendButtonClick (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ChatHistory != null) {
				ChatHistory.Dispose ();
				ChatHistory = null;
			}

			if (MessageBox != null) {
				MessageBox.Dispose ();
				MessageBox = null;
			}

			if (ChatView != null) {
				ChatView.Dispose ();
				ChatView = null;
			}

			if (SendButton != null) {
				SendButton.Dispose ();
				SendButton = null;
			}
		}
	}
}
