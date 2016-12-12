using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions; 

internal class TfsEx
{
    // Connect to TFS(2012) prior to using the API.
    internal static TfsTeamProjectCollection GetTfsCollection()
    {
        string tfsUrl = ConfigurationManager.AppSettings["Tfs.Url"];
        string tfsDomain = ConfigurationManager.AppSettings["Tfs.Domain"];
        string username = ConfigurationManager.AppSettings["Tfs.Username"];
        string password = ConfigurationManager.AppSettings["Tfs.Password"];
    
        var credential = new System.Net.NetworkCredential(username, password, tfsDomain);
        var server = new TfsTeamProjectCollection(new Uri(tfsUrl), credential);
        server.Authenticate();
        return server;
    }
    
    // Return Changesets from a given date to now for a TFS path.
    // tfsPath: TFS server path like "$/"
    // from: DateTime from when to query TFS API. Unsure if consideration for UTC is required.
    internal static IEnumerable<Changeset> GetHistory(TraceWriter log, string tfsPath, DateTime from)
    {
        log.Info($" * Running report from {from.ToString("yyyy-MMM-dd")}");
    
        if (from < DateTime.Now.Subtract(TimeSpan.FromDays(91)))
        {
            log.Info($"Unable to search from {from} as it's too long ago. Too much data");
            return Enumerable.Empty<Changeset>();
        }
            
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    
        // Can retrieve SOAP service from TfsTeamProjectCollection instance
        using (TfsTeamProjectCollection tpc = GetTfsCollection())
        {
            // Can retrieve SOAP service from TfsTeamProjectCollection instance
            VersionControlServer source = tpc.GetService<VersionControlServer>();
            var history = source.QueryHistory(
                            tfsPath, VersionSpec.Latest, 0, RecursionType.Full,
                            null, new DateVersionSpec(from), null, int.MaxValue,
                            includeChanges: false,
                            slotMode: false, includeDownloadInfo: false, sortAscending: false)
                            .OfType<Changeset>().Reverse().ToList();
                            
            log.Info($"TFS history query took: {stopwatch.ElapsedMilliseconds} ms for {history.Count()} items.");
                            
            return history;
        }
    }
    
    // Search the changeset comments based on a search term. Case-insensitive.
    // history: A list of changesets to search in.
    // term: String to search for in comment.
    internal static IEnumerable<Changeset> SearchHistory(TraceWriter log, IEnumerable<Changeset> history, string term)
    {
        log.Info($"Searching term '{term}' (case insensitive) in {history.Count()} items.");
    
        foreach (var h in history)
        {
            if (!string.IsNullOrWhiteSpace(term))
            {
                if (h.Comment.IndexOf(term, StringComparison.OrdinalIgnoreCase) > -1)
                {
                    yield return h;
                }
            }
            else 
            {
                yield return h;
            }
        }
    }
    
