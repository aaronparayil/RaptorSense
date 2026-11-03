using RaptorSense.Helper;

// RaptorSense.Helper.exe              run (as the Windows service, or in a console for testing)
// RaptorSense.Helper.exe install      install and start the service (administrator)
// RaptorSense.Helper.exe uninstall    stop and remove the service (administrator)
switch (args.FirstOrDefault()?.ToLowerInvariant())
{
    case "install": return ServiceInstaller.Install();
    case "uninstall": return ServiceInstaller.Uninstall();
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = ServiceInstaller.ServiceName);
builder.Services.AddHostedService<ThermalService>();
builder.Build().Run();
return 0;
