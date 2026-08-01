namespace Atlas.Edge.Security;

public enum CredentialLifecycleState
{
    Unenrolled = 0,
    Active = 1,
    Refreshing = 2,
    AuthenticationRequired = 3
}
