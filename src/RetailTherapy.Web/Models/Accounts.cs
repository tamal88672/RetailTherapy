using Google.Cloud.Firestore;

namespace RetailTherapy.Web.Models;

/// <summary>
/// One signed-in person. The id is the sign-in provider's user id, never an email or phone number, so one account can
/// keep working if the email changes. No email, name or photo is stored here: Firebase holds the sign-in details.
/// </summary>
[FirestoreData]
public sealed class UserProfile : IHasId
{
    [FirestoreProperty] public string Id { get; set; } = "";
    /// <summary>Chosen once, lowercase, never changed. Null until the person picks one.</summary>
    [FirestoreProperty] public string? Username { get; set; }
    /// <summary>"google.com" or "password" (email link).</summary>
    [FirestoreProperty] public string Provider { get; set; } = "";
    [FirestoreProperty] public string? CreatedOn { get; set; }
    [FirestoreProperty] public string? LastSeenOn { get; set; }
}

/// <summary>
/// Reserves a username. The document id IS the lowercase username, so two people can never hold the same one:
/// creating it inside a transaction fails if it already exists.
/// </summary>
[FirestoreData]
public sealed class UsernameClaim : IHasId
{
    [FirestoreProperty] public string Id { get; set; } = "";
    /// <summary>Owner's user id, or "deleted" after the account was removed (the name stays reserved).</summary>
    [FirestoreProperty] public string Uid { get; set; } = "";
    [FirestoreProperty] public string? CreatedOn { get; set; }
}

/// <summary>A verified sign-in token.</summary>
public sealed record SessionUser(string Uid, string Provider, string? Email, bool EmailVerified);

public enum ClaimResult { Ok, Taken, AlreadyHasUsername, NoUser }

public sealed record UsernameBody(string? Name);
public sealed record ClaimBody(string? VisitorId, string[]? ProductIds);
public sealed record WishBody(string[]? ProductIds);
