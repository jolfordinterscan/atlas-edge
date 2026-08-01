namespace Atlas.Edge.Enrollment;

public interface IEnrollmentClient
{
    Task<EnrollmentResult> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken);
}
