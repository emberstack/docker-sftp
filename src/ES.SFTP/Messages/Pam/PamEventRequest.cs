﻿using MediatR;

namespace ES.SFTP.Messages.Pam;

public class PamEventRequest : IRequest<bool>
{
    public string Username { get; set; }
    public string EventType { get; set; }
    public string Service { get; set; }
}