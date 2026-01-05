namespace ReadyDOS.Example.Integration.Models {
    /// <summary>
    /// Represents an individual recipient with contact and segmentation information.
    /// </summary>
    /// <param name="Email">The email address of the recipient. Cannot be null or empty.</param>
    /// <param name="CustomerId">The unique identifier for the customer. Must be a non-negative integer.</param>
    /// <param name="FirstName">The first name of the recipient. May be null or empty if not provided.</param>
    /// <param name="LastName">The last name of the recipient. May be null or empty if not provided.</param>
    /// <param name="Segment">The segment or group to which the recipient belongs. May be null or empty if not assigned.</param>
    public sealed record Recipient(
        string Email,
        int CustomerId,
        string FirstName,
        string LastName,
        string Segment
    );
}
