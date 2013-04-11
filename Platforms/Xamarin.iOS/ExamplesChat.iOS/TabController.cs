using System;
using System.Drawing;
using MonoTouch.UIKit;

namespace ExamplesChat.iOS
{
    public class TabController : UITabBarController
    {
        UIViewController tab1, tab2;

        //UIButton button;
        //int numClicks = 0;
        //float buttonWidth = 200;
        //float buttonHeight = 50;

        public TabController()
        {
            tab1 = new MainPage();
            tab1.Title = "Chat";

            tab2 = new SettingsPage();
            tab2.Title = "Settings";

            var tabs = new UIViewController[] { tab1, tab2 };

            ViewControllers = tabs;
        }
    }
}