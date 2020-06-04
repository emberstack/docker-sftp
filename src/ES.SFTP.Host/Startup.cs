using System.Reflection;
using Autofac;
using ES.SFTP.Host.Configuration;
using ES.SFTP.Host.Configuration.Elements;
using ES.SFTP.Host.Security;
using ES.SFTP.Host.SSH;
using MediatR;
using MediatR.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ES.SFTP.Host
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
            services.AddControllers();
            services.Configure<SftpConfiguration>(Configuration);
        }

        // ReSharper disable once UnusedMember.Global
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterAssemblyTypes(typeof(IMediator).GetTypeInfo().Assembly).AsImplementedInterfaces();
            builder.RegisterGeneric(typeof(RequestPostProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
            builder.RegisterGeneric(typeof(RequestPreProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
            builder.Register<ServiceFactory>(ctx =>
            {
                var c = ctx.Resolve<IComponentContext>();
                return t => c.Resolve(t);
            });


            builder.RegisterType<SessionHandler>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<HookRunner>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<ConfigurationService>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<AuthenticationService>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<UserManagementService>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<SSHService>().AsImplementedInterfaces().SingleInstance();
        }

        // ReSharper disable once UnusedMember.Global
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment()) app.UseDeveloperExceptionPage();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}