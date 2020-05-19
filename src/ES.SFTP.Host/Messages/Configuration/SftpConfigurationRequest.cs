using ES.SFTP.Host.Configuration.Elements;
using MediatR;

namespace ES.SFTP.Host.Messages.Configuration
{
    public class SftpConfigurationRequest : IRequest<SftpConfiguration>
    {
    }
}