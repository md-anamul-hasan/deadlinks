using System;
using System.Windows;
using System.Windows.Interop;
using DeadLinks.Services;

namespace DeadLinks;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Mdah.Windowing.MaximizeFix.Attach(this);
        Mdah.Windowing.ResponsiveScale.Attach(this, Body);
        // "DeadLinks.exe <folder>" scans just that folder
        Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && DataContext is ViewModels.MainViewModel vm) vm.ScanFolder(args[1]);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.EnableWindows11DarkTheme(new WindowInteropHelper(this).Handle);
    }
}
