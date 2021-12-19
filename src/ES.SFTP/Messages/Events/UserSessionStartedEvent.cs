using MediatR;

namespace ES.SFTP.Messages.Events;

public class UserSessionChangedEvent : INotification
{
    public string Username { get; set; }
    public string SessionState { get; set; }
}