namespace DotaDraft.Api.Integrations.OpenDota;

public class OpenDotaException(string message, Exception? innerException = null) : Exception(message, innerException);
