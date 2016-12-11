#load "..\extensions\TfsEx.csx"

using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    string from = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "from", true) == 0)
        .Value ?? DateTime.Now.Subtract(TimeSpan.FromDays(1)).ToString();

    string tfsPath = ConfigurationManager.AppSettings["Tfs.Path"] ?? "$/";

    // Ignore Jira projects like: Release SHD esd ops qa
    IEnumerable<string> ignoreJiraProjects = (ConfigurationManager.AppSettings["Jira.IgnoreProjects"] ?? string.Empty).Split(' ').Select(s => s.Trim());

    var history = TfsEx.GetHistory(log, tfsPath, DateTime.Parse(from));
    var missingJiraIds = GetMissingJiraIds(log, history, ignoreJiraProjects);

    log.Info($"C# Timer trigger function executed at: {DateTime.Now}");  

    return !missingJiraIds.Any()
        ? req.CreateResponse(HttpStatusCode.BadRequest, $"No tickets from date {from}")
        : req.CreateResponse(HttpStatusCode.OK, $"{string.Join(", ", missingJiraIds.Select(t => t))}");
}

static IEnumerable<string> GetMissingJiraIds(TraceWriter log, IEnumerable<Changeset> history, IEnumerable<string> ignoreJiraProjects)
{
    log.Info($"{history.Count()} changesets");

    // We also want to know about changesets that are missing a Jira ID.
    var missingJiraIds = new List<string>();
    foreach (var cs in history)
    {
        var jiraIds = JiraEx.JiraIds(cs.Comment);
        if (!jiraIds.Any() && cs.Comment.IndexOf("merg", StringComparison.OrdinalIgnoreCase) == -1)
        {
            missingJiraIds.Add($"{cs.ChangesetId} - {cs.Comment}");
        }
    }

    log.Info($"Missing jira IDs: {string.Join(", ", missingJiraIds.Select(t => t))}");

    return missingJiraIds;
}