    // Search the changeset comments based on a Committer username or displayname. Case-insensitive.
    // history: A list of changesets to search in.
    // term: String to search for in Committer or CommitterDisplayName.
    internal static IEnumerable<Changeset> SearchHistoryByUser(TraceWriter log, IEnumerable<Changeset> history, IEnumerable<string> users)
    {
        log.Info($"Searching users '{string.Join(", ", users.Select(u => u))}' (case insensitive) in {history.Count()} items.");
    
        foreach (var h in history)
        {
            if (users.Any())
            {
                foreach (var user in users)
                {
                    if (h.Committer.IndexOf(user, StringComparison.OrdinalIgnoreCase) > -1 || h.CommitterDisplayName.IndexOf(user, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        yield return h;
                    }
                }
            }
            else
            {
                yield return h;
            }
        }
    }
    
    // Given a list of Changesets return a list that are not peer-reviewed. 
    // Note: We use a string like "**<initials>" in the TFS commit comment. This code does not search WorkItem links.
    internal static IEnumerable<string> NotReviewed(TraceWriter log, IEnumerable<Changeset> history)
    {
        log.Info($"{history.Count()} changesets");
    
        // Provide a list of all the jira IDs that appear in the changeset comments.
        var tickets = history
            .Where(cs => !ChangesetEx.IsReviewed(cs))
            .Select(cs => $"CS:{cs.ChangesetId} by {cs.Committer} ({cs.CreationDate.ToString("dd-MMM")})");
    
        return tickets;
    }
    
    // Given a list of Changesets return a list that do not contain a Jira like ID "Project-XXX". 
    // Note: We use a string like "Jira-1234" in the TFS commit comment.
    // history: A list of changesets to search in.
    // ignoreJiraProjects: Ignore Jira IDs that start with/appear in this list.
    internal static IEnumerable<string> GetMissingJiraIds(TraceWriter log, IEnumerable<Changeset> history, IEnumerable<string> ignoreJiraProjects)
    {
        log.Info($"{history.Count()} changesets");
    
        // We also want to know about changesets that are missing a Jira ID.
        var missingJiraIds = new List<string>();
        foreach (var cs in history)
        {
            var jiraIds = JiraEx.JiraIds(cs.Comment);
            if (!jiraIds.Any() && cs.Comment.IndexOf("merg", StringComparison.OrdinalIgnoreCase) == -1)
            {
                missingJiraIds.Add($"CS:{cs.ChangesetId} by {cs.Committer} ({cs.CreationDate.ToString("dd-MMM")})");
            }
        }
    
        log.Info($"Missing jira IDs: {string.Join(", ", missingJiraIds.Select(t => t))}");
    
        return missingJiraIds;
    }
    
    // Given a list of Changesets return a list of Jira IDs in the commit comment "Project-XXX". 
    // Note: We use a string like "Jira-1234" in the TFS commit comment.
    // history: A list of changesets to search in.
    // ignoreJiraProjects: Ignore Jira IDs that start with/appear in this list.
    internal static IEnumerable<string> GetJiraIds(TraceWriter log, IEnumerable<Changeset> history, IEnumerable<string> ignoreJiraProjects)
    {
        log.Info($"{history.Count()} changesets");
    
        // Provide a list of all the jira IDs that appear in the changeset comments.
        var tickets = history.SelectMany(cs => JiraEx.JiraIds(cs.Comment))
            .Where(jiraId => !ignoreJiraProjects.Contains(jiraId, StringComparer.OrdinalIgnoreCase))
            .Distinct();
    
        log.Info($"{tickets.Count()} Jira tickets found in {history.Count()} changesets");
        log.Info($"key in ({string.Join(", ", tickets.Select(t => t))})");
    
        return tickets;
    }
    
    // Find list of changesets between a TFS source and destination that have not been merged between the branches.
    // source: TFS path like "$/Dev"
    // destination: TFS path like "$/Release"
    internal static IEnumerable<MergeCandidate> MergeCandidates(TraceWriter log, string source, string destination)
    {
        // Get a list of merge candidates, ignoring anything starting with "merg" for "merged" "merging" etc
        string ignore = "merg";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    
        // Can retrieve SOAP service from TfsTeamProjectCollection instance
        using (TfsTeamProjectCollection tpc = GetTfsCollection())
        {
            var vcs = tpc.GetService<VersionControlServer>();

            var result = vcs.GetMergeCandidates(source, destination, RecursionType.Full)
                .Where(mc => mc.Changeset.Comment.IndexOf(ignore, StringComparison.OrdinalIgnoreCase) == -1);
                //.Select(mc => mc.ToModel());
                
            log.Info($"Found {result.Count()} merges between {source} and {destination} in {stopwatch.ElapsedMilliseconds} ms");
                
            return result;
        }
    }
}

internal class ChangesetEx
{
    /// <summary>
    /// Has the changeset been reviewed?
    /// </summary>
    internal static bool IsReviewed(Changeset changeset)
    {
        //http://stackoverflow.com/questions/717855/why-is-function-isprefix-faster-than-startswith-in-c
        int stars = changeset.Comment.IndexOf("**");
        if (stars == -1) // no ** at all in the comment then return not-reviewed.
            return false;

        // If the next char after a ** is empty (or is ***), then this is not reviewed
        if (changeset.Comment.Substring(stars + 2, 1) == " " || changeset.Comment.Substring(stars + 2, 1) == "*")
            return false;

        return true;
    }
}

internal class JiraEx
{
    const string Pattern = @"\b[A-Z]+-\d+\b";
    public static IEnumerable<string> JiraIds(string value)
    {
        var r = new Regex(Pattern, RegexOptions.IgnoreCase);
        Match m = r.Match(value);
        while (m.Success)
        {
            foreach (var g in m.Groups)
            {
                if (!g.ToString().StartsWith("Release-", StringComparison.OrdinalIgnoreCase))
                    yield return g.ToString(); // TODO remove this
            }
            m = m.NextMatch();
        }
    }
}

internal class StringEx
{
    /// <summary>
    /// Truncate a string to a length. IsNullOrWhiteSpace aware.
    /// </summary>
    public static string Truncate(string s, int len)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return s.Substring(0, Math.Min(s.Length, 50));
    }
}