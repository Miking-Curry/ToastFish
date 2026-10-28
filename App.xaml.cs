using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ToastFish.ViewModel;

namespace ToastFish
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length == 2 && e.Args[0] == "--authorize-local")
            {
                StartupUri = null;
                Shutdown(Model.Management.ManagementServer.Authorize(e.Args[1]));
                return;
            }
            base.OnStartup(e);
        }
    }
}
