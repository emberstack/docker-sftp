using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using ES.SFTP.Configuration;
using ES.SFTP.Configuration.Elements;
using ES.SFTP.Security;
using ES.SFTP.SSH;
using MediatR;
using MediatR.Pipeline;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("app.logging.json")
        .AddEnvironmentVariables(nameof(ES))
        .AddCommandLine(args)
        .Build())
    .CreateLogger();


try
{
    Log.Information("Starting host");

    var builder = WebApplication.CreateBuilder(args);
    builder.Environment.EnvironmentName =
        Environment.GetEnvironmentVariable($"{nameof(ES)}_{nameof(Environment)}") ??
        Environments.Production;

    builder.Configuration.AddJsonFile("app.logging.json", false, false);
    builder.Configuration.AddJsonFile("config/sftp.json", false, true);
    builder.Configuration.AddEnvironmentVariables(nameof(ES));
    builder.Configuration.AddCommandLine(args);

    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
    builder.Host.UseSerilog((hostingContext, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(hostingContext.Configuration)
        .Enrich.FromLogContext(), true);
    builder.Host.UseConsoleLifetime();


    builder.Services.AddHttpClient();
    builder.Services.AddOptions();
    builder.Services.AddHealthChecks();
    builder.Services.AddMediatR(typeof(void).Assembly);
    builder.Services.AddControllers();

    builder.Services.Configure<SftpConfiguration>(builder.Configuration);

    builder.Host.ConfigureContainer((ContainerBuilder container) =>
    {
        container.RegisterAssemblyTypes(typeof(IMediator).GetTypeInfo().Assembly).AsImplementedInterfaces();
        container.RegisterGeneric(typeof(RequestPostProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
        container.RegisterGeneric(typeof(RequestPreProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
        container.Register<ServiceFactory>(ctx =>
        {
            var c = ctx.Resolve<IComponentContext>();
            return t => c.Resolve(t);
        });


        container.RegisterType<SessionHandler>().AsImplementedInterfaces().SingleInstance();
        container.RegisterType<HookRunner>().AsImplementedInterfaces().SingleInstance();
        container.RegisterType<ConfigurationService>().AsImplementedInterfaces().SingleInstance();
        container.RegisterType<AuthenticationService>().AsImplementedInterfaces().SingleInstance();
        container.RegisterType<UserManagementService>().AsImplementedInterfaces().SingleInstance();
        container.RegisterType<SSHService>().AsImplementedInterfaces().SingleInstance();
    });

    builder.WebHost.UseUrls("http://*:25080");


    var app = builder.Build();

    if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthorization();
    app.UseEndpoints(endpoints => { endpoints.MapControllers(); });


    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}