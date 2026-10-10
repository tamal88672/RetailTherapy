namespace RetailTherapy.Web.Models;

/// <summary>Bound from the "Site" config section. Override with env vars, e.g. Site__AmazonTag.</summary>
public sealed class SiteOptions
{
    public string Name { get; set; } = "RetailTherapy.com";
    public string Tagline { get; set; } = "";
    public string AmazonTag { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Disclosure { get; set; } = "As an Amazon Associate I earn from qualifying purchases.";
    /// <summary>Folder under wwwroot served at "/": v1-magazine, v2-masonry or v3-compact.</summary>
    public string LiveDesign { get; set; } = "v2-masonry";
    /// <summary>What the curated-lists page is called on the site (it lives at /sessions). Rename it here, nothing else changes.</summary>
    public string ListsName { get; set; } = "Therapy Sessions";
    public string ListsTagline { get; set; } = "Hand-picked shelves, one mood at a time.";
    public AdOptions Ads { get; set; } = new();
}

public sealed class AdOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>AdSense publisher id. While it contains "XXXX", placeholder boxes show instead of ads.</summary>
    public string Client { get; set; } = "";
    public string SlotSidebar { get; set; } = "";
    public string SlotInline { get; set; } = "";
}

/// <summary>
/// Sign-in settings ("Auth" section, env vars like Auth__ProjectId). Sign-in is Firebase Authentication with Google and
/// email-link only. The accounts feature stays off (no buttons on the site) until ProjectId and ApiKey are set.
/// All of these are public web settings, not secrets.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Firebase project id (the same as the Google Cloud project id).</summary>
    public string ProjectId { get; set; } = "";
    /// <summary>Firebase web API key (Firebase console, project settings, web app). Public by design.</summary>
    public string ApiKey { get; set; } = "";
    public string AppId { get; set; } = "";
    /// <summary>Defaults to {ProjectId}.firebaseapp.com.</summary>
    public string AuthDomain { get; set; } = "";
    /// <summary>Where Google publishes the keys that sign Firebase tokens. Only changed for tests.</summary>
    public string CertsUrl { get; set; } = "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";
    /// <summary>Extra words usernames may not contain, comma separated (added to the built-in list).</summary>
    public string BlockedNames { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ApiKey);
    public string Domain => string.IsNullOrWhiteSpace(AuthDomain) ? ProjectId + ".firebaseapp.com" : AuthDomain;
}
