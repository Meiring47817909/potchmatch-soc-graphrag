namespace SocService.Detection;

public class DetectionOptions
{
    public const string SectionName = "DetectionRules";

    public FailedLoginsOptions FailedLogins { get; set; } = new();
    public UnauthorisedAccessOptions UnauthorisedAccess { get; set; } = new();
    public RequestRateOptions RequestRate { get; set; } = new();
    public ServiceFailureOptions ServiceFailure { get; set; } = new();
    public int EngineIntervalSeconds { get; set; } = 15;

    public class FailedLoginsOptions
    {
        public int Threshold { get; set; } = 5;
        public int WindowMinutes { get; set; } = 5;
    }

    public class UnauthorisedAccessOptions
    {
        public int Threshold { get; set; } = 3;
        public int WindowMinutes { get; set; } = 5;
    }

    public class RequestRateOptions
    {
        public int Threshold { get; set; } = 100;
        public int WindowSeconds { get; set; } = 60;
    }

    public class ServiceFailureOptions
    {
        public int Threshold { get; set; } = 3;
        public int WindowMinutes { get; set; } = 2;
    }
}
