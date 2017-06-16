using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Preferences;

namespace ExamplesChat.Android
{
    [Activity(Label = "My Activity")]
    public class NetworkCommsSettings : PreferenceActivity
    {
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            AddPreferencesFromResource(Resource.Menu.settings);
            var pref = (EditTextPreference) PreferenceScreen.FindPreference("prefIPAddress");

            pref.PreferenceChange += (obj, args) =>
                {
                    bool result = true;
                    System.Net.IPAddress address;

                    if ((string)args.NewValue != String.Empty && !System.Net.IPAddress.TryParse((string)args.NewValue, out address))
                    {
                        AlertDialog.Builder builder = new AlertDialog.Builder(ApplicationContext);
                        builder.SetTitle("Invalid IP Address");
                        builder.SetMessage("The entered IP address was not valid");
                        builder.SetPositiveButton("OK", (o, e) => { });
                        builder.Show();
                        result = false;
                    }

                    args.Handled = result;
                };

            pref = (EditTextPreference)PreferenceScreen.FindPreference("prefPort");

            pref.PreferenceChange += (obj, args) =>
                {
                    bool result = true;
                    ushort port;

                    if ((string)args.NewValue != String.Empty  && !ushort.TryParse((string)args.NewValue, out port))
                    {
                        AlertDialog.Builder builder = new AlertDialog.Builder(ApplicationContext);
                        builder.SetTitle("Invalid Port");
                        builder.SetMessage("The entered port was not valid");
                        builder.SetPositiveButton("OK", (o, e) => { });
                        builder.Show();
                        result = false;
                    }

                    args.Handled = result;
                };        
        }
    }
}