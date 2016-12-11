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

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    var formData = await req.Content.ReadAsFormDataAsync();
    var textParts = formData["text"].Split(' ');
    var slackToken = formData["token"];
    log.Info("text:" + formData["text"]);

    if (!slackToken.Equals(ConfigurationManager.AppSettings["Slack.Token"]))
    {
        log.Info($"Slack token {slackToken} didn't match the expected token.");
        return Message(req, "la la la TFSbot won't listen to you becuase - you know - token mismatch.");
    }

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

    if (textParts[1].Equals("not-reviewed", StringComparison.OrdinalIgnoreCase))
    {
        var history = TfsEx.GetHistory(log, tfsPath, from);
        var tickets = TfsEx.NotReviewed(log, history);
        var message = tickets.Any()
            ? $"Changesets not reviewed from {from.ToString("dd-MMM")}:\n{string.Join("\n", tickets.Select(t => t))}"
            : $"All changesets reviewed from {from.ToString("dd-MMM")}";
        return Message(req, message);
    }
    else if (textParts[1].Equals("missing-jira", StringComparison.OrdinalIgnoreCase))
    {
        // Ignore Jira projects like: Release SHD esd ops qa
        IEnumerable<string> ignoreJiraProjects = (ConfigurationManager.AppSettings["Jira.IgnoreProjects"] ?? string.Empty).Split(' ').Select(s => s.Trim());

        var history = TfsEx.GetHistory(log, tfsPath, from);
        var missingJiraIds = TfsEx.GetMissingJiraIds(log, history, ignoreJiraProjects);

        return Message(req, $"Missing Jira ticket ID from {from.ToString("dd-MMM")}:\n{string.Join("\n", missingJiraIds.Select(t => t))}"));
    }
    else if (textParts[1].Equals("tickets", StringComparison.OrdinalIgnoreCase))
    {
        // Ignore Jira projects like: Release SHD esd ops qa
        IEnumerable<string> ignoreJiraProjects = (ConfigurationManager.AppSettings["Jira.IgnoreProjects"] ?? string.Empty).Split(' ').Select(s => s.Trim());

        var history = TfsEx.GetHistory(log, tfsPath, from);
        var tickets = TfsEx.GetJiraIds(log, history, ignoreJiraProjects);

        return Message(req, $"Jira activity from {from.ToString("dd-MMM")}.\n\nkey in ({string.Join(", ", tickets.Select(t => t))})");
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

        var term = string.Join(" ", textParts.Skip(2));
        var history = TfsEx.GetHistory(log, tfsPath, from);
        var tickets = TfsEx.SearchHistoryByUser(log, history, new[] { term });

        if (!tickets.Any())
        {
            return Message(req, $"Couldn't find anything from {from.ToString("dd-MMM")} with the committer '{term}'.");
        }

        return Message(req, $"Found {tickets.Count()} changesets from {from.ToString("dd-MMM")}:\n {string.Join("\n", tickets.Select(t => $"{t.ChangesetId} {t.CreationDate} - {StringEx.Truncate(t.Comment.Replace(Environment.NewLine, ""), 50)}"))}");
    }
    else if (textParts[1].Equals("merge", StringComparison.OrdinalIgnoreCase))
    {
        var source = textParts.Count() >= 3 ? textParts[2] : "$/CLS/Src/Main/1611";
        var destination = textParts.Count() >= 4 ? textParts[3] : "$/CLS/Src/Dev";
        var username = textParts.Count() >= 5 ? textParts[4] : string.Empty;

        if (!source.StartsWith("$"))
            source = $"$/CLS/Src{source}";
        if (!destination.StartsWith("$"))
            destination = $"$/CLS/Src{destination}";

        var merges = TfsEx.MergeCandidates(log, source, destination)
            .Select(x => x.Changeset)
            .ToList();

        if (!string.IsNullOrWhiteSpace(username))
        {
            log.Info("filter it");
            merges = merges.Where(h => h.Owner.IndexOf(username, StringComparison.OrdinalIgnoreCase) > -1 || h.OwnerDisplayName.IndexOf(username, StringComparison.OrdinalIgnoreCase) > -1).ToList();
        }

        if (!merges.Any())
        {
            return Message(req, $"Nothing to merge between '{source}' and '{destination}' {username}.");
        }

        return Message(req, $"Found {merges.Count()} merge candidates from {source} to {destination} {username}:\n {string.Join("\n", merges.Select(t => $"{t.ChangesetId} {t.CreationDate.ToString("dd-MMM")} {t.Owner} - {StringEx.Truncate(t.Comment.Replace(Environment.NewLine, ""), 50)}"))}");
    }
    else if (textParts[1].Equals("stats", StringComparison.OrdinalIgnoreCase))
    {
        // tfsbot stats <date> <username1> <username2> ... <usernameN>
        var users = textParts.Skip(3);
        if (!users.Any())
            return Help(req);

        var history = TfsEx.GetHistory(log, tfsPath, from);
        var tickets = TfsEx.SearchHistoryByUser(log, history, users);

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
            string line = String.Format("{0}, {1}, {2}, {3}%\n",
                r.Key, r.Value.Item2, r.Value.Item1, Math.Round(((double)r.Value.Item2 / r.Value.Item1) * 100, 1)
                );
            sb.AppendLine(line);
        }        

        return Message(req, sb.ToString());
    }
    
    log.Info($"C# Timer trigger function executed at: {DateTime.Now}");  

    return Help(req);
}

static HttpResponseMessage Help(HttpRequestMessage req)
{
    return Message(req, $"Yo, TFSbot doesn't understand. Tell me what you want:\n `tfsbot not-reviewed yyyy-MM-dd` - Changesets not peer-reviewed\n `tfsbot missing-jira yyyy-MM-dd` - Changesets missing Jira IDs\n `tfsbot tickets yyyy-MM-dd` - Changeset to Jira activity\n `tfsbot search <term>` - Search 30 days of history\n `tfsbot search-user <username>` - Find 30 days of changes by committer\n `tfsbot merge /source /destination [username]` - List of merge candidates (changesets) between the source and destination.");
}

static HttpResponseMessage Message(HttpRequestMessage req, string message)
{
    return req.CreateResponse(HttpStatusCode.OK, new {
        text = message
    });
}