#load "..\extensions\TfsEx.csx"

using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text;

// TFSbot v1.0

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    var formData = await req.Content.ReadAsFormDataAsync();
    var textParts = formData["text"].Split(' ');
    // var slackToken = formData["token"];
    var slackUsername = formData["user_name"];
    log.Info("text:" + formData["text"]);

    // if (!slackToken.Equals(ConfigurationManager.AppSettings["Slack.Token"]))
    // {
    //     log.Info($"Slack token {slackToken} didn't match the expected token.");
    //     return Message(req, "la la la TFSbot won't listen to you becuase - you know - token mismatch.");
    // }

    // When no arguments are passed then provide a hint to the user about tfsbot.
    if (!textParts.Any() || textParts.Count() == 1 || formData["text"].IndexOf("help", StringComparison.OrdinalIgnoreCase) > -1)
    {
        return Help(req);
    }

    DateTime from = DateTime.Now.Subtract(TimeSpan.FromDays(1));
    string tfsPath = ConfigurationManager.AppSettings["Tfs.Path"] ?? "$/";

    // If there's something like a date in the query let's use that as a date for the query part
    foreach (var part in textParts)
    {
        DateTime tempFrom;
        if (DateTime.TryParse(part, out tempFrom))
        {
            from = tempFrom;
            break;
        }
    }

    if (textParts[1].Equals("not-reviewed", StringComparison.OrdinalIgnoreCase) || textParts[1].Equals("not-reviewed-count", StringComparison.OrdinalIgnoreCase))
    {
        // Allow days for input here
        var d = textParts.Count() > 2 ? textParts.Skip(2).FirstOrDefault() : string.Empty;
        int days;
        if (int.TryParse(d, out days))
        {
            from = DateTime.UtcNow.Subtract(TimeSpan.FromDays(Math.Abs(days)));
        }

        var searchUser = textParts.Count() > 3 ? textParts.Skip(3) : Enumerable.Empty<string>();
        var history = TfsEx.GetHistory(log, tfsPath, from);
        
        if (searchUser.Any())
        {
            history = TfsEx.SearchHistoryByUser(log, history, searchUser);
        }

        var tickets = TfsEx.NotReviewed(log, history)
            .Select(cs => $"{cs.ChangesetId}, {cs.Committer}, {cs.CreationDate.ToString("dd-MMM")}, {StringEx.Truncate(cs.Comment.Replace(Environment.NewLine, string.Empty), 50)}");

        var message = tickets.Any()
            ? $"Changesets not reviewed from {from.ToString("dd-MMM")}:\n{string.Join("\n", tickets.Select(t => t))}"
            : $"All changesets reviewed from {from.ToString("dd-MMM")}";

        if (textParts[1].Equals("not-reviewed-count", StringComparison.OrdinalIgnoreCase))
            message = tickets.Count().ToString();

        return Message(req, message);
    }
    else if (textParts[1].Equals("missing-jira", StringComparison.OrdinalIgnoreCase))
    {
        // Ignore Jira projects like: Release SHD esd ops qa
        IEnumerable<string> ignoreJiraProjects = (ConfigurationManager.AppSettings["Jira.IgnoreProjects"] ?? string.Empty).Split(' ').Select(s => s.Trim());

        var history = TfsEx.GetHistory(log, tfsPath, from);
        var missingJiraIds = TfsEx.GetMissingJiraIds(log, history, ignoreJiraProjects);

        return Message(req, $"Missing Jira ticket ID from {from.ToString("dd-MMM")}:\n{string.Join("\n", missingJiraIds.Select(t => t))}");
    }
    else if (textParts[1].Equals("tickets", StringComparison.OrdinalIgnoreCase))
    {
        // Ignore Jira projects like: Release SHD esd ops qa
        IEnumerable<string> ignoreJiraProjects = (ConfigurationManager.AppSettings["Jira.IgnoreProjects"] ?? string.Empty).Split(' ').Select(s => s.Trim());

        var history = TfsEx.GetHistory(log, tfsPath, from);

        // Don't get Jira IDs from merged changesets
        history = history.Where(cs => cs.Comment.IndexOf("merg", StringComparison.OrdinalIgnoreCase) == -1);

        var tickets = TfsEx.GetJiraIds(log, history, ignoreJiraProjects);

        return Message(req, $"Jira activity from {from.ToString("dd-MMM")}, {tickets.Count()} tickets.\n\nkey in ({string.Join(", ", tickets.Select(t => t))})");
    }
    else if (textParts[1].Equals("search", StringComparison.OrdinalIgnoreCase) && textParts.Count() > 2)
    {
        from = DateTime.Now.Subtract(TimeSpan.FromDays(30));

        var term = string.Join(" ", textParts.Skip(2));
        var history = TfsEx.GetHistory(log, tfsPath, from);
        var tickets = TfsEx.SearchHistory(log, history, term);

        if (!tickets.Any())
        {
            return Message(req, $"Couldn't find anything from {from.ToString("dd-MMM")} with the term '{term}'.");
        }

        return Message(req, $"Found {tickets.Count()} changesets from {from.ToString("dd-MMM")}:\n {string.Join("\n", tickets.Select(t => $"{t.ChangesetId} {t.CommitterDisplayName} - {StringEx.Truncate(t.Comment.Replace(Environment.NewLine, ""), 50)}"))}");
    }
    else if (textParts[1].Equals("search-user", StringComparison.OrdinalIgnoreCase) && textParts.Count() > 2)
    {
        from = DateTime.Now.Subtract(TimeSpan.FromDays(30));

        var searchUser = textParts.Skip(2).Take(1);
        var term = textParts.Count() > 3 ? string.Join(" ", textParts.Skip(3)) : string.Empty;
        var history = TfsEx.GetHistory(log, tfsPath, from);
        var tickets = TfsEx.SearchHistoryByUser(log, history, searchUser);
        if (!string.IsNullOrWhiteSpace(term))
            tickets = TfsEx.SearchHistory(log, tickets, term);

        if (!tickets.Any())
        {
            return Message(req, $"Couldn't find anything from {from.ToString("dd-MMM")} with the committer '{searchUser}' and term '{term}'.");
        }

        return Message(req, $"Found {tickets.Count()} changesets from {from.ToString("dd-MMM")}:\n {string.Join("\n", tickets.Select(t => $"{t.ChangesetId} {t.CreationDate} - {StringEx.Truncate(t.Comment.Replace(Environment.NewLine, ""), 50)}"))}");
    }
    else if (textParts[1].Equals("merge", StringComparison.OrdinalIgnoreCase))
    {
        var source = textParts.Count() >= 3 ? textParts[2] : "$/CLS/Src/Main/1611";
        var destination = textParts.Count() >= 4 ? textParts[3] : "$/CLS/Src/Dev";
        var filterUsernames = textParts.Count() >= 5 ? textParts.Skip(4) : Enumerable.Empty<string>();

        if (!source.StartsWith("$"))
            source = $"$/CLS/Src{source}";
        if (!destination.StartsWith("$"))
            destination = $"$/CLS/Src{destination}";

        var merges = TfsEx.MergeCandidates(log, source, destination)
            .Select(x => x.Changeset)
            .ToList();

        if (filterUsernames.Any())
        {
            log.Info($"filter it with: {string.Join(",", filterUsernames)}");
            merges = merges.Where(h => filterUsernames.Any(s => h.Owner.IndexOf(s, StringComparison.OrdinalIgnoreCase) > -1)
                || filterUsernames.Any(s => h.OwnerDisplayName.IndexOf(s, StringComparison.OrdinalIgnoreCase) > -1)
            ).ToList();
        }

        if (!merges.Any())
        {
            return Message(req, $"Nothing to merge between '{source}' and '{destination}' {string.Join(", ", filterUsernames)}.");
        }

        return Message(req, $"Found {merges.Count()} merge candidates from {source} to {destination} {string.Join(", ", filterUsernames)}:\n {string.Join("\n", merges.Select(t => $"{t.ChangesetId}, {t.CreationDate.ToString("dd-MMM")}, {t.Owner}, {StringEx.Truncate(t.Comment.Replace(Environment.NewLine, ""), 50)}"))}");
    }
    else if (textParts[1].Equals("stats", StringComparison.OrdinalIgnoreCase))
    {
        // Allow days for input here
        var d = textParts.Count() > 2 ? textParts.Skip(2).FirstOrDefault() : string.Empty;
        int days;
        if (int.TryParse(d, out days))
        {
            from = DateTime.UtcNow.Subtract(TimeSpan.FromDays(Math.Abs(days)));
        }

        // tfsbot stats <date> <username1> <username2> ... <usernameN>
        var users = textParts.Skip(3);
        if (!users.Any())
            users = new[] { slackUsername };
        if (string.Join("", users).Trim().Equals("*"))
            users = Enumerable.Empty<string>(); // Don't filter for any users, we want all.

        var history = TfsEx.GetHistory(log, tfsPath, from);
        var changesets = TfsEx.SearchHistoryByUser(log, history, users);

        var result = new Dictionary<string, Tuple<int, int>>();
        foreach (var cs in changesets)
        {
            if (!result.ContainsKey(cs.CommitterDisplayName))
            {
                result.Add(cs.CommitterDisplayName, Tuple.Create(1, ChangesetEx.IsReviewed(cs) ? 1 : 0));
            }
            else
            {
                var item = result[cs.CommitterDisplayName];
                result[cs.CommitterDisplayName] = Tuple.Create(item.Item1 + 1, ChangesetEx.IsReviewed(cs) ? item.Item2 + 1 : item.Item2);
            }                
        }

        var sb = new StringBuilder();
        foreach (var r in result)
        {
            string line = String.Format("{0}, {1}, {2}, {3}%",
                r.Key, r.Value.Item2, r.Value.Item1, Math.Round(((double)r.Value.Item2 / r.Value.Item1) * 100, 1)
                );
            sb.AppendLine(line);
        }        

        var message = sb.Length > 0
            ? $"Stats from {from.ToString("dd-MMM")} for {string.Join(", ", users)}.\nUser, Reviewed, Commits, Review%\n{sb.ToString()}"
            : $"Unable to generate stats from {from.ToString("dd-MMM")} for {string.Join(", ", users.Select(u => u))}.";
        return Message(req, message);
    }
    
    log.Info($"C# Timer trigger function executed at: {DateTime.Now}");  

    return Help(req);
}

static HttpResponseMessage Help(HttpRequestMessage req)
{
    return Message(req, $"Yo, TFSbot doesn't understand. Tell me what you want:\n `tfsbot not-reviewed <yyyy-MM-dd> or <days> [username1] [username2]` - Changesets not peer-reviewed\n `tfsbot missing-jira <yyyy-MM-dd>` - Changesets missing Jira IDs\n `tfsbot tickets <yyyy-MM-dd>` - Changeset to Jira activity\n `tfsbot search <term>` - Search 30 days of history\n `tfsbot search-user <username> [term]` - Find 30 days of changes by committer. Comment search term is optional.\n `tfsbot merge /source /destination [username1] [username2]` - List of merge candidates (changesets) between the source and destination, can be filtered by username(s).\n `tfsbot stats <yyyy-MM-dd> or <days> [username1] [username2] [*]` - Code review stats per username (default to your username).");
}

static HttpResponseMessage Message(HttpRequestMessage req, string message)
{
    return req.CreateResponse(HttpStatusCode.OK, new {
        text = message
    });
}