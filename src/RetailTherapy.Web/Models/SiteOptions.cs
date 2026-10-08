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
