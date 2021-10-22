using ES.SFTP.Configuration.Elements;
using MediatR;

namespace ES.SFTP.Messages.Configuration;

public class SftpConfigurationRequest : IRequest<SftpConfiguration>
{
}