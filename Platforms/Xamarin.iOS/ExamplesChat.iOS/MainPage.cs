
using System;
using System.Drawing;

using MonoTouch.Foundation;
using MonoTouch.UIKit;

using NetworkCommsDotNet;
using DPSBase;
using SevenZipLZMACompressor;
using ProtoBuf;

namespace ExamplesChat.iOS
{
	public partial class MainPage : UIViewController
	{
        UIButton button;
        int numClicks = 0;
        float buttonWidth = 200;
        float buttonHeight = 50;

		public MainPage () : base ("MainPage", null)
		{
		}
		
		public override void DidReceiveMemoryWarning ()
		{
			// Releases the view if it doesn't have a superview.
			base.DidReceiveMemoryWarning ();
			
			// Release any cached data, images, etc that aren't in use.
		}
		
		public override void ViewDidLoad ()
		{
			base.ViewDidLoad ();

            View.Frame = UIScreen.MainScreen.Bounds;
            View.BackgroundColor = UIColor.White;
            View.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;

            button = UIButton.FromType(UIButtonType.RoundedRect);

            button.Frame = new RectangleF(
                View.Frame.Width / 2 - buttonWidth / 2,
                View.Frame.Height - View.Frame.Height / 4 - buttonHeight / 2,
                buttonWidth,
                buttonHeight);

            button.SetTitle("Click me", UIControlState.Normal);

            ChatHistory.Text = "Initialising chat ..." + Environment.NewLine + "Identifier: " + NetworkComms.NetworkIdentifier + Environment.NewLine + Environment.NewLine;
            button.TouchUpInside += (object sender, EventArgs e) =>
            {
                button.SetTitle(String.Format("clicked {0} times", numClicks++), UIControlState.Normal);

                TCPConnection.StartListening(true);
                foreach (System.Net.IPEndPoint localEndPoint in TCPConnection.ExistingLocalListenEndPoints())
                    ChatHistory.Text += localEndPoint.Address + ":" + localEndPoint.Port + Environment.NewLine;

                try
                {
                    NetworkComms.SendObject("Message", "192.168.0.104", 10000, "hello from iphone!!");
                    ChatHistory.Text += "Test send success!.";
                }
                catch (Exception)
                {
                    ChatHistory.Text += "Test send failed!.";
                }
            };

            button.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleTopMargin |
                UIViewAutoresizing.FlexibleBottomMargin;

            View.AddSubview(button);
		}
	}
}

