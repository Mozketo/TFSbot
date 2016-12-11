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
    DateTime dFrom = DateTime.Parse(from);

    string tfsPath = ConfigurationManager.AppSettings["Tfs.Path"] ?? "$/";

    var history = TfsEx.GetHistory(log, tfsPath, DateTime.Parse(from));
    var tickets = NotReviewed(log, history);

    log.Info($"C# Timer trigger function executed at: {DateTime.Now}");  

    if (tickets.Any())
    {
        return req.CreateResponse(HttpStatusCode.OK, new {
            text = $"Changesets not reviewed from {dFrom.ToString("dd-MMM")}:\n{string.Join("\n", tickets.Select(t => t))}"
        });
    }

    return !tickets.Any()
        ? req.CreateResponse(HttpStatusCode.BadRequest, $"No tickets from date {from}")
        : req.CreateResponse(HttpStatusCode.OK, $"{string.Join(", ", tickets.Select(t => t))}");
}

static IEnumerable<string> NotReviewed(TraceWriter log, IEnumerable<Changeset> history)
{
    log.Info($"{history.Count()} changesets");

    // Provide a list of all the jira IDs that appear in the changeset comments.
    var tickets = history
        .Where(cs => !ChangesetEx.IsReviewed(cs))
        .Select(cs => $"CS:{cs.ChangesetId} by {cs.Committer} ({cs.CreationDate.ToString("dd-MMM")})");

    //log.Info($"{tickets.Count()} Jira tickets found in {history.Count()} changesets");
    //log.Info($"key in ({string.Join(", ", tickets.Select(t => t))})");

    return tickets;
}