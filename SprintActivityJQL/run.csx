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
    IEnumerable<string> ignoreJiraProjects = (ConfigurationManager.AppSettings["Tfs.IgnoreJiraProjects"] ?? string.Empty).Split(' ').Select(s => s.Trim());

    var history = TfsEx.GetHistory(log, tfsPath, DateTime.Parse(from));
    var tickets = GetJiraIds(log, history, ignoreJiraProjects);

    log.Info($"C# Timer trigger function executed at: {DateTime.Now}");  

    return !tickets.Any()
        ? req.CreateResponse(HttpStatusCode.BadRequest, $"No tickets from date {from}")
        : req.CreateResponse(HttpStatusCode.OK, $"key in ({string.Join(", ", tickets.Select(t => t))})");
}

static IEnumerable<string> GetJiraIds(TraceWriter log, IEnumerable<Changeset> history, IEnumerable<string> ignoreJiraProjects)
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