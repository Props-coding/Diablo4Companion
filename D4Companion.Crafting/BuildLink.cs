namespace D4Companion.Crafting
{
    public enum BuildSite
    {
        None,
        Maxroll,
        Mobalytics,
        D4Builds,
        D2Core,
        InfinityBuilds
    }

    /// <summary>
    /// What a pasted build link points to. <see cref="Key"/> is what the site's downloader expects
    /// (an id or the full link). <see cref="Problem"/> explains, in plain words, why a link can't be used.
    /// </summary>
    public sealed record BuildLinkInfo(BuildSite Site, string Key, string Problem)
    {
        public bool IsValid => Site != BuildSite.None && Key.Length > 0 && Problem.Length == 0;
    }

    /// <summary>
    /// Works out which guide site a pasted link belongs to. Mirrors the rules of the original import dialog.
    /// </summary>
    public static class BuildLink
    {
        public static string SiteName(BuildSite site) => site switch
        {
            BuildSite.Maxroll => "Maxroll planner",
            BuildSite.Mobalytics => "Mobalytics",
            BuildSite.D4Builds => "D4Builds",
            BuildSite.D2Core => "D2Core",
            BuildSite.InfinityBuilds => "Infinity Builds",
            _ => string.Empty
        };

        public static BuildLinkInfo Detect(string? link)
        {
            string text = (link ?? string.Empty).Trim();
            if (text.Length == 0) return new(BuildSite.None, string.Empty, string.Empty);

            if (Has(text, "maxroll.gg")) return Maxroll(text);
            if (Has(text, "mobalytics.gg")) return Mobalytics(text);
            if (Has(text, "d4builds.gg")) return D4Builds(text);
            if (Has(text, "d2core.com")) return D2Core(text);
            if (Has(text, "infinitybuilds.gg")) return new(BuildSite.InfinityBuilds, text, string.Empty);

            return new(BuildSite.None, string.Empty,
                "This doesn't look like a link from Maxroll, Mobalytics, D4Builds, D2Core or Infinity Builds.");
        }

        private static BuildLinkInfo Maxroll(string text)
        {
            if (!Has(text, "/planner/"))
            {
                return new(BuildSite.Maxroll, string.Empty,
                    "This is a Maxroll guide page. Open the guide's planner and paste that link instead (maxroll.gg/d4/planner/...).");
            }

            string id = text.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            id = Before(Before(id, '#'), '?');
            return id.Length == 8
                ? new(BuildSite.Maxroll, id, string.Empty)
                : new(BuildSite.Maxroll, string.Empty, "This Maxroll planner link looks incomplete. Copy it again from the address bar.");
        }

        private static BuildLinkInfo Mobalytics(string text)
        {
            if (!text.StartsWith("https://mobalytics.gg", StringComparison.OrdinalIgnoreCase))
            {
                return new(BuildSite.Mobalytics, string.Empty, "Paste the full Mobalytics link, starting with https://mobalytics.gg.");
            }

            bool profile = Has(text, "profile");
            bool builds = Has(text, "builds");
            if (profile && builds) return new(BuildSite.Mobalytics, Before(text, '?'), string.Empty);
            if (builds && !text.TrimEnd('/').EndsWith("builds", StringComparison.OrdinalIgnoreCase)) return new(BuildSite.Mobalytics, text, string.Empty);

            return new(BuildSite.Mobalytics, string.Empty,
                "This is a Mobalytics profile or list page. Open the build itself and paste that link instead.");
        }

        private static BuildLinkInfo D4Builds(string text)
        {
            var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string container = parts.MaxBy(p => p.Length) ?? string.Empty;
            string id = container.Split('?', StringSplitOptions.RemoveEmptyEntries).MaxBy(p => p.Length) ?? string.Empty;

            bool valid = id.Length == 36 || (id.Contains('-') && !id.Contains('.'));
            return valid
                ? new(BuildSite.D4Builds, id, string.Empty)
                : new(BuildSite.D4Builds, string.Empty, "This D4Builds link doesn't point to a build. Open the build and copy its link.");
        }

        private static BuildLinkInfo D2Core(string text)
        {
            int index = text.IndexOf("bd=", StringComparison.OrdinalIgnoreCase);
            string id = index < 0 ? string.Empty : Before(text[(index + 3)..], '&');
            return id.Length == 4
                ? new(BuildSite.D2Core, id, string.Empty)
                : new(BuildSite.D2Core, string.Empty, "This D2Core link doesn't include a build code (bd=...). Open the build and copy its link.");
        }

        private static bool Has(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

        private static string Before(string text, char separator)
        {
            int index = text.IndexOf(separator);
            return index < 0 ? text : text[..index];
        }
    }
}
