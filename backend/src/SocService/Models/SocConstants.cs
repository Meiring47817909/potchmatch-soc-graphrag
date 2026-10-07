namespace SocService.Models;

public static class SocConstants
{
    public static class Severity
    {
        public const string Low = "LOW";
        public const string Medium = "MEDIUM";
        public const string High = "HIGH";
    }

    public static class Status
    {
        public const string Open = "OPEN";
        public const string Acknowledged = "ACKNOWLEDGED";
        public const string Resolved = "RESOLVED";
    }

    public static class Rules
    {
        public const string FailedLogins = "Multiple Failed Logins";
        public const string UnauthorisedAccess = "Unauthorised Endpoint Access";
        public const string RequestRate = "POSSIBLE_DOS";
        public const string ServiceFailure = "SERVICE_FAILURE";
    }
}
